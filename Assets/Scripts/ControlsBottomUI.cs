using UnityEngine;

public sealed class ControlsBottomUI : MonoBehaviour
{
    public static ControlsBottomUI Instance { get; private set; }

    [SerializeField] private KeyCode moveUpKey = KeyCode.W;
    [SerializeField] private KeyCode moveLeftKey = KeyCode.A;
    [SerializeField] private KeyCode moveDownKey = KeyCode.S;
    [SerializeField] private KeyCode moveRightKey = KeyCode.D;
    [SerializeField] private KeyCode attackKey = KeyCode.Space;
    [SerializeField] private KeyCode skill1Key = KeyCode.Return;
    [SerializeField] private KeyCode menuKey = KeyCode.F1;

    [SerializeField] private float bottomMargin = 18f;
    [SerializeField] private float panelPadding = 8f;
    [SerializeField] private float buttonHeight = 42f;
    [SerializeField] private float keyButtonWidth = 58f;
    [SerializeField] private float spaceButtonWidth = 120f;
    [SerializeField] private float enterButtonWidth = 122f;
    [SerializeField] private float menuButtonWidth = 112f;
    [SerializeField] private float buttonSpacing = 8f;

    [SerializeField] private Color normalColor = new Color(1f, 1f, 1f, 1f);
    [SerializeField] private Color activeColor = new Color(0.35f, 1f, 0.35f, 1f);
    [SerializeField] private Color cooldownColor = new Color(0.75f, 0.75f, 0.75f, 1f);

    private GUIStyle _buttonStyle;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void EnsureInstance()
    {
        if (FindAnyObjectByType<ControlsBottomUI>() != null)
        {
            return;
        }

        var gameObject = new GameObject(nameof(ControlsBottomUI));
        DontDestroyOnLoad(gameObject);
        gameObject.AddComponent<ControlsBottomUI>();
    }

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
        DontDestroyOnLoad(gameObject);
    }

    private void OnDestroy()
    {
        if (Instance == this)
        {
            Instance = null;
        }
    }

    private void OnGUI()
    {
        var totalWidth = (keyButtonWidth * 4f) + spaceButtonWidth + enterButtonWidth + menuButtonWidth + (buttonSpacing * 6f) + (panelPadding * 2f);
        var totalHeight = buttonHeight + (panelPadding * 2f);
        var rect = new Rect(
            (Screen.width - totalWidth) * 0.5f,
            Screen.height - totalHeight - bottomMargin,
            totalWidth * 1.05f,
            totalHeight);

        GUILayout.BeginArea(rect, GUI.skin.box);
        GUILayout.BeginHorizontal();
        GUILayout.Space(panelPadding);

        DrawKeyButton("W", Input.GetKey(moveUpKey), keyButtonWidth);
        GUILayout.Space(buttonSpacing);
        DrawKeyButton("A", Input.GetKey(moveLeftKey), keyButtonWidth);
        GUILayout.Space(buttonSpacing);
        DrawKeyButton("S", Input.GetKey(moveDownKey), keyButtonWidth);
        GUILayout.Space(buttonSpacing);
        DrawKeyButton("D", Input.GetKey(moveRightKey), keyButtonWidth);
        GUILayout.Space(buttonSpacing);
        DrawKeyButton("SPACE", Input.GetKey(attackKey), spaceButtonWidth);
        GUILayout.Space(buttonSpacing);
        var localPlayer = MapSpawnManager.Instance != null ? MapSpawnManager.Instance.player : null;
        var skill1Cooldown = localPlayer != null ? localPlayer.Skill1CooldownRemaining : 0f;
        var isSkill1CoolingDown = skill1Cooldown > 0.001f;
        var enterLabel = isSkill1CoolingDown ? $"ENTER {skill1Cooldown:0.0}s" : "ENTER";
        DrawKeyButton(enterLabel, Input.GetKey(skill1Key), enterButtonWidth, isSkill1CoolingDown);
        GUILayout.Space(buttonSpacing);
        DrawKeyButton("F1 MENU", Input.GetKey(menuKey), menuButtonWidth);

        GUILayout.Space(panelPadding);
        GUILayout.EndHorizontal();
        GUILayout.EndArea();
    }

    private void DrawKeyButton(string label, bool isActive, float width, bool isCoolingDown = false)
    {
        var cachedColor = GUI.color;
        GUI.color = isCoolingDown ? cooldownColor : (isActive ? activeColor : normalColor);
        GUILayout.Button(label, GetButtonStyle(), GUILayout.Width(width), GUILayout.Height(buttonHeight));
        GUI.color = cachedColor;
    }

    private GUIStyle GetButtonStyle()
    {
        if (_buttonStyle != null)
        {
            return _buttonStyle;
        }

        _buttonStyle = new GUIStyle(GUI.skin.button)
        {
            alignment = TextAnchor.MiddleCenter,
            fontSize = 14,
            fontStyle = FontStyle.Bold
        };
        return _buttonStyle;
    }
}
