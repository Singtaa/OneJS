using System.Net;
using System.Net.Sockets;
using LiteNetLib;
using LiteNetLib.Utils;
using UnityEngine;

namespace OneJS.Engine {
    public class ServerListener : INetEventListener {
        public NetManager NetManager { get; set; }

        public void SendToAllClients(NetDataWriter writer) {
            foreach (var peer in NetManager.ConnectedPeerList) {
                peer.Send(writer, DeliveryMethod.ReliableOrdered);
            }
        }

        public void OnPeerConnected(NetPeer peer) {
            Debug.Log("[Server] Peer connected: " + peer.EndPoint);
        }

        public void OnPeerDisconnected(NetPeer peer, DisconnectInfo disconnectInfo) {
            Debug.Log("[Server] Peer disconnected: " + peer.EndPoint);
        }

        public void OnNetworkError(IPEndPoint endPoint, SocketError socketError) {
        }

        public void OnNetworkReceive(NetPeer peer, NetPacketReader reader, byte channelNumber,
            DeliveryMethod deliveryMethod) {
        }

        public void OnNetworkReceiveUnconnected(IPEndPoint remoteEndPoint, NetPacketReader reader,
            UnconnectedMessageType messageType) {
            Debug.Log($"[Server] ReceiveUnconnected {messageType}. From: {remoteEndPoint}.");
            NetDataWriter wrtier = new NetDataWriter();
            wrtier.Put("SERVER_DISCOVERY_RESPONSE");
            NetManager.SendUnconnectedMessage(wrtier, remoteEndPoint);
        }

        public void OnNetworkLatencyUpdate(NetPeer peer, int latency) {
        }

        public void OnConnectionRequest(ConnectionRequest request) {
            request.AcceptIfKey("key");
        }
    }
}