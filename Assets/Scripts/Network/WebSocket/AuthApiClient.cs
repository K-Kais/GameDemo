using System;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Networking;

namespace GameDemo.Network
{
    public readonly struct AuthCallResult
    {
        public bool Success { get; }
        public string Error { get; }
        public AuthResponse Data { get; }

        private AuthCallResult(bool success, string error, AuthResponse data)
        {
            Success = success;
            Error = error;
            Data = data ?? new AuthResponse();
        }

        public static AuthCallResult Succeeded(AuthResponse data) => new(true, string.Empty, data);
        public static AuthCallResult Failed(string error) => new(false, error, new AuthResponse());
    }

    public readonly struct ActionCallResult
    {
        public bool Success { get; }
        public string Error { get; }
        public ActionResponse Data { get; }

        private ActionCallResult(bool success, string error, ActionResponse data)
        {
            Success = success;
            Error = error;
            Data = data ?? new ActionResponse();
        }

        public static ActionCallResult Succeeded(ActionResponse data) => new(true, string.Empty, data);
        public static ActionCallResult Failed(string error) => new(false, error, new ActionResponse());
    }

    public static class AuthApiClient
    {
        public static Task<AuthCallResult> LoginAsync(
            string apiBaseUrl,
            string userName,
            string password,
            CancellationToken cancellationToken = default)
        {
            return PostAuthAsync(
                apiBaseUrl,
                "/api/auth/login",
                new AuthRequest
                {
                    userName = userName,
                    password = password
                },
                cancellationToken);
        }

        public static Task<ActionCallResult> RegisterAsync(
            string apiBaseUrl,
            string userName,
            string password,
            string confirmPassword,
            bool acceptedTerms,
            CancellationToken cancellationToken = default)
        {
            return PostActionAsync(
                apiBaseUrl,
                "/api/auth/register",
                new RegisterRequest
                {
                    userName = userName,
                    password = password,
                    confirmPassword = confirmPassword,
                    acceptedTerms = acceptedTerms
                },
                cancellationToken);
        }

        public static Task<AuthCallResult> CreateCharacterAsync(
            string apiBaseUrl,
            string token,
            string characterName,
            CancellationToken cancellationToken = default)
        {
            return PostAuthAsync(
                apiBaseUrl,
                "/api/auth/create-character",
                new CreateCharacterRequest
                {
                    characterName = characterName
                },
                cancellationToken,
                token);
        }

        private static async Task<AuthCallResult> PostAuthAsync(
            string apiBaseUrl,
            string path,
            object body,
            CancellationToken cancellationToken,
            string bearerToken = "")
        {
            var (success, responseText, fallbackError) = await SendPostAsync(apiBaseUrl, path, body, cancellationToken, bearerToken);
            if (!success)
            {
                return AuthCallResult.Failed(ExtractError(responseText, fallbackError));
            }

            var auth = JsonUtility.FromJson<AuthResponse>(responseText);
            if (auth == null || string.IsNullOrWhiteSpace(auth.token))
            {
                return AuthCallResult.Failed("Phản hồi đăng nhập không hợp lệ");
            }

            return AuthCallResult.Succeeded(auth);
        }

        private static async Task<ActionCallResult> PostActionAsync(
            string apiBaseUrl,
            string path,
            object body,
            CancellationToken cancellationToken)
        {
            var (success, responseText, fallbackError) = await SendPostAsync(apiBaseUrl, path, body, cancellationToken, string.Empty);
            if (!success)
            {
                return ActionCallResult.Failed(ExtractError(responseText, fallbackError));
            }

            var action = JsonUtility.FromJson<ActionResponse>(responseText) ?? new ActionResponse();
            return ActionCallResult.Succeeded(action);
        }

        private static async Task<(bool Success, string ResponseText, string FallbackError)> SendPostAsync(
            string apiBaseUrl,
            string path,
            object body,
            CancellationToken cancellationToken,
            string bearerToken)
        {
            var url = CombineUrl(apiBaseUrl, path);
            var bodyJson = JsonUtility.ToJson(body ?? new object());

            using var request = new UnityWebRequest(url, UnityWebRequest.kHttpVerbPOST);
            request.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(bodyJson));
            request.downloadHandler = new DownloadHandlerBuffer();
            request.SetRequestHeader("Content-Type", "application/json");
            if (!string.IsNullOrWhiteSpace(bearerToken))
            {
                request.SetRequestHeader("Authorization", $"Bearer {bearerToken}");
            }

            using var registration = cancellationToken.Register(request.Abort);
            var operation = request.SendWebRequest();
            while (!operation.isDone)
            {
                await Task.Yield();
            }

            if (cancellationToken.IsCancellationRequested)
            {
                return (false, string.Empty, "Request was cancelled.");
            }

            var responseText = request.downloadHandler?.text ?? string.Empty;
            var isHttpSuccess = request.responseCode is >= 200 and < 300;
            var success = request.result == UnityWebRequest.Result.Success && isHttpSuccess;
            var fallbackError = request.error;
            return (success, responseText, fallbackError);
        }

        private static string CombineUrl(string baseUrl, string path)
        {
            var normalizedBase = string.IsNullOrWhiteSpace(baseUrl)
                ? "http://localhost:5268"
                : baseUrl.Trim();
            return $"{normalizedBase.TrimEnd('/')}/{path.TrimStart('/')}";
        }

        private static string ExtractError(string responseText, string fallback)
        {
            if (!string.IsNullOrWhiteSpace(responseText))
            {
                var error = JsonUtility.FromJson<ErrorResponse>(responseText);
                if (error != null && !string.IsNullOrWhiteSpace(error.message))
                {
                    return error.message;
                }
            }

            return string.IsNullOrWhiteSpace(fallback) ? "Request failed." : fallback;
        }
    }
}
