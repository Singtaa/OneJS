using System;

namespace OneJS {
    /// <summary>
    /// Generate additional property and event that are compatible with useEventfulState
    /// </summary>
    [AttributeUsage(AttributeTargets.Field, AllowMultiple = false, Inherited = false)]
    public class EventfulPropertyAttribute : Attribute { }
}
