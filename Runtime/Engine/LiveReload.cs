using System.IO;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using LiteNetLib;
using LiteNetLib.Utils;
using NaughtyAttributes;
using UnityEngine;

namespace OneJS.Engine {
    public enum ENetSyncMode {
        Auto,
        Server,
        Client
    }

    [DefaultExecutionOrder(100)]
    [RequireComponent(typeof(ScriptEngine))]
    public class LiveReload : MonoBehaviour {
        public bool IsServer => _mode == ENetSyncMode.Server || _mode == ENetSyncMode.Auto &&
            !Application.isMobilePlatform && !Application.isConsolePlatform;

        public bool IsClient => !IsServer;

        [InfoBox("This component will watch the `{ProjectDir}/OneJS` folder for you. When change is detected, " +
                 "the script engine will reload and the entry script will be re-run.")]
        [SerializeField] [Label("Run on Start")] bool _runOnStart = true;

#pragma warning disable 414
        [Tooltip(
            "Turn on Live Reload for Standalone build. Remember to turn this off for production deployment where " +
            "you don't need Live Reload.")]
        [SerializeField] bool _turnOnForStandalone = true;
#pragma warning restore 414

        [Tooltip("Should be a .js file relative to your `persistentDataPath`." +
                 "")]
        [SerializeField] string _entryScript = "index.js";
        [SerializeField] string _watchFilter = "*.js";


        // Net Sync is disabled for this initial version of OneJS. Will come in the very next update.
        [Tooltip("Allows Live Reload to work across devices (i.e. edit code on PC, live reload on mobile device." + "")]
        [SerializeField] bool _netSync;
        [Tooltip("`Server` broadcasts the file changes. `Client` receives the changes. `Auto` means Server for " +
                 "desktop, and Client for mobile.")]
        [SerializeField] [ShowIf("_netSync")] ENetSyncMode _mode = ENetSyncMode.Auto;
        [Tooltip("Server's port. Any unused port will do.")]
        [SerializeField] [ShowIf("_netSync")] int _port = 9050;

        ScriptEngine _scriptEngine;
        // FileSystemWatcher _watcher;
        string _workingDir;

        CustomWatcher _watcher;
        string[] _changedFilePaths;

        NetManager _net;
        ClientListener _client;
        ServerListener _server;
        int _tick;

        void Awake() {
            _workingDir = ScriptEngine.WorkingDir;
            _scriptEngine = GetComponent<ScriptEngine>();
        }

        void OnDestroy() {
            if (_netSync) {
                _net.Stop();
            }
        }

        void Start() {
#if !UNITY_EDITOR && (UNITY_STANDALONE || UNITY_IOS || UNITY_ANDROID)
            if (!_turnOnForStandalone) {
                if (_runOnStart) {
                    _scriptEngine.RunScript(_entryScript);
                }
                return;
            }
#endif
            if (_netSync) {
                if (IsServer) {
                    // Running as Server
                    _server = new ServerListener(_port);
                    _net = new NetManager(_server)
                        { BroadcastReceiveEnabled = true, UnconnectedMessagesEnabled = true };
                    _server.NetManager = _net;
                    _net.Start(_port);
                    print($"[Server] Net Sync On (port {_port})");
                } else {
                    // Runnning as Client
                    _client = new ClientListener(_port);
                    _net = new NetManager(_client)
                        { BroadcastReceiveEnabled = true, UnconnectedMessagesEnabled = true };
                    _client.NetManager = _net;
                    _client.OnFileChanged += () => { RunScript(); };
                    _net.Start(_port);
                    print($"[Client] Net Sync On (port {_port})");
                }
            }
            if (_runOnStart) {
                _scriptEngine.RunScript(_entryScript);
            }
        }

        void OnEnable() {
#if !UNITY_EDITOR && (UNITY_STANDALONE || UNITY_IOS || UNITY_ANDROID)
            if (!_turnOnForStandalone)
                return;
#endif
            if (_netSync && IsClient)
                return;
            _watcher = new CustomWatcher(_workingDir, _watchFilter);
            _watcher.OnChangeDetected += OnFileChangeDetected;
            _watcher.Start();
            Debug.Log($"Live Reload On");
        }

        void OnDisable() {
#if !UNITY_EDITOR && (UNITY_STANDALONE || UNITY_IOS || UNITY_ANDROID)
            if (!_turnOnForStandalone)
                return;
#endif
            if (_netSync && IsClient)
                return;
            _watcher.Stop();
        }

        // void OnApplicationPause(bool pauseStatus) {
        //     
        // }

        void Update() {
#if !UNITY_EDITOR && (UNITY_STANDALONE || UNITY_IOS || UNITY_ANDROID)
            if (!_turnOnForStandalone)
                return;
#endif
            if (!_netSync || !IsClient) {
                _tick++;
                _watcher.Poll();
            }
            if (_netSync) {
                _net.PollEvents();
                if (IsServer) {
                    _server.Broadcast();
                }
                if (IsClient) {
                    _client.BroadcastForServer();
                }
            }
        }

        void RunScript() {
            _scriptEngine.ReloadAndRunScript(_entryScript);
        }

        void OnFileChangeDetected(string[] paths) {
            if (_netSync && IsServer) {
                NetDataWriter writer = new NetDataWriter();
                writer.Put("LIVE_RELOAD_NET_SYNC");
                writer.Put(_tick);
                writer.Put("UPDATE_FILES");
                writer.Put(paths.Length);
                foreach (var p in paths) {
                    // Note the slashes. On Android, different slashes will be treated as different paths. (Very hard to debug)
                    writer.Put(Path.GetRelativePath(_workingDir, p).Replace(@"\", @"/"));
                    writer.Put(File.ReadAllText(p));
                }
                _server.SendToAllClients(writer);
            }
            RunScript();
        }

        public string GetMD5(string filepath) {
            using (var md5 = MD5.Create()) {
                using (var stream = File.OpenRead(filepath)) {
                    return Encoding.Default.GetString(md5.ComputeHash(stream));
                }
            }
        }

//         [Button("Manual Run")]
//         void ManualRun() {
// #if UNITY_EDITOR
//             if (!Application.isPlaying) {
//                 _scriptEngine = GetComponent<ScriptEngine>();
//                 _scriptEngine.Awake();
//             }
// #endif
//             _scriptEngine.ReloadAndRunScript(_entryScript);
//         }
    }
}