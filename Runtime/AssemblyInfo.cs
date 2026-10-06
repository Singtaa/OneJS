using System.Runtime.CompilerServices;

// OneJS.Runtime.InputSystem sets JSRunner.AddInputSystemModule, the seam that keeps
// this assembly free of any reference to the Input System package. It and
// OneJS.Runtime.Models join context teardown (QuickJSUIBridge.ContextTornDown) and
// record which context made each resource (QuickJSNative.CurrentContextId).
[assembly: InternalsVisibleTo("OneJS.Runtime.InputSystem")]
[assembly: InternalsVisibleTo("OneJS.Runtime.Models")]

// The tests drive JsHost, the context and lifecycle JSRunner and JSPad share, directly.
[assembly: InternalsVisibleTo("OneJS.Tests")]

// Editor code and its tests log caught exceptions through OneJSLog.
[assembly: InternalsVisibleTo("OneJS.Editor")]
[assembly: InternalsVisibleTo("OneJS.Editor.TypeGenerator")]
[assembly: InternalsVisibleTo("OneJS.Tests.Editor")]
