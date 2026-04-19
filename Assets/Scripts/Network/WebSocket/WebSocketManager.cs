using NativeWebSocket;
using System;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace GameDemo.Network
{
    public class WebSocketManager : MonoBehaviour
    {
        public static WebSocketManager Instance;
        [Header("Server")]
        [SerializeField] private string apiBaseUrl = "http://localhost:5268";
        [SerializeField] private string webSocketPath = "/ws";
        [SerializeField] private string defaultMapId = "world";

        [Header("Auto Connect (optional)")]
        [SerializeField] private bool autoConnectOnStart;
        [SerializeField] private bool autoRegisterIfMissing = true;
        [SerializeField] private string autoUserName = "demo_user";
        [SerializeField] private string autoPassword = "123456";

        private WebSocket webSocket;
        private string currentMapId = string.Empty;
        private string sessionToken = string.Empty;
        private string localPlayerId = string.Empty;

        public bool IsConnected => webSocket != null && webSocket.State == WebSocketState.Open;
        public WebSocketState ConnectionState => webSocket?.State ?? WebSocketState.Closed;
        public string CurrentMapId => currentMapId;
        public string SessionToken => sessionToken;
        public string LocalPlayerId => localPlayerId;

        public event Action OnConnected;
        public event Action<WebSocketCloseCode> OnDisconnected;
        public event Action<string> OnConnectionError;
        public event Action<MapSnapshotMessage> OnMapSnapshot;
        public event Action<PlayerMapMessage> OnPlayerJoinedMap;
        public event Action<PlayerMapMessage> OnPlayerLeftMap;
        public event Action<MoveMessage> OnPlayerMoved;
        public event Action<InputMessage> OnEntitySync;
        public event Action<InputBatchMessage> OnEntitySyncBatch;
        public event Action<ChatMessage> OnChatReceived;
        public event Action<PongMessage> OnPong;
        public event Action<ServerErrorMessage> OnServerError;
        public event Action<OpCode, string> OnRawMessage;

        private void Awake()
        {
            Instance = this;
        }

        private async void Start()
        {
            if (!autoConnectOnStart)
            {
                return;
            }

            await ConnectWithCredentialsAsync(autoUserName, autoPassword, autoRegisterIfMissing);
        }

        public void StartConnecting(string token = "")
        {
            _ = ConnectWithTokenAsync(token);
        }

        public async Task ConnectWithCredentialsAsync(
            string userName,
            string password,
            bool registerIfLoginFails = true,
            CancellationToken cancellationToken = default)
        {
            var loginResult = await AuthApiClient.LoginAsync(apiBaseUrl, userName, password, cancellationToken);
            if (!loginResult.Success && registerIfLoginFails)
            {
                var registerResult = await AuthApiClient.RegisterAsync(apiBaseUrl, userName, password, cancellationToken);
                if (!registerResult.Success)
                {
                    Debug.LogError($"[WebSocket] Register failed: {registerResult.Error}");
                    OnConnectionError?.Invoke(registerResult.Error);
                    return;
                }

                loginResult = await AuthApiClient.LoginAsync(apiBaseUrl, userName, password, cancellationToken);
            }

            if (!loginResult.Success)
            {
                Debug.LogError($"[WebSocket] Login failed: {loginResult.Error}");
                OnConnectionError?.Invoke(loginResult.Error);
                return;
            }

            sessionToken = loginResult.Data.token;
            localPlayerId = loginResult.Data.userId ?? string.Empty;
            await ConnectWithTokenAsync(sessionToken);
        }

        public async Task ConnectWithTokenAsync(string token = "")
        {
            if (IsConnected)
            {
                Debug.LogWarning("[WebSocket] Already connected.");
                return;
            }

            await DisconnectAsync();

            if (!string.IsNullOrWhiteSpace(token))
            {
                sessionToken = token;
            }

            if (string.IsNullOrWhiteSpace(localPlayerId))
            {
                localPlayerId = TryExtractPlayerIdFromToken(sessionToken);
            }

            var wsUrl = BuildWebSocketUrl(sessionToken);
            webSocket = new WebSocket(wsUrl);

            webSocket.OnOpen += HandleConnected;
            webSocket.OnMessage += HandleMessage;
            webSocket.OnError += HandleError;
            webSocket.OnClose += HandleClosed;

            try
            {
                await webSocket.Connect();
            }
            catch (Exception ex)
            {
                Debug.LogError($"[WebSocket] Connect failed: {ex.Message}");
                OnConnectionError?.Invoke(ex.Message);
            }
        }

        public async Task SendPingAsync()
        {
            await SendAsync(OpCode.Ping, null);
        }

        public async Task JoinMapAsync(string mapId = null)
        {
            await SendAsync(OpCode.JoinMap, new JoinMapRequest
            {
                mapId = string.IsNullOrWhiteSpace(mapId) ? defaultMapId : mapId
            });
        }

        public async Task MoveAsync(float x, float y)
        {
            await SendAsync(OpCode.Move, new MoveRequest
            {
                x = x,
                y = y
            });
        }

        public async Task SendInputAsync(EntitySyncData data)
        {
            if (data == null)
            {
                return;
            }

            await SendAsync(OpCode.Input, data);
        }

        public async Task ChatAsync(string message)
        {
            await SendAsync(OpCode.Chat, new ChatRequest
            {
                message = message ?? string.Empty
            });
        }

        public async Task SendAsync(OpCode opCode, object payload)
        {
            if (!IsConnected)
            {
                Debug.LogWarning("[WebSocket] Send ignored because socket is not connected.");
                return;
            }

            var json = payload == null ? "{}" : JsonUtility.ToJson(payload);
            var payloadBytes = Encoding.UTF8.GetBytes(json);
            var frame = new byte[1 + payloadBytes.Length];
            frame[0] = (byte)opCode;
            if (payloadBytes.Length > 0)
            {
                Buffer.BlockCopy(payloadBytes, 0, frame, 1, payloadBytes.Length);
            }

            try
            {
                await webSocket.Send(frame);
            }
            catch (Exception ex)
            {
                Debug.LogError($"[WebSocket] Send failed: {ex.Message}");
                OnConnectionError?.Invoke(ex.Message);
            }
        }

        public async Task DisconnectAsync()
        {
            if (webSocket == null)
            {
                return;
            }

            try
            {
                if (webSocket.State == WebSocketState.Open || webSocket.State == WebSocketState.Connecting)
                {
                    await webSocket.Close();
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[WebSocket] Close error: {ex.Message}");
            }
            finally
            {
                webSocket = null;
            }
        }

        private void Update()
        {
#if !UNITY_WEBGL || UNITY_EDITOR
            webSocket?.DispatchMessageQueue();
#endif
        }

        private async void OnApplicationQuit()
        {
            await DisconnectAsync();
        }

        private void HandleConnected()
        {
            Debug.Log("[WebSocket] Connected.");
            OnConnected?.Invoke();
        }

        private void HandleClosed(WebSocketCloseCode closeCode)
        {
            Debug.LogWarning($"[WebSocket] Closed: {closeCode}");
            OnDisconnected?.Invoke(closeCode);
        }

        private void HandleError(string error)
        {
            Debug.LogError($"[WebSocket] Error: {error}");
            OnConnectionError?.Invoke(error);
        }

        private void HandleMessage(byte[] frame)
        {
            if (frame == null || frame.Length == 0)
            {
                return;
            }

            var opCode = (OpCode)frame[0];
            var rawPayload = frame.Length > 1
                ? Encoding.UTF8.GetString(frame, 1, frame.Length - 1)
                : string.Empty;

            OnRawMessage?.Invoke(opCode, rawPayload);

            switch (opCode)
            {
                case OpCode.MapSnapshot:
                    {
                        var data = Deserialize<MapSnapshotMessage>(rawPayload);
                        currentMapId = data.mapId;
                        if (!string.IsNullOrWhiteSpace(data.selfPlayerId))
                        {
                            localPlayerId = data.selfPlayerId;
                        }

                        OnMapSnapshot?.Invoke(data);
                        return;
                    }
                case OpCode.PlayerJoinedMap:
                    {
                        var data = Deserialize<PlayerMapMessage>(rawPayload);
                        OnPlayerJoinedMap?.Invoke(data);
                        return;
                    }
                case OpCode.PlayerLeftMap:
                    {
                        var data = Deserialize<PlayerMapMessage>(rawPayload);
                        OnPlayerLeftMap?.Invoke(data);
                        return;
                    }
                case OpCode.Move:
                    {
                        var data = Deserialize<MoveMessage>(rawPayload);
                        OnPlayerMoved?.Invoke(data);
                        return;
                    }
                case OpCode.Chat:
                    {
                        var data = Deserialize<ChatMessage>(rawPayload);
                        OnChatReceived?.Invoke(data);
                        return;
                    }
                case OpCode.Input:
                    {
                        var batch = Deserialize<InputBatchMessage>(rawPayload);
                        if (batch.players != null && batch.players.Length > 0)
                        {
                            OnEntitySyncBatch?.Invoke(batch);
                            for (var i = 0; i < batch.players.Length; i++)
                            {
                                OnEntitySync?.Invoke(batch.players[i]);
                            }

                            return;
                        }

                        var data = Deserialize<InputMessage>(rawPayload);
                        if (!string.IsNullOrWhiteSpace(data.playerId))
                        {
                            OnEntitySync?.Invoke(data);
                        }

                        return;
                    }
                case OpCode.Pong:
                    {
                        var data = Deserialize<PongMessage>(rawPayload);
                        OnPong?.Invoke(data);
                        return;
                    }
                case OpCode.Error:
                    {
                        var data = Deserialize<ServerErrorMessage>(rawPayload);
                        OnServerError?.Invoke(data);
                        return;
                    }
                default:
                    Debug.Log($"[WebSocket] Unhandled opcode {(byte)opCode} payload: {rawPayload}");
                    return;
            }
        }

        private string BuildWebSocketUrl(string token)
        {
            var normalizedApiBase = string.IsNullOrWhiteSpace(apiBaseUrl)
                ? "http://localhost:5268"
                : apiBaseUrl.Trim();
            var uriBuilder = new UriBuilder(normalizedApiBase);
            uriBuilder.Scheme = uriBuilder.Scheme.Equals("https", StringComparison.OrdinalIgnoreCase) ? "wss" : "ws";
            uriBuilder.Path = webSocketPath.TrimStart('/');

            if (!string.IsNullOrWhiteSpace(token))
            {
                uriBuilder.Query = $"token={Uri.EscapeDataString(token)}";
            }

            return uriBuilder.Uri.ToString();
        }

        private static T Deserialize<T>(string json) where T : class, new()
        {
            if (string.IsNullOrWhiteSpace(json))
            {
                return new T();
            }

            try
            {
                return JsonUtility.FromJson<T>(json) ?? new T();
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[WebSocket] Failed to parse {typeof(T).Name}: {ex.Message}");
                return new T();
            }
        }

        private static string TryExtractPlayerIdFromToken(string token)
        {
            if (string.IsNullOrWhiteSpace(token))
            {
                return string.Empty;
            }

            var parts = token.Split('.');
            if (parts.Length < 2)
            {
                return string.Empty;
            }

            var payload = parts[1]
                .Replace('-', '+')
                .Replace('_', '/');

            switch (payload.Length % 4)
            {
                case 2:
                    payload += "==";
                    break;
                case 3:
                    payload += "=";
                    break;
            }

            try
            {
                var json = Encoding.UTF8.GetString(Convert.FromBase64String(payload));
                var jwtPayload = JsonUtility.FromJson<JwtPayload>(json);
                return jwtPayload?.sub ?? string.Empty;
            }
            catch (FormatException ex)
            {
                Debug.LogWarning($"[WebSocket] Invalid JWT payload format: {ex.Message}");
                return string.Empty;
            }
            catch (ArgumentException ex)
            {
                Debug.LogWarning($"[WebSocket] Invalid JWT payload argument: {ex.Message}");
                return string.Empty;
            }
        }

        [Serializable]
        private sealed class JwtPayload
        {
            public string sub = string.Empty;
        }
    }
}
