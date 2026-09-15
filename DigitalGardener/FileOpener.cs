using System;
using System.Diagnostics;
using System.IO;

namespace DigitalGardener
{
    public static class FileOpener
    {
        /// <summary>
        /// Открыть проводник и выделить файл/папку.
        /// </summary>
        public static void ShowInExplorer(string path)
        {
            if (string.IsNullOrEmpty(path)) return;

            try
            {
                if (File.Exists(path))
                {
                    // /select выделяет файл в папке
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = "explorer.exe",
                        Arguments = $"/select,\"{path}\"",
                        UseShellExecute = true
                    });
                }
                else if (Directory.Exists(path))
                {
                    // папка → просто открываем
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = "explorer.exe",
                        Arguments = $"\"{path}\"",
                        UseShellExecute = true
                    });
                }
                else
                {
                    // файла нет — откроем родительскую папку
                    string? parent = Path.GetDirectoryName(path);
                    if (!string.IsNullOrEmpty(parent) && Directory.Exists(parent))
                    {
                        Process.Start(new ProcessStartInfo
                        {
                            FileName = "explorer.exe",
                            Arguments = $"\"{parent}\"",
                            UseShellExecute = true
                        });
                    }
                }
            }
            catch (Exception ex)
            {
                LoggerService.LogError($"Не удалось открыть {path}",
                    nameof(ShowInExplorer), ex);
            }
        }

        /// <summary>Открыть папку (без выделения файла).</summary>
        public static void OpenFolder(string path)
        {
            if (string.IsNullOrEmpty(path) || !Directory.Exists(path)) return;
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = "explorer.exe",
                    Arguments = $"\"{path}\"",
                    UseShellExecute = true
                });
            }
            catch { }
        }
    }
}