using System;
using OneJS;
using UnityEditor;
using UnityEngine;


[CustomEditor(typeof(Bundler))]
[CanEditMultipleObjects]
public class BundlerEditor : Editor {
    // SerializedProperty _subDirectoriesToIgnore;

    SerializedProperty _scriptsBundleZip;
    SerializedProperty _scriptLibZip;
    SerializedProperty _samplesZip;
    SerializedProperty _vscodeSettings;
    SerializedProperty _tsconfig;

    SerializedProperty _ignoreList;
    SerializedProperty _excludeTS;
    SerializedProperty _uglify;
    SerializedProperty _extractSamples;

    bool showAssets;

    void OnEnable() {
        // _subDirectoriesToIgnore = serializedObject.FindProperty("_subDirectoriesToIgnore");

        _scriptsBundleZip = serializedObject.FindProperty("_scriptsBundleZip");
        _scriptLibZip = serializedObject.FindProperty("_scriptLibZip");
        _samplesZip = serializedObject.FindProperty("_samplesZip");
        _vscodeSettings = serializedObject.FindProperty("_vscodeSettings");
        _tsconfig = serializedObject.FindProperty("_tsconfig");

        _ignoreList = serializedObject.FindProperty("_ignoreList");
        _excludeTS = serializedObject.FindProperty("_excludeTS");
        _uglify = serializedObject.FindProperty("_uglify");
        _extractSamples = serializedObject.FindProperty("_extractSamples");
    }

    public override void OnInspectorGUI() {
        var bundler = target as Bundler;
        serializedObject.Update();
        // EditorGUILayout.PropertyField(_subDirectoriesToIgnore);
        EditorGUILayout.PropertyField(_ignoreList);
        EditorGUILayout.PropertyField(_excludeTS);
        EditorGUILayout.PropertyField(_uglify);
        EditorGUILayout.PropertyField(_extractSamples);
        showAssets = EditorGUILayout.Foldout(showAssets, "ASSETS", true);
        if (showAssets) {
            EditorGUILayout.PropertyField(_scriptsBundleZip, new GUIContent("ScriptsBundle.zip"));
            EditorGUILayout.PropertyField(_scriptLibZip, new GUIContent("ScriptLib.zip"));
            EditorGUILayout.PropertyField(_samplesZip, new GUIContent("Samples.zip"));
            EditorGUILayout.PropertyField(_vscodeSettings, new GUIContent("VSCode Settings"));
            EditorGUILayout.PropertyField(_tsconfig, new GUIContent("tsconfig.json"));
        }
        if (GUILayout.Button(
                new GUIContent("Bundle Scripts for Deployment", "This is done automatically when you build your player."),
                GUI.skin.button, GUILayout.Height(30))) {
            bundler.PackageScriptsForBuild();
        }
        // if (GUILayout.Button(
        //         new GUIContent("Zero Out ScriptsBundle.zip", "Remember to do this before building your player."),
        //         GUI.skin.button)) {
        //     bundler.PackageScriptsForBuild();
        // }
        EditorGUILayout.BeginHorizontal ();
        if (GUILayout.Button(
                new GUIContent("Open WorkingDir", "Open your current OneJS Working Directory in Explorer."),
                GUI.skin.button, GUILayout.Height(30))) {
            bundler.OpenWorkingDir();
        }
        if (GUILayout.Button(
                new GUIContent("Extract ScriptLib", "Flush the ScriptLib folder."),
                GUI.skin.button, GUILayout.Height(30))) {
            bundler.ExtractScriptLibFolder();
        }
        if (GUILayout.Button(
                new GUIContent("Extract Samples", "Flush the Samples folder."),
                GUI.skin.button, GUILayout.Height(30))) {
            bundler.ExtractSamplesFolder();
        }
        EditorGUILayout.EndHorizontal ();
        
        serializedObject.ApplyModifiedProperties();
    }
}