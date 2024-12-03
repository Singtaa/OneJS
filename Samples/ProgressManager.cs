using System;
using System.Collections;
using UnityEngine;
using Random = UnityEngine.Random;

namespace OneJS.Samples {
    public class ProgressManager : MonoBehaviour {
        public float Progress => _progress;

        public event Action<float> OnProgressChanged;

        float _progress = 1f;

        void Start() {
            StartCoroutine(ChangeProgressCo());
        }

        public void SetProgress(float progress) {
            _progress = progress;
            OnProgressChanged?.Invoke(_progress);
        }

        IEnumerator ChangeProgressCo() {
            var waitTime = Random.Range(1f, 5f); // Wait for a random time
            yield return new WaitForSeconds(waitTime);
            ChangeProgress();
            StartCoroutine(ChangeProgressCo()); // Repeat
        }

        void ChangeProgress() {
            var p = Random.Range(0, 1f);
            while (Mathf.Abs(p - _progress) < 0.2f) { // Roll a number that's fairly different from the current value
                p = Random.Range(0, 1f);
            }
            _progress = p;
            OnProgressChanged?.Invoke(_progress);
        }
    }
}