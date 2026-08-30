using System;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;
using PackageInfo = UnityEditor.PackageManager.PackageInfo;

namespace Residue.Editor.Content
{
    /// <summary>
    /// Turns the readable sources of the credits screen (#53) into the text a build actually ships:
    /// the art licences recorded in <c>Assets/Art/Imported/CREDITS.md</c>, and the
    /// "Third Party Notices.md" that ships inside every resolved package. Neither is transcribed by
    /// hand into <c>CreditsPanel</c> — CREDITS.md is the whole point of recording a licence in the
    /// first place (see the Art section of CLAUDE.md), and a hand-typed second copy of either is
    /// exactly the copy that drifts from what actually shipped.
    /// <para>
    /// <b>Projects into a generated C# source file, not a <see cref="TextAsset"/>.</b>
    /// <see cref="Residue.Net.UI.CreditsPanel"/> is a plain class with no serialized fields, following
    /// every other page of the menu shell (see <c>SettingsPanel</c> for why) — there is no Inspector
    /// slot to hold an asset reference, and this project has no <c>Resources</c> folder convention to
    /// fall back on for a class that is never a <see cref="MonoBehaviour"/>. A compiled string constant
    /// needs no reference wiring, no Resources path and no build-inclusion rule to get right; it is
    /// simply part of the assembly, the same way every other line of <c>Residue.Net</c> is.
    /// </para>
    /// </summary>
    public static class CreditsBuilder
    {
        private const string CreditsMdRelativePath = "Assets/Art/Imported/CREDITS.md";
        private const string OutputRelativePath = "Assets/Scripts/Net/UI/CreditsContent.Generated.cs";

        /// <summary>
        /// Joins one package's notice onto the next. Emitted alongside the joined text as
        /// <c>CreditsContent.PackageNoticeSeparator</c> so <c>CreditsPanel</c> can split the notices
        /// back into one label per package without a second, independently-typed copy of this string
        /// ever having a chance to disagree with it.
        /// </summary>
        private const string NoticeSeparator = "\n\n----- next package -----\n\n";

        [MenuItem("Residue/Content/Rebuild Credits", priority = 2)]
        public static void Rebuild()
        {
            string art = ReadArtCredits();
            string packages = BuildPackageNotices(out int packageCount);

            WriteSource(art, packages);
            AssetDatabase.Refresh();

            Debug.Log(
                $"[Residue] Credits rebuilt: {CreditsMdRelativePath} plus notices from " +
                $"{packageCount} resolved package(s) -> {OutputRelativePath}.");
        }

        private static string ReadArtCredits()
        {
            string path = Path.Combine(ProjectRoot, CreditsMdRelativePath);
            if (!File.Exists(path))
            {
                throw new FileNotFoundException(
                    $"[Residue] {CreditsMdRelativePath} is missing. It is the source of truth for art " +
                    "licences and the credits screen reproduces it verbatim — create it (even empty of " +
                    "entries) before rebuilding.");
            }

            return Normalise(File.ReadAllText(path));
        }

        /// <summary>
        /// One block per resolved package that ships a notice file, ordered by package id so the
        /// generated source diffs predictably between rebuilds. Every <i>resolved</i> package rather
        /// than only the direct dependencies in <c>manifest.json</c>: a transitive package still ships
        /// its own compiled code in the build, so its notice is still owed.
        /// </summary>
        private static string BuildPackageNotices(out int packageCount)
        {
            var packages = PackageInfo.GetAllRegisteredPackages()
                .Where(p => !string.IsNullOrEmpty(p.resolvedPath))
                .OrderBy(p => p.name, StringComparer.Ordinal)
                .ToList();

            var sb = new StringBuilder();
            packageCount = 0;

            foreach (var package in packages)
            {
                string noticePath = FindNoticeFile(package.resolvedPath);
                if (noticePath == null) continue;

                if (packageCount > 0) sb.Append(NoticeSeparator);
                packageCount++;

                sb.Append($"{package.name} {package.version}\n\n");
                sb.Append(Normalise(File.ReadAllText(noticePath)));
            }

            return sb.ToString();
        }

        /// <summary>
        /// Package notice files are not named consistently across the packages this project pulls in
        /// — "Third Party Notices.md" and "THIRD PARTY NOTICES.md" both appear — so this matches on
        /// the name with spacing and case ignored rather than on one exact spelling.
        /// </summary>
        private static string FindNoticeFile(string packageDir)
        {
            if (!Directory.Exists(packageDir)) return null;

            foreach (string candidate in Directory.GetFiles(packageDir, "*.md", SearchOption.TopDirectoryOnly))
            {
                string name = Path.GetFileNameWithoutExtension(candidate)
                    .Replace(" ", string.Empty)
                    .ToLowerInvariant();

                if (name == "thirdpartynotices") return candidate;
            }

            return null;
        }

        private static string Normalise(string text) => text.Replace("\r\n", "\n").TrimEnd();

        private static void WriteSource(string art, string packages)
        {
            var sb = new StringBuilder();
            sb.Append("// <auto-generated>\n");
            sb.Append("// Generated by Residue/Content/Rebuild Credits (Assets/Editor/Content/CreditsBuilder.cs)\n");
            sb.Append("// from Assets/Art/Imported/CREDITS.md and every resolved package's own third-party\n");
            sb.Append("// notice file. Do not hand-edit this file — edit the source and rebuild, or a hand\n");
            sb.Append("// edit here becomes exactly the second copy CreditsBuilder exists to prevent.\n");
            sb.Append("// </auto-generated>\n");
            sb.Append("namespace Residue.Net.UI\n{\n");
            sb.Append("    /// <summary>\n");
            sb.Append("    /// Generated text for the credits screen (#53). See\n");
            sb.Append("    /// <c>Residue.Editor.Content.CreditsBuilder</c> for what produced this and why.\n");
            sb.Append("    /// </summary>\n");
            sb.Append("    public static class CreditsContent\n    {\n");
            sb.Append("        /// <summary>Verbatim contents of Assets/Art/Imported/CREDITS.md.</summary>\n");
            sb.Append("        public const string ThirdPartyArt = @\"").Append(Escape(art)).Append("\";\n\n");
            sb.Append("        /// <summary>Every resolved package's own notice file, joined by\n");
            sb.Append("        /// <see cref=\"PackageNoticeSeparator\"/>.</summary>\n");
            sb.Append("        public const string PackageNotices = @\"").Append(Escape(packages)).Append("\";\n\n");
            sb.Append("        /// <summary>Splits <see cref=\"PackageNotices\"/> back into one entry per\n");
            sb.Append("        /// package.</summary>\n");
            sb.Append("        public const string PackageNoticeSeparator = @\"").Append(Escape(NoticeSeparator)).Append("\";\n");
            sb.Append("    }\n}\n");

            string path = Path.Combine(ProjectRoot, OutputRelativePath);
            File.WriteAllText(path, sb.ToString(), new UTF8Encoding(false));
        }

        private static string Escape(string text) => text.Replace("\"", "\"\"");

        private static string ProjectRoot => Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
    }
}
