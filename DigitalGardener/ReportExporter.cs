using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace DigitalGardener
{
    public static class ReportExporter
    {
        public static string GetReportsDir()
        {
            string dir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "DigitalGardener_Reports");
            Directory.CreateDirectory(dir);
            return dir;
        }

        /// <summary>Сохранить отчёт в TXT. Возвращает полный путь.</summary>
        public static string ExportToTxt(IEnumerable<ReportItem> items)
        {
            string dir = GetReportsDir();
            string file = Path.Combine(dir, $"report_{DateTime.Now:yyyy-MM-dd_HH-mm-ss}.txt");

            var sb = new StringBuilder();
            sb.AppendLine("=== Digital Gardener — Отчёт ===");
            sb.AppendLine($"Дата: {DateTime.Now:dd.MM.yyyy HH:mm:ss}");
            sb.AppendLine(new string('=', 50));
            sb.AppendLine();

            foreach (var r in items)
                sb.AppendLine($"[{r.Timestamp}] [{r.Category}] {r.Message}");

            File.WriteAllText(file, sb.ToString(), Encoding.UTF8);
            return file;
        }

        /// <summary>Сохранить отчёт в CSV. Возвращает полный путь.</summary>
        public static string ExportToCsv(IEnumerable<ReportItem> items)
        {
            string dir = GetReportsDir();
            string file = Path.Combine(dir, $"report_{DateTime.Now:yyyy-MM-dd_HH-mm-ss}.csv");

            var sb = new StringBuilder();
            sb.AppendLine("Timestamp;Category;Message");

            foreach (var r in items)
            {
                string msg = r.Message.Replace("\"", "\"\"");
                sb.AppendLine($"\"{r.Timestamp}\";\"{r.Category}\";\"{msg}\"");
            }

            File.WriteAllText(file, sb.ToString(), Encoding.UTF8);
            return file;
        }

        /// <summary>Удалить все сохранённые отчёты. Возвращает (удалено файлов, освобождено байт).</summary>
        public static (int deleted, long freedBytes) DeleteAllReports()
        {
            string dir = GetReportsDir();
            int count = 0;
            long bytes = 0;

            try
            {
                foreach (var f in Directory.GetFiles(dir, "report_*.*"))
                {
                    try
                    {
                        var fi = new FileInfo(f);
                        bytes += fi.Length;
                        fi.Delete();
                        count++;
                    }
                    catch { }
                }
            }
            catch { }

            return (count, bytes);
        }

        /// <summary>Открыть папку с отчётами в проводнике.</summary>
        public static void OpenReportsFolder()
        {
            string dir = GetReportsDir();
            try
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "explorer.exe",
                    Arguments = $"\"{dir}\"",
                    UseShellExecute = true
                });
            }
            catch { }
        }
    }
}