using System.Collections.Generic;
using Jint.Native;
using Jint.Native.Function;

namespace Jint.CommonJS {
    public class InternalModule : IModule {
        public string Id { get; set; }
        public string ResolvedPath { get; set; }

        public List<IModule> Children => new List<IModule>();

        public JsValue Exports { get; set; }

        public InternalModule(string id, string resolvedPath, JsValue constructor) {
            Id = id;
            ResolvedPath = resolvedPath;
            Exports = constructor;
        }
    }
}