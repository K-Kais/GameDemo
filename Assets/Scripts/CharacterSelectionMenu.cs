using UnityEngine;

public class CharacterSelectionMenu : MonoBehaviour
{
    [SerializeField] private MapSpawnManager mapSpawnManager;
    [SerializeField] private bool showOnStart = true;
    [SerializeField] private bool hideAfterSelect = true;
    [SerializeField] private KeyCode toggleMenuKey = KeyCode.Tab;
    [SerializeField] private Vector2 panelSize = new Vector2(340f, 300f);
    [SerializeField] private string panelTitle = "Chon Nhan Vat";

    private bool _isVisible;
    private GUIStyle _titleStyle;

    private void Awake()
    {
        if (mapSpawnManager == null)
        {
            mapSpawnManager = FindAnyObjectByType<MapSpawnManager>();
        }

        _isVisible = showOnStart;
    }

    private void Update()
    {
        if (mapSpawnManager != null && mapSpawnManager.IsAwaitingCharacterSelection)
        {
            _isVisible = true;
            return;
        }

        if (Input.GetKeyDown(toggleMenuKey))
        {
            _isVisible = !_isVisible;
        }
    }

    private void OnGUI()
    {
        if (!_isVisible)
        {
            return;
        }

        if (mapSpawnManager == null)
        {
            mapSpawnManager = FindAnyObjectByType<MapSpawnManager>();
        }

        if (mapSpawnManager == null)
        {
            return;
        }

        if (mapSpawnManager.IsAwaitingCharacterSelection)
        {
            _isVisible = true;
        }

        var prefabs = mapSpawnManager.AvailablePlayerPrefabs;
        var rect = new Rect(
            (Screen.width - panelSize.x) * 0.5f,
            (Screen.height - panelSize.y) * 0.5f,
            panelSize.x,
            panelSize.y);

        GUILayout.BeginArea(rect, GUI.skin.window);
        GUILayout.Space(8f);
        GUILayout.Label(panelTitle, GetTitleStyle());
        GUILayout.Space(8f);

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
                    if (mapSpawnManager.SelectCharacter(i) && hideAfterSelect)
                    {
                        _isVisible = false;
                    }
                }

                GUI.enabled = true;
                GUI.color = cachedColor;
            }
        }

        GUILayout.FlexibleSpace();
        GUILayout.BeginHorizontal();
        GUILayout.Label($"Dang chon: {mapSpawnManager.SelectedCharacterIndex + 1}");
        GUILayout.FlexibleSpace();
        if (!mapSpawnManager.IsAwaitingCharacterSelection &&
            GUILayout.Button("Dong", GUILayout.Width(90f), GUILayout.Height(30f)))
        {
            _isVisible = false;
        }

        GUILayout.EndHorizontal();
        GUILayout.EndArea();
    }

    private GUIStyle GetTitleStyle()
    {
        if (_titleStyle != null)
        {
            return _titleStyle;
        }

        _titleStyle = new GUIStyle(GUI.skin.label)
        {
            fontSize = 18,
            alignment = TextAnchor.MiddleCenter,
            fontStyle = FontStyle.Bold
        };
        return _titleStyle;
    }
}
