using System;
using System.Collections.Generic;
using System.IO;

namespace DigitalGardener
{
    public static class DiskSpaceHelper
    {
        public class DriveInfoItem
        {
            public string DriveName { get; set; } = "";
            public long TotalBytes { get; set; }
            public long FreeBytes { get; set; }
            public long UsedBytes { get; set; }
            public double FreePercent => TotalBytes > 0
                ? (FreeBytes * 100.0 / TotalBytes)
                : 0;
            public bool IsCritical => FreePercent < 10;
            public bool IsLow => FreePercent < 20 && !IsCritical;

            public string ShortText =>
                $"{DriveName} {CollectionSummary.FormatSize(FreeBytes)} свободно из {CollectionSummary.FormatSize(TotalBytes)}";
        }

        /// <summary>Возвращает все подключённые диски (fixed drives).</summary>
        public static List<DriveInfoItem> GetAllDrives()
        {
            var result = new List<DriveInfoItem>();

            try
            {
                foreach (var drive in DriveInfo.GetDrives())
                {
                    try
                    {
                        if (drive.DriveType != DriveType.Fixed) continue;
                        if (!drive.IsReady) continue;

                        long total = drive.TotalSize;
                        long free = drive.TotalFreeSpace;
                        long used = total - free;

                        result.Add(new DriveInfoItem
                        {
                            DriveName = drive.Name.TrimEnd('\\'),
                            TotalBytes = total,
                            FreeBytes = free,
                            UsedBytes = used
                        });
                    }
                    catch { }
                }
            }
            catch { }

            return result;
        }

        /// <summary>Возвращает информацию о диске, на котором лежит файл/папка.</summary>
        public static DriveInfoItem? GetDriveForPath(string path)
        {
            try
            {
                if (string.IsNullOrEmpty(path)) return null;

                string root = Path.GetPathRoot(path) ?? "";
                if (string.IsNullOrEmpty(root)) return null;

                var drive = new DriveInfo(root);
                if (!drive.IsReady) return null;

                long total = drive.TotalSize;
                long free = drive.TotalFreeSpace;
                long used = total - free;

                return new DriveInfoItem
                {
                    DriveName = drive.Name.TrimEnd('\\'),
                    TotalBytes = total,
                    FreeBytes = free,
                    UsedBytes = used
                };
            }
            catch { return null; }
        }

        /// <summary>Возвращает строку для футера: "C: 45.3 ГБ свободно из 100 ГБ".</summary>
        public static string GetSummaryText(string driveLetter)
        {
            try
            {
                var drive = new DriveInfo(driveLetter);
                if (!drive.IsReady) return "";

                long total = drive.TotalSize;
                long free = drive.TotalFreeSpace;

                return $"{drive.Name.TrimEnd('\\')} — свободно " +
                       $"{CollectionSummary.FormatSize(free)} из " +
                       $"{CollectionSummary.FormatSize(total)}";
            }
            catch { return ""; }
        }
    }
}