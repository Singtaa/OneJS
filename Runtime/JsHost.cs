using System;
using UnityEngine;
using UnityEngine.UIElements;

namespace OneJS {
    /// <summary>
    /// The JS half that JSRunner and JSPad share: a context on a root element,
    /// the globals every app reads, the bundle's onPlay and onStop, the cached
    /// per-frame tick, and one way to tear it all down and build it again. Both
    /// components drive their app through this, so `__isPlaying`, the lifecycle
    /// and the tick behave the same in each.
    /// </summary>
    internal sealed class JsHost {
        readonly string _name;
        readonly Func<string, string> _translateError;
        int _onPlayHandle = -1;
        int _onStopHandle = -1;
        bool _onStopInvoked;

        /// <param name="name">The owning component, for log lines ("JSRunner").</param>
        /// <param name="translateError">Maps a JS stack to source lines, or null.</param>
        public JsHost(string name, Func<string, string> translateError = null) {
            _name = name;
            _translateError = translateError ?? (m => m);
        }

        /// <summary>The live context, or null when no app is running.</summary>
        public QuickJSUIBridge Bridge { get; private set; }

        /// <summary>A bundle has run in the live context.</summary>
        public bool ScriptLoaded { get; private set; }

        /// <summary>
        /// Creates a context rendering into <paramref name="root"/> with the
        /// globals every app reads: the platform defines, `__isPlaying`,
        /// `__workingDir`, `__root` and `__bridge`. <paramref name="configure"/>
        /// then adds the component's own (stylesheets, custom globals, preloads).
        /// A context already live is disposed without onStop; a running app is
        /// rebuilt with <see cref="Recreate"/>.
        /// </summary>
        public QuickJSUIBridge Create(VisualElement root, string workingDir, Action<QuickJSUIBridge> configure = null) {
            Dispose();
            var bridge = new QuickJSUIBridge(root, workingDir) { TranslateError = _translateError };
            Bridge = bridge;

            RunnerUtils.InjectPlatformDefines(bridge);
            bridge.Eval($"globalThis.__isPlaying = {(Application.isPlaying ? "true" : "false")}");
            bridge.Eval($"globalThis.__workingDir = '{RunnerUtils.EscapeJsString(bridge.WorkingDir)}'");
            var rootHandle = QuickJSNative.RegisterObject(root);
            bridge.Eval($"globalThis.__root = __csHelpers.wrapObject('UnityEngine.UIElements.VisualElement', {rootHandle})");
            var bridgeHandle = QuickJSNative.RegisterObject(bridge);
            bridge.Eval($"globalThis.__bridge = __csHelpers.wrapObject('QuickJSUIBridge', {bridgeHandle})");

            configure?.Invoke(bridge);
            return bridge;
        }

        /// <summary>
        /// Runs a bundle in the live context: evaluates it, settles its first
        /// render, and caches the tick, event and lifecycle callbacks so each
        /// frame and each event costs no allocation.
        /// </summary>
        public void Run(string code, string filename) {
            Bridge.Eval(code, filename);
            // Settle pending Promise jobs so React's first render lands now
            Bridge.Context.ExecutePendingJobs();
            ScriptLoaded = true;

            Bridge.CacheTickCallback();
            Bridge.CacheEventDispatchCallback();
            _onPlayHandle = RegisterExport("onPlay");
            _onStopHandle = RegisterExport("onStop");
            _onStopInvoked = false;

#if UNITY_WEBGL && !UNITY_EDITOR
            // The browser's requestAnimationFrame drives the JS half on WebGL
            Bridge.Eval("if (typeof __startWebGLTick === 'function') __startWebGLTick();", "webgl-tick-start.js");
#endif
        }

        int RegisterExport(string name) {
            var expr = $"typeof __exports !== 'undefined' && typeof __exports.{name} === 'function' ? __registerCallback(__exports.{name}) : -1";
            try {
                var result = Bridge.Eval(expr);
                return int.TryParse(result, out var h) ? h : -1;
            } catch (Exception ex) {
                // The expression guards on __exports itself, so a throw means the
                // registration primitive is broken (__registerCallback missing):
                // say so rather than silently never calling onPlay or onStop.
                OneJSLog.Exception($"[{_name}] Lifecycle callback registration failed (expr: {expr})", ex, translate: _translateError);
                return -1;
            }
        }

        /// <summary>Calls the bundle's exported onPlay. Play mode only.</summary>
        public void InvokeOnPlay() {
            if (!Application.isPlaying || _onPlayHandle < 0 || Bridge == null) return;
            try {
                Bridge.Context.InvokeCallbackNoAlloc(_onPlayHandle);
                Bridge.Context.ExecutePendingJobs();
            } catch (Exception ex) {
                OneJSLog.Exception($"[{_name}] onPlay() error", ex, translate: _translateError);
            }
            _onStopInvoked = false;
        }

        /// <summary>Calls the bundle's exported onStop, once per onPlay. Play mode only.</summary>
        public void InvokeOnStop() {
            if (!Application.isPlaying || _onStopInvoked || _onStopHandle < 0 || Bridge == null) return;
            _onStopInvoked = true;
            try {
                Bridge.Context.InvokeCallbackNoAlloc(_onStopHandle);
                Bridge.Context.ExecutePendingJobs();
            } catch (Exception ex) {
                OneJSLog.Exception($"[{_name}] onStop() error", ex, translate: _translateError);
            }
        }

        /// <summary>
        /// One frame of the app: timers, rAF, settled C# tasks, particles. On
        /// WebGL the browser ticks the JS half, so only the C# systems run here.
        /// </summary>
        public void Tick() {
            if (!ScriptLoaded || Bridge == null) return;
#if UNITY_WEBGL && !UNITY_EDITOR
            Bridge.TickSystems();
#else
            Bridge.Tick();
#endif
        }

        /// <summary>
        /// Stops the app: onStop in play mode, then <paramref name="afterOnStop"/>
        /// while the context is still alive (the janitor, clearing the panel),
        /// then the context goes.
        /// </summary>
        public void Stop(Action afterOnStop = null) {
            InvokeOnStop();
            afterOnStop?.Invoke();
            Dispose();
        }

        /// <summary>
        /// Every rebuild of a running app: stop the old one (onStop, then
        /// <paramref name="afterOnStop"/>), create a fresh context, run the bundle
        /// and call onPlay. Hot reload, re-enabling the component and a rebuilt
        /// panel all come through here, so none of them skips onStop.
        /// </summary>
        public void Recreate(VisualElement root, string workingDir, string code, string filename,
            Action afterOnStop = null, Action<QuickJSUIBridge> configure = null) {
            Stop(afterOnStop);
            Create(root, workingDir, configure);
            Run(code, filename);
            InvokeOnPlay();
        }

        /// <summary>Disposes the context without calling onStop.</summary>
        public void Dispose() {
            Bridge?.Dispose();
            Bridge = null;
            ScriptLoaded = false;
            _onPlayHandle = -1;
            _onStopHandle = -1;
            _onStopInvoked = false;
        }
    }
}
