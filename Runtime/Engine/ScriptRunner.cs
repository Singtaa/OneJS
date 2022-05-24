using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace OneJS.Engine {
    public class ScriptRunner : MonoBehaviour {
        [Tooltip("Should be a .js file relative to your `persistentDataPath`." +
                 "")]
        [SerializeField] string _entryScript = "index.js";

        ScriptEngine _scriptEngine;
        
        void Awake() {
            _scriptEngine = GetComponent<ScriptEngine>();
        }

        void Start() {
            _scriptEngine.RunScript(_entryScript);
        }
    }
}