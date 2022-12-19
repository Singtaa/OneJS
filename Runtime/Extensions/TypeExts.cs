using System;
using System.Linq;

namespace OneJS.Extensions {
    public static class TypeExts {
        /// <summary>
        /// Returns the type name. If this is a generic type, appends
        /// the list of generic type arguments between angle brackets.
        /// (Does not account for embedded / inner generic arguments.)
        ///
        /// https://stackoverflow.com/a/66604069/150094
        /// </summary>
        /// <param name="type">The type.</param>
        /// <returns>System.String.</returns>
        public static string GetFormattedName(this Type type) {
            if (type.IsGenericType) {
                string genericArguments = type.GetGenericArguments()
                    .Select(x => x.GetFormattedName())
                    .Aggregate((x1, x2) => $"{x1}, {x2}");
                return $"{type.Name.Substring(0, type.Name.IndexOf("`"))}"
                       + $"<{genericArguments}>";
            }
            return type.Name;
        }
    }
}