using System.IO;
using UnityEngine;
using UnityEngine.UIElements;

/// <summary>
/// MonoBehaviour that runs JavaScript from a file path under the project root.
/// Requires a UIDocument component on the same GameObject for UI rendering.
/// </summary>
[RequireComponent(typeof(UIDocument))]
public class JSRunner : MonoBehaviour {
    const string DefaultEntryContent = "console.log(\"Hola, OneJS!\");\n";

    [SerializeField] string _workingDir = "App";
    [SerializeField] string _entryFile = "@outputs/esbuild/app.js";

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

        _bridge.Eval(code, _entryFile);

        // Execute pending Promise jobs immediately to allow React's first render
        _bridge.Context.ExecutePendingJobs();
    }

    void Update() {
        _bridge?.Tick();
    }

    void OnDestroy() {
        _bridge?.Dispose();
        _bridge = null;
    }
}
