using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace OneJS {
    /// <summary>
    /// The default files a working directory has been given, kept in <c>~/.onejs/scaffold</c>.
    ///
    /// <c>~/.onejs/</c> is the home for per-app OneJS state: only small state a project commits
    /// goes there, never a cache or anything machine-local.
    ///
    /// JSRunner writes a default file once: a path in the record is never written again, so a
    /// file the user deletes stays deleted. The record lives beside the files it describes
    /// rather than on the component, so every runner and prefab instance that points at one
    /// working directory agrees, a team that commits <c>~/</c> shares it, and recording a path
    /// never dirties a scene.
    ///
    /// One line per path, sorted, so two teammates who each gain the same new default file
    /// merge cleanly: the path, then a tab and a hash of the content it was written with. A
    /// path recorded without writing (a file that was already there, or one seeded when the
    /// record was first made for an existing app) has the hash only when the file was the
    /// template's content, since only then is it known what it started as.
    /// </summary>
    public sealed class ScaffoldRecord {
        /// <summary>The per-app OneJS state folder inside the working directory.</summary>
        public const string StateFolder = ".onejs";
        public const string FileName = "scaffold";

        readonly SortedDictionary<string, string> _given = new SortedDictionary<string, string>(StringComparer.Ordinal);

        /// <summary>True when a path was added since the record was read.</summary>
        public bool Changed { get; private set; }

        public static string PathIn(string workingDir) => Path.Combine(workingDir, StateFolder, FileName);

        /// <summary>The record in `workingDir`, or null when it has none.</summary>
        public static ScaffoldRecord Read(string workingDir) {
            var file = PathIn(workingDir);
            if (!File.Exists(file)) return null;
            var record = new ScaffoldRecord();
            foreach (var line in File.ReadAllLines(file)) {
                if (string.IsNullOrWhiteSpace(line)) continue;
                var tab = line.IndexOf('\t');
                var path = Normalize(tab < 0 ? line : line.Substring(0, tab));
                var hash = tab < 0 ? null : line.Substring(tab + 1).Trim();
                record._given[path] = string.IsNullOrEmpty(hash) ? null : hash;
            }
            return record;
        }

        public bool Has(string path) => _given.ContainsKey(Normalize(path));

        /// <summary>The hash of the content `path` was written with, or null when it was not written.</summary>
        public string HashOf(string path) => _given.TryGetValue(Normalize(path), out var hash) ? hash : null;

        /// <summary>Records `path`, once. A later add for the same path changes nothing.</summary>
        public void Add(string path, string hash) {
            path = Normalize(path);
            if (_given.ContainsKey(path)) return;
            _given[path] = hash;
            Changed = true;
        }

        /// <summary>
        /// Records `path` as written with the content `hash` stands for, replacing whatever the
        /// record said: what Restore does, since it writes the file again.
        /// </summary>
        public void Set(string path, string hash) {
            path = Normalize(path);
            if (_given.TryGetValue(path, out var had) && had == hash) return;
            _given[path] = hash;
            Changed = true;
        }

        public IEnumerable<string> Paths => _given.Keys;

        public void Write(string workingDir) {
            var sb = new StringBuilder();
            foreach (var kv in _given) {
                sb.Append(kv.Key);
                if (kv.Value != null) sb.Append('\t').Append(kv.Value);
                sb.Append('\n');
            }
            var file = PathIn(workingDir);
            Directory.CreateDirectory(Path.GetDirectoryName(file));
            File.WriteAllText(file, sb.ToString());
            Changed = false;
        }

        /// <summary>A short hash of file content, line endings ignored so a checkout that converts them still matches.</summary>
        public static string Hash(string content) {
            var bytes = Encoding.UTF8.GetBytes((content ?? "").Replace("\r\n", "\n"));
            using (var sha = SHA256.Create()) {
                return string.Concat(sha.ComputeHash(bytes).Take(8).Select(b => b.ToString("x2")));
            }
        }

        static string Normalize(string path) => path.Replace('\\', '/').Trim();
    }
}
