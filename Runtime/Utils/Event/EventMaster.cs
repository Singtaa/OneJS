using System;
using System.Reflection;
using Jint.Native;
using UnityEngine;

namespace OneJS.Utils {
    /// <summary>
    /// https://github.com/sebastienros/jint/issues/1139
    /// This class (EventMaster) will not be necessary after the PR gets merged.
    /// </summary>
    [RequireComponent(typeof(ScriptEngine))]
    public class EventMaster : MonoBehaviour {
        ScriptEngine _scriptEngine;

        void Awake() {
            _scriptEngine = GetComponent<ScriptEngine>();
        }

        public void Add(object obj, string name, DelegateWrapper wrapper) {
        }

        public DelegateWrapper Add(Type type, string name, JsValue handler) {
            var eventInfo = type.GetEvent(name);
            var wrapper = new DelegateWrapper(_scriptEngine.JintEngine, eventInfo, handler);
            eventInfo.AddMethod.Invoke(null, new object[] { wrapper.GetWrapped() });

            return wrapper;
        }

        public void Remove(object obj, string name, DelegateWrapper wrapper) {
        }

        public void Remove(Type type, string name, DelegateWrapper wrapper) {
            var eventInfo = type.GetEvent(name);
            eventInfo.RemoveMethod.Invoke(null, new object[] { wrapper.GetWrapped() });
        }
    }
}