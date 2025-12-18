using System.IO;
using UnityEngine;
using UnityEngine.UIElements;
#if UNITY_WEBGL && !UNITY_EDITOR
using System.Collections;
using UnityEngine.Networking;
#endif

/// <summary>
/// MonoBehaviour that runs JavaScript from a file path under the project root.
/// Requires a UIDocument component on the same GameObject for UI rendering.
///
/// Platform behavior:
/// - Editor/Standalone: Loads JS from filesystem (App/@outputs/esbuild/app.js)
/// - WebGL: Loads from StreamingAssets or uses embedded TextAsset
/// </summary>
[RequireComponent(typeof(UIDocument))]
public class JSRunner : MonoBehaviour {
    const string DefaultEntryContent = "console.log(\"Hola, OneJS!\");\n";

    [SerializeField] string _workingDir = "App";
    [SerializeField] string _entryFile = "@outputs/esbuild/app.js";

    [Header("WebGL")]
    [Tooltip("For WebGL: TextAsset containing the bundled JS. If set, this is used instead of loading from StreamingAssets.")]
    [SerializeField] TextAsset _embeddedScript;
    [Tooltip("For WebGL: Path relative to StreamingAssets (e.g., 'app.js'). Used if Embedded Script is not set.")]
    [SerializeField] string _streamingAssetsPath = "app.js";

    QuickJSUIBridge _bridge;
    UIDocument _uiDocument;

    public string WorkingDir {
        get => _workingDir;
        set => _workingDir = value;
    }

    public string EntryFile {
        get => _entryFile;
        set => _entryFile = value;
    }

    public QuickJSUIBridge Bridge => _bridge;

    string ProjectRoot {
        get {
#if UNITY_EDITOR
            return Path.GetDirectoryName(Application.dataPath);
#else
            return Application.dataPath;
#endif
        }
    }

    string WorkingDirFullPath => Path.Combine(ProjectRoot, _workingDir);
    string EntryFileFullPath => Path.Combine(WorkingDirFullPath, _entryFile);

    void Start() {
        _uiDocument = GetComponent<UIDocument>();
        if (_uiDocument == null || _uiDocument.rootVisualElement == null) {
            Debug.LogError("[JSRunner] UIDocument or rootVisualElement is null");
            return;
        }

        _bridge = new QuickJSUIBridge(_uiDocument.rootVisualElement);

        // Expose the root element to JS as globalThis.__root
        var rootHandle = QuickJSNative.RegisterObject(_uiDocument.rootVisualElement);
        _bridge.Eval($"globalThis.__root = __csHelpers.wrapObject('UnityEngine.UIElements.VisualElement', {rootHandle})");

#if UNITY_WEBGL && !UNITY_EDITOR
        // WebGL: Load from embedded TextAsset or StreamingAssets
        if (_embeddedScript != null) {
            RunScript(_embeddedScript.text, "embedded.js");
        } else {
            StartCoroutine(LoadFromStreamingAssets());
        }
#else
        // Editor/Standalone: Load from filesystem
        var entryPath = EntryFileFullPath;
        var entryDir = Path.GetDirectoryName(entryPath);

        if (!Directory.Exists(entryDir)) {
            Directory.CreateDirectory(entryDir);
        }

        string code;
        if (File.Exists(entryPath)) {
            code = File.ReadAllText(entryPath);
        } else {
            code = DefaultEntryContent;
            File.WriteAllText(entryPath, code);
            Debug.Log($"[JSRunner] Created default entry file at: {entryPath}");
        }

        RunScript(code, _entryFile);
#endif
    }

    void RunScript(string code, string filename) {
        _bridge.Eval(code, filename);
        // Execute pending Promise jobs immediately to allow React's first render
        _bridge.Context.ExecutePendingJobs();
    }

#if UNITY_WEBGL && !UNITY_EDITOR
    IEnumerator LoadFromStreamingAssets() {
        var path = Path.Combine(Application.streamingAssetsPath, _streamingAssetsPath);
        using (var request = UnityWebRequest.Get(path)) {
            yield return request.SendWebRequest();

            if (request.result == UnityWebRequest.Result.Success) {
                RunScript(request.downloadHandler.text, _streamingAssetsPath);
            } else {
                Debug.LogError($"[JSRunner] Failed to load JS from StreamingAssets: {request.error}");
                // Fallback to default content
                RunScript(DefaultEntryContent, "default.js");
            }
        }
    }
#endif

    void Update() {
        _bridge?.Tick();
    }

    void OnDestroy() {
        _bridge?.Dispose();
        _bridge = null;
    }
}
