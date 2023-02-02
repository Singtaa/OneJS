using UnityEngine;
using System;
using System.Collections;

namespace OneJS.Utils {
    public class CoroutineUtil : MonoBehaviour {
        public static CoroutineUtil Instance => instance;
        static CoroutineUtil instance;

        void Awake() {
            instance = this;
        }

        public static void Start(IEnumerator routine) {
            instance.StartCoroutine(routine);
        }

        public static void Stop(IEnumerator routine) {
            instance.StopCoroutine(routine);
        }

        public static void StopAll() {
            instance.StopAllCoroutines();
        }

        /**
         * CAUTION: Chaining may lead to mem leak issue with Jint reloading
        * Usage: StartCoroutine(CoroutineUtils.Chain(...))
        * For example:
        *     StartCoroutine(CoroutineUtils.Chain(
        *         CoroutineUtils.Do(() => Debug.Log("A")),
        *         CoroutineUtils.WaitForSeconds(2),
        *         CoroutineUtils.Do(() => Debug.Log("B"))));
        */
        public static IEnumerator Chain(params IEnumerator[] actions) {
            foreach (IEnumerator action in actions) { // <- this foreach may be source of mem leaks
                yield return instance.StartCoroutine(action);
            }
        }

        /**
        * Usage: StartCoroutine(CoroutineUtils.DelaySeconds(action, delay))
        * For example:
        *     StartCoroutine(CoroutineUtils.DelaySeconds(
        *         () => DebugUtils.Log("2 seconds past"),
        *         2);
        */
        public static IEnumerator DelaySeconds(Action action, float delay) {
            yield return new WaitForSeconds(delay);
            action();
        }

        public static IEnumerator DelayFrames(Action action, int delay) {
            for (int i = 0; i < delay; i++) {
                yield return new WaitForEndOfFrame();
            }
            action();
        }

        public static IEnumerator EndOfFrame(Action action) {
            yield return new WaitForEndOfFrame();
            action();
        }

        public static IEnumerator WaitForSeconds(float t) {
            yield return new WaitForSeconds(t);
        }

        public static IEnumerator WaitForSeconds(float t, Action action) {
            yield return new WaitForSeconds(t);
            action();
        }

        public static IEnumerator WaitForFrames(int t) {
            for (int i = 0; i < t; i++) {
                yield return new WaitForEndOfFrame();
            }
        }

        public static IEnumerator Do(Action action) {
            action();
            yield return 0;
        }
    }
}