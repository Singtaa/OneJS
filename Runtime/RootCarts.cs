#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEngine;

namespace OneJS {
    /// <summary>
    /// The carts at the Unity project root: every <c>&lt;project&gt;/*/oj.json</c>, one level
    /// deep, found by the name in its oj.json (Specs/ROOT_CARTS.md, 5.1 and 5.2).
    ///
    /// A runner links to a cart by that name and never by path, because a folder outside
    /// Assets has no GUID and a path string breaks when the folder moves. The name is
    /// compared trimmed and ignoring case; two carts whose names compare equal are both
    /// refused, naming both paths, rather than one winning by scan order.
    ///
    /// Editor only. A player never scans: the build resolves each name once and ships
    /// the cart under its folder's name.
    /// </summary>
    public static class RootCarts {
        /// <summary>Folders the scan never looks in. Dot folders are skipped too.</summary>
        static readonly HashSet<string> Skipped = new HashSet<string>(StringComparer.OrdinalIgnoreCase) {
            "Assets", "Packages", "ProjectSettings", "Library", "Temp", "Logs", "UserSettings", "node_modules",
        };

        /// <summary>Whether the scan never looks in a root folder of this name, dot folders aside.</summary>
        public static bool IsSkipped(string folder) => Skipped.Contains(folder);

        /// <summary>Where the editor's build of a cart goes, under the project.</summary>
        public const string EditorBuildRoot = "Library/OneJS/Carts";

        /// <summary>Where a player build's bundle of a cart goes, under the project.</summary>
        public const string PlayerBuildRoot = "Assets/OneJS.Generated/Carts";

        /// <summary>Where a cart's files go in a player, under StreamingAssets.</summary>
        public const string PlayerFilesRoot = "onejs/carts";

        /// <summary>The oldest ojplay with <c>build --unity</c>.</summary>
        public const string MinOjplayVersion = "0.11.0";

        /// <summary>One folder at the root holding an oj.json.</summary>
        public sealed class Cart {
            /// <summary>The oj.json name, as written, trimmed. Null when the cart cannot be linked.</summary>
            public string Name;
            /// <summary>The folder's own name, which is the player's key.</summary>
            public string Folder;
            /// <summary>The folder, full path.</summary>
            public string FullPath;
            public string Version;
            public string Entry;
            /// <summary>Why nothing can link to it, or null when it can be linked.</summary>
            public string Problem;

            public bool Usable => Problem == null;
            public string OjJson => Folder + "/oj.json";
        }

        public enum Outcome { Found, Missing, Duplicate }

        /// <summary>A name looked up in a scan.</summary>
        public readonly struct Resolution {
            public readonly Outcome Outcome;
            /// <summary>The cart, when exactly one has the name.</summary>
            public readonly Cart Cart;
            /// <summary>Every cart with the name: one, none, or the duplicates.</summary>
            public readonly IReadOnlyList<Cart> Matches;
            /// <summary>For a missing name: the folder that last answered to it on this machine, now under another name.</summary>
            public readonly Cart RenamedTo;
            public readonly string Name;

            public Resolution(string name, Outcome outcome, Cart cart, IReadOnlyList<Cart> matches, Cart renamedTo) {
                Name = name;
                Outcome = outcome;
                Cart = cart;
                Matches = matches;
                RenamedTo = renamedTo;
            }

            /// <summary>
            /// The sentence the inspector, Play and a player build all say when the name does
            /// not resolve to one cart. Null when it does.
            /// </summary>
            public string Problem {
                get {
                    switch (Outcome) {
                        case Outcome.Missing:
                            return $"No cart named {Name} in this project.";
                        case Outcome.Duplicate:
                            return $"Two carts are named {Name}: {string.Join(" and ", Matches.Select(m => m.OjJson))}. " +
                                "A runner links by name, so it runs neither. Rename one in its oj.json.";
                        default:
                            return null;
                    }
                }
            }
        }

        static List<Cart> _cache;
        static double _cachedAt = double.NegativeInfinity;

        /// <summary>
        /// How long one scan answers for. Reading a few oj.json files is cheap, but the
        /// runner's paths are asked for several times a frame, and a scan per ask would
        /// read them hundreds of times a second. The inspector, Play and a player build
        /// each <see cref="Invalidate"/> first, so none of them acts on an old answer.
        /// </summary>
        const double MaxAgeSeconds = 1.0;

        static string _projectRoot;
        static string ProjectRoot => _projectRoot ??= Path.GetDirectoryName(Application.dataPath);

        /// <summary>Forgets the last scan, so the next question reads the disk.</summary>
        public static void Invalidate() => _cachedAt = double.NegativeInfinity;

        /// <summary>The carts at the root, usable or not, in folder order.</summary>
        public static IReadOnlyList<Cart> All {
            get {
                var now = UnityEditor.EditorApplication.timeSinceStartup;
                if (_cache == null || now - _cachedAt > MaxAgeSeconds) {
                    _cache = Scan(ProjectRoot);
                    _cachedAt = now;
                    RememberSeen(_cache);
                }
                return _cache;
            }
        }

        /// <summary>Reads every <c>&lt;root&gt;/*/oj.json</c>. Uncached; <see cref="All"/> is the cached form.</summary>
        public static List<Cart> Scan(string root) {
            var found = new List<Cart>();
            if (string.IsNullOrEmpty(root) || !Directory.Exists(root)) return found;
            string[] dirs;
            try { dirs = Directory.GetDirectories(root); } catch (IOException) { return found; } catch (UnauthorizedAccessException) { return found; }
            foreach (var dir in dirs.OrderBy(d => d, StringComparer.OrdinalIgnoreCase)) {
                var folder = Path.GetFileName(dir);
                if (folder.StartsWith(".") || Skipped.Contains(folder)) continue;
                var ojJson = Path.Combine(dir, "oj.json");
                if (!File.Exists(ojJson)) continue;
                found.Add(Read(folder, dir, ojJson));
            }
            return found;
        }

        [Serializable]
        class OjJson {
            public string name;
            public string version;
            public string entry;
        }

        /// <summary>
        /// The oj.json in `dir` read as the scan reads one, with `shown` as its folder in what
        /// it says. For a cart that is not at the root yet: Move to Project Root reads the
        /// name of the one in an app's ~ before moving it.
        /// </summary>
        public static Cart ReadAt(string dir, string shown) => Read(shown, dir, Path.Combine(dir, "oj.json"));

        static Cart Read(string folder, string dir, string ojJson) {
            var cart = new Cart { Folder = folder, FullPath = dir };
            string text;
            try {
                text = File.ReadAllText(ojJson);
            } catch (Exception e) when (e is IOException || e is UnauthorizedAccessException) {
                cart.Problem = $"{cart.OjJson} cannot be read ({e.Message}), so nothing can link to it.";
                return cart;
            }
            if (!text.TrimStart().StartsWith("{")) {
                cart.Problem = $"{cart.OjJson} is not a JSON object, so nothing can link to it.";
                return cart;
            }
            OjJson parsed;
            try {
                parsed = JsonUtility.FromJson<OjJson>(text);
            } catch (ArgumentException e) {
                cart.Problem = $"{cart.OjJson} is not valid JSON ({e.Message}), so nothing can link to it.";
                return cart;
            }
            var name = parsed?.name?.Trim();
            if (string.IsNullOrEmpty(name)) {
                cart.Problem = $"{cart.OjJson} has no \"name\", so nothing can link to it. " +
                    $"Add one, such as \"name\": \"{folder}\".";
                return cart;
            }
            cart.Name = name;
            cart.Version = parsed.version;
            cart.Entry = string.IsNullOrEmpty(parsed.entry) ? "index.tsx" : parsed.entry;
            return cart;
        }

        /// <summary>Two cart names are the same name: trimmed, composed, case ignored.</summary>
        public static bool SameName(string a, string b) => Key(a) == Key(b);

        static string Key(string name) =>
            (name ?? "").Trim().Normalize(NormalizationForm.FormC).ToLowerInvariant();

        /// <summary>Looks a name up in the current scan.</summary>
        public static Resolution Resolve(string name) => Resolve(name, All);

        /// <summary>Looks a name up in a given scan. Split out for tests.</summary>
        public static Resolution Resolve(string name, IReadOnlyList<Cart> carts) {
            var matches = carts.Where(c => c.Usable && SameName(c.Name, name)).ToList();
            if (matches.Count == 1) return new Resolution(name, Outcome.Found, matches[0], matches, null);
            if (matches.Count > 1) return new Resolution(name, Outcome.Duplicate, null, matches, null);
            return new Resolution(name, Outcome.Missing, null, matches, RenamedFrom(name, carts));
        }

        // Which folder last answered each name, on this machine

        /// <summary>
        /// The record that lets the inspector say a cart was renamed: name to folder, as
        /// the last scan here saw them. Library, so it is per machine and never committed.
        /// </summary>
        public const string SeenFile = "Library/OneJS/carts-seen.json";

        [Serializable]
        class Seen {
            public List<string> names = new List<string>();
            public List<string> folders = new List<string>();
        }

        static Dictionary<string, string> _seen;

        static Dictionary<string, string> LoadSeen() {
            if (_seen != null) return _seen;
            _seen = new Dictionary<string, string>();
            var file = Path.Combine(ProjectRoot, SeenFile);
            try {
                if (File.Exists(file)) {
                    var seen = JsonUtility.FromJson<Seen>(File.ReadAllText(file));
                    for (var i = 0; seen != null && i < Math.Min(seen.names.Count, seen.folders.Count); i++)
                        _seen[seen.names[i]] = seen.folders[i];
                }
            } catch (Exception e) when (e is IOException || e is ArgumentException || e is UnauthorizedAccessException) {
                // A damaged record only costs the rename hint.
            }
            return _seen;
        }

        static void RememberSeen(List<Cart> carts) {
            var seen = LoadSeen();
            var changed = false;
            foreach (var cart in carts) {
                if (!cart.Usable) continue;
                var key = Key(cart.Name);
                if (seen.TryGetValue(key, out var folder) && folder == cart.Folder) continue;
                seen[key] = cart.Folder;
                changed = true;
            }
            if (!changed) return;
            var record = new Seen();
            foreach (var kv in seen.OrderBy(kv => kv.Key, StringComparer.Ordinal)) {
                record.names.Add(kv.Key);
                record.folders.Add(kv.Value);
            }
            try {
                var file = Path.Combine(ProjectRoot, SeenFile);
                Directory.CreateDirectory(Path.GetDirectoryName(file));
                File.WriteAllText(file, JsonUtility.ToJson(record, true));
            } catch (Exception e) when (e is IOException || e is UnauthorizedAccessException) {
                // Only the rename hint depends on it.
            }
        }

        /// <summary>The cart now in the folder that last answered to `name`, when it answers to another name.</summary>
        static Cart RenamedFrom(string name, IReadOnlyList<Cart> carts) {
            if (!LoadSeen().TryGetValue(Key(name), out var folder)) return null;
            var now = carts.FirstOrDefault(c => string.Equals(c.Folder, folder, StringComparison.Ordinal));
            return now != null && now.Usable && !SameName(now.Name, name) ? now : null;
        }

        // Paths and tooling

        /// <summary>The editor build's folder for a cart: <c>Library/OneJS/Carts/&lt;folder&gt;</c>, full path.</summary>
        public static string EditorBuildDir(Cart cart) =>
            Path.Combine(ProjectRoot, EditorBuildRoot.Replace('/', Path.DirectorySeparatorChar), cart.Folder);

        /// <summary>The player build's folder for a cart, as an asset path.</summary>
        public static string PlayerBuildAssetDir(Cart cart) => PlayerBuildRoot + "/" + cart.Folder;

        /// <summary>The installed ojplay's version, or null when the cart has none.</summary>
        public static string InstalledOjplay(Cart cart) {
            var file = Path.Combine(cart.FullPath, "node_modules", "ojplay", "package.json");
            if (!File.Exists(file)) return null;
            try {
                var version = JsonUtility.FromJson<OjJson>(File.ReadAllText(file))?.version;
                return string.IsNullOrEmpty(version) ? null : version;
            } catch (Exception e) when (e is IOException || e is ArgumentException || e is UnauthorizedAccessException) {
                return null;
            }
        }

        /// <summary>Whether the cart's tooling is installed: its node_modules, with an ojplay that has the Unity target.</summary>
        public static bool IsSetUp(Cart cart) {
            if (!Directory.Exists(Path.Combine(cart.FullPath, "node_modules"))) return false;
            var version = InstalledOjplay(cart);
            return version != null && CompareVersions(version, MinOjplayVersion) >= 0;
        }

        /// <summary>Compares the numeric parts of two versions; a prerelease suffix is ignored.</summary>
        public static int CompareVersions(string a, string b) {
            int[] Parts(string v) => (v ?? "").Split('-', '+')[0].Split('.')
                .Select(p => int.TryParse(p, out var n) ? n : 0).Concat(new[] { 0, 0, 0 }).Take(3).ToArray();
            var x = Parts(a);
            var y = Parts(b);
            for (var i = 0; i < 3; i++) {
                if (x[i] != y[i]) return x[i].CompareTo(y[i]);
            }
            return 0;
        }
    }
}
#endif
