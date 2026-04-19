using GameDemo.Network;
using System;
using System.Collections.Generic;
using UnityEngine;

public class MapSpawnManager : MonoBehaviour
{
    public static MapSpawnManager Instance;

    [SerializeField] private WebSocketManager webSocketManager;
    [SerializeField] private EntityController[] playerPrefabs = Array.Empty<EntityController>();
    [SerializeField] private int defaultCharacterIndex;
    [SerializeField] private bool requireCharacterSelectionBeforeSpawn = true;
    [SerializeField] private Transform playerRoot;
    [SerializeField] private bool destroyRemoteOnDisconnect = true;

    public EntityController player;

    private readonly Dictionary<string, EntityController> _entities = new Dictionary<string, EntityController>();
    private readonly HashSet<string> _snapshotIds = new HashSet<string>();
    private readonly List<string> _removeBuffer = new List<string>();
    private readonly Dictionary<string, EntitySyncData> _latestSyncByPlayerId = new Dictionary<string, EntitySyncData>();
    private readonly Dictionary<string, int> _characterIndexByPlayerId = new Dictionary<string, int>();
    private int _selectedCharacterIndex;
    private bool _hasConfirmedCharacterSelection;
    private string _lastKnownLocalPlayerId = string.Empty;

    public EntityController[] AvailablePlayerPrefabs => playerPrefabs;
    public int SelectedCharacterIndex => _selectedCharacterIndex;
    public bool IsAwaitingCharacterSelection => requireCharacterSelectionBeforeSpawn && !_hasConfirmedCharacterSelection;

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

        _selectedCharacterIndex = ResolveInitialSelectedIndex();
        _hasConfirmedCharacterSelection = !requireCharacterSelectionBeforeSpawn;
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

        if (!string.IsNullOrWhiteSpace(snapshot.selfPlayerId))
        {
            _lastKnownLocalPlayerId = snapshot.selfPlayerId;
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
            SetCharacterIndex(item.playerId, item.characterIndex);
            var syncData = new EntitySyncData
            {
                x = item.x,
                y = item.y,
                dirX = item.dirX,
                dirY = item.dirY,
                characterIndex = item.characterIndex,
                state = item.state,
                attackEvent = false,
                attackHitEvent = false,
                respawnEvent = false,
                currentHp = item.currentHp,
                maxHp = item.maxHp
            };
            _latestSyncByPlayerId[item.playerId] = syncData;

            var entity = EnsureEntity(item.playerId);
            if (entity == null)
            {
                continue;
            }

            ApplyState(entity, syncData);
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

        TrySpawnLocalPlayerIfReady();
    }

    private void HandlePlayerJoinedMap(PlayerMapMessage message)
    {
        if (message == null || string.IsNullOrWhiteSpace(message.playerId))
        {
            return;
        }

        if (IsLocalPlayerId(message.playerId))
        {
            _lastKnownLocalPlayerId = message.playerId;
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
        var syncData = new EntitySyncData
        {
            x = message.x,
            y = message.y,
            dirX = message.dirX,
            dirY = message.dirY,
            characterIndex = message.characterIndex,
            state = message.state,
            attackEvent = message.attackEvent,
            attackHitEvent = message.attackHitEvent,
            respawnEvent = message.respawnEvent,
            currentHp = message.currentHp,
            maxHp = message.maxHp
        };
        SetCharacterIndex(message.playerId, message.characterIndex);
        _latestSyncByPlayerId[message.playerId] = syncData;

        if (IsLocalPlayerId(message.playerId))
        {
            _lastKnownLocalPlayerId = message.playerId;
            var localEntity = EnsureEntity(message.playerId);
            if (localEntity != null)
            {
                localEntity.ApplyNetworkHealth(message.currentHp, message.maxHp);
                localEntity.ApplyServerState(message.state);
            }

            return;
        }

        var entity = EnsureEntity(message.playerId);
        if (entity == null)
        {
            return;
        }

        ApplyState(entity, syncData);
    }

    private EntityController EnsureEntity(string playerId)
    {
        if (_entities.TryGetValue(playerId, out var existing))
        {
            return existing;
        }

        var isLocal = IsLocalPlayerId(playerId);
        if (isLocal && IsAwaitingCharacterSelection)
        {
            return null;
        }

        EntityController entity;
        if (isLocal && player != null)
        {
            entity = player;
        }
        else
        {
            var sourcePrefab = ResolveSpawnPrefab(playerId, isLocal);
            if (sourcePrefab == null)
            {
                if (!isLocal)
                {
                    return null;
                }

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

    public bool SelectCharacter(int index)
    {
        if (playerPrefabs == null || playerPrefabs.Length == 0)
        {
            Debug.LogWarning("[MapSpawn] No player prefabs configured.");
            return false;
        }

        if (index < 0 || index >= playerPrefabs.Length)
        {
            Debug.LogWarning($"[MapSpawn] Character index out of range: {index}");
            return false;
        }

        if (playerPrefabs[index] == null)
        {
            Debug.LogWarning($"[MapSpawn] Character prefab at index {index} is null.");
            return false;
        }

        _selectedCharacterIndex = index;
        _hasConfirmedCharacterSelection = true;
        var localPlayerId = ResolveLocalPlayerId();
        if (!string.IsNullOrWhiteSpace(localPlayerId))
        {
            _characterIndexByPlayerId[localPlayerId] = index;
        }

        TrySpawnLocalPlayerIfReady();
        return true;
    }

    private void RemoveEntity(string playerId)
    {
        if (!_entities.TryGetValue(playerId, out var entity))
        {
            return;
        }

        _entities.Remove(playerId);
        _latestSyncByPlayerId.Remove(playerId);
        _characterIndexByPlayerId.Remove(playerId);

        if (player != null && entity == player)
        {
            return;
        }

        if (entity != null)
        {
            Destroy(entity.gameObject);
        }
    }

    private static void ApplyState(EntityController entity, EntitySyncData syncData)
    {
        entity.ApplyNetworkState(syncData);
    }

    private int ResolveInitialSelectedIndex()
    {
        if (playerPrefabs == null || playerPrefabs.Length == 0)
        {
            return 0;
        }

        var clamped = Mathf.Clamp(defaultCharacterIndex, 0, playerPrefabs.Length - 1);
        if (playerPrefabs[clamped] != null)
        {
            return clamped;
        }

        for (var i = 0; i < playerPrefabs.Length; i++)
        {
            if (playerPrefabs[i] != null)
            {
                return i;
            }
        }

        return 0;
    }

    private EntityController ResolveSpawnPrefab(string playerId, bool isLocalPlayer)
    {
        if (playerPrefabs != null && playerPrefabs.Length > 0)
        {
            if (isLocalPlayer)
            {
                var selected = GetPrefabByIndex(_selectedCharacterIndex);
                if (selected != null)
                {
                    return selected;
                }
            }
            else
            {
                var characterIndex = GetCharacterIndex(playerId);
                if (characterIndex < 0)
                {
                    return null;
                }

                var selected = GetPrefabByIndex(characterIndex);
                if (selected != null)
                {
                    return selected;
                }

                Debug.LogWarning($"[MapSpawn] Remote character prefab missing at index {characterIndex} for '{playerId}'.");
                return null;
            }

            for (var i = 0; i < playerPrefabs.Length; i++)
            {
                if (playerPrefabs[i] != null)
                {
                    return playerPrefabs[i];
                }
            }
        }

        return player;
    }

    private EntityController GetPrefabByIndex(int index)
    {
        if (playerPrefabs == null || index < 0 || index >= playerPrefabs.Length)
        {
            return null;
        }

        return playerPrefabs[index];
    }

    private bool IsLocalPlayerId(string playerId)
    {
        if (string.IsNullOrWhiteSpace(playerId))
        {
            return false;
        }

        var localPlayerId = ResolveLocalPlayerId();
        return !string.IsNullOrWhiteSpace(localPlayerId) &&
            string.Equals(playerId, localPlayerId, StringComparison.Ordinal);
    }

    private string ResolveLocalPlayerId()
    {
        if (webSocketManager != null && !string.IsNullOrWhiteSpace(webSocketManager.LocalPlayerId))
        {
            return webSocketManager.LocalPlayerId;
        }

        return _lastKnownLocalPlayerId;
    }

    private void TrySpawnLocalPlayerIfReady()
    {
        if (player != null || IsAwaitingCharacterSelection)
        {
            return;
        }

        var localPlayerId = ResolveLocalPlayerId();
        if (string.IsNullOrWhiteSpace(localPlayerId))
        {
            return;
        }

        var entity = EnsureEntity(localPlayerId);
        if (entity == null)
        {
            return;
        }

        if (_latestSyncByPlayerId.TryGetValue(localPlayerId, out var syncData))
        {
            ApplyState(entity, syncData);
        }
    }

    private int GetCharacterIndex(string playerId)
    {
        if (string.IsNullOrWhiteSpace(playerId))
        {
            return -1;
        }

        return _characterIndexByPlayerId.TryGetValue(playerId, out var index) ? index : -1;
    }

    private void SetCharacterIndex(string playerId, int characterIndex)
    {
        if (string.IsNullOrWhiteSpace(playerId) || characterIndex < 0)
        {
            return;
        }

        _characterIndexByPlayerId[playerId] = characterIndex;
    }
}
