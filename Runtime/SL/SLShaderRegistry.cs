using System;
using System.Collections.Generic;
using UnityEngine;

namespace OneJS.SL {
    /// <summary>
    /// Every shader generated from a program, by program hash, as a Resources
    /// asset a player can load.
    ///
    /// This is how a native player gets its compiled shaders. The generated
    /// shaders are ordinary assets outside Resources, and a player build packs
    /// an asset only when something it ships references it. `Shader.Find` in a
    /// player sees only shaders the build packed, so before this existed it
    /// found nothing and every program drew on the VM. The registry is that
    /// reference: it lives in a Resources folder, so the build packs it, and it
    /// holds each shader, so the build packs those too.
    ///
    /// Written by the build (<c>SLShaderBuildStep</c> in the editor assembly)
    /// from every `*.sl.json` manifest in the project, never by hand.
    /// </summary>
    public class SLShaderRegistry : ScriptableObject {
        /// <summary>Where the build writes it, under a Resources folder.</summary>
        public const string ResourcePath = "OneJS/SLShaderRegistry";

        [Serializable]
        public struct Entry {
            public string hash;
            public Shader shader;
        }

        [SerializeField] Entry[] _entries = new Entry[0];

        Dictionary<string, Shader> _byHash;

        public int Count => _entries.Length;

        public IReadOnlyList<Entry> Entries => _entries;

        /// <summary>The shader generated from this program, or null when the build had none.</summary>
        public Shader Find(string hash) {
            if (string.IsNullOrEmpty(hash)) return null;
            if (_byHash == null) {
                _byHash = new Dictionary<string, Shader>(_entries.Length);
                foreach (var e in _entries) {
                    if (!string.IsNullOrEmpty(e.hash) && e.shader != null) _byHash[e.hash] = e.shader;
                }
            }
            return _byHash.TryGetValue(hash, out var s) ? s : null;
        }

        /// <summary>Replaces the contents. For the build step, which sorts them so the asset does not churn.</summary>
        public void SetEntries(Entry[] entries) {
            _entries = entries ?? new Entry[0];
            _byHash = null;
        }
    }
}
