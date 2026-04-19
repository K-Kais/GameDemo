using Spine.Unity;
using System.Collections;
using UnityEngine;

namespace GameDemo.Network
{
    public class EntityController : MonoBehaviour
    {
        public bool isLocalPlayer => MapSpawnManager.Instance.player == this;
        public Vector2 direction { get; private set; } = Vector2.right;
        public string state { get; private set; } = AnimationStateNames.Idle;
        public bool attackEvent { get; private set; }
        public float currentHp { get; private set; } = 100f;
        public float maxHp { get; private set; } = 100f;

        [Header("Movement")]
        [SerializeField] private float moveSpeed = 5f;
        [SerializeField, Range(0f, 1f)] private float attackHitNormalizedTime = 0.5f;
        [SerializeField] private float attackFallbackDuration = 0.4f;

        [Header("Remote Smoothing")]
        [SerializeField] private float remoteSmoothTime = 0.06f;
        [SerializeField] private float remotePredictionTime = 0.03f;
        [SerializeField] private float remoteMaxSpeed = 20f;

        [Header("Direction Facing")]
        [SerializeField] private bool faceByDirection = true;
        [SerializeField] private float faceDirectionThreshold = 0.01f;
        [SerializeField] private Transform visualRoot;

        [Header("Health Bar")]
        [SerializeField] private bool showHealthBar = true;
        [SerializeField] private Vector3 healthBarLocalOffset = new Vector3(0f, 1.6f, 0f);
        [SerializeField] private Vector2 healthBarSize = new Vector2(1.1f, 0.12f);
        [SerializeField] private Color healthBarBackgroundColor = new Color(0f, 0f, 0f, 0.7f);
        [SerializeField] private Color healthBarFillColor = new Color(0.2f, 0.9f, 0.2f, 0.95f);

        private EntityNetworkSync _networkSync;
        private SkeletonAnimation _skeletonAnim;
        private Vector3 _remoteTargetPosition;
        private Vector3 _remoteEstimatedVelocity;
        private Vector3 _remoteSmoothVelocity;
        private float _remoteLastSyncTime;
        private bool _hasRemoteSync;
        private bool _attackQueued;
        private bool _isAttackPlaying;
        private string _currentLoopAnimation = string.Empty;
        private float _visualAbsScaleX = 1f;
        private Transform _healthBarRoot;
        private SpriteRenderer _healthBarBackgroundRenderer;
        private SpriteRenderer _healthBarFillRenderer;
        private static Sprite _pixelSprite;
        private Coroutine _attackHitCoroutine;
        private bool _isAttackInputLocked;
        private float _attackInputUnlockTime;
        private bool _showRespawnPopup;
        private bool _respawnRequested;

        private void Awake()
        {
            _networkSync = GetComponent<EntityNetworkSync>();
            _skeletonAnim = GetComponentInChildren<SkeletonAnimation>();

            if (visualRoot == null)
            {
                visualRoot = _skeletonAnim != null ? _skeletonAnim.transform : transform;
            }

            if (visualRoot != null)
            {
                _visualAbsScaleX = Mathf.Abs(visualRoot.localScale.x);
                if (_visualAbsScaleX < 0.0001f)
                {
                    _visualAbsScaleX = 1f;
                }
            }

            EnsureHealthBarVisual();
            UpdateHealthBarVisual();
        }

        private void Update()
        {
            if (isLocalPlayer)
            {
                var isDead = currentHp <= 0.01f;
                if (isDead)
                {
                    state = AnimationStateNames.Dead;
                    _isAttackPlaying = false;
                    if (_attackHitCoroutine != null)
                    {
                        StopCoroutine(_attackHitCoroutine);
                        _attackHitCoroutine = null;
                    }
                }

                RefreshAttackInputLock();

                if (!isDead && !IsAttackInputLocked() && Input.GetKeyDown(KeyCode.Space))
                {
                    attackEvent = true;
                    _attackQueued = true;
                    _isAttackPlaying = true;
                    LockAttackInputFor(attackFallbackDuration);
                    state = AnimationStateNames.Attack;
                    _networkSync?.Attack();
                }

                if (_isAttackPlaying)
                {
                    state = AnimationStateNames.Attack;
                }
                else
                {
                    if (isDead)
                    {
                        state = AnimationStateNames.Dead;
                    }
                    else
                    {
                        float x = Input.GetAxis("Horizontal");
                        float y = Input.GetAxis("Vertical");
                        transform.position += new Vector3(x, y, 0f) * Time.deltaTime * moveSpeed;

                        if (x != 0f || y != 0f)
                        {
                            direction = new Vector2(x, y).normalized;
                            state = AnimationStateNames.Walk;
                        }
                        else
                        {
                            state = AnimationStateNames.Idle;
                        }
                    }
                }
            }
            else
            {
                UpdateRemoteMotion();
            }

            UpdateFacingDirection();
            UpdateAnimation();
        }

        public void ApplyNetworkState(EntitySyncData syncData)
        {
            if (syncData == null)
            {
                return;
            }

            var incomingPosition = new Vector3(syncData.x, syncData.y, transform.position.z);
            var now = Time.time;
            if (!_hasRemoteSync)
            {
                transform.position = incomingPosition;
                _remoteTargetPosition = incomingPosition;
                _remoteEstimatedVelocity = Vector3.zero;
                _remoteSmoothVelocity = Vector3.zero;
                _remoteLastSyncTime = now;
                _hasRemoteSync = true;
            }
            else
            {
                var dt = Mathf.Max(now - _remoteLastSyncTime, 0.001f);
                _remoteEstimatedVelocity = (incomingPosition - _remoteTargetPosition) / dt;
                _remoteTargetPosition = incomingPosition;
                _remoteLastSyncTime = now;
            }

            direction = new Vector2(syncData.dirX, syncData.dirY);
            UpdateFacingDirection();
            state = AnimationStateNames.Normalize(syncData.state);
            if (AnimationStateNames.IsDead(state))
            {
                _attackQueued = false;
                attackEvent = false;
                _isAttackPlaying = false;
                ReleaseAttackInputLock();
                if (_attackHitCoroutine != null)
                {
                    StopCoroutine(_attackHitCoroutine);
                    _attackHitCoroutine = null;
                }
            }

            ApplyNetworkHealth(syncData.currentHp, syncData.maxHp);
            if (AnimationStateNames.IsDead(state))
            {
                return;
            }

            if (syncData.attackEvent)
            {
                attackEvent = true;
                _attackQueued = true;
                _isAttackPlaying = true;
            }
        }

        public void ApplyNetworkHealth(float incomingCurrentHp, float incomingMaxHp)
        {
            var previousHp = currentHp;
            var wasDead = previousHp <= 0.01f;

            if (incomingMaxHp > 0f)
            {
                maxHp = incomingMaxHp;
            }

            if (maxHp <= 0f)
            {
                maxHp = 100f;
            }

            if (incomingCurrentHp <= 0f && incomingMaxHp <= 0f && previousHp > 0f)
            {
                incomingCurrentHp = previousHp;
            }

            currentHp = Mathf.Clamp(incomingCurrentHp, 0f, maxHp);
            var isDeadNow = currentHp <= 0.01f;
            EnsureHealthBarVisual();
            UpdateHealthBarVisual();

            if (isLocalPlayer)
            {
                if (isDeadNow)
                {
                    _showRespawnPopup = true;
                    state = AnimationStateNames.Dead;
                }
                else if (wasDead)
                {
                    _showRespawnPopup = false;
                    _respawnRequested = false;
                    if (AnimationStateNames.IsDead(state))
                    {
                        state = AnimationStateNames.Idle;
                    }
                }
            }
        }

        public void ApplyServerState(string serverState)
        {
            if (string.IsNullOrWhiteSpace(serverState))
            {
                return;
            }

            state = AnimationStateNames.Normalize(serverState);
            if (AnimationStateNames.IsDead(state))
            {
                _showRespawnPopup = isLocalPlayer;
                _attackQueued = false;
                _isAttackPlaying = false;
                ReleaseAttackInputLock();
                if (_attackHitCoroutine != null)
                {
                    StopCoroutine(_attackHitCoroutine);
                    _attackHitCoroutine = null;
                }
            }
        }

        private void UpdateRemoteMotion()
        {
            if (!_hasRemoteSync)
            {
                return;
            }

            var smoothTime = Mathf.Max(0.01f, remoteSmoothTime);
            var predictedTarget = _remoteTargetPosition + (_remoteEstimatedVelocity * remotePredictionTime);
            transform.position = Vector3.SmoothDamp(
                transform.position,
                predictedTarget,
                ref _remoteSmoothVelocity,
                smoothTime,
                remoteMaxSpeed,
                Time.deltaTime);
        }

        private void UpdateAnimation()
        {
            if (_skeletonAnim == null)
            {
                _isAttackPlaying = false;
                return;
            }

            var animationState = _skeletonAnim.AnimationState;
            if (_attackQueued)
            {
                _attackQueued = false;
                attackEvent = false;

                var baseState = ResolvePlayableAnimation(ToAnimationState(state));
                if (string.Equals(baseState, AnimationStateNames.Attack, System.StringComparison.Ordinal))
                {
                    baseState = AnimationStateNames.Idle;
                }

                var attackName = ResolvePlayableAnimation(AnimationStateNames.Attack);
                if (!string.Equals(attackName, AnimationStateNames.Attack, System.StringComparison.Ordinal))
                {
                    _isAttackPlaying = false;
                    ReleaseAttackInputLock();
                    if (!string.IsNullOrWhiteSpace(baseState) &&
                        !string.Equals(_currentLoopAnimation, baseState, System.StringComparison.Ordinal))
                    {
                        animationState.SetAnimation(0, baseState, true);
                        _currentLoopAnimation = baseState;
                    }

                    return;
                }

                var attackEntry = animationState.SetAnimation(0, attackName, false);
                if (attackEntry == null)
                {
                    _isAttackPlaying = false;
                    ReleaseAttackInputLock();
                    return;
                }

                ScheduleAttackHit(attackEntry);
                attackEntry.Complete += _ => HandleAttackAnimationComplete();
                attackEntry.Interrupt += _ => HandleAttackAnimationInterrupted();
                animationState.AddAnimation(0, baseState, true, 0f);
                _currentLoopAnimation = baseState;
                return;
            }

            if (_isAttackPlaying)
            {
                return;
            }

            var loopState = ResolvePlayableAnimation(ToAnimationState(state));
            if (string.Equals(_currentLoopAnimation, loopState, System.StringComparison.Ordinal))
            {
                return;
            }

            if (string.IsNullOrWhiteSpace(loopState))
            {
                return;
            }

            var shouldLoop = !AnimationStateNames.IsDead(loopState);
            animationState.SetAnimation(0, loopState, shouldLoop);
            _currentLoopAnimation = loopState;
        }

        private static string ToAnimationState(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return AnimationStateNames.Idle;
            }

            return AnimationStateNames.Normalize(value);
        }

        private string ResolvePlayableAnimation(string requested, string fallback = AnimationStateNames.Idle)
        {
            var normalizedRequested = string.IsNullOrWhiteSpace(requested) ? fallback : requested;
            if (_skeletonAnim?.Skeleton?.Data?.FindAnimation(normalizedRequested) != null)
            {
                return normalizedRequested;
            }

            if (_skeletonAnim?.Skeleton?.Data?.FindAnimation(fallback) != null)
            {
                return fallback;
            }

            return string.Empty;
        }

        private void HandleAttackAnimationComplete()
        {
            _isAttackPlaying = false;
            ReleaseAttackInputLock();
            if (_attackHitCoroutine != null)
            {
                StopCoroutine(_attackHitCoroutine);
                _attackHitCoroutine = null;
            }
        }

        private void HandleAttackAnimationInterrupted()
        {
            _isAttackPlaying = false;
            ReleaseAttackInputLock();
            if (_attackHitCoroutine != null)
            {
                StopCoroutine(_attackHitCoroutine);
                _attackHitCoroutine = null;
            }
        }

        private void ScheduleAttackHit(Spine.TrackEntry attackEntry)
        {
            if (!isLocalPlayer || _networkSync == null)
            {
                return;
            }

            if (_attackHitCoroutine != null)
            {
                StopCoroutine(_attackHitCoroutine);
                _attackHitCoroutine = null;
            }

            var normalized = Mathf.Clamp01(attackHitNormalizedTime);
            var duration = Mathf.Max(0.01f, attackEntry.AnimationEnd - attackEntry.AnimationStart);
            var timeScale = attackEntry.TimeScale > 0f ? attackEntry.TimeScale : 1f;
            var delay = (duration * normalized) / timeScale;
            var attackDuration = duration / timeScale;

            if (float.IsNaN(delay) || float.IsInfinity(delay) || delay <= 0f)
            {
                delay = Mathf.Max(0.01f, attackFallbackDuration * normalized);
            }

            if (float.IsNaN(attackDuration) || float.IsInfinity(attackDuration) || attackDuration <= 0f)
            {
                attackDuration = Mathf.Max(0.05f, attackFallbackDuration);
            }

            LockAttackInputFor(attackDuration);

            _attackHitCoroutine = StartCoroutine(SendAttackHitAfterDelay(delay));
        }

        private IEnumerator SendAttackHitAfterDelay(float delay)
        {
            yield return new WaitForSeconds(delay);
            _attackHitCoroutine = null;
            _networkSync?.AttackHit();
        }

        private bool IsAttackInputLocked()
        {
            RefreshAttackInputLock();
            return _isAttackInputLocked || _isAttackPlaying;
        }

        private void LockAttackInputFor(float seconds)
        {
            var safeSeconds = Mathf.Max(0.05f, seconds);
            var candidateUnlockTime = Time.time + safeSeconds;
            if (!_isAttackInputLocked || candidateUnlockTime > _attackInputUnlockTime)
            {
                _attackInputUnlockTime = candidateUnlockTime;
            }

            _isAttackInputLocked = true;
        }

        private void ReleaseAttackInputLock()
        {
            _isAttackInputLocked = false;
            _attackInputUnlockTime = 0f;
        }

        private void RefreshAttackInputLock()
        {
            if (!_isAttackInputLocked)
            {
                return;
            }

            if (Time.time < _attackInputUnlockTime)
            {
                return;
            }

            _isAttackInputLocked = false;
            _attackInputUnlockTime = 0f;
            _isAttackPlaying = false;
        }

        private void UpdateFacingDirection()
        {
            if (!faceByDirection || visualRoot == null)
            {
                return;
            }

            if (Mathf.Abs(direction.x) < faceDirectionThreshold)
            {
                return;
            }

            var scale = visualRoot.localScale;
            scale.x = Mathf.Sign(direction.x) * _visualAbsScaleX;
            visualRoot.localScale = scale;
        }

        private void EnsureHealthBarVisual()
        {
            if (!showHealthBar)
            {
                if (_healthBarRoot != null)
                {
                    _healthBarRoot.gameObject.SetActive(false);
                }

                return;
            }

            if (_healthBarRoot == null)
            {
                var rootObject = new GameObject("HealthBar");
                _healthBarRoot = rootObject.transform;
                _healthBarRoot.SetParent(transform, false);

                var backgroundObject = new GameObject("Bg");
                backgroundObject.transform.SetParent(_healthBarRoot, false);
                _healthBarBackgroundRenderer = backgroundObject.AddComponent<SpriteRenderer>();
                _healthBarBackgroundRenderer.sprite = GetPixelSprite();
                _healthBarBackgroundRenderer.color = healthBarBackgroundColor;

                var fillObject = new GameObject("Fill");
                fillObject.transform.SetParent(_healthBarRoot, false);
                _healthBarFillRenderer = fillObject.AddComponent<SpriteRenderer>();
                _healthBarFillRenderer.sprite = GetPixelSprite();
                _healthBarFillRenderer.color = healthBarFillColor;
            }

            SyncOverlaySorting();
            _healthBarRoot.gameObject.SetActive(true);
        }

        private void UpdateHealthBarVisual()
        {
            if (!showHealthBar || _healthBarRoot == null)
            {
                return;
            }

            _healthBarRoot.localPosition = healthBarLocalOffset;
            var safeMaxHp = Mathf.Max(maxHp, 1f);
            var ratio = Mathf.Clamp01(currentHp / safeMaxHp);

            if (_healthBarBackgroundRenderer != null)
            {
                _healthBarBackgroundRenderer.color = healthBarBackgroundColor;
                _healthBarBackgroundRenderer.transform.localPosition = Vector3.zero;
                _healthBarBackgroundRenderer.transform.localScale = new Vector3(healthBarSize.x, healthBarSize.y, 1f);
            }

            if (_healthBarFillRenderer != null)
            {
                _healthBarFillRenderer.color = healthBarFillColor;
                _healthBarFillRenderer.transform.localScale = new Vector3(healthBarSize.x * ratio, healthBarSize.y, 1f);
                _healthBarFillRenderer.transform.localPosition = new Vector3((ratio - 1f) * healthBarSize.x * 0.5f, 0f, -0.01f);
            }
        }

        private void OnGUI()
        {
            if (!isLocalPlayer || !_showRespawnPopup)
            {
                return;
            }

            const float popupWidth = 320f;
            const float popupHeight = 140f;
            var rect = new Rect(
                (Screen.width - popupWidth) * 0.5f,
                (Screen.height - popupHeight) * 0.5f,
                popupWidth,
                popupHeight);

            GUILayout.BeginArea(rect, GUI.skin.box);
            GUILayout.FlexibleSpace();
            GUILayout.Label("Ban da chet");
            GUILayout.Space(8f);
            GUI.enabled = !_respawnRequested;
            if (GUILayout.Button(_respawnRequested ? "Dang hoi sinh..." : "Reporn", GUILayout.Height(38f)))
            {
                _respawnRequested = true;
                _networkSync?.Respawn();
            }

            GUI.enabled = true;
            GUILayout.FlexibleSpace();
            GUILayout.EndArea();
        }

        private void SyncOverlaySorting()
        {
            var visualRenderer = visualRoot != null ? visualRoot.GetComponent<Renderer>() : null;
            var sortingLayerId = visualRenderer != null ? visualRenderer.sortingLayerID : 0;
            var sortingOrderBase = visualRenderer != null ? visualRenderer.sortingOrder : 0;

            if (_healthBarBackgroundRenderer != null)
            {
                _healthBarBackgroundRenderer.sortingLayerID = sortingLayerId;
                _healthBarBackgroundRenderer.sortingOrder = sortingOrderBase + 50;
            }

            if (_healthBarFillRenderer != null)
            {
                _healthBarFillRenderer.sortingLayerID = sortingLayerId;
                _healthBarFillRenderer.sortingOrder = sortingOrderBase + 51;
            }
        }

        private static Sprite GetPixelSprite()
        {
            if (_pixelSprite != null)
            {
                return _pixelSprite;
            }

            _pixelSprite = Sprite.Create(
                Texture2D.whiteTexture,
                new Rect(0f, 0f, 1f, 1f),
                new Vector2(0.5f, 0.5f),
                1f);
            return _pixelSprite;
        }
    }
}
