using GameDemo.Network;
using System.Collections.Generic;
using UnityEngine;

public class MapSpawnManager : MonoBehaviour
{
    public static MapSpawnManager Instance;

    [SerializeField] private WebSocketManager webSocketManager;
    [SerializeField] private EntityController playerPrefab;
    [SerializeField] private Transform playerRoot;
    [SerializeField] private bool destroyRemoteOnDisconnect = true;

    public EntityController player;

    private readonly Dictionary<string, EntityController> _entities = new Dictionary<string, EntityController>();
    private readonly HashSet<string> _snapshotIds = new HashSet<string>();
    private readonly List<string> _removeBuffer = new List<string>();

    private void Awake()
    {
        Instance = this;

        if (webSocketManager == null)
        {
            webSocketManager = FindAnyObjectByType<WebSocketManager>();
        }

        if (playerRoot == null)
        {
            playerRoot = transform;
        }
    }

    private void OnEnable()
    {
        if (webSocketManager == null)
        {
            webSocketManager = FindAnyObjectByType<WebSocketManager>();
        }

        if (webSocketManager == null)
        {
            Debug.LogWarning("[MapSpawn] WebSocketManager not found.");
            return;
        }

        webSocketManager.OnMapSnapshot += HandleMapSnapshot;
        webSocketManager.OnPlayerJoinedMap += HandlePlayerJoinedMap;
        webSocketManager.OnPlayerLeftMap += HandlePlayerLeftMap;
        webSocketManager.OnEntitySyncBatch += HandleEntitySyncBatch;
        webSocketManager.OnDisconnected += HandleDisconnected;
    }

    private void OnDisable()
    {
        if (webSocketManager == null)
        {
            return;
        }

        webSocketManager.OnMapSnapshot -= HandleMapSnapshot;
        webSocketManager.OnPlayerJoinedMap -= HandlePlayerJoinedMap;
        webSocketManager.OnPlayerLeftMap -= HandlePlayerLeftMap;
        webSocketManager.OnEntitySyncBatch -= HandleEntitySyncBatch;
        webSocketManager.OnDisconnected -= HandleDisconnected;
    }

    private void HandleMapSnapshot(MapSnapshotMessage snapshot)
    {
        if (snapshot?.players == null)
        {
            return;
        }

        _snapshotIds.Clear();
        for (var i = 0; i < snapshot.players.Length; i++)
        {
            var item = snapshot.players[i];
            if (string.IsNullOrWhiteSpace(item.playerId))
            {
                continue;
            }

            _snapshotIds.Add(item.playerId);
            var entity = EnsureEntity(item.playerId);
            if (entity == null)
            {
                continue;
            }

            ApplyState(entity, item.x, item.y, item.dirX, item.dirY, item.state);
        }

        _removeBuffer.Clear();
        foreach (var kvp in _entities)
        {
            if (_snapshotIds.Contains(kvp.Key))
            {
                continue;
            }

            if (player != null && kvp.Value == player)
            {
                continue;
            }

            _removeBuffer.Add(kvp.Key);
        }

        for (var i = 0; i < _removeBuffer.Count; i++)
        {
            RemoveEntity(_removeBuffer[i]);
        }
    }

    private void HandlePlayerJoinedMap(PlayerMapMessage message)
    {
        if (message == null || string.IsNullOrWhiteSpace(message.playerId))
        {
            return;
        }

        _ = EnsureEntity(message.playerId);
    }

    private void HandlePlayerLeftMap(PlayerMapMessage message)
    {
        if (message == null || string.IsNullOrWhiteSpace(message.playerId))
        {
            return;
        }

        RemoveEntity(message.playerId);
    }

    private void HandleEntitySyncBatch(InputBatchMessage batch)
    {
        if (batch?.players == null)
        {
            return;
        }

        for (var i = 0; i < batch.players.Length; i++)
        {
            var item = batch.players[i];
            if (item == null || string.IsNullOrWhiteSpace(item.playerId))
            {
                continue;
            }

            ApplyEntitySync(item);
        }
    }

    private void HandleDisconnected(NativeWebSocket.WebSocketCloseCode _)
    {
        if (!destroyRemoteOnDisconnect)
        {
            return;
        }

        _removeBuffer.Clear();
        foreach (var kvp in _entities)
        {
            if (player != null && kvp.Value == player)
            {
                continue;
            }

            _removeBuffer.Add(kvp.Key);
        }

        for (var i = 0; i < _removeBuffer.Count; i++)
        {
            RemoveEntity(_removeBuffer[i]);
        }
    }

    private void ApplyEntitySync(InputMessage message)
    {
        if (webSocketManager != null && string.Equals(message.playerId, webSocketManager.LocalPlayerId, System.StringComparison.Ordinal))
        {
            return;
        }

        var entity = EnsureEntity(message.playerId);
        if (entity == null)
        {
            return;
        }

        ApplyState(entity, message.x, message.y, message.dirX, message.dirY, message.state);
    }

    private EntityController EnsureEntity(string playerId)
    {
        if (_entities.TryGetValue(playerId, out var existing))
        {
            return existing;
        }

        var isLocal = webSocketManager != null &&
            string.Equals(playerId, webSocketManager.LocalPlayerId, System.StringComparison.Ordinal);

        EntityController entity;
        if (isLocal && player != null)
        {
            entity = player;
        }
        else
        {
            var sourcePrefab = playerPrefab != null ? playerPrefab : player;
            if (sourcePrefab == null)
            {
                Debug.LogWarning($"[MapSpawn] Missing prefab for player '{playerId}'.");
                return null;
            }

            entity = Instantiate(sourcePrefab, Vector3.zero, Quaternion.identity, playerRoot);
        }

        if (!isLocal)
        {
            var sync = entity.GetComponent<EntityNetworkSync>();
            if (sync != null)
            {
                sync.enabled = false;
            }
        }
        else
        {
            player = entity;
        }

        _entities[playerId] = entity;
        return entity;
    }

    private void RemoveEntity(string playerId)
    {
        if (!_entities.TryGetValue(playerId, out var entity))
        {
            return;
        }

        _entities.Remove(playerId);

        if (player != null && entity == player)
        {
            return;
        }

        if (entity != null)
        {
            Destroy(entity.gameObject);
        }
    }

    private static void ApplyState(EntityController entity, float x, float y, float dirX, float dirY, string state)
    {
        entity.ApplyNetworkState(x, y, dirX, dirY, state);
    }
}
