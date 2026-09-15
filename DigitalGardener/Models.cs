using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace DigitalGardener
{
    public abstract class ObservableObject : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

        protected bool Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
        {
            if (Equals(field, value)) return false;
            field = value;
            OnPropertyChanged(name);
            return true;
        }
    }

    public class SystemFileItem : ObservableObject
    {
        private bool _isSelected;
        public string FullPath { get; set; } = "";
        public string Name { get; set; } = "";
        public long SizeBytes { get; set; }
        public string SizeText => $"{SizeBytes:N0} байт ({SizeBytes / 1024.0 / 1024.0:F2} МБ)";
        public DateTime LastAccessTime { get; set; }
        public string Reason { get; set; } = "";
        public string Recommendation { get; set; } = "";
        public string Source { get; set; } = "";
        public System.Windows.Media.ImageSource? Icon { get; set; }   // 🆕
        public bool IsSelected { get => _isSelected; set => Set(ref _isSelected, value); }
    }

    public class DuplicateItem : ObservableObject
    {
        private bool _isSelected;
        public string FullPath { get; set; } = "";
        public string Name { get; set; } = "";
        public long SizeBytes { get; set; }
        public string SizeText => $"{SizeBytes:N0} байт";
        public int GroupId { get; set; }
        public string OriginalPath { get; set; } = "";
        public System.Windows.Media.ImageSource? Icon { get; set; }   // 🆕
        public bool IsSelected { get => _isSelected; set => Set(ref _isSelected, value); }
    }

    public class StartupItem : ObservableObject
    {
        private bool _isSelected;
        public string Name { get; set; } = "";
        public string Path { get; set; } = "";
        public string Description { get; set; } = "";
        public string Recommendation { get; set; } = "Проверьте необходимость";
        public string Location { get; set; } = "";
        public bool IsEnabled { get; set; } = true;
        public string StateText => IsEnabled ? "Включено" : "Отключено";
        public bool IsSelected { get => _isSelected; set => Set(ref _isSelected, value); }
    }

    public class ProcessItem : ObservableObject
    {
        private bool _isSelected;
        public string ProcessName { get; set; } = "";
        public int Id { get; set; }
        public long MemoryUsageBytes { get; set; }
        public string MemoryText => $"{MemoryUsageBytes / 1024.0 / 1024.0:F1} МБ";
        public double CpuPercent { get; set; }
        public string Description { get; set; } = "";
        public string Recommendation { get; set; } = "Нормальная нагрузка";
        public string FullPath { get; set; } = "";
        public bool IsSelected { get => _isSelected; set => Set(ref _isSelected, value); }
    }

    public class ReportItem
    {
        public string Timestamp { get; set; } = "";
        public string Category { get; set; } = "";
        public string Message { get; set; } = "";

        public ReportItem() { }
        public ReportItem(string category, string message)
        {
            Timestamp = DateTime.Now.ToString("HH:mm:ss");
            Category = category;
            Message = message;
        }
    }

    public class LockedFileItem : ObservableObject
    {
        private bool _isSelected;
        public string FilePath { get; set; } = "";
        public string FileName { get; set; } = "";
        public string LockingProcesses { get; set; } = "";
        public bool IsSelected { get => _isSelected; set => Set(ref _isSelected, value); }
    }

    public class SystemInfoItem
    {
        public string Category { get; set; } = "";
        public string Name { get; set; } = "";
        public string Value { get; set; } = "";
    }

    public class ProgressSlot : ObservableObject
    {
        private string _title = "";
        private double _percent;
        public string Title { get => _title; set => Set(ref _title, value); }
        public double Percent { get => _percent; set => Set(ref _percent, value); }
    }

    public class CollectionSummary : ObservableObject
    {
        private int _count;
        private long _totalBytes;
        public int Count { get => _count; set { if (Set(ref _count, value)) UpdateText(); } }
        public long TotalBytes { get => _totalBytes; set { if (Set(ref _totalBytes, value)) UpdateText(); } }
        public string Text { get; private set; } = "";

        private void UpdateText()
        {
            Text = $"Всего: {Count} | {FormatSize(TotalBytes)}";
            OnPropertyChanged(nameof(Text));
        }

        public static string FormatSize(long bytes)
        {
            if (bytes < 1024) return $"{bytes} байт";
            if (bytes < 1024 * 1024) return $"{bytes / 1024.0:F2} КБ";
            if (bytes < 1024L * 1024 * 1024) return $"{bytes / 1024.0 / 1024.0:F2} МБ";
            return $"{bytes / 1024.0 / 1024.0 / 1024.0:F2} ГБ";
        }
    }

    public class ProgramItem : ObservableObject
    {
        private bool _isSelected;
        public string DisplayName { get; set; } = "";
        public string DisplayVersion { get; set; } = "";
        public string Publisher { get; set; } = "";
        public string InstallDate { get; set; } = "";
        public string SizeText { get; set; } = "—";
        public long EstimatedSize { get; set; }
        public string UninstallString { get; set; } = "";
        public string QuietUninstallString { get; set; } = "";
        public string InstallLocation { get; set; } = "";
        public string RegistryKeyPath { get; set; } = "";
        public bool IsSelected { get => _isSelected; set => Set(ref _isSelected, value); }
    }

    // ==================== 🆕 НАСТРОЙКИ ====================
    public class AppSettings
    {
        // Старые
        public int UnusedDays { get; set; } = 180;
        public bool AutoRefreshProcesses { get; set; } = false;
        public int AutoRefreshIntervalSec { get; set; } = 5;
        public List<string> ScanRoots { get; set; } = new();
        public bool IsAdminWarningShown { get; set; } = false;
        public bool ShowSystemMonitor { get; set; } = true;

        // 🆕 Скрытые файлы
        public bool ShowHiddenFiles { get; set; } = false;

        // 🆕 Трей
        public bool MinimizeToTray { get; set; } = false;   // сворачивать в трей
        public bool CloseToTray { get; set; } = false;      // закрывать в трей
        public bool TrayHintShown { get; set; } = false;    // подсказка про трей показана
    }
}