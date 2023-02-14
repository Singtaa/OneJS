using System;
using System.Threading;
using System.Threading.Tasks;
using Jint;
using Jint.Native;
using Jint.Runtime.Interop;
using UnityEngine;

namespace OneJS.Engine {
    /*
     * Credit to @twop, who did the original Promise implementation for Jint
     *
     * https://gist.github.com/twop/8d7b849d0105c779eed87bff8ae59722
     */
    public static class AsyncEngine {
        public class AsyncContext {
            internal readonly SemaphoreSlim EngineSemaphore = new(1);
            internal uint PendingTasksCount { get; set; }
            internal Action OnLastTaskFinished { get; set; }
        }

        public class TaskConverter : IObjectConverter {
            private readonly AsyncContext _ctx;

            public TaskConverter(AsyncContext ctx) {
                _ctx = ctx;
            }

            public bool TryConvert(Jint.Engine engine, object value, out JsValue result) {
                result = JsValue.Undefined;
                if (value is not Task task)
                    return false;

                var (promise, resolve, reject) = engine.RegisterPromise();

                async Task SettleOnEngineThread(Action action) {
                    var semaphore = _ctx.EngineSemaphore;
                    try {
                        await semaphore.WaitAsync();
                        action();
                    } finally {
                        _ctx.PendingTasksCount--;
                        var finishExecution = _ctx.PendingTasksCount == 0;
                        semaphore.Release();
                        if (finishExecution) {
                            // it is safe to set result outside semaphore because
                            // this task is the last possible race condition

                            _ctx.OnLastTaskFinished();
                        }
                    }
                }

                // Try convert will ALWAYS happen on the same thread as JS execution
                // Thus it is safe to increment it here without the lock
                _ctx.PendingTasksCount++;

                Task.Run(
                    async () => {
                        try {
                            await task;
                            var resultProperty = task.GetType().GetProperty("Result");
                            var result = resultProperty.GetValue(task);
                            await SettleOnEngineThread(() => {
                                resolve(JsValue.FromObject(engine, result));
                                engine.RunAvailableContinuations();
                            });
                        } catch (Exception e) {
                            await SettleOnEngineThread(() => {
                                reject(JsValue.FromObject(engine, e));
                                engine.RunAvailableContinuations();
                            });
                        }
                    });

                result = promise;
                return true;
            }
        }

        public static (Jint.Engine, AsyncContext) PrepareWithAsyncContext() {
            var ctx = new AsyncContext();
            var engine = new Jint.Engine(options => { options.AddObjectConverter(new TaskConverter(ctx)); });
            return (engine, ctx);
        }

        public static async Task<JsValue> EvaluateAsync(this Jint.Engine engine, AsyncContext ctx, string code) {
            await ctx.EngineSemaphore.WaitAsync();

            // TODO it should probably be wrapped in assert instead or something
            // it should always be zero at the start of the execution
            // e.g. "assert(ctx.PendingTasksCount, 0);
            ctx.PendingTasksCount = 0;

            var completionSource = new TaskCompletionSource<JsValue>();
#pragma warning disable 618
            ctx.OnLastTaskFinished =
                () => completionSource.SetResult(engine.GetCompletionValue().UnwrapIfPromise());
#pragma warning restore 618

            bool finishExecution;

            try {
                engine.Execute(code);

                // means that there were no tasks created during execution
                finishExecution = ctx.PendingTasksCount == 0;
            } finally {
                ctx.EngineSemaphore.Release();
            }


            if (finishExecution) {
#pragma warning disable 618
                return engine.GetCompletionValue().UnwrapIfPromise();
#pragma warning restore 618
            }

            return await completionSource.Task;
        }
    }
}