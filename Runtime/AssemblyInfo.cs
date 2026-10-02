using System.Runtime.CompilerServices;

// OneJS.Runtime.InputSystem sets JSRunner.AddInputSystemModule, the seam that keeps
// this assembly free of any reference to the Input System package.
[assembly: InternalsVisibleTo("OneJS.Runtime.InputSystem")]

// The tests drive JsHost, the context and lifecycle JSRunner and JSPad share, directly.
[assembly: InternalsVisibleTo("OneJS.Tests")]
