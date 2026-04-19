using System.Text.RegularExpressions;
using System.Threading.Tasks;
using UnityEngine;

namespace GameDemo.Network
{
    public sealed class AuthMenuUI : MonoBehaviour
    {
        private enum AuthStep
        {
            Login = 0,
            Register = 1,
            CreateCharacter = 2,
            CharacterSelection = 3
        }

        private const int AccountPasswordMinLength = 6;
        private const int AccountPasswordMaxLength = 30;
        private const int CharacterNameMinLength = 4;
        private const int CharacterNameMaxLength = 12;
        private static readonly Regex AlphaNumericRegex = new("^[a-zA-Z0-9]+$", RegexOptions.Compiled);

        public static AuthMenuUI Instance { get; private set; }

        [SerializeField] private WebSocketManager webSocketManager;
        [SerializeField] private bool showAtStart = true;
        [SerializeField] private KeyCode toggleKey = KeyCode.F1;
        [SerializeField] private Vector2 panelSize = new Vector2(420f, 350f);
        [SerializeField] private string defaultUserName = "demo_user";
        [SerializeField] private string defaultPassword = "123456";

        private bool _isVisible;
        private bool _isSubmitting;
        private bool _hasAuthenticated;
        private string _userName = string.Empty;
        private string _password = string.Empty;
        private string _confirmPassword = string.Empty;
        private bool _acceptedTerms;
        private string _characterName = string.Empty;
        private string _statusMessage = "Nhập tài khoản để đăng nhập.";
        private string _noticeMessage = string.Empty;
        private bool _noticeIsError;
        private float _noticeHideAtTime;
        private AuthStep _step = AuthStep.Login;
        private AuthResponse _pendingAuth = new AuthResponse();

        public bool HasAuthenticated => _hasAuthenticated;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void EnsureInstance()
        {
            if (FindAnyObjectByType<AuthMenuUI>() != null)
            {
                return;
            }

            var gameObject = new GameObject(nameof(AuthMenuUI));
            DontDestroyOnLoad(gameObject);
            gameObject.AddComponent<AuthMenuUI>();
        }

        private void Awake()
        {
            Instance = this;
            _isVisible = showAtStart;
            if (string.IsNullOrWhiteSpace(_userName))
            {
                _userName = defaultUserName;
            }

            if (string.IsNullOrWhiteSpace(_password))
            {
                _password = defaultPassword;
            }

            ResolveWebSocketManager();
        }

        private void OnDestroy()
        {
            if (Instance == this)
            {
                Instance = null;
            }
        }

        private void Update()
        {
            if (Input.GetKeyDown(toggleKey))
            {
                _isVisible = !_isVisible;
            }

            ResolveWebSocketManager();
            if (!_hasAuthenticated && IsAuthenticationSuccessful())
            {
                _hasAuthenticated = true;
            }

            if (_hasAuthenticated && !IsAuthenticationSuccessful())
            {
                _hasAuthenticated = false;
                _step = AuthStep.Login;
                _isVisible = true;
                _statusMessage = "Mất kết nối. Vui lòng đăng nhập lại.";
            }
        }

        private void OnGUI()
        {
            DrawNotice();
            if (!_isVisible)
            {
                return;
            }

            var rect = new Rect(
                (Screen.width - panelSize.x) * 0.5f,
                (Screen.height - panelSize.y) * 0.5f,
                panelSize.x,
                panelSize.y);

            GUILayout.BeginArea(rect, GUI.skin.window);
            GUILayout.Space(8f);
            GUILayout.Label("Đăng nhập / Đăng ký / Tạo nhân vật");
            GUILayout.Space(8f);

            DrawStepHeader();
            GUILayout.Space(8f);

            switch (_step)
            {
                case AuthStep.Login:
                    DrawLoginForm();
                    break;
                case AuthStep.Register:
                    DrawRegisterForm();
                    break;
                case AuthStep.CreateCharacter:
                    DrawCreateCharacterForm();
                    break;
                case AuthStep.CharacterSelection:
                    DrawCharacterSelectionForm();
                    break;
            }

            GUILayout.Space(8f);
            GUILayout.Label(_statusMessage);
            GUILayout.Space(6f);
            GUILayout.Label($"Trạng thái WS: {(webSocketManager != null ? webSocketManager.ConnectionState.ToString() : "N/A")}");

            GUILayout.EndArea();
        }

        private void DrawStepHeader()
        {
            GUI.enabled = !_isSubmitting;
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Đăng nhập", GUILayout.Height(30f)))
            {
                _step = AuthStep.Login;
            }

            if (GUILayout.Button("Đăng ký", GUILayout.Height(30f)))
            {
                _step = AuthStep.Register;
            }

            GUI.enabled = !_isSubmitting && _pendingAuth != null && !string.IsNullOrWhiteSpace(_pendingAuth.token);
            if (GUILayout.Button("Tạo nhân vật", GUILayout.Height(30f)))
            {
                _step = AuthStep.CreateCharacter;
            }

            GUI.enabled = !_isSubmitting && (_hasAuthenticated || IsAuthenticationSuccessful());
            if (GUILayout.Button("Chọn nhân vật", GUILayout.Height(30f)))
            {
                _step = AuthStep.CharacterSelection;
            }

            GUILayout.EndHorizontal();
            GUI.enabled = true;
        }

        private void DrawLoginForm()
        {
            GUILayout.Label("Tài khoản");
            _userName = GUILayout.TextField(_userName ?? string.Empty);
            GUILayout.Space(4f);

            GUILayout.Label("Mật khẩu");
            _password = GUILayout.PasswordField(_password ?? string.Empty, '*');
            GUILayout.Space(10f);

            GUI.enabled = !_isSubmitting;
            if (GUILayout.Button("Đăng nhập", GUILayout.Height(36f)))
            {
                _ = SubmitLoginAsync();
            }

            GUI.enabled = true;
        }

        private void DrawRegisterForm()
        {
            GUILayout.Label("Tài khoản");
            _userName = GUILayout.TextField(_userName ?? string.Empty);
            GUILayout.Space(4f);

            GUILayout.Label("Mật khẩu");
            _password = GUILayout.PasswordField(_password ?? string.Empty, '*');
            GUILayout.Space(4f);

            GUILayout.Label("Mật khẩu xác nhận");
            _confirmPassword = GUILayout.PasswordField(_confirmPassword ?? string.Empty, '*');
            GUILayout.Space(6f);

            _acceptedTerms = GUILayout.Toggle(_acceptedTerms, "Đồng ý điều khoản");
            GUILayout.Space(8f);

            GUI.enabled = !_isSubmitting;
            if (GUILayout.Button("Đăng ký", GUILayout.Height(36f)))
            {
                _ = SubmitRegisterAsync();
            }

            GUI.enabled = true;
        }

        private void DrawCreateCharacterForm()
        {
            GUILayout.Label("Tên nhân vật");
            _characterName = GUILayout.TextField(_characterName ?? string.Empty);
            GUILayout.Space(10f);

            GUI.enabled = !_isSubmitting;
            if (GUILayout.Button("Tạo nhân vật", GUILayout.Height(36f)))
            {
                _ = SubmitCreateCharacterAsync();
            }

            GUI.enabled = true;
        }

        private void DrawCharacterSelectionForm()
        {
            var characterSelectionMenu = global::CharacterSelectionMenu.Instance;
            if (characterSelectionMenu == null)
            {
                characterSelectionMenu = FindAnyObjectByType<global::CharacterSelectionMenu>();
            }

            if (characterSelectionMenu == null)
            {
                GUILayout.Label("Không tìm thấy CharacterSelectionMenu.");
                return;
            }

            characterSelectionMenu.DrawInAuthTab();
        }

        private async Task SubmitLoginAsync()
        {
            ResolveWebSocketManager();
            if (webSocketManager == null)
            {
                _statusMessage = "Không tìm thấy WebSocketManager.";
                return;
            }

            var validationError = ValidateLoginInput(_userName, _password);
            if (!string.IsNullOrEmpty(validationError))
            {
                _statusMessage = validationError;
                _hasAuthenticated = false;
                _isVisible = true;
                ShowNotice(validationError, true);
                return;
            }

            _isSubmitting = true;
            _hasAuthenticated = false;
            if (webSocketManager.IsConnected)
            {
                await webSocketManager.DisconnectAsync();
            }

            var result = await webSocketManager.LoginAsync(_userName, _password);
            _isSubmitting = false;
            if (!result.Success)
            {
                _statusMessage = result.Error;
                _isVisible = true;
                ShowNotice(_statusMessage, true);
                return;
            }

            _pendingAuth = result.Data;
            if (result.Data.requiresCharacterCreation)
            {
                _step = AuthStep.CreateCharacter;
                _statusMessage = "Đăng nhập thành công. Vui lòng tạo nhân vật trước khi vào game.";
                ShowNotice("Đăng nhập thành công!", false);
                _isVisible = true;
                return;
            }

            await webSocketManager.ConnectAuthenticatedSessionAsync(result.Data);
            _hasAuthenticated = await WaitForAuthenticationReadyAsync();
            if (!_hasAuthenticated)
            {
                _statusMessage = "Đăng nhập thành công nhưng chưa kết nối được vào game.";
                _isVisible = true;
                ShowNotice(_statusMessage, true);
                return;
            }

            _statusMessage = "Đăng nhập thành công. Nhấn tab Chọn nhân vật để mở.";
            ShowNotice("Đăng nhập thành công!", false);
            _isVisible = true;
        }

        private async Task SubmitRegisterAsync()
        {
            ResolveWebSocketManager();
            if (webSocketManager == null)
            {
                _statusMessage = "Không tìm thấy WebSocketManager.";
                return;
            }

            var validationError = ValidateRegisterInput(_userName, _password, _confirmPassword, _acceptedTerms);
            if (!string.IsNullOrEmpty(validationError))
            {
                _statusMessage = validationError;
                ShowNotice(validationError, true);
                return;
            }

            _isSubmitting = true;
            var result = await webSocketManager.RegisterAccountAsync(_userName, _password, _confirmPassword, _acceptedTerms);
            _isSubmitting = false;
            if (!result.Success)
            {
                _statusMessage = result.Error;
                ShowNotice(_statusMessage, true);
                return;
            }

            _statusMessage = string.IsNullOrWhiteSpace(result.Data.message)
                ? "Đăng ký thành công"
                : result.Data.message;
            ShowNotice("Đăng ký thành công", false);

            _step = AuthStep.Login;
            _confirmPassword = _password;
            _isVisible = true;
        }

        private async Task SubmitCreateCharacterAsync()
        {
            ResolveWebSocketManager();
            if (webSocketManager == null)
            {
                _statusMessage = "Không tìm thấy WebSocketManager.";
                return;
            }

            var validationError = ValidateCharacterName(_characterName);
            if (!string.IsNullOrEmpty(validationError))
            {
                _statusMessage = validationError;
                ShowNotice(validationError, true);
                return;
            }

            var token = _pendingAuth != null && !string.IsNullOrWhiteSpace(_pendingAuth.token)
                ? _pendingAuth.token
                : webSocketManager.SessionToken;
            if (string.IsNullOrWhiteSpace(token))
            {
                _statusMessage = "Phiên đăng nhập hết hạn, vui lòng thử lại!";
                _step = AuthStep.Login;
                _isVisible = true;
                ShowNotice(_statusMessage, true);
                return;
            }

            _isSubmitting = true;
            var result = await webSocketManager.CreateCharacterAsync(token, _characterName.Trim());
            _isSubmitting = false;
            if (!result.Success)
            {
                _statusMessage = result.Error;
                ShowNotice(_statusMessage, true);
                return;
            }

            _pendingAuth = result.Data;
            await webSocketManager.ConnectAuthenticatedSessionAsync(result.Data);
            _hasAuthenticated = await WaitForAuthenticationReadyAsync();
            if (!_hasAuthenticated)
            {
                _statusMessage = "Tạo nhân vật thành công nhưng chưa kết nối được vào game.";
                _isVisible = true;
                ShowNotice(_statusMessage, true);
                return;
            }

            _statusMessage = "Tạo nhân vật thành công. Nhấn tab Chọn nhân vật để mở.";
            ShowNotice("Tạo nhân vật thành công", false);
            _isVisible = true;
        }

        private static string ValidateLoginInput(string userName, string password)
        {
            if (string.IsNullOrWhiteSpace(userName) || string.IsNullOrWhiteSpace(password))
            {
                return "Bạn cần điền đầy đủ thông tin trước khi đăng nhập";
            }

            return ValidateAccountPassword(userName, password);
        }

        private static string ValidateRegisterInput(string userName, string password, string confirmPassword, bool acceptedTerms)
        {
            if (string.IsNullOrWhiteSpace(userName) ||
                string.IsNullOrWhiteSpace(password) ||
                string.IsNullOrWhiteSpace(confirmPassword))
            {
                return "Bạn cần điền đầy đủ thông tin trước khi đăng ký";
            }

            var accountPasswordError = ValidateAccountPassword(userName, password);
            if (!string.IsNullOrEmpty(accountPasswordError))
            {
                return accountPasswordError;
            }

            if (!string.Equals(password, confirmPassword, System.StringComparison.Ordinal))
            {
                return "Mật khẩu và Mật khẩu xác nhận cần trùng nhau";
            }

            if (!acceptedTerms)
            {
                return "Bạn cần \"Đồng ý điều khoản\" để thực hiện";
            }

            return string.Empty;
        }

        private static string ValidateAccountPassword(string userName, string password)
        {
            if (userName.Length < AccountPasswordMinLength || userName.Length > AccountPasswordMaxLength)
            {
                return "Tài khoản chỉ chấp nhận độ dài 6 - 30 ký tự";
            }

            if (password.Length < AccountPasswordMinLength || password.Length > AccountPasswordMaxLength)
            {
                return "Mật khẩu chỉ chấp nhận độ dài 6 - 30 ký tự";
            }

            if (!AlphaNumericRegex.IsMatch(userName) || !AlphaNumericRegex.IsMatch(password))
            {
                return "Tài khoản, mật khẩu chỉ chấp nhận ký tự a - z, A - Z, 0 - 9";
            }

            return string.Empty;
        }

        private static string ValidateCharacterName(string characterName)
        {
            var trimmed = characterName?.Trim() ?? string.Empty;
            if (trimmed.Length < CharacterNameMinLength || trimmed.Length > CharacterNameMaxLength)
            {
                return "Tên nhân vật chỉ chấp nhận 4 - 12 ký tự!";
            }

            if (!AlphaNumericRegex.IsMatch(trimmed))
            {
                return "Tên nhân vật chỉ chấp nhận ký tự 0 - 9, a - z, A - Z";
            }

            return string.Empty;
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

        private bool IsAuthenticationSuccessful()
        {
            return webSocketManager != null &&
                webSocketManager.IsConnected &&
                !string.IsNullOrWhiteSpace(webSocketManager.LocalPlayerId);
        }

        private async Task<bool> WaitForAuthenticationReadyAsync(int timeoutMilliseconds = 5000)
        {
            var startTime = Time.unscaledTime;
            var timeoutSeconds = Mathf.Max(0.5f, timeoutMilliseconds / 1000f);
            while (Time.unscaledTime - startTime < timeoutSeconds)
            {
                if (IsAuthenticationSuccessful())
                {
                    return true;
                }

                await Task.Delay(50);
            }

            return IsAuthenticationSuccessful();
        }

        private void ShowNotice(string message, bool isError, float durationSeconds = 4f)
        {
            _noticeMessage = message ?? string.Empty;
            _noticeIsError = isError;
            _noticeHideAtTime = Time.unscaledTime + Mathf.Max(1f, durationSeconds);
        }

        private void DrawNotice()
        {
            if (string.IsNullOrWhiteSpace(_noticeMessage))
            {
                return;
            }

            if (Time.unscaledTime >= _noticeHideAtTime)
            {
                _noticeMessage = string.Empty;
                return;
            }

            var rect = new Rect((Screen.width - 520f) * 0.5f, 18f, 520f, 56f);
            var cachedColor = GUI.color;
            GUI.color = _noticeIsError
                ? new Color(1f, 0.6f, 0.6f, 1f)
                : new Color(0.6f, 1f, 0.6f, 1f);
            GUILayout.BeginArea(rect, GUI.skin.box);
            GUILayout.Label(_noticeMessage);
            GUILayout.EndArea();
            GUI.color = cachedColor;
        }

        private void OpenCharacterSelectionMenu()
        {
            if (ShowCharacterSelectionTab())
            {
                return;
            }

            var characterSelectionMenu = global::CharacterSelectionMenu.Instance;
            if (characterSelectionMenu == null)
            {
                characterSelectionMenu = FindAnyObjectByType<global::CharacterSelectionMenu>();
            }

            characterSelectionMenu?.ShowMenu();
        }

        public bool ShowCharacterSelectionTab()
        {
            ResolveWebSocketManager();
            if (webSocketManager == null || !webSocketManager.IsConnected)
            {
                return false;
            }

            _hasAuthenticated = true;
            _step = AuthStep.CharacterSelection;
            _isVisible = true;
            return true;
        }
    }
}
