using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Principal;

namespace DigitalGardener
{
    public static class ProcessManager
    {
        public static List<ProcessItem> GetProcesses()
        {
            var list = new List<ProcessItem>();
            var currentId = Process.GetCurrentProcess().Id;

            foreach (var p in Process.GetProcesses())
            {
                try
                {
                    if (p.Id == 0) continue;
                    string path = "";
                    try { path = p.MainModule?.FileName ?? ""; } catch { }

                    list.Add(new ProcessItem
                    {
                        ProcessName = p.ProcessName,
                        Id = p.Id,
                        MemoryUsageBytes = p.WorkingSet64,
                        Description = string.IsNullOrEmpty(path) ? "Системный процесс" : path,
                        FullPath = path,
                        Recommendation = (p.Id == currentId) ? "Это наше приложение" :
                                          p.WorkingSet64 > 500L * 1024 * 1024 ? "Много памяти" : "ОК"
                    });
                }
                catch { }
                finally { p.Dispose(); }
            }
            return list;
        }

        public static bool Kill(int id)
        {
            EnableDebugPrivilege();

            try
            {
                using var p = Process.GetProcessById(id);
                p.Kill(entireProcessTree: true);
                p.WaitForExit(3000);
                return true;
            }
            catch (Win32Exception w32) when (w32.NativeErrorCode == 5)
            {
                LoggerService.LogError(
                    $"Нет прав для завершения процесса PID={id}. Запустите приложение от администратора.",
                    nameof(Kill), w32);
                return false;
            }
            catch (Exception ex)
            {
                LoggerService.LogError($"Не удалось завершить процесс PID={id}",
                    nameof(Kill), ex);
                return false;
            }
        }

        public static bool IsElevated()
        {
            try
            {
                using var identity = WindowsIdentity.GetCurrent();
                var principal = new WindowsPrincipal(identity);
                return principal.IsInRole(WindowsBuiltInRole.Administrator);
            }
            catch { return false; }
        }

        #region SeDebugPrivilege

        [DllImport("advapi32.dll", SetLastError = true)]
        private static extern bool OpenProcessToken(IntPtr processHandle, uint desiredAccess, out IntPtr tokenHandle);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr GetCurrentProcess();

        [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern bool LookupPrivilegeValue(string? host, string name, out long luid);

        [DllImport("advapi32.dll", SetLastError = true)]
        private static extern bool AdjustTokenPrivileges(IntPtr tokenHandle, bool disableAllPrivileges,
            ref TOKEN_PRIVILEGES newState, uint bufferLength, IntPtr previousState, IntPtr returnLength);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool CloseHandle(IntPtr hObject);

        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        private struct TOKEN_PRIVILEGES
        {
            public int PrivilegeCount;
            public long Luid;
            public int Attributes;
        }

        private const uint TOKEN_ADJUST_PRIVILEGES = 0x0020;
        private const uint TOKEN_QUERY = 0x0008;
        private const int SE_PRIVILEGE_ENABLED = 0x0002;
        private const string SE_DEBUG_NAME = "SeDebugPrivilege";

        private static void EnableDebugPrivilege()
        {
            try
            {
                if (!OpenProcessToken(GetCurrentProcess(),
                    TOKEN_ADJUST_PRIVILEGES | TOKEN_QUERY, out var token)) return;
                try
                {
                    if (!LookupPrivilegeValue(null, SE_DEBUG_NAME, out long luid)) return;

                    var tp = new TOKEN_PRIVILEGES
                    {
                        PrivilegeCount = 1,
                        Luid = luid,
                        Attributes = SE_PRIVILEGE_ENABLED
                    };
                    AdjustTokenPrivileges(token, false, ref tp, 0, IntPtr.Zero, IntPtr.Zero);
                }
                finally { CloseHandle(token); }
            }
            catch { }
        }

        #endregion
    }
}