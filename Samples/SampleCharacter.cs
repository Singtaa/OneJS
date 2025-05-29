using System.Collections;
using UnityEngine;
using Random = UnityEngine.Random;

namespace OneJS.Samples {
    public partial class SampleCharacter : MonoBehaviour {
        [EventfulProperty] float _health = 200f;
        [EventfulProperty] float _maxHealth = 300f;
        [EventfulProperty] int _slotIndex = 0;

        void Start() {
            StartCoroutine(ChangeHealthCo());
            StartCoroutine(ChangeSlotIndexCo());
        }

        IEnumerator ChangeHealthCo() {
            var waitTime = Random.Range(1f, 5f);
            yield return new WaitForSeconds(waitTime);
            var hp = _health;
            while (Mathf.Abs(hp - _health) < _maxHealth * 0.2f) { // Roll a number that's  different from the current value by 0.2 * maxHealth
                hp = Random.Range(0, _maxHealth);
            }
            _health = hp;
            OnHealthChanged?.Invoke(_health);
            StartCoroutine(ChangeHealthCo());
        }

        IEnumerator ChangeSlotIndexCo() {
            var waitTime = Random.Range(1f, 5f);
            yield return new WaitForSeconds(waitTime);
            var index = _slotIndex;
            while (index == _slotIndex) { // Roll a number that's different from the current value
                index = Random.Range(0, 3);
            }
            _slotIndex = index;
            OnSlotIndexChanged?.Invoke(_slotIndex);
            StartCoroutine(ChangeSlotIndexCo());
        }
    }
}