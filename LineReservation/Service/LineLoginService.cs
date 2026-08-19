using System.Diagnostics;
using System.Text;
using System.Text.Json;
using LineReservation.Models;
using Microsoft.Extensions.Options;

namespace LineReservation.Service
{
    public class LineLoginService
    {
        private readonly HttpClient _http;
        private readonly LineLoginOptions _options;
        private readonly ILogger<LineLoginService> _logger;
        private readonly Func_Log _fileLog;

        public LineLoginService(
            HttpClient http,
            IOptions<LineLoginOptions> options,
            ILogger<LineLoginService> logger,
            Func_Log fileLog)
        {
            _http = http;
            _options = options.Value;
            _logger = logger;
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

        public async Task LogAllTokenDataAsync(string code, string expectedNonce, CancellationToken ct)
        {
            var swAll = Stopwatch.StartNew();

            var token = await ExchangeCodeAsync(code, ct);

            Info(
                $"LINE token response: token_type={token.TokenType}, expires_in={token.ExpiresIn}, scope={token.Scope}, access_token={Mask(token.AccessToken)}, refresh_token={Mask(token.RefreshToken)}, id_token={Mask(token.IdToken)}");

            if (!string.IsNullOrWhiteSpace(token.AccessToken))
            {
                LogJwtIfPossible("access_token", token.AccessToken);

                var verifyJson = await TimedGetAsync(
                    "verify",
                    "https://api.line.me/oauth2/v2.1/verify?access_token=" + Uri.EscapeDataString(token.AccessToken),
                    bearer: null,
                    ct);
                Info("LINE verify access_token: " + verifyJson);

                var profileJson = await TimedGetAsync(
                    "profile",
                    "https://api.line.me/v2/profile",
                    bearer: token.AccessToken,
                    ct);
                Info("LINE profile: " + profileJson);

                var userInfoJson = await TimedGetAsync(
                    "userinfo",
                    "https://api.line.me/oauth2/v2.1/userinfo",
                    bearer: token.AccessToken,
                    ct);
                Info("LINE userinfo: " + userInfoJson);
            }

            if (!string.IsNullOrWhiteSpace(token.IdToken))
            {
                var payload = DecodeJwtPayload(token.IdToken);
                Info("LINE id_token payload: " + payload.GetRawText());

                if (payload.TryGetProperty("nonce", out var nonceEl))
                {
                    var nonce = nonceEl.GetString();
                    if (!string.Equals(nonce, expectedNonce, StringComparison.Ordinal))
                    {
                        Error($"LINE id_token nonce mismatch. expected={expectedNonce}, actual={nonce}");
                    }
                }
            }

            swAll.Stop();
            _fileLog.SystemPerformance_txt($"LogAllTokenDataAsync elapsed={swAll.ElapsedMilliseconds}ms");
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

            var sw = Stopwatch.StartNew();
            using var res = await _http.PostAsync("https://api.line.me/oauth2/v2.1/token", content, ct);
            var body = await res.Content.ReadAsStringAsync(ct);
            sw.Stop();
            _fileLog.SystemPerformance_txt($"LINE token HTTP elapsed={sw.ElapsedMilliseconds}ms status={(int)res.StatusCode}");

            Info($"LINE token HTTP {(int)res.StatusCode}: {MaskSecretsInJson(body)}");

            if (!res.IsSuccessStatusCode)
                throw new InvalidOperationException($"LINE token exchange failed: {(int)res.StatusCode} {MaskSecretsInJson(body)}");

            return JsonSerializer.Deserialize<LineTokenResponse>(body)
                   ?? throw new InvalidOperationException("LINE token response is empty.");
        }

        private async Task<string> TimedGetAsync(string name, string url, string? bearer, CancellationToken ct)
        {
            var sw = Stopwatch.StartNew();
            var result = await GetStringAsync(url, bearer, ct);
            sw.Stop();
            _fileLog.SystemPerformance_txt($"LINE {name} HTTP elapsed={sw.ElapsedMilliseconds}ms");
            return result;
        }

        private async Task<string> GetStringAsync(string url, string? bearer, CancellationToken ct)
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, url);
            if (!string.IsNullOrWhiteSpace(bearer))
                req.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", bearer);

            using var res = await _http.SendAsync(req, ct);
            var body = await res.Content.ReadAsStringAsync(ct);
            return $"HTTP {(int)res.StatusCode} {body}";
        }

        private void LogJwtIfPossible(string name, string value)
        {
            try
            {
                var payload = DecodeJwtPayload(value);
                Info($"LINE {name} JWT payload: {payload.GetRawText()}");
            }
            catch (Exception ex)
            {
                Info($"LINE {name} is not a JWT (or cannot decode): {ex.Message}");
            }
        }

        private void Info(string message)
        {
            _logger.LogInformation("{Message}", message);
            _fileLog.SystemLog_Txt(message);
        }

        private void Error(string message)
        {
            _logger.LogWarning("{Message}", message);
            _fileLog.SystemErrorLog_Txt(message);
        }

        private static JsonElement DecodeJwtPayload(string jwt)
        {
            var parts = jwt.Split('.');
            if (parts.Length < 2)
                throw new FormatException("Not a JWT.");

            var json = Encoding.UTF8.GetString(Base64UrlDecode(parts[1]));
            return JsonDocument.Parse(json).RootElement.Clone();
        }

        private static byte[] Base64UrlDecode(string input)
        {
            var s = input.Replace('-', '+').Replace('_', '/');
            switch (s.Length % 4)
            {
                case 2: s += "=="; break;
                case 3: s += "="; break;
            }
            return Convert.FromBase64String(s);
        }

        private static string Mask(string? value)
        {
            if (string.IsNullOrEmpty(value)) return "(null)";
            if (value.Length <= 12) return $"*** (len={value.Length})";
            return $"{value[..6]}...{value[^6..]} (len={value.Length})";
        }

        private static string MaskSecretsInJson(string body)
        {
            try
            {
                using var doc = JsonDocument.Parse(body);
                var dict = new Dictionary<string, object?>();
                foreach (var p in doc.RootElement.EnumerateObject())
                {
                    dict[p.Name] = p.Name is "access_token" or "refresh_token" or "id_token"
                        ? Mask(p.Value.GetString())
                        : p.Value.ToString();
                }
                return JsonSerializer.Serialize(dict);
            }
            catch
            {
                return body;
            }
        }
    }
}
