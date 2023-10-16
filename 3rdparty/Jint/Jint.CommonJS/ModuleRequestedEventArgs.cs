using System;
using Jint.Native;

namespace Jint.CommonJS {
    public class ModuleRequestedEventArgs : EventArgs {
        public string ModuleId { get; }
        public string ResolvedPath { get; }

        public JsValue Exports { get; set; }

        public ModuleRequestedEventArgs(string id, string resolvedPath) {
            this.ModuleId = id;
            this.ResolvedPath = resolvedPath;
        }
    }
}