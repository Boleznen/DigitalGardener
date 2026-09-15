using System;
using System.Collections.Generic;
using System.Management; // добавьте ссылку System.Management

namespace DigitalGardener
{
    public static class SystemInfoProvider
    {
        public static List<SystemInfoItem> GetInfo()
        {
            var list = new List<SystemInfoItem>();

            void Add(string cat, string name, string val) =>
                list.Add(new SystemInfoItem { Category = cat, Name = name, Value = val });

            try
            {
                Add("ОС", "Имя компьютера", Environment.MachineName);
                Add("ОС", "Пользователь", Environment.UserName);
                Add("ОС", "Версия ОС", Environment.OSVersion.ToString());
                Add("ОС", "64-bit", Environment.Is64BitOperatingSystem ? "Да" : "Нет");
                Add("ОС", "Процессоров (логических)", Environment.ProcessorCount.ToString());
                Add("ОС", "Папка Windows", Environment.GetFolderPath(Environment.SpecialFolder.Windows));
            }
            catch { }

            try
            {
                using var searcher = new ManagementObjectSearcher(
                    "SELECT Name, TotalPhysicalMemory FROM Win32_ComputerSystem");
                foreach (var obj in searcher.Get())
                {
                    Add("ПК", "Модель", obj["Name"]?.ToString() ?? "");
                    if (obj["TotalPhysicalMemory"] != null)
                    {
                        long ram = Convert.ToInt64(obj["TotalPhysicalMemory"]);
                        Add("RAM", "Всего", $"{ram / 1024.0 / 1024.0 / 1024.0:F2} ГБ");
                    }
                }
            }
            catch { }

            try
            {
                using var searcher = new ManagementObjectSearcher("SELECT Name FROM Win32_Processor");
                foreach (var obj in searcher.Get())
                    Add("CPU", "Процессор", obj["Name"]?.ToString() ?? "");
            }
            catch { }

            try
            {
                using var searcher = new ManagementObjectSearcher(
                    "SELECT Name, AdapterRAM FROM Win32_VideoController");
                foreach (var obj in searcher.Get())
                {
                    Add("GPU", "Видеокарта", obj["Name"]?.ToString() ?? "");
                    if (obj["AdapterRAM"] != null)
                    {
                        long ram = Convert.ToInt64(obj["AdapterRAM"]);
                        Add("GPU", "Память GPU", $"{ram / 1024.0 / 1024.0:F0} МБ");
                    }
                }
            }
            catch { }

            return list;
        }
    }
}