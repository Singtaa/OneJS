using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UIElements;

namespace OneJS.Tests.Editor {
    /// <summary>
    /// Regenerates the assets BuildValidationScene depends on and wires them
    /// up: a PanelSettings in Tests/BuildValidation/TestApp/ (the folder is a
    /// valid JSRunner project because app.js.txt sits beside it) assigned to
    /// the scene's runner. Run headless with the project closed:
    ///   unity run . -- -executeMethod OneJS.Tests.Editor.BuildValidationSceneSetup.Configure
    /// Idempotent: safe to run again after the scene or assets change.
    /// </summary>
    public static class BuildValidationSceneSetup {
        const string ScenePath = "Assets/Singtaa/OneJS/Tests/BuildValidation/BuildValidationScene.unity";
        const string AppDir = "Assets/Singtaa/OneJS/Tests/BuildValidation/TestApp";
        const string PanelSettingsPath = AppDir + "/PanelSettings.asset";

        public static void Configure() {
            if (!Directory.Exists(AppDir)) {
                Directory.CreateDirectory(AppDir);
                AssetDatabase.Refresh();
            }

            // Open the scene BEFORE loading the asset. OpenScene can trigger an
            // import pass that reimports assets, which destroys the native side
            // of any already-loaded object: a PanelSettings loaded before
            // OpenScene arrived at the assignment as a fake-null and serialized
            // as {fileID: 0}. That cost a debugging session; keep this order.
            var scene = EditorSceneManager.OpenScene(ScenePath);
            JSRunner runner = null;
            foreach (var root in scene.GetRootGameObjects()) {
                runner = root.GetComponentInChildren<JSRunner>(true);
                if (runner != null) break;
            }
            if (runner == null) {
                Debug.LogError("[BuildValidationSceneSetup] No JSRunner in the scene.");
                EditorApplication.Exit(1);
                return;
            }

            var ps = AssetDatabase.LoadAssetAtPath<PanelSettings>(PanelSettingsPath);
            if (ps == null) {
                ps = ScriptableObject.CreateInstance<PanelSettings>();
                AssetDatabase.CreateAsset(ps, PanelSettingsPath);
                AssetDatabase.SaveAssets();
                Debug.Log($"[BuildValidationSceneSetup] Created {PanelSettingsPath}");
            }

            var so = new SerializedObject(runner);
            so.FindProperty("_panelSettings").objectReferenceValue = ps;
            so.ApplyModifiedPropertiesWithoutUndo();
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);

            bool valid = runner.IsPanelSettingsInValidProjectFolder();
            Debug.Log($"[BuildValidationSceneSetup] Assigned {PanelSettingsPath} to the scene runner. " +
                $"Valid project folder: {valid}");
            if (!valid) {
                Debug.LogError("[BuildValidationSceneSetup] Assignment did not produce a valid project folder.");
                EditorApplication.Exit(1);
            }
        }
    }
}
