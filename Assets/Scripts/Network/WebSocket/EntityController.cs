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

        [Header("Hit Flash")]
        [SerializeField] private bool enableHitFlash = true;
        [SerializeField] private float hitFlashDuration = 0.5f;
        [SerializeField] private Color hitFlashColor = new Color(1f, 0.3f, 0.3f, 1f);
        [Header("Hit Effect")]
        [SerializeField] private GameObject hitBloodSplashPrefab;
        [SerializeField] private Vector3 hitBloodSplashLocalOffset = new Vector3(0f, 1f, 0f);
        [SerializeField] private float hitBloodSplashDestroyDelay = 1f;

        [Header("Skill 1")]
        [SerializeField] private KeyCode skill1Key = KeyCode.Return;
        [SerializeField] private GameObject skill1ProjectilePrefab;
        [SerializeField] private Vector3 skill1ProjectileSpawnLocalOffset = new Vector3(0f, 1f, 0f);
        [SerializeField] private float skill1ProjectileSpeed = 14f;
        [SerializeField] private float skill1ProjectileMaxLifetime = 1.2f;
        [SerializeField] private float skill1ProjectileFallbackDistance = 6f;
        [SerializeField] private float skill1Cooldown = 3f;
        [SerializeField] private GameObject skill1HitEffectPrefab;
        [SerializeField] private Vector3 skill1HitEffectLocalOffset = new Vector3(0f, 1f, 0f);
        [SerializeField] private float skill1HitEffectDestroyDelay = 1f;

        [Header("Name Tag")]
        [SerializeField] private bool showNameTag = true;
        [SerializeField] private Vector3 nameTagWorldOffset = new Vector3(0f, 2.2f, 0f);
        [SerializeField] private Color nameTagColor = new Color(1f, 1f, 1f, 1f);

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
        private float _skeletonAbsScaleX = 1f;
        private Transform _healthBarRoot;
        private SpriteRenderer _healthBarBackgroundRenderer;
        private SpriteRenderer _healthBarFillRenderer;
        private static Sprite _pixelSprite;
        private Coroutine _attackHitCoroutine;
        private bool _isAttackInputLocked;
        private float _attackInputUnlockTime;
        private bool _showRespawnPopup;
        private bool _respawnRequested;
        private bool _dismissRespawnPopupUntilRevive;
        private Coroutine _hitFlashCoroutine;
        private bool _hasBaseSkeletonColor;
        private float _baseSkeletonR = 1f;
        private float _baseSkeletonG = 1f;
        private float _baseSkeletonB = 1f;
        private float _baseSkeletonA = 1f;
        private string _displayName = string.Empty;
        private global::CharacterSelectionMenu _characterSelectionMenu;
        private GUIStyle _nameTagStyle;
        private float _skill1CooldownUntil;

        public float Skill1CooldownRemaining => Mathf.Max(0f, _skill1CooldownUntil - Time.time);
        public bool IsSkill1Ready => Skill1CooldownRemaining <= 0.001f;

        private void Awake()
        {
            _networkSync = GetComponent<EntityNetworkSync>();
            _skeletonAnim = GetComponentInChildren<SkeletonAnimation>();

            if (visualRoot == null)
            {
                visualRoot = _skeletonAnim != null ? _skeletonAnim.transform : transform;
            }

            if (_skeletonAnim?.Skeleton != null)
            {
                _skeletonAbsScaleX = Mathf.Abs(_skeletonAnim.Skeleton.ScaleX);
                if (_skeletonAbsScaleX < 0.0001f)
                {
                    _skeletonAbsScaleX = 1f;
                }
            }

            CacheBaseSkeletonColor();
            EnsureHealthBarVisual();
            UpdateHealthBarVisual();
        }

        private void OnDisable()
        {
            if (_hitFlashCoroutine != null)
            {
                StopCoroutine(_hitFlashCoroutine);
                _hitFlashCoroutine = null;
            }

            RestoreSkeletonTint();
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

                if (!isDead && Input.GetKeyDown(skill1Key) && IsSkill1Ready)
                {
                    _networkSync?.Skill1();
                    _skill1CooldownUntil = Time.time + Mathf.Max(0.05f, skill1Cooldown);
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
            ApplyNetworkSkillEvents(syncData.skill1, syncData.skill1TargetPlayerId, syncData.skill1HitEvent);
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

        public void ApplyNetworkSkillEvents(bool skill1Event, string skill1TargetPlayerId, bool skill1HitEvent)
        {
            if (skill1Event)
            {
                SpawnSkill1Projectile(skill1TargetPlayerId);
            }

            if (skill1HitEvent)
            {
                SpawnSkill1HitEffect();
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

            if (currentHp < previousHp - 0.001f)
            {
                TriggerHitFlash();
                SpawnHitBloodSplash();
            }

            if (isLocalPlayer)
            {
                if (isDeadNow)
                {
                    if (!wasDead)
                    {
                        _dismissRespawnPopupUntilRevive = false;
                    }

                    _showRespawnPopup = !_dismissRespawnPopupUntilRevive;
                    state = AnimationStateNames.Dead;
                }
                else if (wasDead)
                {
                    _showRespawnPopup = false;
                    _respawnRequested = false;
                    _dismissRespawnPopupUntilRevive = false;
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
                _showRespawnPopup = isLocalPlayer && !_dismissRespawnPopupUntilRevive;
                _attackQueued = false;
                _isAttackPlaying = false;
                ReleaseAttackInputLock();
                if (_attackHitCoroutine != null)
                {
                    StopCoroutine(_attackHitCoroutine);
                    _attackHitCoroutine = null;
                }
            }
            else
            {
                _showRespawnPopup = false;
                _respawnRequested = false;
                _dismissRespawnPopupUntilRevive = false;
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

        private void TriggerHitFlash()
        {
            if (!enableHitFlash)
            {
                return;
            }

            CacheBaseSkeletonColor();
            if (!_hasBaseSkeletonColor)
            {
                return;
            }

            if (_hitFlashCoroutine != null)
            {
                StopCoroutine(_hitFlashCoroutine);
                _hitFlashCoroutine = null;
            }

            _hitFlashCoroutine = StartCoroutine(HitFlashCoroutine());
        }

        private IEnumerator HitFlashCoroutine()
        {
            ApplySkeletonTint(hitFlashColor);
            yield return new WaitForSeconds(Mathf.Max(0.01f, hitFlashDuration));
            RestoreSkeletonTint();
            _hitFlashCoroutine = null;
        }

        private void SpawnHitBloodSplash()
        {
            if (hitBloodSplashPrefab == null)
            {
                return;
            }

            var spawnPosition = transform.position + hitBloodSplashLocalOffset;
            var splash = Instantiate(hitBloodSplashPrefab, spawnPosition, Quaternion.identity);
            var lifeTime = Mathf.Max(0.05f, hitBloodSplashDestroyDelay);
            Destroy(splash, lifeTime);
        }

        private void SpawnSkill1Projectile(string targetPlayerId)
        {
            if (skill1ProjectilePrefab == null)
            {
                return;
            }

            var spawnPosition = transform.position + skill1ProjectileSpawnLocalOffset;
            var projectile = Instantiate(skill1ProjectilePrefab, spawnPosition, Quaternion.identity);
            var targetTransform = ResolveSkill1TargetTransform(targetPlayerId);
            var facingDirection = direction.sqrMagnitude > 0.0001f
                ? direction.normalized
                : Vector2.right;
            var fallbackDistance = Mathf.Max(0.5f, skill1ProjectileFallbackDistance);
            var fallbackTarget = spawnPosition + new Vector3(facingDirection.x, facingDirection.y, 0f) * fallbackDistance;
            StartCoroutine(MoveSkill1Projectile(projectile.transform, targetTransform, fallbackTarget));
        }

        private IEnumerator MoveSkill1Projectile(Transform projectileTransform, Transform targetTransform, Vector3 fallbackTarget)
        {
            if (projectileTransform == null)
            {
                yield break;
            }

            var maxLifetime = Mathf.Max(0.1f, skill1ProjectileMaxLifetime);
            var speed = Mathf.Max(0.1f, skill1ProjectileSpeed);
            var elapsed = 0f;
            while (elapsed < maxLifetime && projectileTransform != null)
            {
                var targetPosition = targetTransform != null
                    ? targetTransform.position + skill1HitEffectLocalOffset
                    : fallbackTarget;
                var currentPosition = projectileTransform.position;
                var toTarget = targetPosition - currentPosition;
                var distance = toTarget.magnitude;
                if (distance <= 0.05f)
                {
                    break;
                }

                var step = speed * Time.deltaTime;
                if (step >= distance)
                {
                    projectileTransform.position = targetPosition;
                }
                else
                {
                    projectileTransform.position = currentPosition + (toTarget / distance) * step;
                }

                var facing = targetPosition - projectileTransform.position;
                if (facing.sqrMagnitude > 0.0001f)
                {
                    projectileTransform.right = facing;
                }

                elapsed += Time.deltaTime;
                yield return null;
            }

            if (projectileTransform != null)
            {
                Destroy(projectileTransform.gameObject);
            }
        }

        private void SpawnSkill1HitEffect()
        {
            if (skill1HitEffectPrefab == null)
            {
                return;
            }

            var spawnPosition = transform.position + skill1HitEffectLocalOffset;
            var hitEffect = Instantiate(skill1HitEffectPrefab, spawnPosition, Quaternion.identity);
            var lifeTime = Mathf.Max(0.05f, skill1HitEffectDestroyDelay);
            Destroy(hitEffect, lifeTime);
        }

        private Transform ResolveSkill1TargetTransform(string targetPlayerId)
        {
            if (string.IsNullOrWhiteSpace(targetPlayerId))
            {
                return null;
            }

            var mapSpawnManager = MapSpawnManager.Instance;
            if (mapSpawnManager == null)
            {
                return null;
            }

            var targetEntity = mapSpawnManager.GetEntity(targetPlayerId);
            return targetEntity != null ? targetEntity.transform : null;
        }

        private void CacheBaseSkeletonColor()
        {
            if (_hasBaseSkeletonColor || _skeletonAnim?.Skeleton == null)
            {
                return;
            }

            var skeleton = _skeletonAnim.Skeleton;
            _baseSkeletonR = skeleton.R;
            _baseSkeletonG = skeleton.G;
            _baseSkeletonB = skeleton.B;
            _baseSkeletonA = skeleton.A;
            _hasBaseSkeletonColor = true;
        }

        private void ApplySkeletonTint(Color color)
        {
            if (_skeletonAnim?.Skeleton == null)
            {
                return;
            }

            var skeleton = _skeletonAnim.Skeleton;
            skeleton.R = color.r;
            skeleton.G = color.g;
            skeleton.B = color.b;
            skeleton.A = color.a;
        }

        private void RestoreSkeletonTint()
        {
            if (!_hasBaseSkeletonColor || _skeletonAnim?.Skeleton == null)
            {
                return;
            }

            var skeleton = _skeletonAnim.Skeleton;
            skeleton.R = _baseSkeletonR;
            skeleton.G = _baseSkeletonG;
            skeleton.B = _baseSkeletonB;
            skeleton.A = _baseSkeletonA;
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
            if (!faceByDirection || _skeletonAnim?.Skeleton == null)
            {
                return;
            }

            if (Mathf.Abs(direction.x) < faceDirectionThreshold)
            {
                return;
            }

            _skeletonAnim.Skeleton.ScaleX = Mathf.Sign(direction.x) * _skeletonAbsScaleX;
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
            DrawNameTag();

            if (!isLocalPlayer || !_showRespawnPopup)
            {
                return;
            }

            const float popupWidth = 320f;
            const float popupHeight = 190f;
            var rect = new Rect(
                (Screen.width - popupWidth) * 0.5f,
                (Screen.height - popupHeight) * 0.5f,
                popupWidth,
                popupHeight);

            GUILayout.BeginArea(rect, GUI.skin.box);
            GUILayout.FlexibleSpace();
            GUILayout.Label("Ban da chet");
            GUILayout.Space(8f);
            if (GUILayout.Button("Chon nhan vat", GUILayout.Height(38f)))
            {
                _dismissRespawnPopupUntilRevive = true;
                _showRespawnPopup = false;
                OpenCharacterSelectionMenu();
            }

            GUILayout.Space(6f);
            GUI.enabled = !_respawnRequested;
            if (GUILayout.Button(_respawnRequested ? "Dang hoi sinh..." : "Hoi sinh", GUILayout.Height(38f)))
            {
                _respawnRequested = true;
                _networkSync?.Respawn();
            }

            GUI.enabled = true;
            GUILayout.FlexibleSpace();
            GUILayout.EndArea();
        }

        public void SetDisplayName(string displayName)
        {
            _displayName = displayName?.Trim() ?? string.Empty;
        }

        private void DrawNameTag()
        {
            if (!showNameTag || string.IsNullOrWhiteSpace(_displayName))
            {
                return;
            }

            var mainCamera = Camera.main;
            if (mainCamera == null)
            {
                return;
            }

            var worldPosition = transform.position + nameTagWorldOffset;
            var screenPosition = mainCamera.WorldToScreenPoint(worldPosition);
            if (screenPosition.z <= 0f)
            {
                return;
            }

            const float labelWidth = 220f;
            const float labelHeight = 24f;
            var rect = new Rect(
                screenPosition.x - (labelWidth * 0.5f),
                Screen.height - screenPosition.y - labelHeight,
                labelWidth,
                labelHeight);

            var cachedColor = GUI.color;
            GUI.color = nameTagColor;
            GUI.Label(rect, _displayName, GetNameTagStyle());
            GUI.color = cachedColor;
        }

        private GUIStyle GetNameTagStyle()
        {
            if (_nameTagStyle != null)
            {
                return _nameTagStyle;
            }

            _nameTagStyle = new GUIStyle(GUI.skin.label)
            {
                alignment = TextAnchor.MiddleCenter,
                fontStyle = FontStyle.Bold,
                fontSize = 14
            };
            return _nameTagStyle;
        }

        private void OpenCharacterSelectionMenu()
        {
            if (_characterSelectionMenu == null)
            {
                _characterSelectionMenu = global::CharacterSelectionMenu.Instance;
                if (_characterSelectionMenu == null)
                {
                    _characterSelectionMenu = FindAnyObjectByType<global::CharacterSelectionMenu>();
                }
            }

            _characterSelectionMenu?.ShowMenu();
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
