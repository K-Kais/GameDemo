using UnityEngine;

public sealed class LocalPlayerCameraFollow : MonoBehaviour
{
    [SerializeField] private Vector3 followOffset = new Vector3(0f, 0f, -10f);
    [SerializeField] private float smoothTime = 0.12f;
    [SerializeField] private float maxSpeed = 100f;

    private Vector3 _velocity;
    private Transform _currentTarget;

    private void LateUpdate()
    {
        if (!TryResolveTarget(out var target))
        {
            return;
        }

        var desiredPosition = target.position + followOffset;
        transform.position = Vector3.SmoothDamp(
            transform.position,
            desiredPosition,
            ref _velocity,
            Mathf.Max(0.01f, smoothTime),
            maxSpeed,
            Time.deltaTime);
    }

    private bool TryResolveTarget(out Transform target)
    {
        if (_currentTarget != null && _currentTarget.gameObject.activeInHierarchy)
        {
            target = _currentTarget;
            return true;
        }

        _currentTarget = null;
        if (MapSpawnManager.Instance == null || MapSpawnManager.Instance.player == null)
        {
            target = null;
            return false;
        }

        _currentTarget = MapSpawnManager.Instance.player.transform;
        target = _currentTarget;
        return true;
    }
}
