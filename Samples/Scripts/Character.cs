using System;
using System.Collections;
using UnityEngine;
using Random = UnityEngine.Random;

namespace OneJS.Samples {
    public class Character : MonoBehaviour {
        public float Health {
            get { return _health; }
            set {
                _health = value;
                OnHealthChanged?.Invoke(_health);
            }
        }

        public float MaxHealth {
            get { return _maxHealth; }
            set {
                _maxHealth = value;
                OnMaxHealthChanged?.Invoke(_maxHealth);
            }
        }

        public event Action<float> OnHealthChanged;
        public event Action<float> OnMaxHealthChanged;

        [SerializeField] float _health = 200f;
        [SerializeField] float _maxHealth = 200f;

        void Start() {
            StartCoroutine(ChangeHealthCo());
        }

        IEnumerator ChangeHealthCo() {
            var waitTime = Random.Range(1f, 3f);
            yield return new WaitForSeconds(waitTime);
            ChangeHealth();
        }

        void ChangeHealth() {
            _health = Random.Range(0, _maxHealth); // Mimic health change
            OnHealthChanged?.Invoke(_health);
            StartCoroutine(ChangeHealthCo());
        }
    }
}