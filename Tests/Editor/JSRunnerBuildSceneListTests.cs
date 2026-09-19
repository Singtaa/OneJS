using System;
using System.Reflection;
using NUnit.Framework;
using OneJS.Editor;
using UnityEditor;

namespace OneJS.Tests.Editor {
    /// <summary>
    /// Which scenes the build processor walks.
    ///
    /// The processor used to read EditorBuildSettings, so a build that passed its
    /// own BuildPlayerOptions.scenes (the documented command line way, and what CI
    /// does) had its apps chosen from one list while the player shipped another. A
    /// real StandaloneWindows64 build with Build Settings holding SceneA and
    /// BuildPlayerOptions passing SceneB shipped SceneB alongside SceneA's asset,
    /// and SceneB's runner reached the player with no bundle at all.
    ///
    /// These cases cover the selection: which list wins, that it is spent after one
    /// read, and what the fallback keeps. What they cannot cover is the half that
    /// only a player build reaches, namely that Unity calls PrepareForBuild before
    /// OnPreprocessBuild and hands it the list the build is really using. Nothing
    /// in EditMode drives a BuildPlayerContext, so a green run here is not evidence
    /// of that and must not be read as any.
    /// </summary>
    [TestFixture]
    public class JSRunnerBuildSceneListTests {
        MethodInfo _resolve;
        string _sessionKey;
        string _savedRecorded;

        [SetUp]
        public void SetUp() {
            _resolve = typeof(JSRunnerBuildProcessor).GetMethod(
                "ResolveBuildScenes",
                BindingFlags.NonPublic | BindingFlags.Static,
                null, new[] { typeof(EditorBuildSettingsScene[]) }, null);
            Assert.IsNotNull(_resolve,
                "JSRunnerBuildProcessor.ResolveBuildScenes(EditorBuildSettingsScene[]) was not found via " +
                "reflection: these tests are out of sync with the implementation.");

            var keyField = typeof(JSRunnerBuildProcessor).GetField(
                "RequestedScenesKey", BindingFlags.NonPublic | BindingFlags.Static);
            Assert.IsNotNull(keyField,
                "JSRunnerBuildProcessor.RequestedScenesKey was not found via reflection: " +
                "these tests are out of sync with the implementation.");
            _sessionKey = (string)keyField.GetRawConstantValue();

            // SessionState outlives a test, and a build running in this editor
            // session writes the same key, so whatever is there is put back.
            _savedRecorded = SessionState.GetString(_sessionKey, "");
            SessionState.EraseString(_sessionKey);
        }

        [TearDown]
        public void TearDown() {
            if (string.IsNullOrEmpty(_savedRecorded)) SessionState.EraseString(_sessionKey);
            else SessionState.SetString(_sessionKey, _savedRecorded);
        }

        string[] Resolve(params EditorBuildSettingsScene[] buildSettings) {
            return (string[])_resolve.Invoke(null, new object[] { buildSettings });
        }

        /// <summary>
        /// Writes the key the way PrepareForBuild does, leading newline included,
        /// so that recording an empty list stays distinct from recording nothing.
        /// </summary>
        void Record(params string[] scenes) {
            SessionState.SetString(_sessionKey, "\n" + string.Join("\n", scenes));
        }

        static EditorBuildSettingsScene Scene(string path, bool enabled) {
            return new EditorBuildSettingsScene(path, enabled);
        }

        [Test]
        public void RecordedSceneList_WinsOverBuildSettings() {
            Record("Assets/Scenes/SceneB.unity");

            var resolved = Resolve(Scene("Assets/Scenes/SceneA.unity", true));

            Assert.AreEqual(new[] { "Assets/Scenes/SceneB.unity" }, resolved,
                "The build passed SceneB, so SceneB's app is the one whose bundle and assets ship. " +
                "Falling back to Build Settings here is the bug: the player gets one scene's assets " +
                "and another scene's content.");
        }

        [Test]
        public void RecordedSceneList_KeepsOrderAndEveryEntry() {
            Record("Assets/Scenes/One.unity", "Assets/Scenes/Two.unity", "Assets/Scenes/Three.unity");

            Assert.AreEqual(
                new[] { "Assets/Scenes/One.unity", "Assets/Scenes/Two.unity", "Assets/Scenes/Three.unity" },
                Resolve(Scene("Assets/Scenes/Unrelated.unity", true)));
        }

        [Test]
        public void RecordedSceneList_IsSpentAfterOneRead() {
            Record("Assets/Scenes/SceneB.unity");
            Resolve(Scene("Assets/Scenes/SceneA.unity", true));

            var second = Resolve(Scene("Assets/Scenes/SceneA.unity", true));

            Assert.AreEqual(new[] { "Assets/Scenes/SceneA.unity" }, second,
                "A build abandoned after PrepareForBuild leaves a list behind. A later build that " +
                "never went through PrepareForBuild must not walk it.");
        }

        [Test]
        public void NeverRecorded_IsNotTheSameAsRecordedEmpty() {
            // The distinction the leading newline exists to preserve. Nothing
            // recorded means PrepareForBuild never ran, and Build Settings is then
            // the only list there is.
            var neverRecorded = Resolve(Scene("Assets/Scenes/SceneA.unity", true));

            Record();
            var recordedEmpty = Resolve(Scene("Assets/Scenes/SceneA.unity", true));

            Assert.AreEqual(new[] { "Assets/Scenes/SceneA.unity" }, neverRecorded);
            Assert.IsEmpty(recordedEmpty);
        }

        [Test]
        public void NoRecordedList_FallsBackToEnabledBuildSettingsScenes() {
            var resolved = Resolve(
                Scene("Assets/Scenes/SceneA.unity", true),
                Scene("Assets/Scenes/SceneB.unity", true));

            Assert.AreEqual(new[] { "Assets/Scenes/SceneA.unity", "Assets/Scenes/SceneB.unity" }, resolved,
                "A build that passes no scene list ships Build Settings, so the processor walks it too.");
        }

        [Test]
        public void NoRecordedList_SkipsDisabledBuildSettingsScenes() {
            var resolved = Resolve(
                Scene("Assets/Scenes/SceneA.unity", true),
                Scene("Assets/Scenes/Disabled.unity", false));

            Assert.AreEqual(new[] { "Assets/Scenes/SceneA.unity" }, resolved,
                "A scene unticked in Build Settings is not in the player, so its app ships nothing.");
        }

        [Test]
        public void EmptyRecordedList_ResolvesEmptyBecauseUnityBuildsTheOpenScene() {
            // A build that passes null or an empty BuildPlayerOptions.scenes builds
            // whatever scene is open, NOT the Build Settings list. Watched: a build
            // passing null with SceneB open and SceneA ticked in Build Settings put
            // SceneB in the player, and reading Build Settings here shipped SceneA's
            // asset alongside it. Resolving to nothing is what sends
            // OnPreprocessBuild to the open scene instead.
            Record();

            Assert.IsEmpty(Resolve(Scene("Assets/Scenes/SceneA.unity", true)),
                "An empty passed list is not an absent one, and must not fall back to Build Settings.");
        }

        [Test]
        public void NothingAnywhere_ResolvesEmptySoTheOpenSceneIsUsed() {
            Assert.IsEmpty(Resolve(new EditorBuildSettingsScene[0]),
                "An empty result is what makes OnPreprocessBuild fall back to the active scene.");
        }
    }
}
