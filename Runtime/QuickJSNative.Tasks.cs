using System;
using System.Collections.Concurrent;
using System.Threading.Tasks;

/// <summary>
/// Partial class for Task/Promise bridging between C# and JS.
/// When a C# async method returns a Task, we:
/// 1. Generate a unique taskId and return it to JS as InteropType.TaskHandle
/// 2. JS creates a Promise and stores resolve/reject callbacks keyed by taskId
/// 3. When Task completes, we queue the result for dispatch on next tick
/// 4. QuickJSUIBridge.Tick() calls ProcessCompletedTasks() to invoke JS callbacks
/// </summary>
public static partial class QuickJSNative {
    static int _nextTaskId = 1;

    // Completed task results waiting to be dispatched to JS
    // Using ConcurrentQueue because Task continuations run on thread pool
    static readonly ConcurrentQueue<TaskCompletionInfo> _completedTasks = new();

    struct TaskCompletionInfo {
        public int TaskId;
        public bool IsSuccess;
        public object Result;     // Result value (for Task<T>) or null (for Task)
        public string ErrorMessage; // Error message if failed
    }

    /// <summary>
    /// Register a Task for async completion tracking.
    /// Returns a unique taskId that JS uses to create a pending Promise.
    /// </summary>
    public static int RegisterTask(Task task) {
        int taskId = _nextTaskId++;

        // Attach continuation to queue result when task completes
        task.ContinueWith(t => {
            var info = new TaskCompletionInfo { TaskId = taskId };

            if (t.IsFaulted) {
                info.IsSuccess = false;
                info.ErrorMessage = t.Exception?.InnerException?.Message ?? t.Exception?.Message ?? "Task faulted";
            } else if (t.IsCanceled) {
                info.IsSuccess = false;
                info.ErrorMessage = "Task was canceled";
            } else {
                info.IsSuccess = true;
                // Extract result from Task<T> if it has one
                info.Result = GetTaskResult(t);
            }

            _completedTasks.Enqueue(info);
        }, TaskContinuationOptions.ExecuteSynchronously);

        return taskId;
    }

    /// <summary>
    /// Extract result from a Task. Returns null for non-generic Task or void tasks.
    /// </summary>
    static object GetTaskResult(Task task) {
        var taskType = task.GetType();
        if (!taskType.IsGenericType) return null;

        // Get the generic type argument
        var typeArgs = taskType.GetGenericArguments();
        if (typeArgs.Length == 0) return null;

        var resultType = typeArgs[0];

        // VoidTaskResult is an internal struct used by Task (not Task<T>)
        // It indicates a void async method - return null
        if (resultType.Name == "VoidTaskResult") return null;

        // Task<T> has a Result property
        var resultProp = taskType.GetProperty("Result");
        if (resultProp == null) return null;

        try {
            return resultProp.GetValue(task);
        } catch {
            return null;
        }
    }

    /// <summary>
    /// Check if a type is a Task or Task<T>.
    /// </summary>
    public static bool IsTaskType(Type type) {
        if (type == null) return false;
        if (type == typeof(Task)) return true;
        if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(Task<>)) return true;
        return false;
    }

    /// <summary>
    /// Process completed tasks and invoke JS callbacks.
    /// Call this from QuickJSUIBridge.Tick() on the main thread.
    /// Returns the number of tasks processed.
    /// </summary>
    public static int ProcessCompletedTasks(QuickJSContext ctx) {
        if (ctx == null) return 0;

        int processed = 0;
        while (_completedTasks.TryDequeue(out var info)) {
            try {
                if (info.IsSuccess) {
                    // Call __resolveTask(taskId, result)
                    ResolveTaskInJs(ctx, info.TaskId, info.Result);
                } else {
                    // Call __rejectTask(taskId, errorMessage)
                    RejectTaskInJs(ctx, info.TaskId, info.ErrorMessage);
                }
                processed++;
            } catch (Exception ex) {
                UnityEngine.Debug.LogError($"[QuickJS] Error processing task {info.TaskId}: {ex.Message}");
            }
        }

        return processed;
    }

    static void ResolveTaskInJs(QuickJSContext ctx, int taskId, object result) {
        // Convert result to JSON-safe string representation
        string resultJson = ConvertResultToJson(result);
        string code = $"__resolveTask({taskId}, {resultJson})";
        ctx.Eval(code, "<task-resolve>");
        ctx.ExecutePendingJobs();
    }

    static void RejectTaskInJs(QuickJSContext ctx, int taskId, string errorMessage) {
        // Escape the error message for JS string
        string escaped = EscapeJsString(errorMessage ?? "Unknown error");
        string code = $"__rejectTask({taskId}, \"{escaped}\")";
        ctx.Eval(code, "<task-reject>");
        ctx.ExecutePendingJobs();
    }

    static string ConvertResultToJson(object result) {
        if (result == null) return "null";

        // Primitives
        switch (result) {
            case bool b:
                return b ? "true" : "false";
            case int i:
                return i.ToString();
            case long l:
                return l.ToString();
            case float f:
                return f.ToString(System.Globalization.CultureInfo.InvariantCulture);
            case double d:
                return d.ToString(System.Globalization.CultureInfo.InvariantCulture);
            case string s:
                return $"\"{EscapeJsString(s)}\"";
        }

        // Reference types - register as handle and return handle info
        int handle = RegisterObject(result);
        string typeName = EscapeJsString(result.GetType().FullName ?? "System.Object");
        return $"{{ \"__csHandle\": {handle}, \"__csType\": \"{typeName}\" }}";
    }

    /// <summary>
    /// Clear all pending tasks. Call on context destruction.
    /// </summary>
    public static void ClearPendingTasks() {
        while (_completedTasks.TryDequeue(out _)) { }
    }
}
