using System.Text;

namespace LineReservation.Service
{
    public class Func_Log
    {
        private const string BaseDir = @"C:\Website\LineReservation\Log";
        private static readonly object FileLock = new();

        public void SystemLog_Txt(string ex)
        {
            Write(Path.Combine(BaseDir, "SystemLog"),
                DateTime.Now.ToString("yyyy-MM-dd") + " - LineReservation-SystemLog.txt",
                ex);
        }

        public void SystemErrorLog_Txt(string ex)
        {
            Write(Path.Combine(BaseDir, "SystemErrorLog"),
                DateTime.Now.ToString("yyyy-MM-dd") + " - LineReservation-SystemErrorLog.txt",
                ex);
        }

        public void SystemPerformance_txt(string ex)
        {
            Write(Path.Combine(BaseDir, "SystemPerformance"),
                DateTime.Now.ToString("yyyy-MM-dd") + " - LineReservation_SystemPerformance.txt",
                ex);
        }

        private static void Write(string dir, string fileName, string ex)
        {
            try
            {
                if (!Directory.Exists(dir))
                    Directory.CreateDirectory(dir);

                var sourcePath = Path.Combine(dir, fileName);
                lock (FileLock)
                {
                    using var sw = new StreamWriter(sourcePath, true, Encoding.UTF8);
                    sw.WriteLine(DateTime.Now + " - " + ex + "\r\n");
                }
            }
            catch
            {
                // 寫檔失敗不可讓登入流程中斷
            }
        }
    }
}
