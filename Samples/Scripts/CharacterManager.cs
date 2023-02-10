using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Random = UnityEngine.Random;

namespace OneJS.Samples {
    public class CharacterManager : MonoBehaviour {
        public Character[] CharactersArray => _characters.ToArray();

        public event Action<Character[]> OnCharactersArrayChanged;

        List<Character> _characters;

        void Awake() {
            _characters = new List<Character>();
            ResetCharactersArray();
        }

        void Start() {
            StartCoroutine(ChangeCharactersArrayCo());
        }

        IEnumerator ChangeCharactersArrayCo() {
            var waitTime = Random.Range(6f, 20f); // Change characters array every few seconds
            yield return new WaitForSeconds(waitTime);
            ChangeCharactersArray();
        }

        void ChangeCharactersArray() {
            ResetCharactersArray();
            StartCoroutine(ChangeCharactersArrayCo());
        }

        void ResetCharactersArray() {
            // Mimic Characters array change
            var characterCount = Random.Range(1, 10);
            if (characterCount > _characters.Count) {
                print("Adding " + (characterCount - _characters.Count) + " characters");
                for (int i = _characters.Count; i < characterCount; i++) {
                    var go = new GameObject("Character " + i);
                    var character = go.AddComponent<Character>();
                    character.MaxHealth = Random.Range(100, 200);
                    character.Health = Random.Range(1, character.MaxHealth);
                    _characters.Add(character);
                }
            } else if (characterCount < _characters.Count) {
                print("Removing " + (_characters.Count - characterCount) + " characters");
                for (int i = _characters.Count - 1; i >= characterCount; i--) {
                    var character = _characters[i];
                    if (character != null)
                        Destroy(character.gameObject);
                    _characters.RemoveAt(i);
                }
            } else {
                print("No change in characters array");
                return;
            }

            OnCharactersArrayChanged?.Invoke(CharactersArray);
        }
    }
}