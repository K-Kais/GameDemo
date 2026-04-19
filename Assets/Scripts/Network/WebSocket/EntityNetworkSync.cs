using UnityEngine;

namespace GameDemo.Network
{
    public sealed class EntityNetworkSync : MonoBehaviour
    {
        [SerializeField] private WebSocketManager webSocketManager;
        [SerializeField] private float sendInterval = 0.05f;
        [SerializeField] private float movementThreshold = 0.0001f;

        private Vector3 _lastPosition;
        private Vector2 _lastDirection = Vector2.right;
        private Vector3 _lastSentPosition;
        private Vector2 _lastSentDirection = Vector2.right;
        private int _lastSentCharacterIndex = -1;
        private string _lastSentState = string.Empty;
        private bool _hasSentState;
        private float _nextSendTime;
        private EntityController _entityController;
        public EntitySyncData syncData = new EntitySyncData();

        private void Awake()
        {
            if (webSocketManager == null)
            {
                webSocketManager = FindAnyObjectByType<WebSocketManager>();
            }

            _entityController = GetComponent<EntityController>();
            _lastPosition = transform.position;
            _lastSentPosition = _lastPosition;
        }

        private void OnEnable()
        {
            _lastPosition = transform.position;
            _lastSentPosition = _lastPosition;
            _lastSentDirection = _lastDirection;
            _lastSentCharacterIndex = -1;
            _lastSentState = string.Empty;
            _hasSentState = false;
            _nextSendTime = Time.time;
        }

        public void Attack()
        {
            syncData.attackEvent = true;
        }

        public void AttackHit()
        {
            syncData.attackHitEvent = true;
        }

        public void Respawn()
        {
            syncData.respawnEvent = true;
        }

        private void Update()
        {
            if (webSocketManager == null || !webSocketManager.IsConnected)
            {
                return;
            }

            if (_entityController != null && !_entityController.isLocalPlayer)
            {
                return;
            }

            if (Time.time < _nextSendTime)
            {
                return;
            }
            _nextSendTime = Time.time + sendInterval;

            var position = transform.position;
            var delta = position - _lastPosition;
            var isMoving = delta.sqrMagnitude > movementThreshold;

            if (isMoving)
            {
                var direction = new Vector2(delta.x, delta.y).normalized;
                if (direction.sqrMagnitude > 0f)
                {
                    _lastDirection = direction;
                }
            }

            if (syncData == null)
            {
                syncData = new EntitySyncData();
            }

            var payload = syncData;
            payload.x = position.x;
            payload.y = position.y;
            payload.dirX = _lastDirection.x;
            payload.dirY = _lastDirection.y;
            payload.characterIndex = MapSpawnManager.Instance != null
                ? MapSpawnManager.Instance.SelectedCharacterIndex
                : -1;
            payload.state = isMoving ? AnimationStateNames.Walk : AnimationStateNames.Idle;
            payload.attackEvent = syncData.attackEvent;
            payload.attackHitEvent = syncData.attackHitEvent;
            payload.respawnEvent = syncData.respawnEvent;

            var hasChanged =
                !_hasSentState ||
                payload.attackEvent ||
                payload.attackHitEvent ||
                payload.respawnEvent ||
                (position - _lastSentPosition).sqrMagnitude > movementThreshold ||
                (_lastDirection - _lastSentDirection).sqrMagnitude > movementThreshold ||
                payload.characterIndex != _lastSentCharacterIndex ||
                !string.Equals(payload.state, _lastSentState, System.StringComparison.Ordinal);

            if (!hasChanged)
            {
                _lastPosition = position;
                return;
            }

            _ = webSocketManager.SendInputAsync(payload);

            _lastPosition = position;
            _lastSentPosition = position;
            _lastSentDirection = _lastDirection;
            _lastSentCharacterIndex = payload.characterIndex;
            _lastSentState = payload.state;
            _hasSentState = true;

            syncData.state = AnimationStateNames.Idle;
            syncData.attackEvent = false;
            syncData.attackHitEvent = false;
            syncData.respawnEvent = false;
        }
    }
}
