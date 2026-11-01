using System.Collections.Generic;
using NUnit.Framework;
using OneJS.Editor;

namespace OneJS.Tests.Editor {
    /// <summary>
    /// ProjectSetup.GenerateTypings is the headless route to types/csharp.d.ts. Two 31 Oct
    /// cold agents wrote TypeScript declarations for their own C# by hand, because the only
    /// route was an inspector list and a menu item. A new runner lists no assemblies, so the
    /// call has to pick one: the game's own scripts, Assembly-CSharp, unless told otherwise.
    /// </summary>
    public class ProjectSetupTypingsTests {
        static bool Exists(string name) => name == "Assembly-CSharp" || name == "MyGame.Runtime";

        [Test]
        public void ANewRunnerGetsTheGamesOwnScripts() {
            CollectionAssert.AreEqual(new[] { "Assembly-CSharp" },
                ProjectSetup.TypingAssembliesFor(new List<string>(), new List<string>(), Exists));
        }

        [Test]
        public void ARunnerThatListsAssembliesKeepsThem() {
            CollectionAssert.AreEqual(new[] { "MyGame.Runtime" },
                ProjectSetup.TypingAssembliesFor(new List<string> { "MyGame.Runtime" }, new List<string>(), Exists));
        }

        [Test]
        public void AssembliesNamedOnTheCommandLineAreAddedOnce() {
            CollectionAssert.AreEqual(new[] { "Assembly-CSharp", "MyGame.Runtime" },
                ProjectSetup.TypingAssembliesFor(new List<string> { "Assembly-CSharp" }, new List<string> { "MyGame.Runtime", "Assembly-CSharp" }, Exists));
        }

        [Test]
        public void AProjectWithNoLooseScriptsGetsNothingRatherThanAMissingAssembly() {
            CollectionAssert.IsEmpty(ProjectSetup.TypingAssembliesFor(new List<string>(), new List<string>(), _ => false));
        }

        [Test]
        public void ReadsTypingAssembliesFromTheCommandLine() {
            var args = new[] { "Unity", "-batchmode", "-executeMethod", "OneJS.Editor.ProjectSetup.GenerateTypings", "-typingAssemblies", "MyGame.Runtime, MyGame.UI", "-quit" };
            CollectionAssert.AreEqual(new[] { "MyGame.Runtime", "MyGame.UI" }, ProjectSetup.TypingAssembliesFromArgs(args));
            CollectionAssert.IsEmpty(ProjectSetup.TypingAssembliesFromArgs(new[] { "Unity", "-batchmode" }));
        }
    }
}
