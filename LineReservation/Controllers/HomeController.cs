using System.Diagnostics;
using LineReservation.Models;
using LineReservation.Service;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace LineReservation.Controllers
{
    public class HomeController : Controller
    {
        private readonly Func_Log _fileLog;

        public HomeController(Func_Log fileLog)
        {
            _fileLog = fileLog;
        }

        public IActionResult Index()
        {
            return Redirect("~/line/login");
        }

        public IActionResult Privacy()
        {
            return View();
        }

        [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
        public IActionResult Error()
        {
            var feature = HttpContext.Features.Get<IExceptionHandlerPathFeature>();
            if (feature?.Error != null)
            {
                var msg = $"Unhandled exception at {feature.Path}: {feature.Error}";
                _fileLog.SystemErrorLog_Txt(msg);
            }

            return View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
        }
    }
}
