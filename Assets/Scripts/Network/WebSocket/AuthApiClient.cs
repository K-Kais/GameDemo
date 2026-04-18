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

    public static class AuthApiClient
    {
        public static Task<AuthCallResult> LoginAsync(
            string apiBaseUrl,
            string userName,
            string password,
            CancellationToken cancellationToken = default)
        {
            return PostAuthAsync(apiBaseUrl, "/api/auth/login", userName, password, cancellationToken);
        }

        public static Task<AuthCallResult> RegisterAsync(
            string apiBaseUrl,
            string userName,
            string password,
            CancellationToken cancellationToken = default)
        {
            return PostAuthAsync(apiBaseUrl, "/api/auth/register", userName, password, cancellationToken);
        }

        private static async Task<AuthCallResult> PostAuthAsync(
            string apiBaseUrl,
            string path,
            string userName,
            string password,
            CancellationToken cancellationToken)
        {
            var url = CombineUrl(apiBaseUrl, path);
            var bodyJson = JsonUtility.ToJson(new AuthRequest
            {
                userName = userName,
                password = password
            });

            using var request = new UnityWebRequest(url, UnityWebRequest.kHttpVerbPOST);
            request.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(bodyJson));
            request.downloadHandler = new DownloadHandlerBuffer();
            request.SetRequestHeader("Content-Type", "application/json");

            using var registration = cancellationToken.Register(request.Abort);
            var operation = request.SendWebRequest();
            while (!operation.isDone)
            {
                await Task.Yield();
            }

            if (cancellationToken.IsCancellationRequested)
            {
                return AuthCallResult.Failed("Request was cancelled.");
            }

            var responseText = request.downloadHandler?.text ?? string.Empty;
            var isHttpSuccess = request.responseCode is >= 200 and < 300;
            if (request.result != UnityWebRequest.Result.Success || !isHttpSuccess)
            {
                return AuthCallResult.Failed(ExtractError(responseText, request.error));
            }

            var auth = JsonUtility.FromJson<AuthResponse>(responseText);
            if (auth == null || string.IsNullOrWhiteSpace(auth.token))
            {
                return AuthCallResult.Failed("Invalid auth response from server.");
            }

            return AuthCallResult.Succeeded(auth);
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
