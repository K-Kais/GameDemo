using System;

namespace GameDemo.Network
{
    [Serializable]
    public sealed class AuthRequest
    {
        public string userName = string.Empty;
        public string password = string.Empty;
    }

    [Serializable]
    public sealed class AuthResponse
    {
        public string token = string.Empty;
        public string userId = string.Empty;
        public string userName = string.Empty;
    }

    [Serializable]
    public sealed class ErrorResponse
    {
        public string message = string.Empty;
    }

    [Serializable]
    public sealed class JoinMapRequest
    {
        public string mapId = "world";
    }

    [Serializable]
    public sealed class MoveRequest
    {
        public float x;
        public float y;
    }

    [Serializable]
    public sealed class ChatRequest
    {
        public string message = string.Empty;
    }

    [Serializable]
    public sealed class MapPlayerSnapshot
    {
        public string playerId = string.Empty;
        public string userName = string.Empty;
        public float x;
        public float y;
    }

    [Serializable]
    public sealed class MapSnapshotMessage
    {
        public string mapId = string.Empty;
        public MapPlayerSnapshot[] players = Array.Empty<MapPlayerSnapshot>();
    }

    [Serializable]
    public sealed class PlayerMapMessage
    {
        public string playerId = string.Empty;
        public string userName = string.Empty;
        public string mapId = string.Empty;
    }

    [Serializable]
    public sealed class MoveMessage
    {
        public string playerId = string.Empty;
        public float x;
        public float y;
    }

    [Serializable]
    public sealed class ChatMessage
    {
        public string from = string.Empty;
        public string userName = string.Empty;
        public string message = string.Empty;
        public string sentAtUtc = string.Empty;
    }

    [Serializable]
    public sealed class PongMessage
    {
        public string serverTimeUtc = string.Empty;
    }

    [Serializable]
    public sealed class ServerErrorMessage
    {
        public string error = string.Empty;
    }
}
