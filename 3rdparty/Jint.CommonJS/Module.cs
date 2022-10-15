using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Jint.Native;
using Jint.Native.Function;
using Jint.Native.Object;
using Jint.Runtime;
using Jint.Runtime.Interop;
using UnityEngine;

namespace Jint.CommonJS {
    public class Module : IModule {
        /// <summary>
        /// This module's children
        /// </summary>
        public List<IModule> Children { get; } = new List<IModule>();

        protected Module parentModule;

        protected ModuleLoadingEngine engine;

        /// <summary>
        /// Determines if this module is the main module.
        /// </summary>
        public bool isMainModule => this.parentModule == null;

        public string Id { get; set; }

        /// <summary>
        /// Contains the module's public API.
        /// </summary>
        public JsValue Exports { get; set; }

        public readonly string filePath;

        /// <summary>
        /// Creates a new Module instaznce with the specified module id. The module is resolved to a file on disk
        /// according to the CommonJS specification.
        /// </summary>
        internal Module(ModuleLoadingEngine e, string moduleId, string resolvedPath, Module parent = null) {
            if (e == null) {
                throw new System.ArgumentNullException(nameof(e));
            }

            this.engine = e;

            if (string.IsNullOrEmpty(moduleId)) {
                throw new System.ArgumentException("A moduleId is required.", nameof(moduleId));
            }

            Id = moduleId;
            // this.filePath = e.Resolver.ResolvePath(Id, parent ?? this);
            this.filePath = resolvedPath;
            this.parentModule = parent;

            if (parent != null) {
                parent.Children.Add(this);
            }
#pragma warning disable 618
            this.Exports = engine.engine.Object.Construct(new JsValue[] { });
#pragma warning restore 618

            string extension = Path.GetExtension(this.filePath);
            var loader = this.engine.FileExtensionParsers[extension] ?? this.engine.FileExtensionParsers["default"];

            e.ModuleCache.Add(resolvedPath, this);

            loader(this.filePath, this);
        }

        protected JsValue Require(string moduleId) {
            return engine.Load(moduleId, this);
        }

        public JsValue Compile(string sourceCode, string filePath) {
            var moduleObject = JsValue.FromObject(this.engine.engine, this);

            try {
                var func = engine.engine.Evaluate($@"
                    ;(function (module, exports, __dirname, require) {{
                        {sourceCode}
                    }})
                ");
                var requireFunc = new DelegateWrapper(engine.engine, new Func<string, JsValue>(this.Require));
                engine.engine.Call(func, moduleObject, this.Exports, Path.GetDirectoryName(filePath), requireFunc);
            } catch (JavaScriptException jse) {
                StringBuilder sb = new StringBuilder();
                sb.Append($"Javascript Error at {filePath} (Line {jse.Location.Start.Line - 2})\n\n");
                sb.Append(jse.ToString());
                throw new Exception(sb.ToString());
            }

            return Exports;
        }
    }
}