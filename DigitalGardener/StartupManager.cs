using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Security;
using Microsoft.Win32;

namespace DigitalGardener
{
    public class StartupManager
    {
        private const string RunHKCU = @"Software\Microsoft\Windows\CurrentVersion\Run";
        private const string RunHKLM = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run";

        public bool IsLikelyProblematic(string? command)
        {
            if (string.IsNullOrEmpty(command)) return false;
            var c = command.ToLowerInvariant();
            return c.Contains("temp") || c.Contains("malware") || c.Contains("virus");
        }

        public List<StartupItem> GetStartupItems()
        {
            var items = new List<StartupItem>();

            ReadRunKey(Registry.CurrentUser, RunHKCU, "HKCU\\Run", items);
            ReadRunKey(Registry.LocalMachine, RunHKLM, "HKLM\\Run", items);

            string userStartup = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                @"Microsoft\Windows\Start Menu\Programs\Startup");
            ScanStartupFolder(userStartup, "Startup (User)", items);

            string commonStartup = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                @"Microsoft\Windows\Start Menu\Programs\Startup");
            ScanStartupFolder(commonStartup, "Startup (Common)", items);

            return items;
        }

        private void ReadRunKey(RegistryKey root, string subKey, string location, List<StartupItem> items)
        {
            try
            {
                using var key = root.OpenSubKey(subKey, writable: false);
                if (key == null) return;
                foreach (var name in key.GetValueNames())
                {
                    var value = key.GetValue(name)?.ToString() ?? "";
                    items.Add(new StartupItem
                    {
                        Name = name,
                        Path = value,
                        Description = "Элемент автозагрузки реестра",
                        Location = location,
                        Recommendation = IsLikelyProblematic(value) ? "Проверить" : "ОК",
                        IsEnabled = true
                    });
                }
            }
            catch (Exception ex)
            {
                LoggerService.LogError("Не удалось прочитать ключ автозагрузки", nameof(ReadRunKey), ex);
            }
        }

        private void ScanStartupFolder(string folder, string location, List<StartupItem> items)
        {
            try
            {
                if (!Directory.Exists(folder)) return;
                foreach (var file in Directory.GetFiles(folder))
                {
                    var fi = new FileInfo(file);
                    items.Add(new StartupItem
                    {
                        Name = fi.Name,
                        Path = fi.FullName,
                        Description = "Файл в папке автозагрузки",
                        Location = location,
                        Recommendation = "Проверьте необходимость",
                        IsEnabled = true
                    });
                }
            }
            catch (Exception ex)
            {
                LoggerService.LogError("Не удалось просканировать папку автозагрузки", nameof(ScanStartupFolder), ex);
            }
        }

        /// <summary>
        /// Отключение. Возвращает (успех, сообщение для отчёта).
        /// </summary>
        public (bool success, string message) DisableWithMessage(StartupItem item)
        {
            try
            {
                // 1. HKCU\Run — доступно без UAC
                if (item.Location == "HKCU\\Run")
                {
                    try
                    {
                        using var key = Registry.CurrentUser.OpenSubKey(RunHKCU, writable: true);
                        if (key != null)
                        {
                            key.DeleteValue(item.Name, throwOnMissingValue: false);
                            return (true, $"Отключено из HKCU: {item.Name}");
                        }
                        return (false, $"Не удалось открыть HKCU\\Run для записи");
                    }
                    catch (UnauthorizedAccessException)
                    {
                        return (false, $"Нет прав на HKCU\\Run (запустите от админа)");
                    }
                }

                // 2. HKLM\Run — нужен админ
                if (item.Location == "HKLM\\Run")
                {
                    try
                    {
                        using var key = Registry.LocalMachine.OpenSubKey(RunHKLM, writable: true);
                        if (key != null)
                        {
                            key.DeleteValue(item.Name, throwOnMissingValue: false);
                            return (true, $"Отключено из HKLM: {item.Name}");
                        }
                    }
                    catch (UnauthorizedAccessException) { }
                    catch (SecurityException) { }

                    // Пробуем через reg.exe с UAC
                    return TryRegDeleteWithUac(item.Name)
                        ? (true, $"Отключено из HKLM через UAC: {item.Name}")
                        : (false, $"UAC отклонён или ошибка reg.exe для {item.Name}");
                }

                // 3. Папка Startup — перемещаем файл в бэкап
                if (item.Location.StartsWith("Startup"))
                {
                    try
                    {
                        string backupDir = Path.Combine(
                            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                            "DigitalGardener_DisabledStartup");
                        Directory.CreateDirectory(backupDir);

                        string fileName = Path.GetFileName(item.Path);
                        string dest = Path.Combine(backupDir, fileName);

                        if (File.Exists(dest))
                            dest = Path.Combine(backupDir,
                                Path.GetFileNameWithoutExtension(fileName) + "_" + Guid.NewGuid().ToString("N")[..6]
                                + Path.GetExtension(fileName));

                        // Снимаем атрибуты "системный/скрытый" — иначе desktop.ini не дастся переместить
                        try
                        {
                            var attrs = File.GetAttributes(item.Path);
                            attrs &= ~FileAttributes.System;
                            attrs &= ~FileAttributes.Hidden;
                            attrs &= ~FileAttributes.ReadOnly;
                            File.SetAttributes(item.Path, attrs);
                        }
                        catch { }

                        File.Move(item.Path, dest, overwrite: false);
                        return (true, $"Перемещено в бэкап: {fileName}");
                    }
                    catch (UnauthorizedAccessException)
                    {
                        return (false, $"Нет прав на перемещение {item.Name}");
                    }
                    catch (IOException ioEx)
                    {
                        return (false, $"Файл занят или защищён: {ioEx.Message}");
                    }
                }

                return (false, $"Неизвестное расположение: {item.Location}");
            }
            catch (Exception ex)
            {
                LoggerService.LogError($"Ошибка отключения {item.Name}", nameof(DisableWithMessage), ex);
                return (false, $"Исключение: {ex.Message}");
            }
        }

        // Оставляем старый метод для совместимости
        public bool Disable(StartupItem item) => DisableWithMessage(item).success;

        private bool TryRegDeleteWithUac(string valueName)
        {
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = "reg.exe",
                    Arguments = $"delete \"HKLM\\SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\Run\" /v \"{valueName}\" /f",
                    Verb = "runas",           // ← вызовет UAC
                    UseShellExecute = true,
                    CreateNoWindow = false,   // ← НЕ скрываем, чтобы UAC был виден
                    WindowStyle = ProcessWindowStyle.Normal
                };
                var p = Process.Start(psi);
                p?.WaitForExit();
                return p?.ExitCode == 0;
            }
            catch (Exception ex)
            {
                // Если пользователь отменил UAC — Win32Exception
                LoggerService.LogError("reg.exe не выполнен", nameof(TryRegDeleteWithUac), ex);
                return false;
            }
        }
    }
}