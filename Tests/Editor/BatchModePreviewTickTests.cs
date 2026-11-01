using NUnit.Framework;
using UnityEngine;

namespace OneJS.Tests.Editor {
    /// <summary>
    /// In Auto mode the OneJS overlay ticks the selected runner, else the one closest
    /// to the Scene view camera. A batch-mode editor has no Scene view, so with nothing
    /// selected no preview ticked there: a 31 Oct cold agent checking its HUD headlessly
    /// saw C# state changes never reach React. With no camera to measure from and
    /// nothing selected, every runner ticks.
    /// </summary>
    public class BatchModePreviewTickTests {
        [Test]
        public void EveryRunnerTicksWithNoSceneViewAndNothingSelected() {
            Assume.That(Application.isBatchMode, "only a batch-mode editor is sure to have no Scene view");
            UnityEditor.Selection.objects = new Object[0];
            Assert.IsNotNull(JSRunner.EditModeUpdateFilter, "the overlay registers the filter on load");
            var go = new GameObject("BatchTickProbe");
            go.SetActive(false);
            try {
                var runner = go.AddComponent<JSRunner>();
                Assert.IsTrue(JSRunner.EditModeUpdateFilter(runner), "an unselected runner with no Scene view ticks");
            } finally {
                Object.DestroyImmediate(go);
            }
        }
    }
}
