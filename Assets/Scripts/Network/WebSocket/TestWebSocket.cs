using UnityEngine;

namespace GameDemo.Network
{
    public class TestWebSocket : MonoBehaviour
    {
        public WebSocketManager webSocketmanager;
        private void Awake()
        {
            webSocketmanager = FindAnyObjectByType<WebSocketManager>();
        }
        private void Update()
        {
            if (Input.GetKeyDown(KeyCode.T))
            {
                _ = webSocketmanager.SendAsync(OpCode.Chat, new ChatRequest
                {
                    message = "Hello from Unity! This is pikachu"
                });
            }
        }
    }
}
