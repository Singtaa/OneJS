using System;
using Jint.Native;

namespace Jint.CommonJS {
    public class ModuleRequestedEventArgs : EventArgs {
        public string ModuleId { get; }

        public JsValue Exports { get; set; }

        public ModuleRequestedEventArgs(string id) {
            this.ModuleId = id;
        }
    }
}