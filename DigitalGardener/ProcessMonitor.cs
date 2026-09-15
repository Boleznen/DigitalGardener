using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

namespace DigitalGardener
{
    public static class ProcessMonitor
    {
        public static List<ProcessItem> GetRunningProcesses()
        {
            var processes = new List<ProcessItem>();

            var allProcesses = Process.GetProcesses();

            foreach (var p in allProcesses)
            {
                try
                {
                    // ИСПРАВЛЕНИЕ: Записываем в MemoryUsageBytes.
                    // WorkingSetMB посчитается сам через формулу в модели.
                    var item = new ProcessItem
                    {
                        ProcessName = p.ProcessName,
                        Id = p.Id,
                        MemoryUsageBytes = p.WorkingSet64, // <-- Сюда пишем байты
                        CpuPercent = 0.0, // Можно добавить логику расчета CPU позже
                        Description = p.MainModule?.FileVersionInfo?.FileDescription ?? "Нет описания",
                        Recommendation = "Нормальная нагрузка"
                    };

                    processes.Add(item);
                }
                catch
                {
                    // Пропускаем процессы, к которым нет доступа (например, системные)
                    continue;
                }
            }

            return processes;
        }
    }
}
