namespace Jint.CommonJS {
    public interface IModuleResolver {
        /// <summary>
        /// Resolves a module ID to a file on disk.
        /// </summary>
        string ResolvePath(string moduleId, Module fromModule = null);
    }
}