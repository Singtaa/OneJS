using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace OneJS {
    /// <summary>
    /// Partial class for Task/Promise bridging between C# and JS.
    /// When a C# async method returns a Task, we:
    /// 1. Generate a unique taskId and return it to JS as InteropType.TaskHandle
    /// 2. JS creates a Promise and stores resolve/reject callbacks keyed by taskId
    /// 3. When Task completes, we queue the result for dispatch on next tick
    /// 4. QuickJSUIBridge.Tick() calls ProcessCompletedTasks() to invoke JS callbacks
    ///
    /// The completion queue is process-wide, shared by every live context, but a completion
    /// belongs to the context that registered it and only that context may take it out.
    /// Without that rule a second JSRunner's tick consumes the first one's completion and
    /// resolves it into a context where the task id means nothing, leaving the owning
    /// Promise pending forever with nothing logged (issue #120).
    /// </summary>
    public static partial class QuickJSNative {
        static int _nextTaskId = 1;

        // Completed task results waiting to be dispatched to JS
        // Using ConcurrentQueue because Task continuations run on thread pool
        static readonly ConcurrentQueue<TaskCompletionInfo> _completedTasks = new();

        // Task queue monitoring
        const int TaskQueueWarningThreshold = 100;    // Warn when queue exceeds this
        const int MaxTasksPerTick = 50;               // Examine at most this many per tick to avoid blocking
        static bool _taskQueueWarningLogged;
        static int _peakTaskQueueSize;
        static int _foreignCompletionCount;
        static bool _unownedTaskWarningLogged;

        struct TaskCompletionInfo {
            public int TaskId;
            public int OwnerContextId; // Context that registered the task; 0 = unowned
            public bool IsSuccess;
            public object Result;     // Result value (for Task<T>) or null (for Task)
            public string ErrorMessage; // Error message if failed
        }

        // MARK: Context Ownership
        //
        // Ownership is recorded as a small integer id rather than as a QuickJSContext
        // reference or a native pointer, for two reasons:
        //
        //   - A managed reference would keep a disposed context alive for as long as one of
        //     its completions sat in the queue.
        //   - A native pointer is reusable: qjs_create can hand the next context the address
        //     a destroyed one had, and a completion from the dead context would then be
        //     claimed by an unrelated live one.
        //
        // Ids come from one counter and are never reused. These registries are deliberately
        // NOT cleared by ResetStaticState: with "Enter Play Mode" domain reload disabled the
        // managed contexts survive that reset, and forgetting a live context would have its
        // completions discarded as orphans. When the domain really does reload, the statics
        // go with it and there is nothing to clear.
        static int _nextContextId;
        static readonly ConcurrentDictionary<IntPtr, int> _contextIdsByPtr = new();
        static readonly ConcurrentDictionary<int, byte> _liveContextIds = new();

        /// <summary>
        /// Allocate an ownership id for a newly created context and map its native pointer
        /// to it, so the dispatch path can name the context a Task was returned to. Called
        /// by QuickJSContext's constructor. Ids are never reused within a session.
        /// </summary>
        internal static int RegisterContext(IntPtr nativePtr) {
            int id = Interlocked.Increment(ref _nextContextId);
            _liveContextIds[id] = 0;
            if (nativePtr != IntPtr.Zero) _contextIdsByPtr[nativePtr] = id;
            return id;
        }

        /// <summary>
        /// Forget a context that is going away, and drop the completions it owns. Those
        /// promises died with the context, and entries nobody can ever claim would otherwise
        /// be examined and put back by every surviving context on every tick.
        /// </summary>
        internal static void UnregisterContext(IntPtr nativePtr, int contextId) {
            if (nativePtr != IntPtr.Zero) _contextIdsByPtr.TryRemove(nativePtr, out _);
            if (contextId == 0) return;
            _liveContextIds.TryRemove(contextId, out _);
            DiscardCompletionsForContext(contextId);
        }

        /// <summary>
        /// Ownership id of the context currently dispatching, or 0 outside a dispatch.
        /// </summary>
        internal static int CurrentContextId {
            get {
                var ptr = _currentContextPtr;
                if (ptr == IntPtr.Zero) return 0;
                return _contextIdsByPtr.TryGetValue(ptr, out var id) ? id : 0;
            }
        }

        // MARK: Task Registration
        /// <summary>
        /// Register a Task for async completion tracking, owned by the given context.
        /// Returns a unique taskId that JS uses to create a pending Promise.
        /// Only <paramref name="ctx"/> will resolve the resulting completion.
        /// </summary>
        public static int RegisterTask(QuickJSContext ctx, Task task) {
            return RegisterTaskForContext(ctx?.Id ?? 0, task);
        }

        /// <summary>
        /// Register a Task against a context ownership id. Used by the dispatch path, which
        /// holds a native context pointer rather than a QuickJSContext.
        /// </summary>
        internal static int RegisterTaskForContext(int ownerContextId, Task task) {
            int taskId = _nextTaskId++;

            // Attach continuation to queue result when task completes
            task.ContinueWith(t => {
                var info = new TaskCompletionInfo { TaskId = taskId, OwnerContextId = ownerContextId };

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
            // It indicates a void async method: return null
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

        // MARK: Completion Dispatch
        /// <summary>
        /// Process completed tasks owned by this context and invoke their JS callbacks.
        /// Call this from QuickJSUIBridge.Tick() on the main thread.
        /// Completions belonging to another live context are left in the queue for it.
        /// Returns the number of tasks processed.
        /// </summary>
        public static int ProcessCompletedTasks(QuickJSContext ctx) {
            if (ctx == null) return 0;

            // Check queue size for monitoring
            int queueSize = _completedTasks.Count;
            if (queueSize > _peakTaskQueueSize) _peakTaskQueueSize = queueSize;

            // Warn if queue is growing unbounded
            if (queueSize >= TaskQueueWarningThreshold && !_taskQueueWarningLogged) {
                _taskQueueWarningLogged = true;
                Debug.LogWarning(
                    $"[QuickJSNative] Task completion queue size ({queueSize}) exceeded {TaskQueueWarningThreshold}. " +
                    "Tasks may be completing faster than they can be processed. " +
                    "Consider reducing async operation frequency or checking for runaway task creation.");
            }

            // Reset warning flag when queue drains
            if (queueSize < TaskQueueWarningThreshold / 2) {
                _taskQueueWarningLogged = false;
            }

            // Bound the pass by entries EXAMINED, not by entries processed.
            //
            // Foreign completions go back in the queue instead of being consumed, so a budget
            // spent only on processed entries would never terminate: a queue holding nothing
            // but another context's completions would dequeue and re-enqueue the same entries
            // forever, with processed stuck at zero. Capping at the number of entries present
            // when the pass started also stops a pass from re-examining what it just put back,
            // and it is what keeps a busy runner from starving a quiet one: foreign entries
            // are pushed to the BACK, so each pass moves this context's own completions
            // closer to the front and they are reached within a bounded number of ticks.
            int budget = Math.Min(MaxTasksPerTick, queueSize);
            int ownerId = ctx.Id;
            int processed = 0;

            for (int examined = 0; examined < budget; examined++) {
                if (!_completedTasks.TryDequeue(out var info)) break;

                if (info.OwnerContextId != 0 && info.OwnerContextId != ownerId) {
                    // Another context's completion. Put it back for its owner's next tick,
                    // unless that owner is gone, in which case nothing will ever claim it and
                    // keeping it would cost every live context an examination slot per tick.
                    if (_liveContextIds.ContainsKey(info.OwnerContextId)) {
                        _completedTasks.Enqueue(info);
                        _foreignCompletionCount++;
                    }
                    continue;
                }

                // OwnerContextId == 0 means no context was dispatching when the task was
                // registered, so no JS promise is waiting on it in any context and whoever
                // ticks first may retire it. That is only safe while _nextTaskId is
                // process-global, which is what makes such an id impossible to confuse with a
                // live task here. If task ids are ever made per context, these entries must be
                // dropped at this point instead of resolved, or an unowned completion will
                // settle a real promise that happens to share its id. See issue #120.
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
                    Debug.LogError($"[QuickJS] Error processing task {info.TaskId}: {ex.Message}");
                }
            }

            // Drain microtasks once for the whole batch rather than after every task. The
            // Promise .then callbacks scheduled by the resolves/rejects above all run here, in
            // the same order, matching how a real event loop drains microtasks after a batch of
            // synchronous resolutions (and avoiding up to MaxTasksPerTick redundant drains).
            if (processed > 0) ctx.ExecutePendingJobs();

            return processed;
        }

        // ResolveTaskInJs/RejectTaskInJs only schedule the promise settlement; ProcessCompletedTasks
        // drains the resulting microtasks once after the batch.
        static void ResolveTaskInJs(QuickJSContext ctx, int taskId, object result) {
            // Convert result to JSON-safe string representation
            string resultJson = ConvertResultToJson(result);
            string code = $"__resolveTask({taskId}, {resultJson})";
            ctx.Eval(code, "<task-resolve>");
        }

        static void RejectTaskInJs(QuickJSContext ctx, int taskId, string errorMessage) {
            // Escape the error message for JS string
            string escaped = EscapeJsString(errorMessage ?? "Unknown error");
            string code = $"__rejectTask({taskId}, \"{escaped}\")";
            ctx.Eval(code, "<task-reject>");
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

            // Reference types: register as handle and return handle info
            int handle = RegisterObject(result);
            string typeName = EscapeJsString(result.GetType().FullName ?? "System.Object");
            return $"{{ \"__csHandle\": {handle}, \"__csType\": \"{typeName}\" }}";
        }

        // MARK: Queue Maintenance
        /// <summary>
        /// Clear all pending tasks, for every context. Call when the last context goes away.
        /// To retire one context's completions, use DiscardCompletionsForContext.
        /// </summary>
        public static void ClearPendingTasks() {
            while (_completedTasks.TryDequeue(out _)) { }
            _taskQueueWarningLogged = false;
        }

        /// <summary>
        /// Drop every queued completion owned by the given context, leaving other contexts'
        /// entries in place and in order. Returns how many were dropped.
        /// </summary>
        public static int DiscardCompletionsForContext(int contextId) {
            if (contextId == 0) return 0;

            // Bounded by the entries present now, so a completion arriving mid-rotation
            // cannot keep this spinning.
            int rotations = _completedTasks.Count;
            int discarded = 0;
            for (int i = 0; i < rotations; i++) {
                if (!_completedTasks.TryDequeue(out var info)) break;
                if (info.OwnerContextId == contextId) {
                    discarded++;
                    continue;
                }
                _completedTasks.Enqueue(info);
            }
            return discarded;
        }

        /// <summary>
        /// Returns the current number of pending task completions waiting to be processed,
        /// across every context.
        /// </summary>
        public static int GetPendingTaskCount() {
            return _completedTasks.Count;
        }

        /// <summary>
        /// Returns the peak task queue size since last reset.
        /// Useful for debugging async operation patterns.
        /// </summary>
        public static int GetPeakTaskQueueSize() {
            return _peakTaskQueueSize;
        }

        /// <summary>
        /// Number of completions that have been examined by a context which did not register
        /// them and put back for their owner, since the last ResetTaskQueueMonitoring. A
        /// steadily climbing value with more than one runner live is normal; it is the cost
        /// of sharing one queue, and it is what the per-tick examination budget bounds.
        /// </summary>
        public static int GetForeignCompletionCount() {
            return _foreignCompletionCount;
        }

        /// <summary>
        /// Resets task queue monitoring statistics.
        /// </summary>
        public static void ResetTaskQueueMonitoring() {
            _peakTaskQueueSize = _completedTasks.Count;
            _taskQueueWarningLogged = false;
            _foreignCompletionCount = 0;
        }
    }
}
