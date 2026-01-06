using UnityEngine;
using UnityEngine.SceneManagement;

namespace OneJS {
    /// <summary>
    /// Marker component for live reload cleanup.
    /// When live reload occurs, all root GameObjects after this one in the hierarchy are destroyed.
    /// This allows JavaScript code to create GameObjects that get cleaned up on reload.
    /// </summary>
    [AddComponentMenu("")] // Hide from Add Component menu
    public class Janitor : MonoBehaviour {
        bool _destroyed;

        /// <summary>
        /// Destroy all root GameObjects in the active scene that appear after this one in the hierarchy.
        /// </summary>
        public void Clean() {
            if (_destroyed) return;

            var scene = SceneManager.GetActiveScene();
            var rootObjects = scene.GetRootGameObjects();

            // Find our index
            int janitorIndex = -1;
            for (int i = 0; i < rootObjects.Length; i++) {
                if (rootObjects[i] == gameObject) {
                    janitorIndex = i;
                    break;
                }
            }

            if (janitorIndex < 0) return;

            // Destroy all objects after us
            int destroyed = 0;
            for (int i = rootObjects.Length - 1; i > janitorIndex; i--) {
                var obj = rootObjects[i];
                if (obj == null) continue;

                // Skip DontDestroyOnLoad objects (they won't be in the scene's root objects anyway, but just in case)
                if (obj.scene != scene) continue;

                Destroy(obj);
                destroyed++;
            }

            if (destroyed > 0) {
                Debug.Log($"[Janitor] Cleaned up {destroyed} GameObject(s)");
            }
        }

        void OnDestroy() {
            _destroyed = true;
        }

#if UNITY_EDITOR
        [ContextMenu("Clean Now")]
        void CleanNow() => Clean();
#endif
    }
}
