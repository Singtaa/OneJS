using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// A file entry for cartridge extraction.
/// Path is relative to the cartridge folder (e.g., "index.tsx" or "components/Button.tsx").
/// </summary>
[Serializable]
public class CartridgeFileEntry {
    [Tooltip("Target path relative to cartridge folder")]
    public string path;
    [Tooltip("TextAsset containing the file content")]
    public TextAsset content;
}

/// <summary>
/// An object entry to be injected as a JavaScript global under __cartridges.{slug}.
/// Key is the property name, Value is any UnityEngine.Object.
/// </summary>
[Serializable]
public class CartridgeObjectEntry {
    [Tooltip("Property name under __cartridges.{slug} (e.g., 'config' becomes __cartridges.myCartridge.config)")]
    public string key;
    [Tooltip("Any Unity object to expose to JavaScript")]
    public UnityEngine.Object value;
}

/// <summary>
/// A UI Cartridge bundles reusable UI components/utilities as a ScriptableObject.
/// Can be dragged onto JSRunner to auto-extract files at build time and inject objects at runtime.
///
/// Files are extracted to: {WorkingDir}/@cartridges/{slug}/
/// Objects are injected as: __cartridges.{slug}.{key}
/// </summary>
[CreateAssetMenu(fileName = "NewCartridge", menuName = "OneJS/UI Cartridge", order = 100)]
public class UICartridge : ScriptableObject {
    [Tooltip("Identifier used for folder name and JS access (e.g., 'colorPicker' -> __cartridges.colorPicker)")]
    [SerializeField] string _slug;

    [Tooltip("Human-readable display name")]
    [SerializeField] string _displayName;

    [Tooltip("Description of what this cartridge provides")]
    [TextArea(2, 4)]
    [SerializeField] string _description;

    [Tooltip("Files to extract to @cartridges/{slug}/")]
    [PairDrawer("←")]
    [SerializeField] List<CartridgeFileEntry> _files = new List<CartridgeFileEntry>();

    [Tooltip("Unity objects to inject as __cartridges.{slug}.{key}")]
    [PairDrawer("→")]
    [SerializeField] List<CartridgeObjectEntry> _objects = new List<CartridgeObjectEntry>();

    // Public API
    public string Slug => _slug;
    public string DisplayName => string.IsNullOrEmpty(_displayName) ? _slug : _displayName;
    public string Description => _description;
    public IReadOnlyList<CartridgeFileEntry> Files => _files;
    public IReadOnlyList<CartridgeObjectEntry> Objects => _objects;
}
