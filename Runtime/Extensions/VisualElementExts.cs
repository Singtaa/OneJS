using System;
using System.Linq;
using System.Reflection;
using UnityEngine.UIElements;

namespace OneJS.Extensions {
    public static class VisualElementExts {
        public static void Register(this CallbackEventHandler cbeh, Type eventType,
            EventCallback<EventBase> handler, TrickleDown useTrickleDown = TrickleDown.NoTrickleDown) {
            var flags = BindingFlags.Public | BindingFlags.Instance;
            var mi = cbeh.GetType().GetMethods(flags)
                .Where(m => m.Name == "RegisterCallback" && m.GetGenericArguments().Length == 1).First();
            mi = mi.MakeGenericMethod(eventType);
            mi.Invoke(cbeh, new object[] { handler, useTrickleDown });
        }
        
        public static void Unregister(this CallbackEventHandler cbeh, Type eventType,
            EventCallback<EventBase> handler, TrickleDown useTrickleDown = TrickleDown.NoTrickleDown) {
            var flags = BindingFlags.Public | BindingFlags.Instance;
            var mi = cbeh.GetType().GetMethods(flags)
                .Where(m => m.Name == "UnregisterCallback" && m.GetGenericArguments().Length == 1).First();
            mi = mi.MakeGenericMethod(eventType);
            mi.Invoke(cbeh, new object[] { handler, useTrickleDown });
        }
    }
}