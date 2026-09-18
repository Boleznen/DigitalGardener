using System;
using System.IO;
using System.Linq;
using System.Text;

namespace DigitalGardener
{
    public static class LoggerService
    {
        private static readonly string LogDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "DigitalGardener_Logs");
        private static readonly object LockObj = new object();
        private const int MaxLogFiles = 20;

        /// <summary>
        /// Имя файла лога для текущей сессии. Фиксируется один раз — при первом
        /// обращении к классу. Формат: errors_2026-09-19_14-32-01.log
        /// </summary>
        private static readonly string SessionLogFile = BuildSessionFileName();

        /// <summary>
        /// Событие: новая запись в логе. Подписчик (MainWindow) дописывает её в UI в реальном времени.
        /// </summary>
        public static event Action<string>? OnErrorLogged;

        static LoggerService()
        {
            try
            {
                if (!Directory.Exists(LogDir))
                    Directory.CreateDirectory(LogDir);
                CleanOldLogs();
            }
            catch { }
        }

        private static string BuildSessionFileName()
            => $"errors_{DateTime.Now:yyyy-MM-dd_HH-mm-ss}.log";

        private static string GetLogFilePath()
            => Path.Combine(LogDir, SessionLogFile);

        private static void CleanOldLogs()
        {
            try
            {
                var files = Directory.GetFiles(LogDir, "errors_*.log")
                    .Select(f => new FileInfo(f))
                    .OrderByDescending(f => f.CreationTime)
                    .ToList();

                for (int i = MaxLogFiles; i < files.Count; i++)
                {
                    try { files[i].Delete(); } catch { }
                }
            }
            catch { }
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

                    string entry = sb.ToString();
                    File.AppendAllText(GetLogFilePath(), entry, Encoding.UTF8);

                    try { OnErrorLogged?.Invoke(entry); } catch { }
                }
                catch { }
            }
        }

        public static string GetLogContents()
        {
            try
            {
                string logPath = GetLogFilePath();
                return File.Exists(logPath)
                    ? File.ReadAllText(logPath, Encoding.UTF8)
                    : "Записей в журнале ошибок пока нет.";
            }
            catch (Exception ex) { return $"Ошибка чтения журнала: {ex.Message}"; }
        }

        /// <summary>Имя файла лога текущей сессии (для отображения в UI).</summary>
        public static string GetCurrentLogFileName() => SessionLogFile;

        /// <summary>Путь к папке, где лежат все лог-файлы.</summary>
        public static string GetLogsFolderPath() => LogDir;

        /// <summary>Удаляет файл лога текущей сессии. Используется кнопкой «Очистить журнал ошибок».</summary>
        public static bool ClearCurrentLog()
        {
            lock (LockObj)
            {
                try
                {
                    string logPath = GetLogFilePath();
                    if (File.Exists(logPath))
                    {
                        File.Delete(logPath);
                        return true;
                    }
                    return false;
                }
                catch { return false; }
            }
        }
    }
}