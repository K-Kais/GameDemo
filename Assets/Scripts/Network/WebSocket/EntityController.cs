using UnityEngine;

namespace GameDemo.Network
{
    public class EntityController : MonoBehaviour
    {
        public bool isLocalPlayer => MapSpawnManager.Instance.player == this;
        public Vector2 direction { get; private set; } = Vector2.right;
        public string state { get; private set; } = "Idle";

        private void Update()
        {
            if (!isLocalPlayer) return;

            float x = Input.GetAxis("Horizontal");
            float y = Input.GetAxis("Vertical");
            transform.position += new Vector3(x, y, 0f) * Time.deltaTime * 5f;
        }

        public void ApplyNetworkState(float x, float y, float dirX, float dirY, string currentState)
        {
            transform.position = new Vector3(x, y, transform.position.z);
            direction = new Vector2(dirX, dirY);
            state = currentState ?? string.Empty;
        }
    }
}
