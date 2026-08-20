using System;
using System.Collections.Concurrent;
using System.Reflection;
using UnityEngine;
using UnityEngine.Scripting;

namespace OneJS {
    /// <summary>
    /// Binds a named JS function to a typed C# delegate. Backing implementation
    /// for QuickJSContext/QuickJSUIBridge/JSRunner.GetJSFunction.
    ///
    /// The binding resolves the name lazily against the context returned by its
    /// provider: the JS global only has to exist by the first invocation, and a
    /// provider that returns the current context (JSRunner's does) makes the
    /// delegate survive hot reload, re-resolving against the fresh context on the
    /// next call. Resolution registers the function via __registerCallback once
    /// per context; invocation goes through QuickJSContext.InvokeCallback (the
    /// allocating path: fine for game-event frequency, use the
    /// InvokeCallbackNoAlloc family directly for per-frame hot paths).
    /// </summary>
    internal sealed class JsFunctionBinding {
        readonly Func<QuickJSContext> _contextProvider;
        readonly string _name;
        QuickJSContext _resolvedContext;
        int _handle = -1;

        JsFunctionBinding(Func<QuickJSContext> contextProvider, string name) {
            _contextProvider = contextProvider;
            _name = name;
        }

        /// <summary>
        /// Creates a TDelegate whose invocation calls the JS function named
        /// <paramref name="name"/> ("fn" or a dotted path like "game.ui.fn",
        /// resolved from globalThis) on the context supplied by
        /// <paramref name="contextProvider"/> at call time.
        /// </summary>
        internal static TDelegate CreateDelegate<TDelegate>(Func<QuickJSContext> contextProvider, string name)
            where TDelegate : Delegate {
            var invokeMethod = typeof(TDelegate).GetMethod("Invoke");
            if (invokeMethod == null)
                throw new ArgumentException($"{typeof(TDelegate).FullName} is not a delegate type");

            var parameters = invokeMethod.GetParameters();
            foreach (var p in parameters) {
                if (p.ParameterType.IsByRef)
                    throw new ArgumentException(
                        $"GetJSFunction does not support ref/out parameters ({typeof(TDelegate).FullName})");
            }
            if (parameters.Length > 4)
                throw new ArgumentException(
                    $"GetJSFunction supports up to 4 parameters; {typeof(TDelegate).FullName} has {parameters.Length}");

            var binding = new JsFunctionBinding(contextProvider, name);
            var returnType = invokeMethod.ReturnType;

            MethodInfo mi;
            if (returnType == typeof(void)) {
                mi = parameters.Length switch {
                    0 => _invokeVoid0,
                    1 => GetGeneric(_invokeVoid1Cache, _invokeVoid1Open, parameters[0].ParameterType),
                    2 => GetGeneric(_invokeVoid2Cache, _invokeVoid2Open, parameters[0].ParameterType, parameters[1].ParameterType),
                    3 => GetGeneric(_invokeVoid3Cache, _invokeVoid3Open, parameters[0].ParameterType, parameters[1].ParameterType, parameters[2].ParameterType),
                    _ => GetGeneric(_invokeVoid4Cache, _invokeVoid4Open, parameters[0].ParameterType, parameters[1].ParameterType, parameters[2].ParameterType, parameters[3].ParameterType),
                };
            } else {
                mi = parameters.Length switch {
                    0 => GetGeneric(_invokeRet0Cache, _invokeRet0Open, returnType),
                    1 => GetGeneric(_invokeRet1Cache, _invokeRet1Open, parameters[0].ParameterType, returnType),
                    2 => GetGeneric(_invokeRet2Cache, _invokeRet2Open, parameters[0].ParameterType, parameters[1].ParameterType, returnType),
                    3 => GetGeneric(_invokeRet3Cache, _invokeRet3Open, parameters[0].ParameterType, parameters[1].ParameterType, parameters[2].ParameterType, returnType),
                    _ => GetGeneric(_invokeRet4Cache, _invokeRet4Open, parameters[0].ParameterType, parameters[1].ParameterType, parameters[2].ParameterType, parameters[3].ParameterType, returnType),
                };
            }

            return (TDelegate)Delegate.CreateDelegate(typeof(TDelegate), binding, mi);
        }

        // MARK: Resolution
        QuickJSContext ResolveContext() {
            var ctx = _contextProvider();
            if (ctx == null || !ctx.IsAlive)
                throw new InvalidOperationException(
                    $"Cannot call JS function '{_name}': no running JS context (is the runner initialized?)");
            if (!ReferenceEquals(ctx, _resolvedContext)) {
                _handle = ResolveHandle(ctx, _name);
                _resolvedContext = ctx;
            }
            return ctx;
        }

        static int ResolveHandle(QuickJSContext ctx, string name) {
            // EscapeJsString escapes for single-quoted JS strings, so the name
            // literal below must use single quotes.
            var escaped = CartridgeUtils.EscapeJsString(name);
            var expr =
                "(function(n){var p=n.split('.');var f=globalThis;" +
                "for(var i=0;i<p.length&&f!=null;i++)f=f[p[i]];" +
                "return typeof f==='function'?__registerCallback(f):-1})('" + escaped + "')";
            var result = ctx.Eval(expr, "get-js-function");
            if (!int.TryParse(result, out var handle) || handle < 0)
                throw new MissingMemberException(
                    $"JS function '{name}' was not found on globalThis or is not a function");
            return handle;
        }

        object Call(object[] args) {
            var ctx = ResolveContext();
            return ctx.InvokeCallback(_handle, args);
        }

        static TResult ConvertReturn<TResult>(object value) {
            if (value == null) return default;
            if (value is TResult direct) return direct;
            var converted = QuickJSNative.ConvertToTargetType(value, typeof(TResult));
            if (converted is TResult t) return t;
            try {
                return (TResult)Convert.ChangeType(value, typeof(TResult));
            } catch (Exception) {
                throw new InvalidCastException(
                    $"JS function returned a {value.GetType().Name}; cannot convert to {typeof(TResult).Name}");
            }
        }

        // MARK: Typed invoke surface (bound via Delegate.CreateDelegate)
        public void InvokeVoid0() => Call(Array.Empty<object>());
        public void InvokeVoid1<T1>(T1 a1) => Call(new object[] { a1 });
        public void InvokeVoid2<T1, T2>(T1 a1, T2 a2) => Call(new object[] { a1, a2 });
        public void InvokeVoid3<T1, T2, T3>(T1 a1, T2 a2, T3 a3) => Call(new object[] { a1, a2, a3 });
        public void InvokeVoid4<T1, T2, T3, T4>(T1 a1, T2 a2, T3 a3, T4 a4) => Call(new object[] { a1, a2, a3, a4 });

        public TResult InvokeRet0<TResult>() => ConvertReturn<TResult>(Call(Array.Empty<object>()));
        public TResult InvokeRet1<T1, TResult>(T1 a1) => ConvertReturn<TResult>(Call(new object[] { a1 }));
        public TResult InvokeRet2<T1, T2, TResult>(T1 a1, T2 a2) => ConvertReturn<TResult>(Call(new object[] { a1, a2 }));
        public TResult InvokeRet3<T1, T2, T3, TResult>(T1 a1, T2 a2, T3 a3) => ConvertReturn<TResult>(Call(new object[] { a1, a2, a3 }));
        public TResult InvokeRet4<T1, T2, T3, T4, TResult>(T1 a1, T2 a2, T3 a3, T4 a4) => ConvertReturn<TResult>(Call(new object[] { a1, a2, a3, a4 }));

        // MARK: Open method caches
        static MethodInfo Open(string name) =>
            typeof(JsFunctionBinding).GetMethod(name, BindingFlags.Instance | BindingFlags.Public);

        static readonly MethodInfo _invokeVoid0 = Open(nameof(InvokeVoid0));
        static readonly MethodInfo _invokeVoid1Open = Open(nameof(InvokeVoid1));
        static readonly MethodInfo _invokeVoid2Open = Open(nameof(InvokeVoid2));
        static readonly MethodInfo _invokeVoid3Open = Open(nameof(InvokeVoid3));
        static readonly MethodInfo _invokeVoid4Open = Open(nameof(InvokeVoid4));
        static readonly MethodInfo _invokeRet0Open = Open(nameof(InvokeRet0));
        static readonly MethodInfo _invokeRet1Open = Open(nameof(InvokeRet1));
        static readonly MethodInfo _invokeRet2Open = Open(nameof(InvokeRet2));
        static readonly MethodInfo _invokeRet3Open = Open(nameof(InvokeRet3));
        static readonly MethodInfo _invokeRet4Open = Open(nameof(InvokeRet4));

        static readonly ConcurrentDictionary<Type[], MethodInfo> _invokeVoid1Cache = new(TypeArrayComparer.Instance);
        static readonly ConcurrentDictionary<Type[], MethodInfo> _invokeVoid2Cache = new(TypeArrayComparer.Instance);
        static readonly ConcurrentDictionary<Type[], MethodInfo> _invokeVoid3Cache = new(TypeArrayComparer.Instance);
        static readonly ConcurrentDictionary<Type[], MethodInfo> _invokeVoid4Cache = new(TypeArrayComparer.Instance);
        static readonly ConcurrentDictionary<Type[], MethodInfo> _invokeRet0Cache = new(TypeArrayComparer.Instance);
        static readonly ConcurrentDictionary<Type[], MethodInfo> _invokeRet1Cache = new(TypeArrayComparer.Instance);
        static readonly ConcurrentDictionary<Type[], MethodInfo> _invokeRet2Cache = new(TypeArrayComparer.Instance);
        static readonly ConcurrentDictionary<Type[], MethodInfo> _invokeRet3Cache = new(TypeArrayComparer.Instance);
        static readonly ConcurrentDictionary<Type[], MethodInfo> _invokeRet4Cache = new(TypeArrayComparer.Instance);

        static MethodInfo GetGeneric(ConcurrentDictionary<Type[], MethodInfo> cache, MethodInfo open, params Type[] typeArgs) =>
            cache.GetOrAdd(typeArgs, args => open.MakeGenericMethod(args));

        sealed class TypeArrayComparer : System.Collections.Generic.IEqualityComparer<Type[]> {
            public static readonly TypeArrayComparer Instance = new();

            public bool Equals(Type[] x, Type[] y) {
                if (x.Length != y.Length) return false;
                for (int i = 0; i < x.Length; i++)
                    if (x[i] != y[i]) return false;
                return true;
            }

            public int GetHashCode(Type[] obj) {
                int hash = 17;
                foreach (var t in obj) hash = hash * 31 + t.GetHashCode();
                return hash;
            }
        }

        // Never called: exists so IL2CPP sees the generic instantiations for
        // common value-type signatures (reference types share one instantiation).
        [Preserve]
        static void AotHints() {
            var b = new JsFunctionBinding(() => null, "");
            b.InvokeVoid1<int>(default);
            b.InvokeVoid1<float>(default);
            b.InvokeVoid1<double>(default);
            b.InvokeVoid1<bool>(default);
            b.InvokeVoid1<Vector3>(default);
            b.InvokeVoid1<Color>(default);
            b.InvokeVoid2<int, int>(default, default);
            b.InvokeVoid2<float, float>(default, default);
            b.InvokeRet0<int>();
            b.InvokeRet0<float>();
            b.InvokeRet0<double>();
            b.InvokeRet0<bool>();
            b.InvokeRet1<int, int>(default);
            b.InvokeRet1<float, float>(default);
        }
    }
}
