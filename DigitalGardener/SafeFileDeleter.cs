using System;
using System.IO;
using Microsoft.VisualBasic.FileIO;

namespace DigitalGardener
{
    /// <summary>
    /// Умный удалятор файлов и папок.
    /// - Не спамит ошибками, если файл уже удалён.
    /// - При «Отмена» в диалоге Windows — пропускает ЭТОТ файл, продолжает остальные.
    /// - Занятые файлы → пропуск без остановки процесса.
    /// </summary>
    public static class SafeFileDeleter
    {
        public static string DeleteOne(string path, bool toRecycle, out long freedBytes)
        {
            freedBytes = 0;

            if (string.IsNullOrEmpty(path)) return "notfound";

            bool isDir = Directory.Exists(path);
            bool isFile = File.Exists(path);

            if (!isDir && !isFile) return "notfound";

            try
            {
                if (isDir)
                    freedBytes = GetDirectorySize(path);
                else
                    freedBytes = new FileInfo(path).Length;
            }
            catch { }

            try
            {
                if (isDir)
                {
                    if (toRecycle)
                        FileSystem.DeleteDirectory(path, UIOption.OnlyErrorDialogs,
                            RecycleOption.SendToRecycleBin, UICancelOption.DoNothing);
                    else
                        Directory.Delete(path, recursive: true);
                }
                else
                {
                    if (toRecycle)
                        FileSystem.DeleteFile(path, UIOption.OnlyErrorDialogs,
                            RecycleOption.SendToRecycleBin, UICancelOption.DoNothing);
                    else
                        File.Delete(path);
                }

                return "ok";
            }
            catch (FileNotFoundException) { freedBytes = 0; return "notfound"; }
            catch (DirectoryNotFoundException) { freedBytes = 0; return "notfound"; }
            catch (OperationCanceledException)
            {
                freedBytes = 0;
                return "skipped";
            }
            catch (IOException ioEx)
            {
                freedBytes = 0;
                LoggerService.LogError(
                    $"Не удалось удалить (занят?): {path}. Причина: {ioEx.Message}",
                    nameof(DeleteOne), null);
                return "skipped";
            }
            catch (UnauthorizedAccessException uaEx)
            {
                freedBytes = 0;
                LoggerService.LogError(
                    $"Нет прав на удаление: {path}. Причина: {uaEx.Message}",
                    nameof(DeleteOne), null);
                return "skipped";
            }
            catch (Exception ex)
            {
                freedBytes = 0;
                LoggerService.LogError($"Ошибка удаления: {path}", nameof(DeleteOne), ex);
                return "skipped";
            }
        }

        private static long GetDirectorySize(string dir)
        {
            long total = 0;
            var stack = new System.Collections.Generic.Stack<string>();
            stack.Push(dir);

            while (stack.Count > 0)
            {
                var current = stack.Pop();
                try
                {
                    foreach (var f in Directory.GetFiles(current))
                    {
                        try { total += new FileInfo(f).Length; } catch { }
                    }
                    foreach (var d in Directory.GetDirectories(current))
                        stack.Push(d);
                }
                catch { }
            }
            return total;
        }
    }
}