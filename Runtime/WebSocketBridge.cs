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
    /// - QuickJSUIBridge.Tick() calls ProcessEvents() to dispatch to JS
    /// </summary>
    public static class WebSocketBridge {
        static int _nextSocketId = 1;
        static readonly ConcurrentDictionary<int, SocketState> _sockets = new();
        static readonly ConcurrentQueue<WebSocketEvent> _events = new();

        const int MaxEventsPerTick = 50;

        struct WebSocketEvent {
            public int SocketId;
            public string Type;   // "open", "message", "error", "close"
            public string Data;   // message text or error message
            public int Code;      // close code
            public string Reason; // close reason
        }

        class SocketState {
            public ClientWebSocket Socket;
            public CancellationTokenSource Cts;
            public readonly ConcurrentQueue<SendItem> SendQueue = new();
            public volatile bool SendLoopRunning;
        }

        struct SendItem {
            public string Data;
        }

        /// <summary>
        /// Open a new WebSocket connection. Returns a socket ID immediately.
        /// Connection happens asynchronously; "open" or "error" event fires when ready.
        /// </summary>
        public static int Connect(string url, string protocols) {
            int id = _nextSocketId++;
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
            var state = new SocketState { Socket = ws, Cts = cts };
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

            if (!state.SendLoopRunning) {
                state.SendLoopRunning = true;
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
        /// Process queued events and dispatch to JS.
        /// Called from QuickJSUIBridge.Tick() on the main thread.
        /// </summary>
        public static int ProcessEvents(QuickJSContext ctx) {
            if (ctx == null) return 0;

            int processed = 0;
            while (processed < MaxEventsPerTick && _events.TryDequeue(out var evt)) {
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
        /// Close all WebSocket connections. Call on context destruction / live reload.
        /// </summary>
        public static void CloseAll() {
            foreach (var kvp in _sockets) {
                try {
                    kvp.Value.Cts.Cancel();
                    kvp.Value.Socket.Dispose();
                } catch { }
            }
            _sockets.Clear();
            while (_events.TryDequeue(out _)) { }
        }

        // MARK: Background Async

        static async Task ConnectAndReceiveAsync(int id, string url, SocketState state) {
            try {
                await state.Socket.ConnectAsync(new Uri(url), state.Cts.Token);
                _events.Enqueue(new WebSocketEvent { SocketId = id, Type = "open" });
            } catch (Exception ex) {
                _events.Enqueue(new WebSocketEvent {
                    SocketId = id, Type = "error", Data = ex.Message
                });
                _events.Enqueue(new WebSocketEvent {
                    SocketId = id, Type = "close", Code = 1006,
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
                        _events.Enqueue(new WebSocketEvent {
                            SocketId = id, Type = "close",
                            Code = closeCode, Reason = closeReason
                        });
                        break;
                    }

                    messageBuffer.Write(buffer, 0, result.Count);

                    if (result.EndOfMessage) {
                        var messageData = Encoding.UTF8.GetString(
                            messageBuffer.GetBuffer(), 0, (int)messageBuffer.Length);
                        _events.Enqueue(new WebSocketEvent {
                            SocketId = id, Type = "message", Data = messageData
                        });
                        messageBuffer.SetLength(0);
                    }
                }
            } catch (OperationCanceledException) {
                // Normal shutdown
            } catch (WebSocketException ex) {
                _events.Enqueue(new WebSocketEvent {
                    SocketId = id, Type = "error", Data = ex.Message
                });
                _events.Enqueue(new WebSocketEvent {
                    SocketId = id, Type = "close", Code = 1006,
                    Reason = "Connection lost"
                });
            } catch (Exception ex) {
                _events.Enqueue(new WebSocketEvent {
                    SocketId = id, Type = "error", Data = ex.Message
                });
                _events.Enqueue(new WebSocketEvent {
                    SocketId = id, Type = "close", Code = 1006,
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

                    var bytes = Encoding.UTF8.GetBytes(item.Data);
                    var segment = new ArraySegment<byte>(bytes);
                    await state.Socket.SendAsync(
                        segment, WebSocketMessageType.Text, true, state.Cts.Token);
                }
            } catch (Exception ex) {
                _events.Enqueue(new WebSocketEvent {
                    SocketId = id, Type = "error", Data = ex.Message
                });
            } finally {
                state.SendLoopRunning = false;

                // Check if more items were enqueued while we were finishing
                if (!state.SendQueue.IsEmpty && state.Socket.State == WebSocketState.Open) {
                    state.SendLoopRunning = true;
                    _ = Task.Run(() => ProcessSendQueueAsync(id, state));
                }
            }
        }

        static void CleanupSocket(int id) {
            if (_sockets.TryRemove(id, out var state)) {
                try { state.Socket.Dispose(); } catch { }
                try { state.Cts.Dispose(); } catch { }
            }
        }

        // MARK: JS Dispatch

        static void DispatchToJs(QuickJSContext ctx, WebSocketEvent evt) {
            string dataEscaped = EscapeJsString(evt.Data ?? "");
            string reasonEscaped = EscapeJsString(evt.Reason ?? "");
            string code = $"__dispatchWebSocketEvent({evt.SocketId},\"{evt.Type}\",\"{dataEscaped}\",{evt.Code},\"{reasonEscaped}\")";
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
                .Replace("\t", "\\t");
        }
    }
}
