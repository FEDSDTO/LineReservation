using System.Text.Json;
using LineReservation.Models;
using Microsoft.Extensions.Options;

namespace LineReservation.Service
{
    public class LineLoginService
    {
        private readonly HttpClient _http;
        private readonly LineLoginOptions _options;
        private readonly Func_Log _fileLog;

        public LineLoginService(
            HttpClient http,
            IOptions<LineLoginOptions> options,
            Func_Log fileLog)
        {
            _http = http;
            _options = options.Value;
            _fileLog = fileLog;
        }

        public string BuildAuthorizeUrl(string state, string nonce)
        {
            var query = new Dictionary<string, string>
            {
                ["response_type"] = "code",
                ["client_id"] = _options.ChannelId,
                ["redirect_uri"] = _options.RedirectUri,
                ["state"] = state,
                ["scope"] = _options.Scope,
                ["nonce"] = nonce,
                ["prompt"] = "consent"
            };

            return "https://access.line.me/oauth2/v2.1/authorize?" +
                   string.Join("&", query.Select(kv =>
                       $"{Uri.EscapeDataString(kv.Key)}={Uri.EscapeDataString(kv.Value)}"));
        }

        public async Task<string?> LogAllTokenDataAsync(string code, string expectedNonce, CancellationToken ct)
        {
            _ = expectedNonce;

            var token = await ExchangeCodeAsync(code, ct);
            if (string.IsNullOrWhiteSpace(token.AccessToken))
                throw new InvalidOperationException("LINE access_token is empty.");

            var profileJson = await GetStringAsync("https://api.line.me/v2/profile", token.AccessToken, ct);
            var lineUserId = ParseProfileUserId(profileJson);
            _fileLog.SystemLog_Txt($"LINE profile userId={lineUserId}");
            return lineUserId;
        }

        private static string? ParseProfileUserId(string profileJson)
        {
            using var doc = JsonDocument.Parse(profileJson);
            return doc.RootElement.TryGetProperty("userId", out var userIdEl)
                ? userIdEl.GetString()
                : null;
        }

        private async Task<LineTokenResponse> ExchangeCodeAsync(string code, CancellationToken ct)
        {
            using var content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["grant_type"] = "authorization_code",
                ["code"] = code,
                ["redirect_uri"] = _options.RedirectUri,
                ["client_id"] = _options.ChannelId,
                ["client_secret"] = _options.ChannelSecret
            });

            using var res = await _http.PostAsync("https://api.line.me/oauth2/v2.1/token", content, ct);
            var body = await res.Content.ReadAsStringAsync(ct);

            if (!res.IsSuccessStatusCode)
                throw new InvalidOperationException($"LINE token exchange failed: {(int)res.StatusCode}");

            return JsonSerializer.Deserialize<LineTokenResponse>(body)
                   ?? throw new InvalidOperationException("LINE token response is empty.");
        }

        private async Task<string> GetStringAsync(string url, string? bearer, CancellationToken ct)
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, url);
            if (!string.IsNullOrWhiteSpace(bearer))
                req.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", bearer);

            using var res = await _http.SendAsync(req, ct);
            var body = await res.Content.ReadAsStringAsync(ct);
            if (!res.IsSuccessStatusCode)
                throw new InvalidOperationException($"LINE profile failed: {(int)res.StatusCode}");
            return body;
        }
    }
}
