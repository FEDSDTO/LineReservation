using LineReservation.Models;
using LineReservation.Service;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace LineReservation.Controllers
{
    [Route("line")]
    public class LineController : Controller
    {
        private const string StateKey = "LineLogin.State";
        private const string NonceKey = "LineLogin.Nonce";

        private readonly LineLoginService _line;
        private readonly LineLoginOptions _options;
        private readonly ILogger<LineController> _logger;
        private readonly Func_Log _fileLog;

        public LineController(
            LineLoginService line,
            IOptions<LineLoginOptions> options,
            ILogger<LineController> logger,
            Func_Log fileLog)
        {
            _line = line;
            _options = options.Value;
            _logger = logger;
            _fileLog = fileLog;
        }

        [HttpGet("login")]
        public IActionResult Login()
        {
            try
            {
                if (string.IsNullOrWhiteSpace(_options.ChannelId) ||
                    string.IsNullOrWhiteSpace(_options.RedirectUri) ||
                    string.IsNullOrWhiteSpace(_options.ChannelSecret))
                {
                    var msg =
                        $"LINE Login 設定不完整。ChannelId空={string.IsNullOrWhiteSpace(_options.ChannelId)}, RedirectUri空={string.IsNullOrWhiteSpace(_options.RedirectUri)}, ChannelSecret空={string.IsNullOrWhiteSpace(_options.ChannelSecret)}";
                    _logger.LogError("{Message}", msg);
                    _fileLog.SystemErrorLog_Txt(msg);
                    return Content("LINE Login 設定未載入，請確認網站目錄的 appsettings.json 有 LineLogin 區段後回收 IIS。");
                }

                var state = Guid.NewGuid().ToString("N");
                var nonce = Guid.NewGuid().ToString("N");
                HttpContext.Session.SetString(StateKey, state);
                HttpContext.Session.SetString(NonceKey, nonce);

                var url = _line.BuildAuthorizeUrl(state, nonce);
                var infoMsg = $"Redirect to LINE authorize. redirect_uri={_options.RedirectUri}";
                _logger.LogInformation("{Message}", infoMsg);
                _fileLog.SystemLog_Txt(infoMsg);
                return Redirect(url);
            }
            catch (Exception ex)
            {
                var msg = $"LINE login exception: {ex}";
                _logger.LogError(ex, "{Message}", msg);
                _fileLog.SystemErrorLog_Txt(msg);
                return Content("LINE 登入發生錯誤，請查看 SystemErrorLog。");
            }
        }

        [HttpGet("callback")]
        public async Task<IActionResult> Callback(
            string? code,
            string? state,
            string? error,
            string? error_description,
            CancellationToken ct)
        {
            if (!string.IsNullOrWhiteSpace(error))
            {
                var msg = $"LINE callback error={error}, description={error_description}";
                _logger.LogWarning("{Message}", msg);
                _fileLog.SystemErrorLog_Txt(msg);
                return Content($"LINE 授權失敗：{error} {error_description}");
            }

            var expectedState = HttpContext.Session.GetString(StateKey);
            var expectedNonce = HttpContext.Session.GetString(NonceKey) ?? "";
            HttpContext.Session.Remove(StateKey);
            HttpContext.Session.Remove(NonceKey);

            if (string.IsNullOrWhiteSpace(code) ||
                string.IsNullOrWhiteSpace(state) ||
                !string.Equals(state, expectedState, StringComparison.Ordinal))
            {
                var msg =
                    $"LINE callback invalid. hasCode={!string.IsNullOrWhiteSpace(code)}, stateMatch={string.Equals(state, expectedState, StringComparison.Ordinal)}";
                _logger.LogWarning("{Message}", msg);
                _fileLog.SystemErrorLog_Txt(msg);
                return Content("LINE callback 驗證失敗（code/state）。");
            }

            try
            {
                await _line.LogAllTokenDataAsync(code, expectedNonce, ct);
            }
            catch (Exception ex)
            {
                var msg = $"LINE callback exception: {ex.Message}";
                _logger.LogError(ex, "{Message}", msg);
                _fileLog.SystemErrorLog_Txt(msg);
                return Content("LINE 換 token 或寫 log 失敗。");
            }

            _fileLog.SystemLog_Txt("LINE callback success, redirect to " + _options.SuccessRedirectUrl);
            return Redirect(_options.SuccessRedirectUrl);
        }
    }
}
