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
    private readonly Dictionary<string, int> _spawnedCharacterIndexByPlayerId = new Dictionary<string, int>();
    private readonly Dictionary<string, string> _userNameByPlayerId = new Dictionary<string, string>();
    private int _selectedCharacterIndex;
    private bool _hasConfirmedCharacterSelection;
    private string _lastKnownLocalPlayerId = string.Empty;

    public EntityController[] AvailablePlayerPrefabs => playerPrefabs;
    public int SelectedCharacterIndex => _selectedCharacterIndex;
    public bool IsAwaitingCharacterSelection => IsAuthenticationReady() && requireCharacterSelectionBeforeSpawn && !_hasConfirmedCharacterSelection;

    private void Awake()
    {
        Instance = this;

        if (webSocketManager == null && WebSocketManager.Instance != null)
        {
            webSocketManager = WebSocketManager.Instance;
        }

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
        if (webSocketManager == null && WebSocketManager.Instance != null)
        {
            webSocketManager = WebSocketManager.Instance;
        }

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
            SetUserName(item.playerId, item.userName);
            SetCharacterIndex(item.playerId, item.characterIndex);
            if (IsLocalPlayerId(item.playerId) && item.characterIndex >= 0)
            {
                _selectedCharacterIndex = item.characterIndex;
            }

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

            ApplyDisplayNameToEntity(item.playerId, entity);
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

        SetUserName(message.playerId, message.userName);
        var joinedEntity = EnsureEntity(message.playerId);
        ApplyDisplayNameToEntity(message.playerId, joinedEntity);
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
        RemoveLocalEntityOnDisconnect();

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

    private void RemoveLocalEntityOnDisconnect()
    {
        var localPlayerId = ResolveLocalPlayerId();
        if (!string.IsNullOrWhiteSpace(localPlayerId))
        {
            RemoveEntity(localPlayerId, destroyLocalPlayer: true);
        }
        else if (player != null)
        {
            var localEntity = player;
            player = null;

            _removeBuffer.Clear();
            foreach (var kvp in _entities)
            {
                if (kvp.Value == localEntity)
                {
                    _removeBuffer.Add(kvp.Key);
                }
            }

            for (var i = 0; i < _removeBuffer.Count; i++)
            {
                var key = _removeBuffer[i];
                _entities.Remove(key);
                _latestSyncByPlayerId.Remove(key);
                _characterIndexByPlayerId.Remove(key);
                _spawnedCharacterIndexByPlayerId.Remove(key);
                _userNameByPlayerId.Remove(key);
            }

            Destroy(localEntity.gameObject);
        }

        _lastKnownLocalPlayerId = string.Empty;
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
            if (message.characterIndex >= 0)
            {
                _selectedCharacterIndex = message.characterIndex;
            }

            var localEntity = EnsureEntity(message.playerId);
            if (localEntity != null)
            {
                ApplyDisplayNameToEntity(message.playerId, localEntity);
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

        ApplyDisplayNameToEntity(message.playerId, entity);
        ApplyState(entity, syncData);
    }

    private EntityController EnsureEntity(string playerId)
    {
        var isLocal = IsLocalPlayerId(playerId);
        if (isLocal && IsAwaitingCharacterSelection)
        {
            return null;
        }

        var expectedCharacterIndex = ResolveExpectedCharacterIndex(playerId, isLocal);
        if (_entities.TryGetValue(playerId, out var existing))
        {
            if (!_spawnedCharacterIndexByPlayerId.ContainsKey(playerId) && expectedCharacterIndex >= 0)
            {
                _spawnedCharacterIndexByPlayerId[playerId] = expectedCharacterIndex;
            }

            if (ShouldSwapCharacterPrefab(playerId, expectedCharacterIndex))
            {
                return RecreateEntity(playerId, existing, expectedCharacterIndex, isLocal);
            }

            return existing;
        }

        EntityController entity;
        if (isLocal && player != null)
        {
            entity = player;
        }
        else
        {
            var sourcePrefab = ResolveSpawnPrefab(playerId, isLocal, expectedCharacterIndex);
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

        ConfigureSpawnedEntity(playerId, entity, isLocal, expectedCharacterIndex);
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
        var shouldRespawnAfterSelect = IsLocalPlayerDead(localPlayerId);
        TrySpawnLocalPlayerIfReady();
        if (!string.IsNullOrWhiteSpace(localPlayerId))
        {
            SendCharacterSelectionInput(localPlayerId, index, shouldRespawnAfterSelect);
        }

        return true;
    }

    private void RemoveEntity(string playerId, bool destroyLocalPlayer = false)
    {
        if (!_entities.TryGetValue(playerId, out var entity))
        {
            return;
        }

        _entities.Remove(playerId);
        _latestSyncByPlayerId.Remove(playerId);
        _characterIndexByPlayerId.Remove(playerId);
        _spawnedCharacterIndexByPlayerId.Remove(playerId);
        _userNameByPlayerId.Remove(playerId);

        if (player != null && entity == player)
        {
            if (!destroyLocalPlayer)
            {
                return;
            }

            player = null;
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

    private EntityController ResolveSpawnPrefab(string playerId, bool isLocalPlayer, int expectedCharacterIndex)
    {
        if (playerPrefabs != null && playerPrefabs.Length > 0)
        {
            var expected = GetPrefabByIndex(expectedCharacterIndex);
            if (expected != null)
            {
                return expected;
            }

            if (!isLocalPlayer)
            {
                if (expectedCharacterIndex >= 0)
                {
                    Debug.LogWarning($"[MapSpawn] Remote character prefab missing at index {expectedCharacterIndex} for '{playerId}'.");
                }

                return null;
            }

            var selected = GetPrefabByIndex(_selectedCharacterIndex);
            if (selected != null)
            {
                return selected;
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

    private int ResolveExpectedCharacterIndex(string playerId, bool isLocalPlayer)
    {
        var characterIndex = GetCharacterIndex(playerId);
        if (characterIndex >= 0)
        {
            return characterIndex;
        }

        return isLocalPlayer ? _selectedCharacterIndex : -1;
    }

    private bool ShouldSwapCharacterPrefab(string playerId, int expectedCharacterIndex)
    {
        if (expectedCharacterIndex < 0)
        {
            return false;
        }

        if (!_spawnedCharacterIndexByPlayerId.TryGetValue(playerId, out var spawnedCharacterIndex))
        {
            return false;
        }

        return spawnedCharacterIndex >= 0 && spawnedCharacterIndex != expectedCharacterIndex;
    }

    private EntityController RecreateEntity(string playerId, EntityController currentEntity, int expectedCharacterIndex, bool isLocalPlayer)
    {
        var sourcePrefab = ResolveSpawnPrefab(playerId, isLocalPlayer, expectedCharacterIndex);
        if (sourcePrefab == null)
        {
            Debug.LogWarning($"[MapSpawn] Missing replacement prefab for player '{playerId}' character index {expectedCharacterIndex}.");
            return currentEntity;
        }

        var spawnPosition = currentEntity != null ? currentEntity.transform.position : Vector3.zero;
        var spawnRotation = currentEntity != null ? currentEntity.transform.rotation : Quaternion.identity;
        var replacement = Instantiate(sourcePrefab, spawnPosition, spawnRotation, playerRoot);
        ConfigureSpawnedEntity(playerId, replacement, isLocalPlayer, expectedCharacterIndex);
        if (currentEntity != null)
        {
            Destroy(currentEntity.gameObject);
        }

        return replacement;
    }

    private void ConfigureSpawnedEntity(string playerId, EntityController entity, bool isLocalPlayer, int characterIndex)
    {
        if (entity == null)
        {
            return;
        }

        if (!isLocalPlayer)
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
        ApplyDisplayNameToEntity(playerId, entity);
        if (characterIndex >= 0)
        {
            _spawnedCharacterIndexByPlayerId[playerId] = characterIndex;
        }
        else
        {
            _spawnedCharacterIndexByPlayerId.Remove(playerId);
        }
    }

    private void SendCharacterSelectionInput(string localPlayerId, int selectedCharacterIndex, bool requestRespawn)
    {
        if (webSocketManager == null || !webSocketManager.IsConnected || string.IsNullOrWhiteSpace(localPlayerId))
        {
            return;
        }

        var payload = new EntitySyncData
        {
            characterIndex = selectedCharacterIndex,
            state = AnimationStateNames.Idle,
            respawnEvent = requestRespawn
        };

        if (_latestSyncByPlayerId.TryGetValue(localPlayerId, out var latestSync) && latestSync != null)
        {
            payload.x = latestSync.x;
            payload.y = latestSync.y;
            payload.dirX = latestSync.dirX;
            payload.dirY = latestSync.dirY;
            payload.state = string.IsNullOrWhiteSpace(latestSync.state) ? AnimationStateNames.Idle : latestSync.state;
            payload.currentHp = latestSync.currentHp;
            payload.maxHp = latestSync.maxHp;
        }
        else if (player != null)
        {
            var position = player.transform.position;
            payload.x = position.x;
            payload.y = position.y;
            payload.dirX = player.direction.x;
            payload.dirY = player.direction.y;
            payload.state = string.IsNullOrWhiteSpace(player.state) ? AnimationStateNames.Idle : player.state;
            payload.currentHp = player.currentHp;
            payload.maxHp = player.maxHp;
        }

        _ = webSocketManager.SendInputAsync(payload);
    }

    private bool IsLocalPlayerDead(string localPlayerId)
    {
        if (!string.IsNullOrWhiteSpace(localPlayerId) &&
            _latestSyncByPlayerId.TryGetValue(localPlayerId, out var latestSync) &&
            latestSync != null)
        {
            if (latestSync.currentHp <= 0.01f)
            {
                return true;
            }

            if (AnimationStateNames.IsDead(latestSync.state))
            {
                return true;
            }
        }

        return player != null && player.currentHp <= 0.01f;
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

    private void SetUserName(string playerId, string userName)
    {
        if (string.IsNullOrWhiteSpace(playerId) || string.IsNullOrWhiteSpace(userName))
        {
            return;
        }

        _userNameByPlayerId[playerId] = userName.Trim();
    }

    private string ResolveDisplayName(string playerId)
    {
        if (string.IsNullOrWhiteSpace(playerId))
        {
            return string.Empty;
        }

        return _userNameByPlayerId.TryGetValue(playerId, out var userName) &&
            !string.IsNullOrWhiteSpace(userName)
            ? userName
            : playerId;
    }

    private void ApplyDisplayNameToEntity(string playerId, EntityController entity)
    {
        if (entity == null)
        {
            return;
        }

        entity.SetDisplayName(ResolveDisplayName(playerId));
    }

    private bool IsAuthenticationReady()
    {
        if (AuthMenuUI.Instance != null && !AuthMenuUI.Instance.HasAuthenticated)
        {
            return false;
        }

        return webSocketManager != null &&
            webSocketManager.IsConnected;
    }
}
