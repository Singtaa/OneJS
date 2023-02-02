using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using LiteNetLib;
using LiteNetLib.Utils;
using UnityEngine;

namespace OneJS.Engine {
    public class ClientListener : INetEventListener {
        public NetManager NetManager { get; private set; }
        public bool ConnectedToServer => _serverPeer != null;

        public event Action OnFileChanged;

        DateTime _lastBroadcastTime;
        int _serverPort;
        NetPeer _serverPeer;
        ScriptEngine _scriptEngine;

        public ClientListener(int serverPort, ScriptEngine scriptEngine) {
            _scriptEngine = scriptEngine;
            _lastBroadcastTime = DateTime.Now.AddSeconds(-100);
            _serverPort = serverPort;
        }

        public NetManager InitNetManager() {
            NetManager = new NetManager(this) {
                BroadcastReceiveEnabled = true,
                UnconnectedMessagesEnabled = true,
                IPv6Mode = IPv6Mode.Disabled
            };
            return NetManager;
        }

        public void Start(bool useRandomPortForClient) {
            if (useRandomPortForClient)
                NetManager.Start();
            else
                NetManager.Start(_serverPort);
        }

        public void Stop() {
            if (_serverPeer != null) {
                _serverPeer.Disconnect();
                _serverPeer = null;
            }
            NetManager.Stop();
        }

        public void BroadcastForServer(string serverIP = "") {
            if (_serverPeer != null || (DateTime.Now - _lastBroadcastTime).TotalSeconds < 10) {
                return;
            }
            if (!String.IsNullOrEmpty(serverIP)) {
                Debug.Log("Attempting to Connect to Server");
                NetManager.Connect(serverIP, _serverPort, "key");
                _lastBroadcastTime = DateTime.Now;
                return;
            }
            NetDataWriter writer = new NetDataWriter();
            writer.Put("LOOKING_FOR_SERVER");
            NetManager.SendBroadcast(writer, _serverPort);
            _lastBroadcastTime = DateTime.Now;
        }

        public void OnPeerConnected(NetPeer peer) {
            Debug.Log($"[Client {NetManager.LocalPort}] connected to: {peer.EndPoint.Address}:{peer.EndPoint.Port}");
            // _connectedToServer = true;
            _serverPeer = peer;
        }

        public void OnPeerDisconnected(NetPeer peer, DisconnectInfo disconnectInfo) {
            // _connectedToServer = false;
            _serverPeer = null;
        }

        public void OnNetworkError(IPEndPoint endPoint, SocketError socketError) {
            // _connectedToServer = false;
            _serverPeer = null;
        }

        public void OnNetworkReceive(NetPeer peer, NetPacketReader reader, byte channelNumber,
            DeliveryMethod deliveryMethod) {
            var type = reader.GetString();
            if (type != "LIVE_RELOAD_NET_SYNC")
                return;
            var tickNumber = reader.GetInt();
            var cmd = reader.GetString();
            if (cmd == "UPDATE_FILES") {
                var num = reader.GetInt();
                // Debug.Log($"[Server] UPDATE_FILES {num} files");
                for (int i = 0; i < num; i++) {
                    var path = reader.GetString();
                    var text = reader.GetString();
                    File.WriteAllText(Path.Combine(_scriptEngine.WorkingDir, path), text);
                }
                OnFileChanged?.Invoke();
            }
        }

        public void OnNetworkReceiveUnconnected(IPEndPoint remoteEndPoint, NetPacketReader reader,
            UnconnectedMessageType messageType) {
            var text = reader.GetString(100);
            if (text == "SERVER_DISCOVERY_RESPONSE" && _serverPeer == null) {
                Debug.Log($"[Client] SERVER_DISCOVERY_RESPONSE received. From: {remoteEndPoint}.");
                NetManager.Connect(remoteEndPoint, "key");
            }
        }

        public void OnNetworkLatencyUpdate(NetPeer peer, int latency) {
        }

        public void OnConnectionRequest(ConnectionRequest request) {
        }
    }
}