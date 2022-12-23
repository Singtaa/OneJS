using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using UnityEngine;

namespace OneJS.Engine {
    public class CustomWatcher {
        public int Interval { get; set; } = 200;

        public event Action<string[]> OnChangeDetected;

        Dictionary<string, DateTime> _fileHashDict;
        string _dir;
        string _filter;
        string[] _detectedPaths = new string[] { };

        Thread _thread;
        object _obj = new object();

        public CustomWatcher(string dir, string filter) {
            _dir = dir;
            _filter = filter;
            _fileHashDict = GetFileHashDict();
        }

        public void Start() {
            _thread = new Thread(WatchLoop);
            _thread.Start();
        }

        public void Stop() {
            if (_thread != null && _thread.IsAlive)
                _thread.Abort();
        }

        public void Poll() {
            lock (_obj) {
                if (_detectedPaths.Length > 0) {
                    OnChangeDetected?.Invoke(_detectedPaths);
                    _detectedPaths = new string[] { };
                }
            }
        }

        void WatchLoop() {
            while (true) {
                Thread.Sleep(Interval);
                CheckForChanges();
            }
        }

        void CheckForChanges() {
            var newDict = GetFileHashDict();
            var res = new List<string>();
            foreach (var kvp in newDict) {
                var path = kvp.Key;
                var dt = kvp.Value;
                if (!_fileHashDict.ContainsKey(path)) {
                    res.Add(path);
                    _fileHashDict.Add(path, dt);
                } else if (_fileHashDict[path] != dt) {
                    res.Add(path);
                    _fileHashDict[path] = dt;
                }
            }
            lock (_obj) {
                _detectedPaths = res.ToArray();
            }
        }

        Dictionary<string, DateTime> GetFileHashDict() {
            var res = new Dictionary<string, DateTime>();
            var files = Directory.GetFiles(_dir, _filter, SearchOption.AllDirectories);
            foreach (var path in files) {
                var fi = new FileInfo(path);
                res.Add(path, fi.LastWriteTimeUtc);
            }
            // DirectoryInfo dirInfo = new DirectoryInfo(_dir);
            // var fis = dirInfo.EnumerateFiles(_filter, SearchOption.AllDirectories)
            //     .AsParallel().ToArray();
            // foreach (var fi in fis) {
            //     res.Add(fi.FullName, fi.LastWriteTimeUtc);
            // }
            return res;
        }

        string GetMD5(string filepath) {
            using (var md5 = MD5.Create()) {
                using (var stream = File.OpenRead(filepath)) {
                    return Encoding.Default.GetString(md5.ComputeHash(stream));
                }
            }
        }
    }
}