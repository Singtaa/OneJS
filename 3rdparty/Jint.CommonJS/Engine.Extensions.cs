namespace Jint.CommonJS {
    public static class EngineExtensions {
        public static ModuleLoadingEngine CommonJS(this Jint.Engine e, string workingDir) {
            return new ModuleLoadingEngine(e, workingDir);
        }
    }
}