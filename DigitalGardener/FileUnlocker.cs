using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace DigitalGardener
{
    /// <summary>
    /// Определяет процессы, блокирующие файл (через Restart Manager API Windows).
    /// </summary>
    public static class FileUnlocker
    {
        public static List<LockedFileItem> BuildLockedItem(string filePath)
        {
            var list = new List<LockedFileItem>();
            var procs = WhoIsLocking(filePath);
            var item = new LockedFileItem
            {
                FilePath = filePath,
                FileName = Path.GetFileName(filePath),
                LockingProcesses = procs.Count == 0 ? "—" : string.Join(", ", procs)
            };
            list.Add(item);
            return list;
        }

        public static List<string> WhoIsLocking(string path)
        {
            var result = new List<string>();
            uint handle;
            string key = Guid.NewGuid().ToString();
            int res = RmStartSession(out handle, 0, key);
            if (res != 0) return result;

            try
            {
                string[] resources = { path };
                res = RmRegisterResources(handle, (uint)resources.Length, resources, 0, null, 0, null);
                if (res != 0) return result;

                uint pnProcInfoNeeded = 0, pnProcInfo = 0, lpdwRebootReasons = 0;
                res = RmGetList(handle, out pnProcInfoNeeded, ref pnProcInfo, null, ref lpdwRebootReasons);
                if (res == 234 /*ERROR_MORE_DATA*/)
                {
                    var processInfo = new RM_PROCESS_INFO[pnProcInfoNeeded];
                    pnProcInfo = pnProcInfoNeeded;
                    res = RmGetList(handle, out pnProcInfoNeeded, ref pnProcInfo, processInfo, ref lpdwRebootReasons);
                    if (res == 0)
                    {
                        for (int i = 0; i < pnProcInfo; i++)
                        {
                            try
                            {
                                var p = Process.GetProcessById(processInfo[i].Process.dwProcessId);
                                result.Add($"{p.ProcessName} (PID {p.Id})");
                            }
                            catch { }
                        }
                    }
                }
            }
            finally
            {
                RmEndSession(handle);
            }
            return result;
        }

        #region Restart Manager P/Invoke

        [StructLayout(LayoutKind.Sequential)]
        private struct RM_UNIQUE_PROCESS
        {
            public int dwProcessId;
            public System.Runtime.InteropServices.ComTypes.FILETIME ProcessStartTime;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct RM_PROCESS_INFO
        {
            public RM_UNIQUE_PROCESS Process;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
            public string strAppName;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)]
            public string strServiceShortName;
            public int ApplicationType;
            public uint AppStatus;
            public uint TSSessionId;
            [MarshalAs(UnmanagedType.Bool)]
            public bool bRestartable;
        }

        [DllImport("rstrtmgr.dll", CharSet = CharSet.Unicode)]
        private static extern int RmStartSession(out uint pSessionHandle, int dwSessionFlags, string strSessionKey);

        [DllImport("rstrtmgr.dll")]
        private static extern int RmEndSession(uint pSessionHandle);

        [DllImport("rstrtmgr.dll", CharSet = CharSet.Unicode)]
        private static extern int RmRegisterResources(uint pSessionHandle, uint nFiles, string[] rgsFilenames,
            uint nApplications, RM_UNIQUE_PROCESS[]? rgApplications, uint nServices, string[]? rgsServiceNames);

        [DllImport("rstrtmgr.dll")]
        private static extern int RmGetList(uint dwSessionHandle, out uint pnProcInfoNeeded,
            ref uint pnProcInfo, [In, Out] RM_PROCESS_INFO[]? rgAffectedApps, ref uint lpdwRebootReasons);

        #endregion
    }
}