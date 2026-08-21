using LineReservation.Models;
using LineReservation.Service;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
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
        private readonly FEDSMBRContext _db;
        private readonly Func_Log _fileLog;

        public LineController(
            LineLoginService line,
            IOptions<LineLoginOptions> options,
            FEDSMBRContext db,
            Func_Log fileLog)
        {
            _line = line;
            _options = options.Value;
            _db = db;
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
                    _fileLog.SystemErrorLog_Txt(msg);
                    return Content("LINE Login 設定未載入，請確認網站目錄的 appsettings.json 有 LineLogin 區段後回收 IIS。");
                }

                var state = Guid.NewGuid().ToString("N");
                var nonce = Guid.NewGuid().ToString("N");
                Response.Cookies.Append(StateKey, state, LoginCookieOptions());
                Response.Cookies.Append(NonceKey, nonce, LoginCookieOptions());

                var url = _line.BuildAuthorizeUrl(state, nonce);
                return Redirect(url);
            }
            catch (Exception ex)
            {
                var msg = $"LINE login exception: {ex}";
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
                _fileLog.SystemErrorLog_Txt(msg);
                return Content($"LINE 授權失敗：{error} {error_description}");
            }

            var expectedState = Request.Cookies[StateKey];
            var expectedNonce = Request.Cookies[NonceKey] ?? "";
            Response.Cookies.Delete(StateKey, DeleteCookieOptions());
            Response.Cookies.Delete(NonceKey, DeleteCookieOptions());

            if (string.IsNullOrWhiteSpace(code) ||
                string.IsNullOrWhiteSpace(state) ||
                !string.Equals(state, expectedState, StringComparison.Ordinal))
            {
                var msg =
                    $"LINE callback invalid. hasCode={!string.IsNullOrWhiteSpace(code)}, stateMatch={string.Equals(state, expectedState, StringComparison.Ordinal)}";
                _fileLog.SystemErrorLog_Txt(msg);
                return Content("LINE callback 驗證失敗（code/state）。");
            }

            string? lineUserId;
            try
            {
                lineUserId = await _line.LogAllTokenDataAsync(code, expectedNonce, ct);
            }
            catch (Exception ex)
            {
                var msg = $"LINE callback exception: {ex.Message}";
                _fileLog.SystemErrorLog_Txt(msg);
                return Content("LINE 換 token 或寫 log 失敗。");
            }

            if (string.IsNullOrWhiteSpace(lineUserId))
            {
                _fileLog.SystemErrorLog_Txt("LINE profile userId is empty.");
                return Content("無法取得 LINE userId。");
            }

            try
            {
                var member = await _db.Members.AsNoTracking()
                    .FirstOrDefaultAsync(m => m.LineId == lineUserId, ct);

                if (member == null)
                {
                    _fileLog.SystemErrorLog_Txt($"Member not found. LineId={lineUserId}");
                    return Content("找不到對應會員。");
                }

                var memberToken = await _db.MemberTokens.AsNoTracking()
                    .Where(t => t.MemberId == member.Id && t.EntityStatus == 1)
                    .OrderByDescending(t => t.ExpireDate)
                    .ThenByDescending(t => t.Id)
                    .FirstOrDefaultAsync(ct);

                if (memberToken == null)
                {
                    _fileLog.SystemErrorLog_Txt($"MemberToken not found. MemberId={member.Id}");
                    return Content("找不到有效會員憑證。");
                }

                var redirectUrl = _options.SuccessRedirectUrl + memberToken.Token.ToString("D");
                _fileLog.SystemLog_Txt($"LINE callback success, MemberId={member.Id}, redirect to {redirectUrl}");
                return Redirect(redirectUrl);
            }
            catch (Exception ex)
            {
                var msg = $"Member mapping exception: {ex.Message}";
                _fileLog.SystemErrorLog_Txt(msg);
                return Content("查詢會員憑證失敗。");
            }
        }

        private CookieOptions LoginCookieOptions() => new()
        {
            HttpOnly = true,
            Secure = Request.IsHttps,
            SameSite = SameSiteMode.Lax,
            Path = "/",
            MaxAge = TimeSpan.FromMinutes(10)
        };

        private static CookieOptions DeleteCookieOptions() => new()
        {
            Path = "/"
        };
    }
}
