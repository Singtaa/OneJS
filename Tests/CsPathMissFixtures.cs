namespace OneJS.Tests.PathMiss {
    /// <summary>
    /// A type whose full name is also a namespace in another assembly:
    /// OneJS.Tests.Editor declares OneJS.Tests.PathMiss.Shared.Deep. C# allows
    /// that only across assemblies that do not reference each other, which is
    /// why this one lives here and the rest of the fixtures with the tests
    /// (Editor/CsPathMissTests.cs).
    /// </summary>
    public static class Shared {
        public static string Name => "shared";
    }
}
