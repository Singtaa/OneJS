using UnityEngine;

/// <summary>
/// ScriptableObject stored inside a JSRunner's target project folder.
/// The folder path is derived from this asset's path in the Editor.
/// Created automatically on first project init; referenced by JSRunner.
/// </summary>
public class ProjectConfig : ScriptableObject {
    [SerializeField, HideInInspector] string _instanceId;

    /// <summary>
    /// Instance ID of the JSRunner that owns this config (for debug/inspector).
    /// </summary>
    public string InstanceId => _instanceId;

    internal void SetInstanceId(string id) {
        _instanceId = id;
    }
}
