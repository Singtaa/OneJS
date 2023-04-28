namespace Jint.CommonJS {
    public static class EngineExtensions {
        public static ModuleLoadingEngine CommonJS(this Jint.Engine e, string workingDir, string[] pathMappings) {
            return new ModuleLoadingEngine(e, workingDir, pathMappings);
        }
    }
}