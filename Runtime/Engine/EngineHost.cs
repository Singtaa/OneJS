using System;
using System.Reflection;
using OneJS.Utils;
using Puerts;
using UnityEngine;

namespace OneJS {
    /// <summary>
    /// Used to provide host objects and host functions to the JS side under `onejs` global variable
    /// </summary>
    public class EngineHost : IDisposable {
        // public readonly Interop interop;
        public event ActionCallback onReload;
        public event ActionCallback onDestroy;

        public delegate void ActionCallback();
        // public delegate void JSCallback(object v);

        readonly JsEnv _jsEnv;

        public EngineHost(ScriptEngine engine) {
            // interop = new(engine);
            _jsEnv = engine.JsEnv;
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

            var handlerDelegate = GenericDelegateWrapper.Wrap(_jsEnv, eventInfo, handler);
            var isOnReloadEvent = eventSource == this && eventName == nameof(onReload);

            eventInfo.AddEventHandler(eventSource, handlerDelegate);

            if (!isOnReloadEvent) {
                onReload += unsubscribe;
            }
            return () => {
                unsubscribe();

                if (!isOnReloadEvent) {
                    onReload -= unsubscribe;
                }
            };

            void unsubscribe() {
                eventInfo.RemoveEventHandler(eventSource, handlerDelegate);
            }
        }

        public Action subscribe(string eventName, GenericDelegate handler) => subscribe(this, eventName, handler);

        public void InvokeOnReload() {
            onReload?.Invoke();
        }

        public void InvokeOnDestroy() {
            onDestroy?.Invoke();
        }

        public void Dispose() {
            onReload = null;
            onDestroy = null;
        }

        // // TODO
        // public class Interop {
        //     public readonly JsObject classes;
        //     public readonly JsObject objects;
        //     
        //     readonly ScriptEngine _engine;
        //
        //     public Interop(ScriptEngine engine) {
        //         classes = new(engine.JintEngine);
        //         objects = new(engine.JintEngine);
        //
        //         foreach (var pair in engine.StaticClasses) {
        //             var type = AssemblyFinder.FindType(pair.staticClass);
        //
        //             if (type != null) {
        //                 classes[pair.module] = TypeReference.CreateTypeReference(engine.JintEngine, type);
        //             } else {
        //                 UnityEngine.Debug.LogWarning(
        //                     $"[ScriptEngine] Cannot find type \"{pair.staticClass}\". Please check \"Static Classes\" array.", engine);
        //             }
        //         }
        //
        //         foreach (var pair in engine.Objects) {
        //             objects[pair.module] = JsValue.FromObject(engine.JintEngine, pair.obj);
        //         }
        //     }
        //     
        //     public void AddClass(string module, Type type) {
        //         classes[module] = TypeReference.CreateTypeReference(_engine.JintEngine, type);
        //     }
        //     
        //     public void AddObject(string module, object obj) {
        //         objects[module] = JsValue.FromObject(_engine.JintEngine, obj);
        //     }
        // }
    }
}