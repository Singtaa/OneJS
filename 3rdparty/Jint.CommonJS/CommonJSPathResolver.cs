using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Jint.CommonJS {
    public class CommonJSPathResolver : IModuleResolver {
// #if UNITY_EDITOR
//         private static readonly string
//             WORKING_DIR = Path.Combine(Path.GetDirectoryName(Application.dataPath)!, "OneJS");
// #else
//         private static readonly string WORKING_DIR = Path.Combine(Application.persistentDataPath, "OneJS");
// #endif

        private readonly IEnumerable<string> extensionHandlers;
        string _workingDir;
        string[] _pathMappings;

        public CommonJSPathResolver(string workingDir, string[] pathMappings, IEnumerable<string> extensionHandlers) {
            _workingDir = workingDir;
            _pathMappings = pathMappings.Concat(new[] { "" }).ToArray();
            this.extensionHandlers = extensionHandlers;
        }

        public string ResolvePath(string moduleId, Module parent) {
            var cwd = parent != null
                ? Path.GetDirectoryName(parent.filePath)
                : _workingDir;
            var path = Path.GetFullPath(Path.Combine(cwd, moduleId));

            var isRelativeModule = moduleId.StartsWith('.');
            var triedPaths = new List<string>();

            foreach (var candidatePath in isRelativeModule ? EnumeratePathWithExtensions(path) : EnumerateModuleLookupPaths(moduleId)) {
                triedPaths.Add(candidatePath);

                if (File.Exists(candidatePath)) {
                    return new FileInfo(candidatePath).FullName;
                }
            }

            throw new FileNotFoundException($"Module \"{moduleId}\" could not be resolved. Tried paths:\n\t{string.Join("\n\t", triedPaths)}");
        }

        IEnumerable<string> EnumerateModuleLookupPaths(string moduleId) => _pathMappings
            .Select(pm => Path.Combine(_workingDir, pm + moduleId))
            .SelectMany(EnumeratePathWithExtensions);

        IEnumerable<string> EnumeratePathWithExtensions(string path) => extensionHandlers
            .Where(ext => ext != "default")
            .SelectMany(ext => new[] { path + ext, Path.Combine(path, "index" + ext) })
            .Prepend(path);
    }
}
