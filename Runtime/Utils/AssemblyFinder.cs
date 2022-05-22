using System;
using System.Linq;
using System.Reflection;

namespace OneJS.Utils {
    public class AssemblyFinder {
        static Assembly[] _assemblies;

        /// <summary>
        /// Can be slow
        /// </summary>
        public static Type FindType(string name) {
            if (_assemblies == null)
                _assemblies = AppDomain.CurrentDomain.GetAssemblies();
            if (String.IsNullOrEmpty(name))
                return null;
            foreach (var asm in _assemblies) {
                var type = asm.GetType(name);
                if (type != null)
                    return type;
            }
            // foreach (var asm in _assemblies) {
            //     var types = asm.GetTypes();
            //     var type = types.Where(t => t.FullName == name).FirstOrDefault();
            //     if (type != null)
            //         return type;
            // }
            return null;
        }
    }
}