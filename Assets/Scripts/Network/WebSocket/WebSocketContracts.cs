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
    public sealed class RegisterRequest
    {
        public string userName = string.Empty;
        public string password = string.Empty;
        public string confirmPassword = string.Empty;
        public bool acceptedTerms;
    }

    [Serializable]
    public sealed class CreateCharacterRequest
    {
        public string characterName = string.Empty;
    }

    [Serializable]
    public sealed class AuthResponse
    {
        public string token = string.Empty;
        public string userId = string.Empty;
        public string userName = string.Empty;
        public bool requiresCharacterCreation;
        public string characterName = string.Empty;
    }

    [Serializable]
    public sealed class ActionResponse
    {
        public string message = string.Empty;
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
        public float dirX;
        public float dirY;
        public int characterIndex = -1;
        public string state = string.Empty;
        public float currentHp;
        public float maxHp;
    }

    [Serializable]
    public sealed class MapSnapshotMessage
    {
        public string mapId = string.Empty;
        public string selfPlayerId = string.Empty;
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
    public sealed class InputMessage
    {
        public string playerId = string.Empty;
        public float x;
        public float y;
        public float dirX;
        public float dirY;
        public int characterIndex = -1;
        public string state = string.Empty;
        public bool attackEvent;
        public bool attackHitEvent;
        public bool respawnEvent;
        public float currentHp;
        public float maxHp;
    }

    [Serializable]
    public sealed class InputBatchMessage
    {
        public string mapId = string.Empty;
        public InputMessage[] players = Array.Empty<InputMessage>();
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
