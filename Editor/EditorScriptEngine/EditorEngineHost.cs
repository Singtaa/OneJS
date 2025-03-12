using System;
using System.Reflection;
using OneJS.Utils;
using Puerts;
using UnityEngine;
using UnityEngine.UIElements;

namespace OneJS.Editor {
    public class EditorEngineHost : IEngineHost, IDisposable {
        public event Action onReload;
        public event Action onDispose;

        EditorScriptEngine _engine;

        JsEnv _sandboxEnv;

        public EditorEngineHost(EditorScriptEngine engine) {
            _engine = engine;
            _engine.OnReload += DoReload;
            _engine.OnDispose += Dispose;
        }

        public void Dispose() {
            _engine.OnReload -= DoReload;
            onReload = null;
            
            onDispose?.Invoke();
            _engine.OnDispose -= Dispose;
            onDispose = null;
            
            _sandboxEnv?.Dispose();
            _sandboxEnv = null;
        }

        public void DoReload() {
            onReload?.Invoke();
        }

        /// <summary>
        /// Execute the given JavaScript code using the main JS Environment
        /// </summary>
        public void Execute(string jsCode) {
            _engine.Execute(jsCode);
        }
        
        public void RegisterRenderer(Type type, Action<UnityEngine.Object, VisualElement> render) {
            _engine.RegisterRenderer(type, render);
        }
        
        /// <summary>
        /// Use this method to subscribe to an event on an object regardless of JS engine.
        ///
        /// NOTE: There's now a `subscribe` method on the JS side that does the same thing and also supports static events.
        /// Use `import { subscribe } from 'onejs-core/utils'` to use it.
        /// </summary>
        /// <param name="eventSource">The object containing the event</param>
        /// <param name="eventName">The name of the event</param>
        /// <param name="handler">A C# delegate or a JS function</param>
        /// <returns>A function to unsubscribe event</returns>
        /// <exception cref="ArgumentNullException"></exception>
        /// <exception cref="NotSupportedException"></exception>
        /// <exception cref="ArgumentException"></exception>
        public Action subscribe(object eventSource, string eventName, GenericDelegate handler) {
            if (eventSource is null) {
                throw new ArgumentNullException(nameof(eventSource), "[SubscribeEvent] Event source is null.");
            } else if (eventSource is JSObject) {
                throw new NotSupportedException("[SubscribeEvent] Cannot subscribe event on JS value.");
            }

            var eventInfo = eventSource.GetType().GetEvent(eventName, BindingFlags.Public | BindingFlags.Instance);
            if (eventInfo is null) {
                throw new ArgumentException(
                    $"[SubscribeEvent] Cannot find event \"{eventName}\" on type \"{eventSource.GetType()}\".",
                    nameof(eventName));
            }

            var handlerDelegate = GenericDelegateWrapper.Wrap(_engine.JsEnv, eventInfo, handler);
            var isOnReloadEvent = eventSource == this && eventName == nameof(onReload);
            var isOnDisposeEvent = eventSource == this && eventName == nameof(onDispose);

            eventInfo.AddEventHandler(eventSource, handlerDelegate);

            if (!isOnReloadEvent) {
                onReload += unsubscribe;
                onDispose += unsubscribe;
            }
            return () => {
                unsubscribe();

                if (!isOnReloadEvent) {
                    onReload -= unsubscribe;
                }
                if (!isOnDisposeEvent) {
                    onDispose -= unsubscribe;
                }
            };

            void unsubscribe() {
                eventInfo.RemoveEventHandler(eventSource, handlerDelegate);
            }
        }

        public Action subscribe(string eventName, GenericDelegate handler) => subscribe(this, eventName, handler);

        /// <summary>
        /// Use for event cleanup
        /// </summary>
        public void teardown(Action action) {
            onReload += _teardown;
            onDispose += _teardown;
            
            void _teardown() {
                action();
                
                onReload -= _teardown;
                onDispose -= _teardown;
            }
        }

        /// <summary>
        /// Execute the given JavaScript code using a sandboxed JS Environment
        /// </summary>
        public void SandboxExecute(string jsCode) {
            Dispose();
            _sandboxEnv = new JsEnv();
            foreach (var preload in _engine.preloads) {
                _sandboxEnv.Eval(preload.text);
            }
            var addToGlobal = _sandboxEnv.Eval<Action<string, object>>(@"__addToGlobal");
            addToGlobal("___document", _engine.document);
            addToGlobal("___workingDir", _engine.WorkingDir);
            addToGlobal("onejs", this);
            foreach (var obj in _engine.globalObjects) {
                addToGlobal(obj.name, obj.obj);
            }
            _sandboxEnv.Eval(jsCode, "sandbox_js_env");
        }
        
        public void ApplyStyleSheets(VisualElement ve) {
            _engine.ApplyStyleSheets(ve);
        }
    }
}