using System;
using System.Collections.Generic;
using Jint.Native;

namespace Jint.CommonJS {
    public interface IModule {
        string Id { get; set; }
        List<IModule> Children { get; }

        JsValue Exports { get; set; }
    }
}