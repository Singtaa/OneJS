using System.IO;
using System.Security.Cryptography;
using System.Text;
using LiteNetLib;
using LiteNetLib.Utils;
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

        [SerializeField]
        bool _runOnStart = true;

#pragma warning disable 414
        [Tooltip(
            "Turn on Live Reload for Standalone build. Remember to turn this off for production deployment where " +
            "you don't need Live Reload.")]
        [SerializeField]
        bool _turnOnForStandalone = true;
#pragma warning restore 414

        [Tooltip("Should be a .js file relative to the WorkingDir, which by default is '{projectDir}/OneJS' for editor-time, " +
                 "and '{persistentDataPath}/OneJS' for standalone build.")]
        [SerializeField]
        string _entryScript = "index.js";
        [SerializeField] string _watchFilter = "*.js";


        // Net Sync is disabled for this initial version of OneJS. Will come in the very next update.
        [Tooltip("Allows Live Reload to work across devices (i.e. edit code on PC, live reload on mobile device." + "")]
        [SerializeField]
        bool _netSync;
        [Tooltip("`Server` broadcasts the file changes. `Client` receives the changes. `Auto` means Server for " +
                 "desktop, and Client for mobile.")]
        [SerializeField]
        ENetSyncMode _mode = ENetSyncMode.Auto;
        [Tooltip(
            "Port for both Server and Client. (Client also listens on a port for better discovery across devices.)")]
        [SerializeField]
        int _port = 9050;
        
        [Tooltip(
            "Explicit IP address to use for server. Use this if you are having problems with network discovery. Keep this empty to use auto discovery.")]
        [SerializeField]
        string _serverIP = "";
        [Tooltip(
            "Set this to true when you need to Net Sync apps on the same device. (i.e. between Editor and Standalone)")]
        [SerializeField]
        bool _useRandomPortForClient = false;

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
            _scriptEngine = GetComponent<ScriptEngine>();
            _workingDir = _scriptEngine.WorkingDir;
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
                    _net = new NetManager(_server) {
                        BroadcastReceiveEnabled = true,
                        UnconnectedMessagesEnabled = true,
                        IPv6Mode = IPv6Mode.Disabled
                    };
                    _server.NetManager = _net;
                    _net.Start(_port);
                    print($"[Server] Net Sync On (port {_port})");
                } else {
                    // Runnning as Client
                    _client = new ClientListener(_port, _scriptEngine);
                    _net = _client.InitNetManager();
                    _client.OnFileChanged += () => { Reload(); };
                    _client.Start(_useRandomPortForClient);

                    print($"[Client] Net Sync On (port {_net.LocalPort})");
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
            Debug.Log($"Live Reload On (entry script: {_entryScript})");
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

        void OnApplicationFocus(bool focusStatus) {
            // print($"OnFocus ({focusStatus})");
            if (!focusStatus) {
                if (_netSync && IsClient) {
                    _client.Stop();
                }
            } else {
                if (_netSync && IsClient) {
                    _client.Start(_useRandomPortForClient);
                }
            }
        }

        void OnApplicationPause(bool pauseStatus) {
            // print($"OnPause ({pauseStatus})");
        }

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
                    _client.BroadcastForServer(_serverIP);
                }
            }
        }

        [ContextMenu("Reload")]
        public void Reload() {
            _scriptEngine.ReloadAndRunScript(_entryScript);
        }
        
        public void SetEntryScriptAndReload(string path) {
            if (!File.Exists(Path.Combine(_scriptEngine.WorkingDir, path))) {
                print("File not found: " + path);
                return;
            }
            _entryScript = path;
            Reload();
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
            Reload();
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