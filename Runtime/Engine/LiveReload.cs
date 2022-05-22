using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using LiteNetLib;
using LiteNetLib.Utils;
using NaughtyAttributes;
using UnityEngine;
using UnityEngine.Serialization;

namespace OneJS.Engine {
    public enum ENetSyncMode {
        Auto,
        Server,
        Client
    }

    [RequireComponent(typeof(ScriptEngine))]
    public class LiveReload : MonoBehaviour {
        public bool IsServer => _mode == ENetSyncMode.Server || _mode == ENetSyncMode.Auto &&
            !Application.isMobilePlatform && !Application.isConsolePlatform;

        public bool IsClient => !IsServer;

        [InfoBox("This component will watch a single file for you. When change is detected, " +
                 "the engine will reload and the script will be re-run.")]
        [SerializeField] [Label("Run on Start")] bool _runOnStart = true;

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
        FileSystemWatcher _watcher;
        string _workingDir;

        bool _fileChanged;
        Dictionary<string, string> _fileHashDict = new Dictionary<string, string>();
        HashSet<string> _potentialChangedFilePaths = new HashSet<string>();

        NetManager _net;
        ClientListener _client;
        ServerListener _server;
        int _tick;

        void Awake() {
            _workingDir = Application.persistentDataPath;
            _scriptEngine = GetComponent<ScriptEngine>();
        }

        void OnDestroy() {
            if (_netSync) {
                _net.Stop();
            }
        }

        void Start() {
            _tick++;
            if (_netSync) {
                InitFileHashDict();
                if (IsServer) {
                    // Running as Server
                    _server = new ServerListener();
                    _net = new NetManager(_server) { BroadcastReceiveEnabled = true };
                    _server.NetManager = _net;
                    _net.Start(_port);
                } else {
                    // Runnning as Client
                    _client = new ClientListener(_port);
                    _net = new NetManager(_client) { UnconnectedMessagesEnabled = true };
                    _client.NetManager = _net;
                    _client.OnFileChanged += () => { _fileChanged = true; };
                    _net.Start();
                }
                print("Net Sync On");
            }
            if (_runOnStart) {
                _scriptEngine.RunScript(_entryScript);
            }
        }

        void OnEnable() {
            if (_netSync && IsClient)
                return;
            _watcher = new FileSystemWatcher(Application.persistentDataPath);
            _watcher.NotifyFilter = NotifyFilters.CreationTime | NotifyFilters.DirectoryName | NotifyFilters.FileName |
                                    NotifyFilters.LastWrite | NotifyFilters.LastAccess | NotifyFilters.Attributes |
                                    NotifyFilters.Size | NotifyFilters.Security;
            _watcher.IncludeSubdirectories = true;
            _watcher.EnableRaisingEvents = true;
            _watcher.Filter = _watchFilter;
            _watcher.Changed += OnWatchEvent;
            _watcher.Deleted += OnWatchEvent;
            _watcher.Created += OnWatchEvent;
            _watcher.Renamed += OnWatchEvent;
            Debug.Log($"Live Reload On");
        }

        void OnDisable() {
            if (_netSync && IsClient)
                return;
            _watcher.Dispose();
        }

        void Update() {
            if (_netSync) {
                _net.PollEvents();
                if (IsClient) {
                    _client.BroadcastForServer();
                } else if (IsServer) {
                    if (_potentialChangedFilePaths.Count > 0) {
                        NetDataWriter writer = new NetDataWriter();
                        writer.Put("LIVE_RELOAD_NET_SYNC");
                        writer.Put(_tick);
                        writer.Put("UPDATE_FILES");
                        writer.Put(_potentialChangedFilePaths.Count);
                        foreach (var p in _potentialChangedFilePaths.ToArray()) {
                            // Note the slashes. On Android, different slashes will be treated as different paths. (Very hard to debug)
                            writer.Put(Path.GetRelativePath(_workingDir, p).Replace(@"\", @"/"));
                            writer.Put(File.ReadAllText(p));
                        }
                        _server.SendToAllClients(writer);
                        _potentialChangedFilePaths.Clear();
                    }
                }
            }
            if (_fileChanged) {
                _fileChanged = false;
                _scriptEngine.ReloadAndRunScript(_entryScript);
            }
        }

        /// <summary>
        /// NOTE: Mono's implementation of FileSystemEventArgs.FullPath is bugged
        /// It's not really returning the fullpath for nested files 
        /// </summary>
        void OnWatchEvent(object sender, FileSystemEventArgs e) {
            if (!e.Name.EndsWith(".js"))
                return;
            _fileChanged = true;
            if (_netSync && IsServer) {
                var paths = GetPotentialFilePaths(e.Name);
                foreach (var p in paths) {
                    if (!_potentialChangedFilePaths.Contains(p))
                        _potentialChangedFilePaths.Add(p);
                }
            }
        }

        void InitFileHashDict() {
            var files = Directory.GetFiles(Application.persistentDataPath, "*.js", SearchOption.AllDirectories);
            foreach (var path in files) {
                _fileHashDict.Add(path, GetMD5(path));
            }
        }

        string[] GetPotentialFilePaths(string filename) {
            List<string> res = new List<string>();
            var files = Directory.GetFiles(_workingDir, "*.js", SearchOption.AllDirectories);
            var potentialPaths = files.Where(f => f.EndsWith(filename)).ToArray();

            foreach (var path in potentialPaths) {
                if (_fileHashDict.ContainsKey(path)) {
                    var newHash = GetMD5(path);
                    if (newHash == _fileHashDict[path])
                        continue;
                    res.Add(path);
                    _fileHashDict[path] = newHash;
                } else {
                    res.Add(path);
                    _fileHashDict.Add(path, GetMD5(path));
                }
            }
            return res.ToArray();
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