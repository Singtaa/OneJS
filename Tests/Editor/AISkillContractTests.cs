using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using NUnit.Framework;
using OneJS.Editor;

namespace OneJS.Tests.Editor {
    /// <summary>
    /// The shipped AI Skills carry the asset version and the minimum Unity version
    /// in their frontmatter, hand written, where a catalog and an agent both read them.
    ///
    /// Nothing else notices when they fall behind package.json. A skill claiming an
    /// older version still parses, still loads, and still answers questions, so the
    /// drift surfaces as a customer being told the wrong thing rather than as an
    /// error. That is the failure this guards, and it is why the check compares the
    /// two files rather than asserting against a constant copied from one of them.
    /// </summary>
    public class AISkillContractTests {
        static string PackageRoot {
            get {
                var root = AISkillsInstaller.FindPackageRoot();
                Assert.IsFalse(string.IsNullOrEmpty(root),
                    "Could not resolve the OneJS package root. If the layout moved, fix " +
                    "AISkillsInstaller.FindPackageRoot rather than deleting this test.");
                return root;
            }
        }

        static string[] SkillFiles {
            get {
                var skillsDir = Path.Combine(PackageRoot, "AI", "Skills");
                Assert.IsTrue(Directory.Exists(skillsDir),
                    $"Skills folder missing at {skillsDir}. A guard that quietly stops " +
                    "running is worse than no guard, so this fails rather than skips.");

                var files = Directory.GetFiles(skillsDir, "SKILL.md", SearchOption.AllDirectories);
                Assert.IsNotEmpty(files, $"No SKILL.md found under {skillsDir}.");
                return files;
            }
        }

        static string PackageJsonField(string field) {
            var path = Path.Combine(PackageRoot, "package.json");
            Assert.IsTrue(File.Exists(path), $"package.json missing at {path}");

            var m = Regex.Match(File.ReadAllText(path), $"\"{field}\"\\s*:\\s*\"([^\"]+)\"");
            Assert.IsTrue(m.Success, $"No '{field}' field in {path}");
            return m.Groups[1].Value;
        }

        static string Frontmatter(string skillPath, string key) {
            var m = Regex.Match(File.ReadAllText(skillPath), $"^  {key}:\\s*\"([^\"]+)\"",
                RegexOptions.Multiline);
            Assert.IsTrue(m.Success, $"No '{key}' in the frontmatter of {skillPath}");
            return m.Groups[1].Value;
        }

        [Test]
        public void SkillAssetVersion_MatchesPackageJson() {
            var expected = PackageJsonField("version");

            foreach (var skill in SkillFiles) {
                Assert.AreEqual(expected, Frontmatter(skill, "asset-version"),
                    $"{Path.GetFileName(Path.GetDirectoryName(skill))} claims a different asset " +
                    $"version than package.json.\nBump 'asset-version' and 'last-verified' in " +
                    $"{skill} whenever the package version moves.");
            }
        }

        [Test]
        public void SkillMinimumUnity_MatchesPackageJson() {
            var expected = PackageJsonField("unity");

            foreach (var skill in SkillFiles) {
                var declared = Frontmatter(skill, "unity");
                Assert.IsTrue(declared.StartsWith(expected),
                    $"{Path.GetFileName(Path.GetDirectoryName(skill))} declares Unity " +
                    $"'{declared}', which does not start with package.json's '{expected}'. " +
                    "A skill promising a lower floor than the package supports sends agents " +
                    "down a path that cannot work.");
            }
        }

        [Test]
        public void SkillLastVerified_IsAParseableDate() {
            foreach (var skill in SkillFiles) {
                var raw = Frontmatter(skill, "last-verified");
                Assert.IsTrue(
                    System.DateTime.TryParseExact(raw, "yyyy-MM-dd",
                        System.Globalization.CultureInfo.InvariantCulture,
                        System.Globalization.DateTimeStyles.None, out var parsed),
                    $"'last-verified' in {skill} is '{raw}', not a YYYY-MM-DD date.");

                Assert.LessOrEqual(parsed, System.DateTime.UtcNow.Date.AddDays(1),
                    $"'last-verified' in {skill} is in the future.");
            }
        }
    }
}
