using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using Microsoft.Win32;

namespace DigitalGardener
{
    public class InstalledProgram
    {
        public string DisplayName { get; set; } = "";
        public string DisplayVersion { get; set; } = "";
        public string Publisher { get; set; } = "";
        public string InstallDate { get; set; } = "";
        public long EstimatedSize { get; set; }
        public string UninstallString { get; set; } = "";
        public string QuietUninstallString { get; set; } = "";
        public string InstallLocation { get; set; } = "";
        public string RegistryKeyPath { get; set; } = "";
    }

    public static class ProgramUninstaller
    {
        public static List<InstalledProgram> GetInstalledPrograms()
        {
            var result = new List<InstalledProgram>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            ScanKey(Registry.LocalMachine,
                @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall", result, seen);
            ScanKey(Registry.LocalMachine,
                @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall", result, seen);
            ScanKey(Registry.CurrentUser,
                @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall", result, seen);

            result.Sort((a, b) =>
                string.Compare(a.DisplayName, b.DisplayName, StringComparison.OrdinalIgnoreCase));

            return result;
        }

        private static void ScanKey(RegistryKey root, string subKey,
            List<InstalledProgram> list, HashSet<string> seen)
        {
            try
            {
                using var key = root.OpenSubKey(subKey);
                if (key == null) return;

                foreach (var subName in key.GetSubKeyNames())
                {
                    try
                    {
                        using var app = key.OpenSubKey(subName);
                        if (app == null) continue;

                        string name = app.GetValue("DisplayName")?.ToString() ?? "";
                        if (string.IsNullOrWhiteSpace(name)) continue;

                        string systemComponent = app.GetValue("SystemComponent")?.ToString() ?? "0";
                        if (systemComponent == "1") continue;

                        string uninstall = app.GetValue("UninstallString")?.ToString() ?? "";
                        if (string.IsNullOrWhiteSpace(uninstall)) continue;

                        string keyId = $"{root.Name}\\{subKey}\\{subName}";
                        if (!seen.Add(keyId)) continue;

                        long size = 0;
                        var sizeObj = app.GetValue("EstimatedSize");
                        if (sizeObj != null)
                        {
                            try { size = Convert.ToInt64(sizeObj) * 1024; } catch { }
                        }

                        string installDate = app.GetValue("InstallDate")?.ToString() ?? "";
                        if (installDate.Length == 8)
                        {
                            try
                            {
                                installDate = $"{installDate.Substring(6, 2)}." +
                                              $"{installDate.Substring(4, 2)}." +
                                              $"{installDate.Substring(0, 4)}";
                            }
                            catch { }
                        }

                        list.Add(new InstalledProgram
                        {
                            DisplayName = name,
                            DisplayVersion = app.GetValue("DisplayVersion")?.ToString() ?? "",
                            Publisher = app.GetValue("Publisher")?.ToString() ?? "",
                            InstallDate = installDate,
                            EstimatedSize = size,
                            UninstallString = uninstall,
                            QuietUninstallString = app.GetValue("QuietUninstallString")?.ToString() ?? "",
                            InstallLocation = app.GetValue("InstallLocation")?.ToString() ?? "",
                            RegistryKeyPath = keyId
                        });
                    }
                    catch { }
                }
            }
            catch (Exception ex)
            {
                LoggerService.LogError($"Ошибка чтения {subKey}", nameof(ScanKey), ex);
            }
        }

        public static (bool success, string message) RunUninstallerWithMessage(InstalledProgram program)
        {
            try
            {
                // 1) Проверяем QuietUninstallString (тихая установка)
                string cmd = !string.IsNullOrEmpty(program.QuietUninstallString)
                    ? program.QuietUninstallString
                    : program.UninstallString;

                if (string.IsNullOrWhiteSpace(cmd))
                    return (false, "Нет команды деинсталляции в реестре");

                // 2) Парсим команду
                string exe, args;
                ParseCommand(cmd, out exe, out args);

                if (string.IsNullOrWhiteSpace(exe))
                    return (false, "Не удалось определить путь к деинсталлятору");

                // 3) Специальная обработка MSI
                if (exe.Equals("MsiExec.exe", StringComparison.OrdinalIgnoreCase) ||
                    exe.Equals("msiexec.exe", StringComparison.OrdinalIgnoreCase) ||
                    exe.Equals("msiexec", StringComparison.OrdinalIgnoreCase))
                {
                    string systemMsiexec = Path.Combine(
                        Environment.GetFolderPath(Environment.SpecialFolder.System),
                        "msiexec.exe");

                    if (!File.Exists(systemMsiexec))
                        return (false, "Не найден msiexec.exe");

                    var msiPsi = new ProcessStartInfo
                    {
                        FileName = systemMsiexec,
                        Arguments = args,
                        UseShellExecute = true
                    };
                    Process.Start(msiPsi);
                    return (true, $"Запущен MSI-деинсталлятор: {program.DisplayName}");
                }

                // 4) Обычный .exe — проверяем существование
                string expandedExe = Environment.ExpandEnvironmentVariables(exe);

                if (!File.Exists(expandedExe))
                {
                    // 🆕 Файл не найден — программа уже удалена
                    bool removed = RemoveFromRegistry(program);
                    if (removed)
                    {
                        return (true,
                            $"⚠️ Программа уже удалена с диска. Запись из реестра удалена: {program.DisplayName}");
                    }
                    return (false, $"Файл не найден: {expandedExe}. " +
                        "Попробуйте удалить запись вручную через regedit.");
                }

                var psi = new ProcessStartInfo
                {
                    FileName = expandedExe,
                    Arguments = args,
                    UseShellExecute = true,
                    WorkingDirectory = Path.GetDirectoryName(expandedExe) ?? ""
                };
                Process.Start(psi);
                return (true, $"Запущен деинсталлятор: {program.DisplayName}");
            }
            catch (Exception ex)
            {
                LoggerService.LogError($"Ошибка запуска деинсталлятора {program.DisplayName}",
                    nameof(RunUninstallerWithMessage), ex);
                return (false, $"Ошибка: {ex.Message}");
            }
        }

        /// <summary>
        /// Удаляет запись о программе из реестра.
        /// Используется, когда файл деинсталлятора не существует.
        /// </summary>
        public static bool RemoveFromRegistry(InstalledProgram program)
        {
            try
            {
                if (string.IsNullOrEmpty(program.RegistryKeyPath))
                    return false;

                string fullPath = program.RegistryKeyPath;

                string hive, subKey;
                if (fullPath.StartsWith("HKEY_CURRENT_USER\\", StringComparison.OrdinalIgnoreCase))
                {
                    hive = "HKCU";
                    subKey = fullPath.Substring("HKEY_CURRENT_USER\\".Length);
                }
                else if (fullPath.StartsWith("HKEY_LOCAL_MACHINE\\", StringComparison.OrdinalIgnoreCase))
                {
                    hive = "HKLM";
                    subKey = fullPath.Substring("HKEY_LOCAL_MACHINE\\".Length);
                }
                else
                {
                    return false;
                }

                using var rootKey = hive == "HKCU"
                    ? Registry.CurrentUser
                    : Registry.LocalMachine;

                int lastSlash = subKey.LastIndexOf('\\');
                if (lastSlash < 0) return false;

                string parentPath = subKey.Substring(0, lastSlash);
                string childName = subKey.Substring(lastSlash + 1);

                using var parentKey = rootKey.OpenSubKey(parentPath, writable: true);
                if (parentKey == null) return false;

                parentKey.DeleteSubKeyTree(childName, throwOnMissingSubKey: false);
                return true;
            }
            catch (Exception ex)
            {
                LoggerService.LogError(
                    $"Не удалось удалить запись из реестра: {program.DisplayName}",
                    nameof(RemoveFromRegistry), ex);
                return false;
            }
        }

        // Для обратной совместимости
        public static bool RunUninstaller(InstalledProgram program)
            => RunUninstallerWithMessage(program).success;

        /// <summary>
        /// Парсит строку команды: "C:\Path\app.exe" /arg1 /arg2 → exe, args
        /// </summary>
        private static void ParseCommand(string cmd, out string exe, out string args)
        {
            cmd = cmd.Trim();
            exe = "";
            args = "";

            // Вариант 1: команда начинается с кавычки
            if (cmd.StartsWith("\""))
            {
                int endQuote = cmd.IndexOf('"', 1);
                if (endQuote > 0)
                {
                    exe = cmd.Substring(1, endQuote - 1);
                    args = cmd.Substring(endQuote + 1).Trim();
                    return;
                }
            }

            // Вариант 2: команда содержит .exe — берём до .exe включительно
            int exeIdx = cmd.IndexOf(".exe", StringComparison.OrdinalIgnoreCase);
            if (exeIdx > 0)
            {
                exe = cmd.Substring(0, exeIdx + 4).Trim();
                args = cmd.Substring(exeIdx + 4).Trim();
                return;
            }

            // Вариант 3: просто команда без .exe (например, "msiexec")
            int spaceIdx = cmd.IndexOf(' ');
            if (spaceIdx > 0)
            {
                exe = cmd.Substring(0, spaceIdx);
                args = cmd.Substring(spaceIdx + 1).Trim();
            }
            else
            {
                exe = cmd;
                args = "";
            }
        }

        /// <summary>
        /// Ищет файл в системных папках Windows.
        /// </summary>
        private static string? FindInPath(string fileName)
        {
            try
            {
                string system32 = Environment.GetFolderPath(Environment.SpecialFolder.System);
                string candidate = Path.Combine(system32, fileName);
                if (File.Exists(candidate)) return candidate;

                string windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
                candidate = Path.Combine(windows, fileName);
                if (File.Exists(candidate)) return candidate;
            }
            catch { }
            return null;
        }
    }
}