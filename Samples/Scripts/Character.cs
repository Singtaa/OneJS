using System.Collections;
using UnityEngine;
using Random = UnityEngine.Random;

namespace OneJS.Samples {
    public partial class Character : MonoBehaviour {
        [SerializeField, EventfulProperty] float _health = 200f;
        [SerializeField, EventfulProperty] float _maxHealth = 200f;

        void Start() {
            StartCoroutine(ChangeHealthCo());
        }

        IEnumerator ChangeHealthCo() {
            var waitTime = Random.Range(1f, 3f);
            yield return new WaitForSeconds(waitTime);
            ChangeHealth();
        }

        void ChangeHealth() {
            Health = Random.Range(0, _maxHealth); // Mimic health change
            StartCoroutine(ChangeHealthCo());
        }
    }
}
