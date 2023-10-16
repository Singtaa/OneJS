#if ONEJS_V8
using System.IO;
using System.Threading.Tasks;
using Microsoft.ClearScript;
using UnityEngine;

namespace OneJS.Engine {
    public class OneJSDocumentLoader : DefaultDocumentLoader {
        ScriptEngine _onejsScriptEngine;

        public OneJSDocumentLoader(ScriptEngine onejsScriptEngine) {
            _onejsScriptEngine = onejsScriptEngine;
        }

        public override Document LoadDocument(DocumentSettings settings, DocumentInfo? sourceInfo,
            string fileName, DocumentCategory category, DocumentContextCallback contextCallback) {
            if (IsExposedNamespaceModule(fileName, out string ns)) {
                var documentInfo = new DocumentInfo(fileName) { Category = category, ContextCallback = contextCallback };
                return new StringDocument(documentInfo, @$"module.exports = clr.{ns}");
            }
            if (IsExposedStaticClass(fileName, out string sc)) {
                var documentInfo = new DocumentInfo(fileName) { Category = category, ContextCallback = contextCallback };
                return new StringDocument(documentInfo, @$"module.exports = clr.{sc}");
            }

            if (Path.IsPathRooted(fileName)) {
                return base.LoadDocument(settings, sourceInfo, fileName, category, contextCallback);
            }

            var finalPath = fileName;
            foreach (var pathMapping in _onejsScriptEngine.PathMappings) {
                var path = Path.Combine(_onejsScriptEngine.WorkingDir, pathMapping, fileName);
                if (Directory.Exists(path) && !File.Exists(path + ".js")) {
                    path = Path.Combine(path, "index");
                }
                if (File.Exists(path + ".js")) {
                    finalPath = path;
                    break;
                }
            }
            return base.LoadDocument(settings, sourceInfo, finalPath, category, contextCallback);
        }

        bool IsExposedNamespaceModule(string str, out string ns) {
            foreach (var nsmp in _onejsScriptEngine.Namespaces) {
                if (nsmp.module == str) {
                    ns = nsmp.@namespace;
                    return true;
                }
            }

            ns = "";
            return false;
        }

        bool IsExposedStaticClass(string str, out string sc) {
            foreach (var scmp in _onejsScriptEngine.StaticClasses) {
                if (scmp.module == str) {
                    sc = scmp.staticClass;
                    return true;
                }
            }

            sc = "";
            return false;
        }
    }
}
#endif
