using System.Text;

namespace Residue.Net.Connect
{
    /// <summary>
    /// Turning what a player typed into what the Lobby service will accept.
    /// <para>
    /// The join code is the entire co-op UX: one person reads six characters aloud and the other
    /// types them. Everything that can go wrong between those two acts is a formatting problem, and
    /// every one of them is fixable here rather than by the player. A code read over voice arrives
    /// lowercase; a code pasted out of a chat window arrives with a trailing newline, a leading
    /// space, or hyphenated into groups because that is how the sender made it readable.
    /// </para>
    /// What is deliberately <b>not</b> done: no character is remapped. Turning <c>O</c> into
    /// <c>0</c> would rescue a misheard code and silently corrupt a correctly typed one, and the
    /// player has no way to tell which happened. Ambiguity is better reported than guessed at.
    /// </summary>
    public static class JoinCode
    {
        /// <summary>Unity Lobby invite codes are six characters. Anything else is a typo.</summary>
        public const int Length = 6;

        /// <summary>
        /// Uppercase, and strip everything that is not a letter or a digit.
        /// <para>
        /// Whitespace, hyphens, newlines and stray punctuation all go. Prose does not: pasting
        /// "join code: ABC123" yields "JOINCODEABC123", which fails <see cref="IsWellFormed"/> and
        /// is told so. Trying to find a code inside a sentence would occasionally find the wrong
        /// six characters and be believed.
        /// </para>
        /// </summary>
        public static string Normalise(string raw)
        {
            if (string.IsNullOrEmpty(raw)) return string.Empty;

            var builder = new StringBuilder(raw.Length);
            foreach (char c in raw)
            {
                if (c >= '0' && c <= '9') builder.Append(c);
                else if (c >= 'a' && c <= 'z') builder.Append((char)(c - 'a' + 'A'));
                else if (c >= 'A' && c <= 'Z') builder.Append(c);
            }
            return builder.ToString();
        }

        /// <summary>
        /// True for something that could be a join code. Checked before any network call so a typo
        /// costs nothing and reports itself immediately, rather than after a round trip that comes
        /// back with a service error the player cannot act on.
        /// </summary>
        public static bool IsWellFormed(string code)
        {
            if (string.IsNullOrEmpty(code) || code.Length != Length) return false;

            foreach (char c in code)
            {
                bool ok = (c >= '0' && c <= '9') || (c >= 'A' && c <= 'Z');
                if (!ok) return false;
            }
            return true;
        }

        /// <summary>
        /// The host's copy, spaced into two groups of three. Purely for reading aloud — six
        /// characters in one run get miscounted, three and three do not. Never send this to the
        /// service; <see cref="Normalise"/> would strip the space again anyway.
        /// </summary>
        public static string ForReading(string code)
        {
            if (string.IsNullOrEmpty(code)) return string.Empty;
            if (code.Length != Length) return code;
            return $"{code.Substring(0, 3)} {code.Substring(3)}";
        }
    }
}
