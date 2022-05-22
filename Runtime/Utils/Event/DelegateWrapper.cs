using System;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using Jint;
using Jint.Native;
using UnityEngine;

namespace OneJS.Utils {
    /// <summary>
    /// This is for wrapping a Jint function, pinning it to an unique .Net delegate
    /// </summary>
    public class DelegateWrapper {
        Jint.Engine _engine;
        EventInfo _eventInfo;
        JsValue _handler;
        Delegate _del;

        /// <summary>
        /// https://nondisplayable.ca/2017/03/31/using-reflection-to-bind-lambda-to-event-handler.html
        /// </summary>
        public DelegateWrapper(Jint.Engine engine, EventInfo eventInfo, JsValue handler) {
            this._engine = engine;
            this._eventInfo = eventInfo;
            this._handler = handler;

            var handlerType = _eventInfo.EventHandlerType;
            MethodInfo invoke = handlerType.GetMethod("Invoke");
            ParameterInfo[] pars = invoke.GetParameters();
            var typeTypes = pars.Select(p => p.ParameterType.GetType()).ToArray();
            var paramTypes = pars.Select(p => p.ParameterType).ToArray();

            var methodInfo = typeof(DelegateWrapper).GetMethod("GetAction", typeTypes);
            if (methodInfo == null)
                throw new Exception(
                    "Cannot find Method Info. DelegateWrapper only support event handlers with up to 4 parameters.");
            if (typeTypes.Length > 0)
                methodInfo = methodInfo.MakeGenericMethod(paramTypes);

            var h = (Delegate)methodInfo.Invoke(this, typeTypes);
            _del = Delegate.CreateDelegate(handlerType, h, "Invoke");
        }

        public Delegate GetWrapped() {
            return _del;
        }

        public Action GetAction() {
            return (() => { _handler.As<Jint.Native.Function.FunctionInstance>().Call(); });
        }

        public Action<A> GetAction<A>(Type ta) {
            return ((a) => {
                var aa = JsValue.FromObject(_engine, a);
                _handler.As<Jint.Native.Function.FunctionInstance>().Call(null, aa);
            });
        }

        public Action<A, B> GetAction<A, B>(Type ta, Type tb) {
            return ((a, b) => {
                var aa = JsValue.FromObject(_engine, a);
                var bb = JsValue.FromObject(_engine, b);
                _handler.As<Jint.Native.Function.FunctionInstance>().Call(null, aa, bb);
            });
        }

        public Action<A, B, C> GetAction<A, B, C>(Type ta, Type tb, Type tc) {
            return ((a, b, c) => {
                var aa = JsValue.FromObject(_engine, a);
                var bb = JsValue.FromObject(_engine, b);
                var cc = JsValue.FromObject(_engine, c);
                _handler.As<Jint.Native.Function.FunctionInstance>().Call(null, aa, bb, cc);
            });
        }

        public Action<A, B, C, D> GetAction<A, B, C, D>(Type ta, Type tb, Type tc, Type td) {
            return ((a, b, c, d) => {
                var aa = JsValue.FromObject(_engine, a);
                var bb = JsValue.FromObject(_engine, b);
                var cc = JsValue.FromObject(_engine, c);
                var dd = JsValue.FromObject(_engine, d);
                _handler.As<Jint.Native.Function.FunctionInstance>().Call(null, aa, bb, cc, dd);
            });
        }
    }
}