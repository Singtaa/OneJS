using UnityEngine;

namespace OneJS.Utils {
    /// <summary>
    /// Version-portable wrappers for UnityEngine.Object find APIs. The FindObjectsSortMode
    /// overloads are obsolete on Unity 6.4+ (deprecation warnings that Unity turns into hard
    /// errors in later releases), but their parameterless replacements only exist on 6000.4+.
    /// </summary>
    public static class FindCompat {
        public static T[] FindObjectsByType<T>() where T : Object {
#if UNITY_6000_4_OR_NEWER
            return Object.FindObjectsByType<T>();
#else
            return Object.FindObjectsByType<T>(FindObjectsSortMode.None);
#endif
        }

        public static T[] FindObjectsByTypeIncludingInactive<T>() where T : Object {
#if UNITY_6000_4_OR_NEWER
            return Object.FindObjectsByType<T>(FindObjectsInactive.Include);
#else
            return Object.FindObjectsByType<T>(FindObjectsInactive.Include, FindObjectsSortMode.None);
#endif
        }
    }
}
