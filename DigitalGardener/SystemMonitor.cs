using System;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace DigitalGardener
{
    public static class SystemMonitor
    {
        private static PerformanceCounter? _cpuCounter;
        private static bool _cpuInitTried;

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
        private class MEMORYSTATUSEX
        {
            public uint dwLength;
            public uint dwMemoryLoad;
            public ulong ullTotalPhys;
            public ulong ullAvailPhys;
            public ulong ullTotalPageFile;
            public ulong ullAvailPageFile;
            public ulong ullTotalVirtual;
            public ulong ullAvailVirtual;
            public ulong ullAvailExtendedVirtual;

            public MEMORYSTATUSEX()
            {
                dwLength = (uint)Marshal.SizeOf(typeof(MEMORYSTATUSEX));
            }
        }

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GlobalMemoryStatusEx([In, Out] MEMORYSTATUSEX lpBuffer);

        public static float GetCpuUsage()
        {
            try
            {
                if (!_cpuInitTried)
                {
                    _cpuInitTried = true;
                    _cpuCounter = new PerformanceCounter("Processor", "% Processor Time", "_Total");
                    _cpuCounter.NextValue();
                }
                if (_cpuCounter == null) return 0;
                return _cpuCounter.NextValue();
            }
            catch { return 0; }
        }

        public static float GetRamUsage()
        {
            try
            {
                var status = new MEMORYSTATUSEX();
                if (GlobalMemoryStatusEx(status)) return status.dwMemoryLoad;
                return 0;
            }
            catch { return 0; }
        }

        public static double GetTotalRamGb()
        {
            try
            {
                var status = new MEMORYSTATUSEX();
                if (GlobalMemoryStatusEx(status)) return status.ullTotalPhys / 1024.0 / 1024.0 / 1024.0;
                return 0;
            }
            catch { return 0; }
        }

        public static double GetAvailableRamGb()
        {
            try
            {
                var status = new MEMORYSTATUSEX();
                if (GlobalMemoryStatusEx(status)) return status.ullAvailPhys / 1024.0 / 1024.0 / 1024.0;
                return 0;
            }
            catch { return 0; }
        }

        /// <summary>Освобождает PerformanceCounter. Вызывать при закрытии приложения.</summary>
        public static void Dispose()
        {
            try
            {
                _cpuCounter?.Dispose();
                _cpuCounter = null;
                _cpuInitTried = false;
            }
            catch { }
        }
    }
}