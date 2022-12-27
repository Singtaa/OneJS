using System;
using System.Net;
using System.Net.Sockets;
using LiteNetLib;
using LiteNetLib.Utils;
using UnityEngine;

namespace OneJS.Engine {
    public class ServerListener : INetEventListener {
        public NetManager NetManager { get; set; }

        DateTime _lastBroadcastTime;
        int _broadcastPort;

        public ServerListener(int broadcastPort) {
            _lastBroadcastTime = DateTime.Now.AddSeconds(-100);
            _broadcastPort = broadcastPort;
        }

        public void Broadcast() {
            if ((DateTime.Now - _lastBroadcastTime).TotalSeconds < 10) {
                return;
            }
            NetDataWriter writer = new NetDataWriter();
            writer.Put("SERVER_DISCOVERY_RESPONSE");
            NetManager.SendBroadcast(writer, _broadcastPort);
            _lastBroadcastTime = DateTime.Now;
        }

        public void SendToAllClients(NetDataWriter writer) {
            foreach (var peer in NetManager.ConnectedPeerList) {
                peer.Send(writer, DeliveryMethod.ReliableOrdered);
            }
        }

        public void OnPeerConnected(NetPeer peer) {
            Debug.Log("[Server] Peer connected: " + peer.EndPoint);
        }

        public void OnPeerDisconnected(NetPeer peer, DisconnectInfo disconnectInfo) {
            Debug.Log("[Server] Peer disconnected: " + peer.EndPoint + " REASON: " + disconnectInfo.Reason);
        }

        public void OnNetworkError(IPEndPoint endPoint, SocketError socketError) {
        }

        public void OnNetworkReceive(NetPeer peer, NetPacketReader reader, byte channelNumber,
            DeliveryMethod deliveryMethod) {
        }

        public void OnNetworkReceiveUnconnected(IPEndPoint remoteEndPoint, NetPacketReader reader,
            UnconnectedMessageType messageType) {
            var text = reader.GetString(100);
            if (text == "LOOKING_FOR_SERVER") {
                Debug.Log($"[Server] LOOKING_FOR_SERVER received. From: {remoteEndPoint}.");
                NetDataWriter wrtier = new NetDataWriter();
                wrtier.Put("SERVER_DISCOVERY_RESPONSE");
                NetManager.SendUnconnectedMessage(wrtier, remoteEndPoint);
            }
        }

        public void OnNetworkLatencyUpdate(NetPeer peer, int latency) {
        }

        public void OnConnectionRequest(ConnectionRequest request) {
            request.AcceptIfKey("key");
        }
    }
}