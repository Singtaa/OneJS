using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Jint.Native;
using Jint.Native.Object;
using Jint.Runtime.Interop;
using UnityEngine;

namespace Jint.CommonJS {
    public class ModuleLoadingEngine {
        public static event EventHandler<ModuleRequestedEventArgs> ModuleRequested;

        public delegate JsValue FileExtensionParser(string path, IModule module);

        public Dictionary<string, IModule> ModuleCache = new Dictionary<string, IModule>();
        public Dictionary<string, FileExtensionParser> FileExtensionParsers =
            new Dictionary<string, FileExtensionParser>();

        public readonly Engine engine;
        public IModuleResolver Resolver { get; set; }

        public ModuleLoadingEngine(Engine e, string workingDir, IModuleResolver resolver = null) {
            this.engine = e;
            this.Resolver = resolver;

            FileExtensionParsers.Add("default", this.LoadJS);
            FileExtensionParsers.Add(".js", this.LoadJS);
            FileExtensionParsers.Add(".json", this.LoadJson);

            if (resolver == null) {
                this.Resolver = new CommonJSPathResolver(workingDir, this.FileExtensionParsers.Keys);
            }
        }

        private JsValue LoadJS(string path, IModule module) {
            var sourceCode = File.ReadAllText(path);
            if (module is Module) {
                module.Exports = (module as Module).Compile(sourceCode, path);
            } else {
#pragma warning disable 618
                module.Exports = engine.Execute(sourceCode).GetCompletionValue();
#pragma warning restore 618
            }
            return module.Exports;
        }

        private JsValue LoadJson(string path, IModule module) {
            var sourceCode = File.ReadAllText(path);
#pragma warning disable 618
            module.Exports = engine.Json.Parse(JsValue.Undefined, new[] { JsValue.FromObject(this.engine, sourceCode) })
                .AsObject();
#pragma warning restore 618
            return module.Exports;
        }

        protected ModuleLoadingEngine RegisterInternalModule(InternalModule mod) {
            ModuleCache.Add(mod.Id, mod);
            return this;
        }

        /// <summary>
        /// Registers an internal module to the provided delegate handler.
        /// </summary>
        public ModuleLoadingEngine RegisterInternalModule(string id, Delegate d) {
            this.RegisterInternalModule(id, new DelegateWrapper(engine, d));
            return this;
        }

        /// <summary>
        /// Registers an internal module under the specified id to the provided .NET CLR type.
        /// </summary>
        public ModuleLoadingEngine RegisterInternalModule(string id, Type clrType) {
            this.RegisterInternalModule(id, id, TypeReference.CreateTypeReference(engine, clrType));
            return this;
        }

        /// <summary>
        /// Registers an internal module under the specified id to any JsValue instance.
        /// </summary>
        public ModuleLoadingEngine RegisterInternalModule(string id, string resolvedPath, JsValue value) {
            this.RegisterInternalModule(new InternalModule(id, resolvedPath, value));
            return this;
        }

        /// <summary>
        /// Registers an internal module to the specified ID to the provided object instance.
        /// </summary>
        public ModuleLoadingEngine RegisterInternalModule(string id, object instance) {
            this.RegisterInternalModule(id, id, JsValue.FromObject(this.engine, instance));
            return this;
        }

        public JsValue RunMain(string mainModuleName) {
            if (string.IsNullOrWhiteSpace(mainModuleName)) {
                throw new System.ArgumentException("A Main module path is required.", nameof(mainModuleName));
            }

            return this.Load(mainModuleName);
        }

        public JsValue Load(string moduleName, Module parent = null) {
            IModule mod;

            if (string.IsNullOrEmpty(moduleName)) {
                throw new System.ArgumentException("moduleName is required.", nameof(moduleName));
            }

            string resolvedPath = moduleName;

            if (ModuleCache.ContainsKey(moduleName) ||
                ModuleCache.ContainsKey(resolvedPath = this.Resolver.ResolvePath(moduleName, parent))) {
                mod = ModuleCache[resolvedPath];
                if (parent != null)
                    parent.Children.Add(mod);
                return mod.Exports;
            }

            var requestedModule = new ModuleRequestedEventArgs(moduleName, resolvedPath);
            ModuleRequested?.Invoke(this, requestedModule);

            if (requestedModule.Exports != null && requestedModule.Exports != JsValue.Undefined) {
                ModuleCache.Add(resolvedPath,
                    (mod = new InternalModule(moduleName, resolvedPath, requestedModule.Exports)));
                return mod.Exports;
            }

            return new Module(this, moduleName, resolvedPath, parent).Exports;
        }
    }
}