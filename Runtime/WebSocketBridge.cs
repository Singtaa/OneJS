using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace OneJS {
    /// <summary>
    /// Manages WebSocket connections for JavaScript.
    /// Uses System.Net.WebSockets.ClientWebSocket on native platforms.
    /// On WebGL, browser's native WebSocket is used directly (no C# bridge needed).
    ///
    /// Architecture:
    /// - JS calls Connect/Send/Close via CS.OneJS.WebSocketBridge
    /// - Background threads handle async I/O and enqueue events
    /// - QuickJSUIBridge.Tick() calls ProcessEvents(contextId) to dispatch to JS
    /// - Each QuickJSUIBridge registers a context so events route to the correct JS runtime
    /// </summary>
    public static class WebSocketBridge {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStaticState() {
            CloseAll();
            _nextSocketId = 0;
            _nextContextId = 0;
            _contextQueues.Clear();
        }

        static int _nextSocketId = 0;
        static int _nextContextId = 0;
        static readonly ConcurrentDictionary<int, SocketState> _sockets = new();
        static readonly ConcurrentDictionary<int, ConcurrentQueue<WebSocketEvent>> _contextQueues = new();

        const int MaxEventsPerTick = 50;

        struct WebSocketEvent {
            public int SocketId;
            public int ContextId;
            public string Type;   // "open", "message", "error", "close"
            public string Data;   // message text, base64 binary data, or error message
            public int Code;      // close code
            public string Reason; // close reason
            public bool IsBinary; // true if Data contains base64-encoded binary
        }

        class SocketState {
            public ClientWebSocket Socket;
            public CancellationTokenSource Cts;
            public int ContextId;
            public readonly ConcurrentQueue<SendItem> SendQueue = new();
            public int SendLoopFlag; // 0 = not running, 1 = running (use Interlocked)
        }

        struct SendItem {
            public string Data;
            public byte[] BinaryData;
            public bool IsBinary;
        }

        /// <summary>
        /// Register a new context and return its ID. Call from QuickJSUIBridge constructor.
        /// </summary>
        public static int RegisterContext() {
            int id = Interlocked.Increment(ref _nextContextId);
            _contextQueues[id] = new ConcurrentQueue<WebSocketEvent>();
            return id;
        }

        /// <summary>
        /// Unregister a context and clean up its event queue. Call from QuickJSUIBridge.Dispose().
        /// </summary>
        public static void UnregisterContext(int contextId) {
            _contextQueues.TryRemove(contextId, out _);
        }

        /// <summary>
        /// Open a new WebSocket connection. Returns a socket ID immediately.
        /// Connection happens asynchronously; "open" or "error" event fires when ready.
        /// </summary>
        public static int Connect(string url, string protocols, int contextId) {
            int id = Interlocked.Increment(ref _nextSocketId);
            var ws = new ClientWebSocket();

            if (!string.IsNullOrEmpty(protocols)) {
                foreach (var proto in protocols.Split(',')) {
                    var trimmed = proto.Trim();
                    if (trimmed.Length > 0) {
                        ws.Options.AddSubProtocol(trimmed);
                    }
                }
            }

            var cts = new CancellationTokenSource();
            var state = new SocketState { Socket = ws, Cts = cts, ContextId = contextId };
            _sockets[id] = state;

            Task.Run(() => ConnectAndReceiveAsync(id, url, state));

            return id;
        }

        /// <summary>
        /// Send a text message on an open WebSocket.
        /// </summary>
        public static void Send(int socketId, string data) {
            if (!_sockets.TryGetValue(socketId, out var state)) return;
            if (state.Socket.State != WebSocketState.Open) return;

            state.SendQueue.Enqueue(new SendItem { Data = data });

            if (Interlocked.CompareExchange(ref state.SendLoopFlag, 1, 0) == 0) {
                Task.Run(() => ProcessSendQueueAsync(socketId, state));
            }
        }

        /// <summary>
        /// Send a binary message on an open WebSocket.
        /// Accepts base64-encoded data from JS, decodes to bytes on C# side.
        /// </summary>
        public static void SendBinary(int socketId, string base64Data) {
            if (!_sockets.TryGetValue(socketId, out var state)) return;
            if (state.Socket.State != WebSocketState.Open) return;

            byte[] bytes;
            try {
                bytes = Convert.FromBase64String(base64Data);
            } catch (FormatException ex) {
                Debug.LogError($"[WebSocketBridge] Invalid base64 data: {ex.Message}");
                return;
            }

            state.SendQueue.Enqueue(new SendItem { BinaryData = bytes, IsBinary = true });

            if (Interlocked.CompareExchange(ref state.SendLoopFlag, 1, 0) == 0) {
                Task.Run(() => ProcessSendQueueAsync(socketId, state));
            }
        }

        /// <summary>
        /// Close a WebSocket connection.
        /// Uses CloseOutputAsync to send the close frame without blocking the receive loop.
        /// The receive loop will see the server's close response and dispatch the close event.
        /// </summary>
        public static void Close(int socketId, int code, string reason) {
            if (!_sockets.TryGetValue(socketId, out var state)) return;
            if (state.Socket.State == WebSocketState.Closed ||
                state.Socket.State == WebSocketState.Aborted) return;

            Task.Run(async () => {
                try {
                    var status = (WebSocketCloseStatus)code;
                    await state.Socket.CloseOutputAsync(status, reason ?? "", state.Cts.Token);
                } catch {
                    // Close may fail if already closing; cancel to stop receive loop
                    state.Cts.Cancel();
                }
            });
        }

        /// <summary>
        /// Get the ready state of a WebSocket (0=CONNECTING, 1=OPEN, 2=CLOSING, 3=CLOSED).
        /// </summary>
        public static int GetReadyState(int socketId) {
            if (!_sockets.TryGetValue(socketId, out var state)) return 3;

            return state.Socket.State switch {
                WebSocketState.Connecting => 0,
                WebSocketState.Open => 1,
                WebSocketState.CloseSent => 2,
                WebSocketState.CloseReceived => 2,
                WebSocketState.Closed => 3,
                WebSocketState.Aborted => 3,
                _ => 3,
            };
        }

        /// <summary>
        /// Process queued events for a specific context and dispatch to JS.
        /// Called from QuickJSUIBridge.Tick() on the main thread.
        /// </summary>
        public static int ProcessEvents(QuickJSContext ctx, int contextId) {
            if (ctx == null) return 0;
            if (!_contextQueues.TryGetValue(contextId, out var queue)) return 0;

            int processed = 0;
            while (processed < MaxEventsPerTick && queue.TryDequeue(out var evt)) {
                try {
                    DispatchToJs(ctx, evt);
                    processed++;
                } catch (Exception ex) {
                    Debug.LogError($"[WebSocketBridge] Error dispatching event: {ex.Message}");
                }
            }

            return processed;
        }

        /// <summary>
        /// Close all WebSocket connections, optionally filtered by context.
        /// When contextId is provided, only closes sockets belonging to that context.
        /// When contextId is -1 (default), closes all sockets.
        /// </summary>
        public static void CloseAll(int contextId = -1) {
            foreach (var kvp in _sockets) {
                if (contextId >= 0 && kvp.Value.ContextId != contextId) continue;
                try {
                    kvp.Value.Cts.Cancel();
                    kvp.Value.Socket.Dispose();
                } catch { }
                _sockets.TryRemove(kvp.Key, out _);
            }

            // If closing all, clear all context queues too
            if (contextId < 0) {
                foreach (var kvp in _contextQueues) {
                    while (kvp.Value.TryDequeue(out _)) { }
                }
                _sockets.Clear();
            } else if (_contextQueues.TryGetValue(contextId, out var queue)) {
                while (queue.TryDequeue(out _)) { }
            }
        }

        // MARK: Background Async

        /// <summary>
        /// Enqueue an event to the correct per-context queue.
        /// </summary>
        static void EnqueueEvent(WebSocketEvent evt) {
            if (_contextQueues.TryGetValue(evt.ContextId, out var queue)) {
                queue.Enqueue(evt);
            }
        }

        static async Task ConnectAndReceiveAsync(int id, string url, SocketState state) {
            int ctxId = state.ContextId;

            try {
                await state.Socket.ConnectAsync(new Uri(url), state.Cts.Token);
                // Pass negotiated sub-protocol in the Data field of the "open" event
                EnqueueEvent(new WebSocketEvent {
                    SocketId = id, ContextId = ctxId, Type = "open",
                    Data = state.Socket.SubProtocol ?? ""
                });
            } catch (Exception ex) {
                EnqueueEvent(new WebSocketEvent {
                    SocketId = id, ContextId = ctxId, Type = "error", Data = ex.Message
                });
                EnqueueEvent(new WebSocketEvent {
                    SocketId = id, ContextId = ctxId, Type = "close", Code = 1006,
                    Reason = "Connection failed"
                });
                CleanupSocket(id);
                return;
            }

            // Receive loop
            var buffer = new byte[8192];
            var messageBuffer = new MemoryStream();

            try {
                while (state.Socket.State == WebSocketState.Open && !state.Cts.IsCancellationRequested) {
                    var segment = new ArraySegment<byte>(buffer);
                    var result = await state.Socket.ReceiveAsync(segment, state.Cts.Token);

                    if (result.MessageType == WebSocketMessageType.Close) {
                        int closeCode = (int)(state.Socket.CloseStatus ?? WebSocketCloseStatus.NormalClosure);
                        string closeReason = state.Socket.CloseStatusDescription ?? "";
                        EnqueueEvent(new WebSocketEvent {
                            SocketId = id, ContextId = ctxId, Type = "close",
                            Code = closeCode, Reason = closeReason
                        });
                        break;
                    }

                    messageBuffer.Write(buffer, 0, result.Count);

                    if (result.EndOfMessage) {
                        bool isBinary = result.MessageType == WebSocketMessageType.Binary;
                        string messageData;

                        if (isBinary) {
                            messageData = Convert.ToBase64String(
                                messageBuffer.GetBuffer(), 0, (int)messageBuffer.Length);
                        } else {
                            messageData = Encoding.UTF8.GetString(
                                messageBuffer.GetBuffer(), 0, (int)messageBuffer.Length);
                        }

                        EnqueueEvent(new WebSocketEvent {
                            SocketId = id, ContextId = ctxId, Type = "message",
                            Data = messageData, IsBinary = isBinary
                        });
                        messageBuffer.SetLength(0);
                    }
                }
            } catch (OperationCanceledException) {
                // Normal shutdown
            } catch (WebSocketException ex) {
                EnqueueEvent(new WebSocketEvent {
                    SocketId = id, ContextId = ctxId, Type = "error", Data = ex.Message
                });
                EnqueueEvent(new WebSocketEvent {
                    SocketId = id, ContextId = ctxId, Type = "close", Code = 1006,
                    Reason = "Connection lost"
                });
            } catch (Exception ex) {
                EnqueueEvent(new WebSocketEvent {
                    SocketId = id, ContextId = ctxId, Type = "error", Data = ex.Message
                });
                EnqueueEvent(new WebSocketEvent {
                    SocketId = id, ContextId = ctxId, Type = "close", Code = 1006,
                    Reason = ex.Message
                });
            } finally {
                messageBuffer.Dispose();
                CleanupSocket(id);
            }
        }

        static async Task ProcessSendQueueAsync(int id, SocketState state) {
            try {
                while (state.SendQueue.TryDequeue(out var item)) {
                    if (state.Socket.State != WebSocketState.Open) break;

                    if (item.IsBinary) {
                        var segment = new ArraySegment<byte>(item.BinaryData);
                        await state.Socket.SendAsync(
                            segment, WebSocketMessageType.Binary, true, state.Cts.Token);
                    } else {
                        var bytes = Encoding.UTF8.GetBytes(item.Data);
                        var segment = new ArraySegment<byte>(bytes);
                        await state.Socket.SendAsync(
                            segment, WebSocketMessageType.Text, true, state.Cts.Token);
                    }
                }
            } catch (Exception ex) {
                EnqueueEvent(new WebSocketEvent {
                    SocketId = id, ContextId = state.ContextId, Type = "error", Data = ex.Message
                });
            } finally {
                Interlocked.Exchange(ref state.SendLoopFlag, 0);

                // Check if more items were enqueued while we were finishing
                if (!state.SendQueue.IsEmpty && state.Socket.State == WebSocketState.Open) {
                    if (Interlocked.CompareExchange(ref state.SendLoopFlag, 1, 0) == 0) {
                        _ = Task.Run(() => ProcessSendQueueAsync(id, state));
                    }
                }
            }
        }

        static void CleanupSocket(int id) {
            if (_sockets.TryRemove(id, out var state)) {
                try { state.Cts.Cancel(); } catch { }
                try { state.Socket.Dispose(); } catch { }
                try { state.Cts.Dispose(); } catch { }
            }
        }

        // MARK: JS Dispatch

        static void DispatchToJs(QuickJSContext ctx, WebSocketEvent evt) {
            string dataEscaped = EscapeJsString(evt.Data ?? "");
            string reasonEscaped = EscapeJsString(evt.Reason ?? "");
            string isBinary = evt.IsBinary ? "true" : "false";
            string code = $"__dispatchWebSocketEvent({evt.SocketId},\"{evt.Type}\",\"{dataEscaped}\",{evt.Code},\"{reasonEscaped}\",{isBinary})";
            ctx.Eval(code, "<ws-event>");
            ctx.ExecutePendingJobs();
        }

        static string EscapeJsString(string s) {
            if (string.IsNullOrEmpty(s)) return s;
            return s
                .Replace("\\", "\\\\")
                .Replace("\"", "\\\"")
                .Replace("\n", "\\n")
                .Replace("\r", "\\r")
                .Replace("\t", "\\t")
                .Replace("\0", "\\0")
                .Replace("\u2028", "\\u2028")
                .Replace("\u2029", "\\u2029");
        }
    }
}
