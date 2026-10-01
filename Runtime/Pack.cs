using System;
using System.Collections.Generic;
using UnityEngine;

namespace OneJS {
    /// <summary>
    /// A file entry for pack extraction.
    /// Path is relative to the pack folder (e.g., "index.tsx" or "components/Button.tsx").
    /// </summary>
    [Serializable]
    public class PackFileEntry {
        [Tooltip("Target path relative to pack folder")]
        public string path;
        [Tooltip("TextAsset containing the file content")]
        public TextAsset content;
    }

    /// <summary>
    /// An object entry accessible via __pack(path) at runtime.
    /// Key is the property name, Value is any UnityEngine.Object.
    /// </summary>
    [Serializable]
    public class PackObjectEntry {
        [Tooltip("Property name accessible via __pack('slug').{key} (e.g., 'config' becomes __pack('myPack').config)")]
        public string key;
        [Tooltip("Any Unity object to expose to JavaScript")]
        public UnityEngine.Object value;
    }

    /// <summary>The old name of <see cref="PackFileEntry"/>.</summary>
    [Obsolete("CartridgeFileEntry is now PackFileEntry.")]
    [Serializable]
    public class CartridgeFileEntry : PackFileEntry { }

    /// <summary>The old name of <see cref="PackObjectEntry"/>.</summary>
    [Obsolete("CartridgeObjectEntry is now PackObjectEntry.")]
    [Serializable]
    public class CartridgeObjectEntry : PackObjectEntry { }

    /// <summary>
    /// A Pack bundles reusable UI components/utilities as a ScriptableObject.
    /// Can be dragged onto JSRunner to auto-extract files at build time and inject objects at runtime.
    ///
    /// Files are extracted to: {WorkingDir}/@packs/{slug}/ (no namespace)
    /// Files are extracted to: {WorkingDir}/@packs/@{namespace}/{slug}/ (with namespace)
    /// A runner made before the rename uses @cartridges instead (see JSRunner.PackFolder).
    /// Pack is accessible via: __pack('slug') or __pack('@namespace/slug')
    /// </summary>
    [CreateAssetMenu(fileName = "NewPack", menuName = "OneJS/Pack", order = 100)]
    public class Pack : ScriptableObject {
        [Tooltip("Optional namespace for organizing packs (e.g., 'myCompany' -> @packs/@myCompany/{slug})")]
        [SerializeField] string _namespace;

        [Tooltip("Identifier used for folder name and JS access (e.g., 'colorPicker' -> __pack('colorPicker'))")]
        [SerializeField] string _slug;

        [Tooltip("Human-readable display name")]
        [SerializeField] string _displayName;

        [Tooltip("Description of what this pack provides")]
        [TextArea(2, 4)]
        [SerializeField] string _description;

        [Tooltip("Optional content version (e.g., '1.0.0'). Recorded on extraction so the editor can flag extracted files as outdated after the pack changes. Bump it whenever the pack's files change.")]
        [SerializeField] string _version;

        [Tooltip("Files to extract to @packs/{slug}/ or @packs/@{namespace}/{slug}/")]
        [PairDrawer("←")]
        [SerializeField] List<PackFileEntry> _files = new List<PackFileEntry>();

        [Tooltip("Unity objects accessible via __pack('slug').{key}")]
        [PairDrawer("→")]
        [SerializeField] List<PackObjectEntry> _objects = new List<PackObjectEntry>();

        // Public API
        public string Namespace => _namespace;
        public string Slug => _slug;
        public string DisplayName => string.IsNullOrEmpty(_displayName) ? _slug : _displayName;
        public string Description => _description;
        public string Version => _version;
        public IReadOnlyList<PackFileEntry> Files => _files;
        public IReadOnlyList<PackObjectEntry> Objects => _objects;

        /// <summary>
        /// Gets the relative path from the runner's pack folder to this pack's folder.
        /// Returns "@{namespace}/{slug}" if namespace is set, otherwise just "{slug}".
        /// </summary>
        public string RelativePath {
            get {
                if (string.IsNullOrEmpty(_namespace)) return _slug;
                return $"@{_namespace}/{_slug}";
            }
        }
    }
}
