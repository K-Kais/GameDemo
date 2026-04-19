using GameDemo.Network;
using UnityEngine;

public class CharacterSelectionMenu : MonoBehaviour
{
    public static CharacterSelectionMenu Instance { get; private set; }

    [SerializeField] private MapSpawnManager mapSpawnManager;
    [SerializeField] private WebSocketManager webSocketManager;
    [SerializeField] private bool hideAfterSelect = true;
    public bool _isVisible;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void EnsureInstance()
    {
        if (FindAnyObjectByType<CharacterSelectionMenu>() != null)
        {
            return;
        }

        var gameObject = new GameObject(nameof(CharacterSelectionMenu));
        DontDestroyOnLoad(gameObject);
        gameObject.AddComponent<CharacterSelectionMenu>();
    }

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;

        if (mapSpawnManager == null)
        {
            mapSpawnManager = FindAnyObjectByType<MapSpawnManager>();
        }

        ResolveWebSocketManager();
        _isVisible = false;
    }

    private void OnDestroy()
    {
        if (Instance == this)
        {
            Instance = null;
        }
    }

    public void ShowMenu()
    {
        var authMenu = AuthMenuUI.Instance;
        if (authMenu == null)
        {
            authMenu = FindAnyObjectByType<AuthMenuUI>();
        }

        if (authMenu != null && authMenu.ShowCharacterSelectionTab())
        {
            return;
        }
    }

    public void DrawInAuthTab()
    {
        if (!CanOpenCharacterSelection())
        {
            GUILayout.Label("Ban can dang nhap thanh cong de chon nhan vat.");
            return;
        }

        if (mapSpawnManager == null)
        {
            mapSpawnManager = FindAnyObjectByType<MapSpawnManager>();
        }

        if (mapSpawnManager == null)
        {
            GUILayout.Label("Dang doi khoi tao he thong nhan vat...");
            return;
        }

        DrawSelectionBody(allowCloseButton: false);
    }

    private void Update()
    {
        _isVisible = false;
    }

    private void OnGUI()
    {
        // Standalone window is intentionally disabled.
    }

    private bool CanOpenCharacterSelection()
    {
        ResolveWebSocketManager();
        if (webSocketManager == null || !webSocketManager.IsConnected)
        {
            return false;
        }

        if (AuthMenuUI.Instance != null && !AuthMenuUI.Instance.HasAuthenticated)
        {
            return false;
        }

        return true;
    }

    private void DrawSelectionBody(bool allowCloseButton)
    {
        if (mapSpawnManager == null)
        {
            GUILayout.Label("Dang doi khoi tao he thong nhan vat...");
            return;
        }

        var prefabs = mapSpawnManager.AvailablePlayerPrefabs;
        if (prefabs == null || prefabs.Length == 0)
        {
            GUILayout.Label("Chua co prefab nhan vat trong MapSpawnManager.");
        }
        else
        {
            for (var i = 0; i < prefabs.Length; i++)
            {
                var prefab = prefabs[i];
                var label = prefab != null ? prefab.name : $"Character {i + 1} (null)";
                var isSelected = i == mapSpawnManager.SelectedCharacterIndex;

                var cachedColor = GUI.color;
                if (isSelected)
                {
                    GUI.color = new Color(0.65f, 1f, 0.65f, 1f);
                }

                GUI.enabled = prefab != null;
                if (GUILayout.Button($"{i + 1}. {label}", GUILayout.Height(32f)))
                {
                    mapSpawnManager.SelectCharacter(i);
                }

                GUI.enabled = true;
                GUI.color = cachedColor;
            }
        }

        GUILayout.Space(10f);
        GUILayout.BeginHorizontal();
        GUILayout.Label($"Dang chon: {mapSpawnManager.SelectedCharacterIndex + 1}");
        GUILayout.FlexibleSpace();
        if (allowCloseButton &&
            !mapSpawnManager.IsAwaitingCharacterSelection &&
            GUILayout.Button("Dong", GUILayout.Width(90f), GUILayout.Height(30f)))
        {
            // Kept for compatibility when embedded in other containers.
        }

        GUILayout.EndHorizontal();
    }

    private void ResolveWebSocketManager()
    {
        if (webSocketManager == null && WebSocketManager.Instance != null)
        {
            webSocketManager = WebSocketManager.Instance;
        }

        if (webSocketManager != null)
        {
            return;
        }

        webSocketManager = FindAnyObjectByType<WebSocketManager>();
    }
}
