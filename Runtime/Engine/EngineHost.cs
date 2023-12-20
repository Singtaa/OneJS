using System;
using System.Reflection;
using Jint.Native;
using Jint.Native.Function;
using Jint.Runtime.Interop;
using OneJS.Utils;

namespace OneJS.Engine {
    /// <summary>
    /// Used to provide host objects and host functions to the JS side under `OneJS` global variable
    /// </summary>
    public class EngineHost : IDisposable {
        public readonly Interop interop;
        public event Action OnReload;
        public event Action OnDestroy;

        readonly Jint.Engine _jintEngine;

        public EngineHost(ScriptEngine engine) {
            interop = new(engine);
            _jintEngine = engine.JintEngine;
        }

        /// <summary>
        /// Use this method to subscribe to an event on an object regardless of JS engine.
        /// </summary>
        /// <param name="eventSource">The object containing the event</param>
        /// <param name="eventName">The name of the event</param>
        /// <param name="handler">A C# delegate or a JS function</param>
        /// <returns>A function to unsubscribe event</returns>
        /// <exception cref="ArgumentNullException"></exception>
        /// <exception cref="NotSupportedException"></exception>
        /// <exception cref="ArgumentException"></exception>
        public Action subscribe(object eventSource, string eventName, object handler) {
            if (eventSource is null) {
                throw new ArgumentNullException(nameof(eventSource), "[SubscribeEvent] Event source is null.");
            } else if (eventSource is JsValue) {
                throw new NotSupportedException("[SubscribeEvent] Cannot subscribe event on JS value.");
            }

            var eventInfo = eventSource.GetType().GetEvent(eventName, BindingFlags.Public | BindingFlags.Instance);
            if (eventInfo is null) {
                throw new ArgumentException($"[SubscribeEvent] Cannot find event \"{eventName}\" on type \"{eventSource.GetType()}\".",
                    nameof(eventName));
            }

            var handlerDelegate = handler switch {
                // Never hit this case from the JS side though
                Delegate csHandler => csHandler,
                FunctionInstance jsHandler => Utils.DelegateWrapper.Wrap(_jintEngine, eventInfo, jsHandler),
                null => throw new ArgumentNullException(nameof(handler), "[SubscribeEvent] Handler is null."),
                _ => throw new ArgumentException($"[SubscribeEvent] Cannot convert handler of type \"{handler.GetType()}\" to delegate.",
                    nameof(handler)),
            };
            var isOnReloadEvent = eventSource == this && eventName == nameof(OnReload);

            eventInfo.AddEventHandler(eventSource, handlerDelegate);

            if (!isOnReloadEvent) {
                OnReload += unsubscribe;
            }

            return () => {
                unsubscribe();

                if (!isOnReloadEvent) {
                    OnReload -= unsubscribe;
                }
            };

            void unsubscribe() => eventInfo.RemoveEventHandler(eventSource, handlerDelegate);
        }

        // JINT will call this method if the handler is a JS function. The handler will be passed as a FunctionInstance.
        // Without this method, the handler will be passed as a System.Func<> delegate.
        public Action subscribe(object eventSource, string eventName, JsValue handler) => subscribe(eventSource, eventName, handler as object);

        // Also a couple of overloads for convenience (defaulting eventSource to this when not provided) 
        public Action subscribe(string eventName, JsValue handler) => subscribe(this, eventName, handler as object);
        public Action subscribe(string eventName, object handler) => subscribe(this, eventName, handler);

        public void InvokeOnReload() => OnReload?.Invoke();

        public void InvokeOnDestroy() => OnDestroy?.Invoke();

        public void Dispose() {
            OnReload = null;
            OnDestroy = null;
        }

        public class Interop {
            public readonly JsObject classes;
            public readonly JsObject objects;
            
            readonly ScriptEngine _engine;

            public Interop(ScriptEngine engine) {
                classes = new(engine.JintEngine);
                objects = new(engine.JintEngine);

                foreach (var pair in engine.StaticClasses) {
                    var type = AssemblyFinder.FindType(pair.staticClass);

                    if (type != null) {
                        classes[pair.module] = TypeReference.CreateTypeReference(engine.JintEngine, type);
                    } else {
                        UnityEngine.Debug.LogWarning(
                            $"[ScriptEngine] Cannot find type \"{pair.staticClass}\". Please check \"Static Classes\" array.", engine);
                    }
                }

                foreach (var pair in engine.Objects) {
                    objects[pair.module] = JsValue.FromObject(engine.JintEngine, pair.obj);
                }
            }
            
            public void AddClass(string module, Type type) {
                classes[module] = TypeReference.CreateTypeReference(_engine.JintEngine, type);
            }
            
            public void AddObject(string module, object obj) {
                objects[module] = JsValue.FromObject(_engine.JintEngine, obj);
            }
        }
    }
}