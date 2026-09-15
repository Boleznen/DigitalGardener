using System;
using System.IO;
using System.Text;

namespace DigitalGardener
{
    public static class LoggerService
    {
        private static readonly string LogDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "DigitalGardener_Logs");
        private static readonly object LockObj = new object();

        static LoggerService()
        {
            try
            {
                if (!Directory.Exists(LogDir))
                    Directory.CreateDirectory(LogDir);
            }
            catch { /* тихо */ }
        }

        private static string GetLogFilePath()
        {
            string datePart = DateTime.Now.ToString("yyyy-MM-dd");
            return Path.Combine(LogDir, $"errors_{datePart}.log");
        }

        public static void LogError(string message, string methodName, Exception? ex)
        {
            lock (LockObj)
            {
                try
                {
                    var sb = new StringBuilder();
                    sb.AppendLine($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] Ошибка в методе: {methodName}");
                    sb.AppendLine($"Сообщение: {message}");
                    if (ex != null)
                    {
                        sb.AppendLine($"Исключение: {ex.Message}");
                        sb.AppendLine($"Трассировка: {ex.StackTrace}");
                    }
                    sb.AppendLine(new string('-', 50));
                    File.AppendAllText(GetLogFilePath(), sb.ToString(), Encoding.UTF8);
                }
                catch { /* чтобы не ломать приложение */ }
            }
        }

        public static string GetLogContents()
        {
            try
            {
                string logPath = GetLogFilePath();
                return File.Exists(logPath) ? File.ReadAllText(logPath, Encoding.UTF8) : "Записей в журнале ошибок пока нет.";
            }
            catch (Exception ex)
            {
                return $"Ошибка чтения журнала: {ex.Message}";
            }
        }
    }
}
