using System;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace Residue.Gameplay.Simulation
{
    /// <summary>Where a loaded run came from.</summary>
    public enum RunSaveSource
    {
        None,
        Primary,
        Backup
    }

    /// <summary>
    /// The host-only disk boundary for an M5 run save.
    /// <para>
    /// It deliberately stores an opaque payload. The versioned run snapshot owns what game state
    /// means; this type owns the less interesting but more destructive problem of getting those
    /// bytes to disk without replacing the only good copy with a partial write.
    /// </para>
    /// </summary>
    public sealed class RunSaveStore
    {
        public const int CurrentFormatVersion = 1;

        private const string Magic = "OILEDUP-SAVE";
        private const int HeaderLineCount = 4;

        private static readonly Encoding Utf8 = new UTF8Encoding(false, true);

        public string SlotPath { get; }
        public string BackupPath => SlotPath + ".bak";
        public string TemporaryPath => SlotPath + ".tmp";

        public RunSaveStore(string slotPath)
        {
            if (string.IsNullOrWhiteSpace(slotPath))
                throw new ArgumentException("A save slot needs a path.", nameof(slotPath));

            SlotPath = Path.GetFullPath(slotPath);
        }

        /// <summary>
        /// Write a complete envelope beside the slot, flush it to disk, then atomically swap it in.
        /// The old primary becomes the backup only when it was itself a valid save.
        /// </summary>
        public bool TrySave(string payload, out string refusal)
        {
            refusal = null;
            payload ??= string.Empty;

            try
            {
                string directory = Path.GetDirectoryName(SlotPath);
                if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);

                DeleteTemporaryFile();
                byte[] envelope = Encode(payload, CurrentFormatVersion);

                using (var stream = new FileStream(
                           TemporaryPath, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                           4096, FileOptions.WriteThrough))
                {
                    stream.Write(envelope, 0, envelope.Length);
                    stream.Flush(true);
                }

                if (!File.Exists(SlotPath))
                {
                    File.Move(TemporaryPath, SlotPath);
                    return true;
                }

                // Never overwrite the last known-good backup with a corrupt primary. File.Replace
                // is still used in both branches, so the final path is never observed half-written.
                string backup = TryDecodeFile(SlotPath, out _, out _)
                    ? BackupPath
                    : null;

                File.Replace(TemporaryPath, SlotPath, backup, true);
                return true;
            }
            catch (Exception exception) when (exception is IOException
                                               or UnauthorizedAccessException
                                               or System.Security.SecurityException)
            {
                refusal = $"Could not save the run: {exception.Message}";
                DeleteTemporaryFile();
                return false;
            }
        }

        /// <summary>
        /// Load the primary slot, or its last known-good predecessor when the primary is damaged.
        /// Recovery is reported rather than silently hidden so the caller can warn before saving
        /// over the damaged file.
        /// </summary>
        public bool TryLoad(out string payload, out RunSaveSource source, out string refusal)
        {
            if (TryDecodeFile(SlotPath, out payload, out string primaryFailure))
            {
                source = RunSaveSource.Primary;
                refusal = null;
                return true;
            }

            if (TryDecodeFile(BackupPath, out payload, out string backupFailure))
            {
                source = RunSaveSource.Backup;
                refusal = $"The primary save could not be read ({primaryFailure}); recovered its backup.";
                return true;
            }

            payload = null;
            source = RunSaveSource.None;
            refusal = $"No loadable run save. Primary: {primaryFailure} Backup: {backupFailure}";
            return false;
        }

        private static byte[] Encode(string payload, int version)
        {
            byte[] body = Utf8.GetBytes(payload);
            string hash;
            using (var sha = SHA256.Create()) hash = ToHex(sha.ComputeHash(body));

            string header = Magic + "\n" +
                            version.ToString(CultureInfo.InvariantCulture) + "\n" +
                            body.Length.ToString(CultureInfo.InvariantCulture) + "\n" +
                            hash + "\n";

            byte[] headerBytes = Encoding.ASCII.GetBytes(header);
            var envelope = new byte[headerBytes.Length + body.Length];
            Buffer.BlockCopy(headerBytes, 0, envelope, 0, headerBytes.Length);
            Buffer.BlockCopy(body, 0, envelope, headerBytes.Length, body.Length);
            return envelope;
        }

        private static bool TryDecodeFile(string path, out string payload, out string refusal)
        {
            payload = null;

            if (!File.Exists(path))
            {
                refusal = "file does not exist.";
                return false;
            }

            try
            {
                return TryDecode(File.ReadAllBytes(path), out payload, out refusal);
            }
            catch (Exception exception) when (exception is IOException
                                               or UnauthorizedAccessException
                                               or System.Security.SecurityException)
            {
                refusal = exception.Message;
                return false;
            }
        }

        private static bool TryDecode(byte[] envelope, out string payload, out string refusal)
        {
            payload = null;
            int offset = 0;
            var lines = new string[HeaderLineCount];

            for (int i = 0; i < lines.Length; i++)
            {
                int newline = Array.IndexOf(envelope, (byte)'\n', offset);
                if (newline < 0)
                {
                    refusal = "save header is truncated.";
                    return false;
                }

                lines[i] = Encoding.ASCII.GetString(envelope, offset, newline - offset);
                offset = newline + 1;
            }

            if (!string.Equals(lines[0], Magic, StringComparison.Ordinal))
            {
                refusal = "save signature is not recognised.";
                return false;
            }

            if (!int.TryParse(lines[1], NumberStyles.None, CultureInfo.InvariantCulture, out int version))
            {
                refusal = "save format version is invalid.";
                return false;
            }

            if (version != CurrentFormatVersion)
            {
                refusal = $"save format {version} is unsupported (expected {CurrentFormatVersion}).";
                return false;
            }

            if (!int.TryParse(lines[2], NumberStyles.None, CultureInfo.InvariantCulture, out int bodyLength)
                || bodyLength < 0
                || envelope.Length - offset != bodyLength)
            {
                refusal = "save payload length does not match its header.";
                return false;
            }

            var body = new byte[bodyLength];
            Buffer.BlockCopy(envelope, offset, body, 0, bodyLength);

            string actualHash;
            using (var sha = SHA256.Create()) actualHash = ToHex(sha.ComputeHash(body));
            if (!string.Equals(lines[3], actualHash, StringComparison.OrdinalIgnoreCase))
            {
                refusal = "save payload failed its integrity check.";
                return false;
            }

            try
            {
                payload = Utf8.GetString(body);
                refusal = null;
                return true;
            }
            catch (DecoderFallbackException)
            {
                refusal = "save payload is not valid UTF-8.";
                return false;
            }
        }

        private void DeleteTemporaryFile()
        {
            try
            {
                if (File.Exists(TemporaryPath)) File.Delete(TemporaryPath);
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }

        private static string ToHex(byte[] bytes)
        {
            var text = new StringBuilder(bytes.Length * 2);
            foreach (byte value in bytes) text.Append(value.ToString("x2", CultureInfo.InvariantCulture));
            return text.ToString();
        }
    }
}
