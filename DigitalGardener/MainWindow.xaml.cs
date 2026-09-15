using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using LiveChartsCore;
using LiveChartsCore.SkiaSharpView;
using LiveChartsCore.SkiaSharpView.Painting;
using LiveChartsCore.Defaults;
using Microsoft.VisualBasic.FileIO;
using SkiaSharp;
using SearchOption = System.IO.SearchOption;
using MessageBox = System.Windows.MessageBox;
using ComboBox = System.Windows.Controls.ComboBox;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;

namespace DigitalGardener
{
    public partial class MainWindow : Window
    {
        // ==================== КОЛЛЕКЦИИ ====================
        public ObservableCollection<SystemFileItem> TempFiles { get; set; } = new();
        public ObservableCollection<SystemFileItem> BrowserCacheFiles { get; set; } = new();
        public ObservableCollection<SystemFileItem> GameCacheItems { get; set; } = new();
        public ObservableCollection<SystemFileItem> UnusedFiles { get; set; } = new();
        public ObservableCollection<DuplicateItem> DuplicateCandidates { get; set; } = new();
        public ObservableCollection<StartupItem> StartupItems { get; set; } = new();
        public ObservableCollection<ProcessItem> RunningProcesses { get; set; } = new();
        public ObservableCollection<ReportItem> ReportItems { get; set; } = new();
        public ObservableCollection<LockedFileItem> LockedFiles { get; set; } = new();
        public ObservableCollection<SystemInfoItem> SystemInfoItems { get; set; } = new();
        public ObservableCollection<ProgressSlot> ProgressSlots { get; set; } = new();
        public ObservableCollection<ProgramItem> InstalledPrograms { get; set; } = new();

        // ==================== СВОДКИ ====================
        public CollectionSummary TempSummary { get; set; } = new();
        public CollectionSummary BrowserSummary { get; set; } = new();
        public CollectionSummary GameCacheSummary { get; set; } = new();
        public CollectionSummary UnusedSummary { get; set; } = new();
        public CollectionSummary DuplicatesSummary { get; set; } = new();
        public CollectionSummary StartupSummary { get; set; } = new();
        public CollectionSummary ProcessesSummary { get; set; } = new();
        public CollectionSummary LockedSummary { get; set; } = new();

        // ==================== ГРАФИКИ ====================
        public ISeries[] CpuSeries { get; set; } = Array.Empty<ISeries>();
        public ISeries[] RamSeries { get; set; } = Array.Empty<ISeries>();
        public Axis[] CpuXAxes { get; set; } = Array.Empty<Axis>();
        public Axis[] CpuYAxes { get; set; } = Array.Empty<Axis>();
        public Axis[] RamXAxes { get; set; } = Array.Empty<Axis>();
        public Axis[] RamYAxes { get; set; } = Array.Empty<Axis>();

        private readonly ObservableCollection<ObservableValue> _cpuValues = new();
        private readonly ObservableCollection<ObservableValue> _ramValues = new();
        private const int MaxChartPoints = 60;

        // ==================== СЕРВИСЫ ====================
        private readonly StartupManager _startupManager = new();
        private readonly TrayManager _trayManager = new();
        private DispatcherTimer? _processTimer;
        private DispatcherTimer? _monitorTimer;
        private DispatcherTimer? _diskSpaceTimer;
        private bool _initializing = true;
        private bool _realClose = false;

        // ==================== КОНСТРУКТОР ====================
        public MainWindow()
        {
            InitializeComponent();
            DataContext = this;

            // Хуки сводок
            HookSummary(TempFiles, TempSummary);
            HookSummary(BrowserCacheFiles, BrowserSummary);
            HookSummary(GameCacheItems, GameCacheSummary);
            HookSummary(UnusedFiles, UnusedSummary);
            HookSummary(DuplicateCandidates, DuplicatesSummary);
            HookSummary(StartupItems, StartupSummary);
            HookSummary(RunningProcesses, ProcessesSummary);
            HookSummary(LockedFiles, LockedSummary);

            InitCharts();

            // Трей
            _trayManager.Initialize(this);
            _trayManager.OnShowRequested += () => Dispatcher.Invoke(() =>
            {
                _trayManager.RestoreMainWindow();
                _trayManager.Hide();
            });
            _trayManager.OnSettingsRequested += () => Dispatcher.Invoke(OpenSettings_Click);
            _trayManager.OnExitRequested += () => Dispatcher.Invoke(() =>
            {
                _realClose = true;
                Close();
            });

            // Проверка админа
            if (!ProcessManager.IsElevated())
            {
                AdminWarningPanel.Visibility = Visibility.Visible;
                AddReport("Внимание", "Приложение запущено без прав администратора. " +
                    "Некоторые функции могут быть недоступны.");
            }

            // Загружаем настройки
            ApplySettingsToUi();

            LoadStartupItems();
            LoadSystemInfo();

            // Обновление свободного места на диске
            UpdateDiskSpaceText();
            _diskSpaceTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(15) };
            _diskSpaceTimer.Tick += (_, __) => UpdateDiskSpaceText();
            _diskSpaceTimer.Start();

            // Прогрев CPU counter
            SystemMonitor.GetCpuUsage();

            // Мониторинг CPU/RAM
            _monitorTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            _monitorTimer.Tick += MonitorTimer_Tick;
            _monitorTimer.Start();

            // Горячие клавиши
            PreviewKeyDown += MainWindow_PreviewKeyDown;

            _initializing = false;
        }

        // ==================== ХУКИ СВОДОК ====================
        private void HookSummary<T>(ObservableCollection<T> collection,
            CollectionSummary summary) where T : class
        {
            void Recalc()
            {
                summary.Count = collection.Count;
                long total = 0;
                foreach (var obj in collection)
                {
                    var prop = obj.GetType().GetProperty("SizeBytes");
                    if (prop != null && prop.GetValue(obj) is long size) total += size;
                }
                summary.TotalBytes = total;
            }

            collection.CollectionChanged += (_, __) => Recalc();
            Recalc();
        }

        // ==================== ГРАФИКИ ====================
        private void InitCharts()
        {
            var cpuLine = new LineSeries<ObservableValue>
            {
                Values = _cpuValues,
                Fill = null,
                GeometrySize = 0,
                LineSmoothness = 0.5,
                Stroke = new SolidColorPaint(SKColor.Parse("#007ACC"), 2)
            };

            var ramLine = new LineSeries<ObservableValue>
            {
                Values = _ramValues,
                Fill = null,
                GeometrySize = 0,
                LineSmoothness = 0.5,
                Stroke = new SolidColorPaint(SKColor.Parse("#22C55E"), 2)
            };

            CpuSeries = new ISeries[] { cpuLine };
            RamSeries = new ISeries[] { ramLine };

            CpuYAxes = new[] { new Axis { MinLimit = 0, MaxLimit = 100, IsVisible = true,
                LabelsPaint = new SolidColorPaint(SKColor.Parse("#B4B4BE")) } };
            RamYAxes = new[] { new Axis { MinLimit = 0, MaxLimit = 100, IsVisible = true,
                LabelsPaint = new SolidColorPaint(SKColor.Parse("#B4B4BE")) } };

            CpuXAxes = new[] { new Axis { IsVisible = false } };
            RamXAxes = new[] { new Axis { IsVisible = false } };
        }

        private void MonitorTimer_Tick(object? sender, EventArgs e)
        {
            if (ShowMonitorCheck?.IsChecked != true) return;
            try
            {
                float cpu = SystemMonitor.GetCpuUsage();
                float ram = SystemMonitor.GetRamUsage();

                _cpuValues.Add(new ObservableValue(cpu));
                _ramValues.Add(new ObservableValue(ram));

                while (_cpuValues.Count > MaxChartPoints) _cpuValues.RemoveAt(0);
                while (_ramValues.Count > MaxChartPoints) _ramValues.RemoveAt(0);

                if (CpuRamText != null)
                {
                    double totalRam = SystemMonitor.GetTotalRamGb();
                    double freeRam = SystemMonitor.GetAvailableRamGb();
                    CpuRamText.Text = $"CPU: {cpu:F1}%   RAM: {ram:F1}%  ({freeRam:F1} / {totalRam:F1} ГБ свободно)";
                }
            }
            catch { }
        }

        private void ShowMonitor_Changed(object sender, RoutedEventArgs e)
        {
            if (_initializing) return;
            bool show = ShowMonitorCheck.IsChecked == true;
            SettingsService.Current.ShowSystemMonitor = show;
            SettingsService.SaveCurrent();

            if (show) { _monitorTimer?.Start(); AddReport("Мониторинг", "Графики включены."); }
            else { _monitorTimer?.Stop(); AddReport("Мониторинг", "Графики отключены (экономим ресурсы)."); }
        }

        // ==================== СВОБОДНОЕ МЕСТО ====================
        private void UpdateDiskSpaceText()
        {
            try
            {
                var text = DiskSpaceHelper.GetSummaryText("C:");
                if (DiskSpaceText != null) DiskSpaceText.Text = text;
            }
            catch { }
        }

        // ==================== НАСТРОЙКИ ====================
        private void ApplySettingsToUi()
        {
            var s = SettingsService.Current;

            int idx = s.UnusedDays switch { 30 => 0, 60 => 1, 90 => 2, _ => 3 };
            if (UnusedDaysCombo != null) UnusedDaysCombo.SelectedIndex = idx;

            if (AutoRefreshProcessesCheck != null)
                AutoRefreshProcessesCheck.IsChecked = s.AutoRefreshProcesses;

            int intervalIdx = s.AutoRefreshIntervalSec switch { 3 => 0, 5 => 1, 10 => 2, 30 => 3, _ => 1 };
            if (AutoRefreshIntervalCombo != null) AutoRefreshIntervalCombo.SelectedIndex = intervalIdx;

            if (ShowMonitorCheck != null) ShowMonitorCheck.IsChecked = s.ShowSystemMonitor;

            UpdateProcessTimer();
        }

        private void UnusedDaysCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_initializing) return;
            if (UnusedDaysCombo?.SelectedItem is ComboBoxItem item &&
                int.TryParse(item.Content?.ToString()?.Split(' ')[0], out int days))
            {
                SettingsService.Current.UnusedDays = days;
                SettingsService.SaveCurrent();
            }
        }

        private void AutoRefreshProcesses_Changed(object sender, RoutedEventArgs e)
        {
            if (_initializing) return;
            SettingsService.Current.AutoRefreshProcesses = AutoRefreshProcessesCheck.IsChecked == true;
            SettingsService.SaveCurrent();
            UpdateProcessTimer();
        }

        private void AutoRefreshInterval_Changed(object sender, SelectionChangedEventArgs e)
        {
            if (_initializing) return;
            if (AutoRefreshIntervalCombo?.SelectedItem is ComboBoxItem item &&
                int.TryParse(item.Content?.ToString()?.Split(' ')[0], out int sec))
            {
                SettingsService.Current.AutoRefreshIntervalSec = sec;
                SettingsService.SaveCurrent();
                UpdateProcessTimer();
            }
        }

        private void UpdateProcessTimer()
        {
            _processTimer?.Stop();
            _processTimer = null;

            if (SettingsService.Current.AutoRefreshProcesses)
            {
                _processTimer = new DispatcherTimer
                {
                    Interval = TimeSpan.FromSeconds(SettingsService.Current.AutoRefreshIntervalSec)
                };
                _processTimer.Tick += async (_, __) =>
                {
                    var list = await Task.Run(() => ProcessManager.GetProcesses());
                    var selectedIds = RunningProcesses.Where(x => x.IsSelected).Select(x => x.Id).ToHashSet();
                    RunningProcesses.Clear();
                    foreach (var p in list)
                    {
                        p.IsSelected = selectedIds.Contains(p.Id);
                        RunningProcesses.Add(p);
                    }
                };
                _processTimer.Start();
            }
        }

        // ==================== PROGRESS ====================
        private ProgressSlot CreateSlot(string title)
        {
            var slot = new ProgressSlot { Title = title, Percent = 0 };
            ProgressSlots.Add(slot);
            while (ProgressSlots.Count > 5)
                ProgressSlots.RemoveAt(0);
            return slot;
        }

        private async Task RunWithProgress(string title, Func<Task> action)
        {
            var slot = CreateSlot(title);
            slot.Percent = 10;
            try
            {
                await action();
                slot.Percent = 100;
            }
            catch (Exception ex)
            {
                slot.Percent = 100;
                LoggerService.LogError(title, nameof(RunWithProgress), ex);
            }
            await Task.Delay(1500);
            ProgressSlots.Remove(slot);
        }

        // ==================== TEMP ====================
        private async void ScanTempAndDownloads_Click(object sender, RoutedEventArgs e)
        {
            await RunWithProgress("Temp", async () =>
            {
                AddReport("Очистка", "Сканирование Temp...");

                var savedSel = TableViewHelper.SaveSelection(TempFiles, "FullPath");

                var list = await Task.Run(() => TempCleaner.Scan());

                // 🆕 Заполняем иконки
                foreach (var f in list)
                    f.Icon = FileIconHelper.GetIconForExtension(Path.GetExtension(f.FullPath));

                TempFiles.Clear();
                foreach (var f in list) TempFiles.Add(f);

                TableViewHelper.RestoreSelection(TempFiles, savedSel, "FullPath");
                UpdateExtCombo(TempExtCombo, TempFiles);

                AddReport("Очистка", $"Найдено {list.Count} файлов.");
            });
        }

        private void DeleteSelected_Click(object sender, RoutedEventArgs e) =>
            DeleteItems(TempFiles.Cast<object>().ToList(), toRecycle: true);

        private void DeleteToRecycleBin_Click(object sender, RoutedEventArgs e) =>
            DeleteItems(TempFiles.Cast<object>().ToList(), toRecycle: true);

        private void ArchiveFile_Click(object sender, RoutedEventArgs e) =>
            ArchiveItems(TempFiles.Cast<object>().ToList());

        private void SelectAllTemp_Click(object sender, RoutedEventArgs e)
        {
            bool all = TempFiles.All(x => x.IsSelected);
            foreach (var f in TempFiles) f.IsSelected = !all;
        }

        private void TempDataGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e) =>
            OpenSelectedInExplorer(TempDataGrid);

        private void TempSearch_TextChanged(object sender, TextChangedEventArgs e) =>
            TableViewHelper.ApplyFilter(CollectionViewSource.GetDefaultView(TempFiles),
                TempSearchBox.Text, TempExtCombo.SelectedItem?.ToString() ?? "");

        private void TempExt_SelectionChanged(object sender, SelectionChangedEventArgs e) =>
            TableViewHelper.ApplyFilter(CollectionViewSource.GetDefaultView(TempFiles),
                TempSearchBox.Text, TempExtCombo.SelectedItem?.ToString() ?? "");

        private void ExportTempCsv_Click(object sender, RoutedEventArgs e) =>
            ExportTableCsv(TempFiles, "temp");

        // ==================== BROWSER ====================
        private async void ScanBrowserCache_Click(object sender, RoutedEventArgs e)
        {
            await RunWithProgress("Кэш", async () =>
            {
                AddReport("Кэш браузеров", "Сканирование...");

                var savedSel = TableViewHelper.SaveSelection(BrowserCacheFiles, "FullPath");

                var list = await Task.Run(() => BrowserCacheCleaner.Scan());

                foreach (var f in list)
                    f.Icon = FileIconHelper.GetIconForExtension(Path.GetExtension(f.FullPath));

                BrowserCacheFiles.Clear();
                foreach (var f in list) BrowserCacheFiles.Add(f);

                TableViewHelper.RestoreSelection(BrowserCacheFiles, savedSel, "FullPath");
                UpdateExtCombo(BrowserExtCombo, BrowserCacheFiles);

                AddReport("Кэш браузеров", $"Найдено {list.Count} файлов.");
            });
        }

        private void DeleteSelectedBrowser_Click(object sender, RoutedEventArgs e) =>
            DeleteItems(BrowserCacheFiles.Cast<object>().ToList(), toRecycle: true);

        private void RecycleSelectedBrowser_Click(object sender, RoutedEventArgs e) =>
            DeleteItems(BrowserCacheFiles.Cast<object>().ToList(), toRecycle: true);

        private void ArchiveSelectedBrowser_Click(object sender, RoutedEventArgs e) =>
            ArchiveItems(BrowserCacheFiles.Cast<object>().ToList());

        private void SelectAllBrowser_Click(object sender, RoutedEventArgs e)
        {
            bool all = BrowserCacheFiles.All(x => x.IsSelected);
            foreach (var f in BrowserCacheFiles) f.IsSelected = !all;
        }

        private void BrowserDataGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e) =>
            OpenSelectedInExplorer(BrowserDataGrid);

        private void BrowserSearch_TextChanged(object sender, TextChangedEventArgs e) =>
            TableViewHelper.ApplyFilter(CollectionViewSource.GetDefaultView(BrowserCacheFiles),
                BrowserSearchBox.Text, BrowserExtCombo.SelectedItem?.ToString() ?? "");

        private void BrowserExt_SelectionChanged(object sender, SelectionChangedEventArgs e) =>
            TableViewHelper.ApplyFilter(CollectionViewSource.GetDefaultView(BrowserCacheFiles),
                BrowserSearchBox.Text, BrowserExtCombo.SelectedItem?.ToString() ?? "");

        private void ExportBrowserCsv_Click(object sender, RoutedEventArgs e) =>
            ExportTableCsv(BrowserCacheFiles, "browser");

        // ==================== GAME CACHE ====================
        private async void ScanGameCache_Click(object sender, RoutedEventArgs e)
        {
            await RunWithProgress("Игровой кэш", async () =>
            {
                AddReport("Игровой кэш", "Сканирование...");
                var list = await Task.Run(() => GameCacheCleaner.Scan());
                GameCacheItems.Clear();
                foreach (var f in list) GameCacheItems.Add(f);
                AddReport("Игровой кэш", $"Найдено: {list.Count}, " +
                    $"{CollectionSummary.FormatSize(list.Sum(x => x.SizeBytes))}");
            });
        }

        private async void CleanSelectedGameCache_Click(object sender, RoutedEventArgs e)
        {
            var selected = GameCacheItems.Where(x => x.IsSelected).ToList();
            if (selected.Count == 0) { AddReport("Игровой кэш", "Ничего не выбрано."); return; }

            await RunWithProgress("Очистка кэша", async () =>
            {
                int totalFiles = 0; long totalFreed = 0;
                foreach (var item in selected)
                {
                    var (ok, failed, freed) = await Task.Run(() => GameCacheCleaner.CleanCache(item));
                    totalFiles += ok; totalFreed += freed;
                    AddReport("Игровой кэш", $"{item.Name}: {ok} файлов");
                }
                AddReport("Игровой кэш", $"Готово. Удалено {totalFiles}, " +
                    $"{CollectionSummary.FormatSize(totalFreed)}");

                var refreshed = await Task.Run(() => GameCacheCleaner.Scan());
                GameCacheItems.Clear();
                foreach (var f in refreshed) GameCacheItems.Add(f);
            });
        }

        private async void CleanAllGameCache_Click(object sender, RoutedEventArgs e)
        {
            if (GameCacheItems.Count == 0) { AddReport("Игровой кэш", "Сначала сканируйте."); return; }

            await RunWithProgress("Очистка всего", async () =>
            {
                int totalFiles = 0; long totalFreed = 0;
                foreach (var item in GameCacheItems.ToList())
                {
                    var (ok, failed, freed) = await Task.Run(() => GameCacheCleaner.CleanCache(item));
                    totalFiles += ok; totalFreed += freed;
                }
                AddReport("Игровой кэш", $"Удалено {totalFiles}, " +
                    $"{CollectionSummary.FormatSize(totalFreed)}");
                GameCacheItems.Clear();
            });
        }

        private void SelectAllGameCache_Click(object sender, RoutedEventArgs e)
        {
            bool all = GameCacheItems.All(x => x.IsSelected);
            foreach (var f in GameCacheItems) f.IsSelected = !all;
        }

        private void GameCacheDataGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e) =>
            OpenSelectedInExplorer(GameCacheDataGrid);

        private void GameSearch_TextChanged(object sender, TextChangedEventArgs e) =>
            TableViewHelper.ApplyFilter(CollectionViewSource.GetDefaultView(GameCacheItems),
                GameSearchBox.Text, "");

        private void ExportGameCsv_Click(object sender, RoutedEventArgs e) =>
            ExportTableCsv(GameCacheItems, "game_cache");        // ==================== UNUSED ====================
        private async void ScanUnusedFiles_Click(object sender, RoutedEventArgs e)
        {
            int days = SettingsService.Current.UnusedDays;

            await RunWithProgress($"Старые ({days}д)", async () =>
            {
                AddReport("Неиспользуемые", $"Сканирование (>{days} дней)...");

                var savedSel = TableViewHelper.SaveSelection(UnusedFiles, "FullPath");

                var roots = SettingsService.Current.ScanRoots;
                var list = await Task.Run(() => UnusedFileFinder.Scan(roots, days));

                foreach (var f in list)
                {
                    f.Icon = string.IsNullOrEmpty(f.FullPath) || Directory.Exists(f.FullPath)
                        ? FileIconHelper.GetFolderIcon()
                        : FileIconHelper.GetIconForExtension(Path.GetExtension(f.FullPath));
                }

                UnusedFiles.Clear();
                foreach (var f in list) UnusedFiles.Add(f);

                TableViewHelper.RestoreSelection(UnusedFiles, savedSel, "FullPath");
                UpdateExtCombo(UnusedExtCombo, UnusedFiles);

                AddReport("Неиспользуемые", $"Найдено {list.Count} (>{days} дней).");
            });
        }

        private void DeleteSelectedUnused_Click(object sender, RoutedEventArgs e) =>
            DeleteItems(UnusedFiles.Cast<object>().ToList(), toRecycle: true);

        private void RecycleSelectedUnused_Click(object sender, RoutedEventArgs e) =>
            DeleteItems(UnusedFiles.Cast<object>().ToList(), toRecycle: true);

        private void ArchiveSelectedUnused_Click(object sender, RoutedEventArgs e) =>
            ArchiveItems(UnusedFiles.Cast<object>().ToList());

        private void UnusedDataGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e) =>
            OpenSelectedInExplorer(UnusedDataGrid);

        private void UnusedSearch_TextChanged(object sender, TextChangedEventArgs e) =>
            TableViewHelper.ApplyFilter(CollectionViewSource.GetDefaultView(UnusedFiles),
                UnusedSearchBox.Text, UnusedExtCombo.SelectedItem?.ToString() ?? "");

        private void UnusedExt_SelectionChanged(object sender, SelectionChangedEventArgs e) =>
            TableViewHelper.ApplyFilter(CollectionViewSource.GetDefaultView(UnusedFiles),
                UnusedSearchBox.Text, UnusedExtCombo.SelectedItem?.ToString() ?? "");

        private void ExportUnusedCsv_Click(object sender, RoutedEventArgs e) =>
            ExportTableCsv(UnusedFiles, "unused");

        // ==================== DUPLICATES ====================
        private async void ScanDuplicates_Click(object sender, RoutedEventArgs e)
        {
            await RunWithProgress("Дубликаты", async () =>
            {
                AddReport("Дубликаты", "Сканирование...");
                var roots = new List<string>
                {
                    Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                    Environment.GetFolderPath(Environment.SpecialFolder.MyPictures),
                    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads")
                };
                var dups = await DuplicateFinder.FindDuplicatesAsync(roots);

                foreach (var d in dups)
                    d.Icon = FileIconHelper.GetIconForExtension(Path.GetExtension(d.FullPath));

                DuplicateCandidates.Clear();
                foreach (var d in dups) DuplicateCandidates.Add(d);
                AddReport("Дубликаты", $"Найдено {dups.Count} дубликатов.");
            });
        }

        private void DeleteDuplicateToRecycleBin_Click(object sender, RoutedEventArgs e)
        {
            var selected = DuplicateCandidates.Where(x => x.IsSelected).ToList();
            if (selected.Count == 0) { AddReport("Дубликаты", "Ничего не выбрано."); return; }
            int ok = 0;
            foreach (var d in selected)
            {
                try
                {
                    FileSystem.DeleteFile(d.FullPath, UIOption.OnlyErrorDialogs, RecycleOption.SendToRecycleBin);
                    DuplicateCandidates.Remove(d);
                    ok++;
                }
                catch (Exception ex)
                {
                    LoggerService.LogError("Ошибка удаления дубликата", nameof(DeleteDuplicateToRecycleBin_Click), ex);
                }
            }
            AddReport("Дубликаты", $"В корзину: {ok}/{selected.Count}");
        }

        private void ArchiveDuplicate_Click(object sender, RoutedEventArgs e)
        {
            var selected = DuplicateCandidates.Where(x => x.IsSelected).ToList();
            if (selected.Count == 0) { AddReport("Дубликаты", "Ничего не выбрано."); return; }
            int ok = 0;
            string archiveDir = GetArchiveDir();
            foreach (var d in selected)
            {
                try
                {
                    string dest = MakeUniquePath(archiveDir, d.Name);
                    File.Move(d.FullPath, dest);
                    DuplicateCandidates.Remove(d);
                    ok++;
                }
                catch (Exception ex)
                {
                    LoggerService.LogError("Ошибка архивации", nameof(ArchiveDuplicate_Click), ex);
                }
            }
            AddReport("Дубликаты", $"В архив: {ok}/{selected.Count}");
        }

        private void DuplicatesDataGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e) =>
            OpenSelectedInExplorer(DuplicatesDataGrid);

        private void DupSearch_TextChanged(object sender, TextChangedEventArgs e) =>
            TableViewHelper.ApplyFilter(CollectionViewSource.GetDefaultView(DuplicateCandidates),
                DupSearchBox.Text, "");

        private void ExportDupCsv_Click(object sender, RoutedEventArgs e) =>
            ExportTableCsv(DuplicateCandidates, "duplicates");

        // ==================== STARTUP ====================
        private void LoadStartupItems_Click(object sender, RoutedEventArgs e) => LoadStartupItems();

        private void LoadStartupItems()
        {
            AddReport("Автозагрузка", "Загрузка списка...");
            StartupItems.Clear();
            foreach (var item in _startupManager.GetStartupItems())
                StartupItems.Add(item);
        }

        private void DisableStartupItem_Click(object sender, RoutedEventArgs e)
        {
            var selected = StartupItems.Where(x => x.IsSelected).ToList();
            if (selected.Count == 0) { AddReport("Автозагрузка", "Ничего не выбрано."); return; }

            int ok = 0;
            foreach (var item in selected)
            {
                var (success, message) = _startupManager.DisableWithMessage(item);
                AddReport("Автозагрузка", message);
                if (success) { item.IsEnabled = false; item.Recommendation = "Отключено"; ok++; }
            }
            AddReport("Автозагрузка", $"Итого: {ok}/{selected.Count}");
        }

        private void OptimizeStartup_Click(object sender, RoutedEventArgs e)
        {
            var bad = StartupItems.Where(x => x.IsEnabled &&
                (x.Recommendation.Contains("Проверить") ||
                 x.Recommendation.Contains("необходимость") ||
                 x.Path.ToLower().Contains("temp"))).ToList();

            if (bad.Count == 0) { AddReport("Автозагрузка", "Нечего оптимизировать."); return; }

            int ok = 0;
            foreach (var item in bad)
            {
                var (success, message) = _startupManager.DisableWithMessage(item);
                AddReport("Автозагрузка", message);
                if (success) { item.IsEnabled = false; item.Recommendation = "Отключено"; ok++; }
            }
            AddReport("Автозагрузка", $"Оптимизировано: {ok}/{bad.Count}");
        }

        private void StartupSearch_TextChanged(object sender, TextChangedEventArgs e) =>
            TableViewHelper.ApplyFilter(CollectionViewSource.GetDefaultView(StartupItems),
                StartupSearchBox.Text, "");

        private void ExportStartupCsv_Click(object sender, RoutedEventArgs e) =>
            ExportTableCsv(StartupItems, "startup");

        // ==================== PROCESSES ====================
        private async void MonitorProcesses_Click(object sender, RoutedEventArgs e)
        {
            await RunWithProgress("Процессы", async () =>
            {
                AddReport("Процессы", "Обновление...");
                var list = await Task.Run(() => ProcessManager.GetProcesses());
                RunningProcesses.Clear();
                foreach (var p in list) RunningProcesses.Add(p);
                AddReport("Процессы", $"Загружено: {list.Count}.");
            });
        }

        private void KillProcess_Click(object sender, RoutedEventArgs e)
        {
            var selected = RunningProcesses.Where(x => x.IsSelected).ToList();
            if (selected.Count == 0) { AddReport("Процессы", "Ничего не выбрано."); return; }
            int ok = 0;
            foreach (var p in selected)
            {
                if (ProcessManager.Kill(p.Id)) { ok++; RunningProcesses.Remove(p); }
                else AddReport("Процессы", $"Не удалось завершить {p.ProcessName} (PID {p.Id}).");
            }
            AddReport("Процессы", $"Завершено: {ok}/{selected.Count}");
        }

        private void ProcessesDataGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e) =>
            OpenSelectedInExplorer(ProcessesDataGrid);

        private void ProcSearch_TextChanged(object sender, TextChangedEventArgs e) =>
            TableViewHelper.ApplyFilter(CollectionViewSource.GetDefaultView(RunningProcesses),
                ProcSearchBox.Text, "");

        private void ExportProcCsv_Click(object sender, RoutedEventArgs e) =>
            ExportTableCsv(RunningProcesses, "processes");

        // ==================== PROGRAMS ====================
        private async void LoadPrograms_Click(object sender, RoutedEventArgs e)
        {
            await RunWithProgress("Программы", async () =>
            {
                AddReport("Удаление программ", "Загрузка списка...");
                var list = await Task.Run(() => ProgramUninstaller.GetInstalledPrograms());

                InstalledPrograms.Clear();
                foreach (var p in list)
                {
                    InstalledPrograms.Add(new ProgramItem
                    {
                        DisplayName = p.DisplayName,
                        DisplayVersion = p.DisplayVersion,
                        Publisher = p.Publisher,
                        InstallDate = p.InstallDate,
                        EstimatedSize = p.EstimatedSize,
                        SizeText = p.EstimatedSize > 0
                            ? $"{p.EstimatedSize / 1024.0 / 1024.0:F1} МБ" : "—",
                        UninstallString = p.UninstallString,
                        QuietUninstallString = p.QuietUninstallString,
                        InstallLocation = p.InstallLocation,
                        RegistryKeyPath = p.RegistryKeyPath
                    });
                }

                if (ProgramsCountText != null)
                    ProgramsCountText.Text = InstalledPrograms.Count.ToString();

                AddReport("Удаление программ", $"Найдено: {InstalledPrograms.Count}");
            });
        }

        private void UninstallPrograms_Click(object sender, RoutedEventArgs e)
        {
            var selected = InstalledPrograms.Where(x => x.IsSelected).ToList();
            if (selected.Count == 0) { AddReport("Удаление программ", "Ничего не выбрано."); return; }

            var result = MessageBox.Show(
                $"Будут запущены штатные деинсталляторы для {selected.Count} программ.\n\n" +
                "⚠️ Проходите шаги деинсталлятора до конца для каждой программы.\n\n" +
                "Продолжить?",
                "Удаление программ",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (result != MessageBoxResult.Yes) return;

            int ok = 0, failed = 0;
            foreach (var p in selected)
            {
                var inst = new InstalledProgram
                {
                    DisplayName = p.DisplayName,
                    UninstallString = p.UninstallString,
                    QuietUninstallString = p.QuietUninstallString
                };

                var (success, message) = ProgramUninstaller.RunUninstallerWithMessage(inst);
                AddReport("Удаление программ", message);

                if (success)
                {
                    ok++;
                    System.Threading.Thread.Sleep(1500);
                }
                else failed++;
            }

            AddReport("Удаление программ", $"Итого: запущено {ok}/{selected.Count}, ошибок: {failed}");
        }

        private void ProgramsSearch_TextChanged(object sender, TextChangedEventArgs e) =>
            TableViewHelper.ApplyFilter(CollectionViewSource.GetDefaultView(InstalledPrograms),
                ProgramsSearchBox.Text, "");

        private void ExportProgramsCsv_Click(object sender, RoutedEventArgs e) =>
            ExportTableCsv(InstalledPrograms, "programs");

        // ==================== REPORT ====================
        private void RefreshLog_Click(object sender, RoutedEventArgs e)
        {
            LogTextBox.Text = LoggerService.GetLogContents();
            AddReport("Журнал", "Журнал обновлён.");
        }

        private void ClearReport_Click(object sender, RoutedEventArgs e)
        {
            ReportItems.Clear();
            LogTextBox.Text = string.Empty;
            AddReport("Журнал", "Отчёт очищен.");
        }

        private void ExportTxt_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (ReportItems.Count == 0) { AddReport("Экспорт", "Отчёт пуст."); return; }
                string path = ReportExporter.ExportToTxt(ReportItems);
                AddReport("Экспорт", $"Сохранено в TXT: {Path.GetFileName(path)}");
            }
            catch (Exception ex) { AddReport("Экспорт", $"Ошибка: {ex.Message}"); }
        }

        private void ExportCsv_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (ReportItems.Count == 0) { AddReport("Экспорт", "Отчёт пуст."); return; }
                string path = ReportExporter.ExportToCsv(ReportItems);
                AddReport("Экспорт", $"Сохранено в CSV: {Path.GetFileName(path)}");
            }
            catch (Exception ex) { AddReport("Экспорт", $"Ошибка: {ex.Message}"); }
        }

        private void OpenReportsFolder_Click(object sender, RoutedEventArgs e)
        {
            ReportExporter.OpenReportsFolder();
            AddReport("Экспорт", "Открыта папка с отчётами.");
        }

        private void DeleteAllReports_Click(object sender, RoutedEventArgs e)
        {
            var (deleted, freed) = ReportExporter.DeleteAllReports();
            AddReport("Экспорт", $"Удалено файлов: {deleted}. Освобождено: {CollectionSummary.FormatSize(freed)}");
        }

        // ==================== UPDATE CHECK ====================
        private async void CheckUpdates_Click(object sender, RoutedEventArgs e)
        {
            AddReport("Обновление", "Проверка обновлений...");
            var info = await UpdateChecker.CheckAsync();

            if (string.IsNullOrEmpty(info.LatestVersion))
            {
                AddReport("Обновление", "Не удалось проверить (нет интернета?)");
                MessageBox.Show("Не удалось проверить обновления.\nПроверьте интернет.",
                    "Обновление", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (!info.HasUpdate)
            {
                AddReport("Обновление", $"У вас последняя версия {UpdateChecker.CurrentVersion}.");
                MessageBox.Show($"У вас уже последняя версия v{UpdateChecker.CurrentVersion}.",
                    "Обновление", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            AddReport("Обновление", $"Доступна версия: v{info.LatestVersion}");

            var result = MessageBox.Show(
                $"🎉 Доступна новая версия v{info.LatestVersion}!\n\n" +
                $"Текущая: v{UpdateChecker.CurrentVersion}\n\n" +
                "Открыть страницу загрузки на GitHub?",
                "Доступно обновление",
                MessageBoxButton.YesNo,
                MessageBoxImage.Information);

            if (result == MessageBoxResult.Yes)
            {
                try
                {
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = string.IsNullOrEmpty(info.HtmlUrl)
                            ? "https://github.com/Boleznen/DigitalGardener/releases"
                            : info.HtmlUrl,
                        UseShellExecute = true
                    });
                    AddReport("Обновление", "Открыта страница релиза.");
                }
                catch (Exception ex) { AddReport("Обновление", $"Ошибка: {ex.Message}"); }
            }
        }

        // ==================== LOCKED FILES ====================
        private void AddLockedFile_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new Microsoft.Win32.OpenFileDialog();
            if (dlg.ShowDialog() == true)
            {
                var list = FileUnlocker.BuildLockedItem(dlg.FileName);
                foreach (var it in list) LockedFiles.Add(it);
                AddReport("Заблокированные", $"Добавлен файл: {dlg.FileName}");
            }
        }

        private void AddLockedFolder_Click(object sender, RoutedEventArgs e)
        {
            // 🆕 Используем WPF-нативный OpenFolderDialog (надёжнее WinForms)
            var dlg = new Microsoft.Win32.OpenFolderDialog
            {
                Title = "Выберите папку для анализа блокировок",
                Multiselect = false
            };

            if (dlg.ShowDialog() == true)
            {
                string folder = dlg.FolderName;
                int added = 0;
                int errors = 0;

                try
                {
                    // Ищем файлы во ВСЕХ подпапках, а не только в корне
                    foreach (var f in Directory.EnumerateFiles(folder, "*",
                        System.IO.SearchOption.AllDirectories))
                    {
                        try
                        {
                            foreach (var it in FileUnlocker.BuildLockedItem(f))
                            {
                                LockedFiles.Add(it);
                                added++;
                            }
                        }
                        catch { errors++; }
                    }

                    if (added == 0)
                    {
                        AddReport("Заблокированные",
                            $"⚠️ В папке {folder} не найдено файлов для анализа.");
                    }
                    else
                    {
                        AddReport("Заблокированные",
                            $"Из папки {folder} добавлено файлов: {added}" +
                            (errors > 0 ? $", ошибок: {errors}" : ""));
                    }
                }
                catch (Exception ex)
                {
                    AddReport("Заблокированные", $"Ошибка: {ex.Message}");
                    LoggerService.LogError("Ошибка добавления папки",
                        nameof(AddLockedFolder_Click), ex);
                }
            }
        }

        private void UnlockSelected_Click(object sender, RoutedEventArgs e)
        {
            int ok = 0;
            foreach (var lf in LockedFiles.Where(x => x.IsSelected).ToList())
            {
                try
                {
                    var procs = FileUnlocker.WhoIsLocking(lf.FilePath);
                    lf.LockingProcesses = procs.Count == 0 ? "—" : string.Join(", ", procs);

                    foreach (var pStr in procs)
                    {
                        var m = System.Text.RegularExpressions.Regex.Match(pStr, @"PID (\d+)");
                        if (m.Success && int.TryParse(m.Groups[1].Value, out int pid))
                        {
                            if (ProcessManager.Kill(pid)) ok++;
                        }
                    }
                    AddReport("Заблокированные", $"Освобождён: {lf.FileName}");
                }
                catch (Exception ex)
                {
                    LoggerService.LogError("Ошибка освобождения", nameof(UnlockSelected_Click), ex);
                }
            }
            AddReport("Заблокированные", $"Завершено процессов: {ok}");
        }

        private void DeleteLockedFile_Click(object sender, RoutedEventArgs e)
        {
            foreach (var lf in LockedFiles.Where(x => x.IsSelected).ToList())
            {
                try
                {
                    FileSystem.DeleteFile(lf.FilePath, UIOption.OnlyErrorDialogs, RecycleOption.SendToRecycleBin);
                    LockedFiles.Remove(lf);
                    AddReport("Заблокированные", $"Удалён: {lf.FileName}");
                }
                catch (Exception ex) { AddReport("Заблокированные", $"Ошибка: {ex.Message}"); }
            }
        }

        private void SelectAllLocked_Click(object sender, RoutedEventArgs e)
        {
            if (LockedFiles.Count == 0)
            {
                AddReport("Заблокированные", "Список пуст.");
                return;
            }

            bool all = LockedFiles.All(x => x.IsSelected);
            foreach (var f in LockedFiles) f.IsSelected = !all;

            AddReport("Заблокированные",
                all ? "Выделение снято." : $"Выбрано всё: {LockedFiles.Count} файлов.");
        }

        private void LockedDataGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e) =>
            OpenSelectedInExplorer(LockedDataGrid);

        private void LockedSearch_TextChanged(object sender, TextChangedEventArgs e) =>
            TableViewHelper.ApplyFilter(CollectionViewSource.GetDefaultView(LockedFiles),
                LockedSearchBox.Text, "");

        private void ExportLockedCsv_Click(object sender, RoutedEventArgs e) =>
            ExportTableCsv(LockedFiles, "locked");

        // ==================== SYSTEM INFO ====================
        private void LoadSystemInfo()
        {
            try
            {
                SystemInfoItems.Clear();
                foreach (var i in SystemInfoProvider.GetInfo())
                    SystemInfoItems.Add(i);
            }
            catch (Exception ex)
            {
                LoggerService.LogError("Ошибка загрузки инфо", nameof(LoadSystemInfo), ex);
            }
        }

        // ==================== TELEGRAM ====================
        private void OpenTelegram_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = "https://t.me/Boleznen",
                    UseShellExecute = true
                });
                AddReport("Помощь", "Открыт Telegram разработчика.");
            }
            catch (Exception ex) { AddReport("Помощь", $"Ошибка: {ex.Message}"); }
        }

        // ==================== EXPLORER ====================
        private void OpenInExplorer_Click(object sender, RoutedEventArgs e)
        {
            if (sender is MenuItem mi && mi.Parent is ContextMenu cm && cm.PlacementTarget is DataGrid dg)
                OpenSelectedInExplorer(dg);
        }

        private void OpenSelectedInExplorer(DataGrid dg)
        {
            try
            {
                var item = dg.SelectedItem;
                if (item == null) return;

                var type = item.GetType();
                string? path = (type.GetProperty("FullPath")?.GetValue(item)
                            ?? type.GetProperty("FilePath")?.GetValue(item)
                            ?? type.GetProperty("Path")?.GetValue(item)) as string;

                if (!string.IsNullOrEmpty(path))
                    FileOpener.ShowInExplorer(path);
            }
            catch (Exception ex)
            {
                LoggerService.LogError("Ошибка проводника", nameof(OpenSelectedInExplorer), ex);
            }
        }

        // ==================== ARCHIVE FOLDER ====================
        private void OpenArchiveFolder_Click(object sender, RoutedEventArgs e)
        {
            string dir = GetArchiveDir();
            FileOpener.OpenFolder(dir);
            AddReport("Архив", $"Открыта папка: {dir}");
        }

        // ==================== HIDDEN FILES ====================
        private void ToggleHiddenFiles_Click(object sender, RoutedEventArgs e)
        {
            var s = SettingsService.Current;
            s.ShowHiddenFiles = !s.ShowHiddenFiles;
            SettingsService.SaveCurrent();

            AddReport("Настройки", s.ShowHiddenFiles
                ? "Скрытые файлы теперь показываются."
                : "Скрытые файлы скрыты.");

            // Обновляем все представления
            foreach (var coll in new System.Collections.IEnumerable[]
                { TempFiles, BrowserCacheFiles, GameCacheItems, UnusedFiles })
            {
                CollectionViewSource.GetDefaultView(coll)?.Refresh();
            }
        }

        // ==================== SETTINGS WINDOW ====================
        private void OpenSettings_Click(object sender, RoutedEventArgs e)
        {
            // Открываем простое окно настроек (встроенное)
            var dlg = new SettingsWindow(SettingsService.Current) { Owner = this };
            if (dlg.ShowDialog() == true)
            {
                SettingsService.SaveCurrent();
                ApplySettingsToUi();
                AddReport("Настройки", "Настройки сохранены.");
            }
        }

        // ==================== RESTART AS ADMIN ====================
        private void RestartAsAdmin_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = Process.GetCurrentProcess().MainModule?.FileName ?? "",
                    UseShellExecute = true,
                    Verb = "runas"
                };
                Process.Start(psi);
                _realClose = true;
                Close();
            }
            catch (Exception ex)
            {
                AddReport("Ошибка", $"Не удалось перезапустить: {ex.Message}");
            }
        }

        // ==================== WINDOW EVENTS ====================
        private void Window_Closing(object sender, CancelEventArgs e)
        {
            var s = SettingsService.Current;

            if (!_realClose && s.CloseToTray && _trayManager.IsVisible)
            {
                e.Cancel = true;
                _trayManager.MinimizeToTray();
                if (!s.TrayHintShown)
                {
                    s.TrayHintShown = true;
                    SettingsService.SaveCurrent();
                    _trayManager.ShowNotification("Digital Gardener",
                        "Приложение свёрнуто в трей. Двойной клик по иконке — открыть.");
                }
            }
            else
            {
                _trayManager.Dispose();
                _diskSpaceTimer?.Stop();
                _monitorTimer?.Stop();
                _processTimer?.Stop();
            }
        }

        private void Window_StateChanged(object sender, EventArgs e)
        {
            var s = SettingsService.Current;

            if (WindowState == WindowState.Minimized && s.MinimizeToTray)
            {
                _trayManager.MinimizeToTray();
                if (!s.TrayHintShown)
                {
                    s.TrayHintShown = true;
                    SettingsService.SaveCurrent();
                    _trayManager.ShowNotification("Digital Gardener",
                        "Свёрнуто в трей. Двойной клик по иконке — открыть.");
                }
            }
        }

        // ==================== HOTKEYS ====================
        private void MainWindow_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            try
            {
                bool ctrl = Keyboard.Modifiers.HasFlag(ModifierKeys.Control);

                // Ctrl+A — выбрать все в текущей таблице
                if (ctrl && e.Key == Key.A)
                {
                    var dg = FindFocusedDataGrid();
                    if (dg != null)
                    {
                        SelectAllInDataGrid(dg);
                        e.Handled = true;
                    }
                }
                // Delete — удалить выбранные
                else if (e.Key == Key.Delete)
                {
                    var dg = FindFocusedDataGrid();
                    if (dg != null && dg.SelectedItem != null)
                    {
                        DeleteSelectedInGrid(dg);
                        e.Handled = true;
                    }
                }
                // F5 — обновить
                else if (e.Key == Key.F5)
                {
                    var dg = FindFocusedDataGrid();
                    if (dg != null)
                    {
                        RefreshCurrentTab();
                        e.Handled = true;
                    }
                }
                // Ctrl+E — экспорт
                else if (ctrl && e.Key == Key.E)
                {
                    var dg = FindFocusedDataGrid();
                    if (dg != null)
                    {
                        ExportCurrentGrid(dg);
                        e.Handled = true;
                    }
                }
                // Escape — снять выделение
                else if (e.Key == Key.Escape)
                {
                    var dg = FindFocusedDataGrid();
                    if (dg != null)
                    {
                        dg.SelectedItems.Clear();
                        e.Handled = true;
                    }
                }
                // Ctrl+S — настройки
                else if (ctrl && e.Key == Key.S)
                {
                    OpenSettings_Click(sender, new RoutedEventArgs());
                    e.Handled = true;
                }
            }
            catch { }
        }

        private DataGrid? FindFocusedDataGrid()
        {
            var focused = Keyboard.FocusedElement as DependencyObject;
            while (focused != null)
            {
                if (focused is DataGrid dg) return dg;
                focused = VisualTreeHelper.GetParent(focused);
            }
            return null;
        }

        private void SelectAllInDataGrid(DataGrid dg)
        {
            try
            {
                if (dg.ItemsSource is System.Collections.IEnumerable items)
                {
                    bool all = true;
                    foreach (var item in items)
                    {
                        var prop = item.GetType().GetProperty("IsSelected");
                        if (prop != null && prop.GetValue(item) is bool b && !b)
                        {
                            all = false;
                            break;
                        }
                    }

                    foreach (var item in items)
                    {
                        var prop = item.GetType().GetProperty("IsSelected");
                        if (prop != null) prop.SetValue(item, !all);
                    }
                }
            }
            catch { }
        }

        private void DeleteSelectedInGrid(DataGrid dg)
        {
            try
            {
                if (dg.ItemsSource == TempFiles) DeleteSelected_Click(dg, new RoutedEventArgs());
                else if (dg.ItemsSource == BrowserCacheFiles) DeleteSelectedBrowser_Click(dg, new RoutedEventArgs());
                else if (dg.ItemsSource == UnusedFiles) DeleteSelectedUnused_Click(dg, new RoutedEventArgs());
                else if (dg.ItemsSource == RunningProcesses) KillProcess_Click(dg, new RoutedEventArgs());
                else if (dg.ItemsSource == DuplicateCandidates) DeleteDuplicateToRecycleBin_Click(dg, new RoutedEventArgs());
            }
            catch { }
        }

        private void RefreshCurrentTab()
        {
            try
            {
                if (TempDataGrid?.IsVisible == true) ScanTempAndDownloads_Click(this, new RoutedEventArgs());
                else if (BrowserDataGrid?.IsVisible == true) ScanBrowserCache_Click(this, new RoutedEventArgs());
                else if (GameCacheDataGrid?.IsVisible == true) ScanGameCache_Click(this, new RoutedEventArgs());
                else if (UnusedDataGrid?.IsVisible == true) ScanUnusedFiles_Click(this, new RoutedEventArgs());
                else if (DuplicatesDataGrid?.IsVisible == true) ScanDuplicates_Click(this, new RoutedEventArgs());
                else if (ProcessesDataGrid?.IsVisible == true) MonitorProcesses_Click(this, new RoutedEventArgs());
            }
            catch { }
        }

        private void ExportCurrentGrid(DataGrid dg)
        {
            try
            {
                if (dg.ItemsSource == TempFiles) ExportTempCsv_Click(dg, new RoutedEventArgs());
                else if (dg.ItemsSource == BrowserCacheFiles) ExportBrowserCsv_Click(dg, new RoutedEventArgs());
                else if (dg.ItemsSource == GameCacheItems) ExportGameCsv_Click(dg, new RoutedEventArgs());
                else if (dg.ItemsSource == UnusedFiles) ExportUnusedCsv_Click(dg, new RoutedEventArgs());
                else if (dg.ItemsSource == DuplicateCandidates) ExportDupCsv_Click(dg, new RoutedEventArgs());
                else if (dg.ItemsSource == StartupItems) ExportStartupCsv_Click(dg, new RoutedEventArgs());
                else if (dg.ItemsSource == RunningProcesses) ExportProcCsv_Click(dg, new RoutedEventArgs());
                else if (dg.ItemsSource == InstalledPrograms) ExportProgramsCsv_Click(dg, new RoutedEventArgs());
                else if (dg.ItemsSource == LockedFiles) ExportLockedCsv_Click(dg, new RoutedEventArgs());
            }
            catch { }
        }

        // ==================== HELPERS ====================
        public void AddReport(string category, string message)
        {
            var entry = new ReportItem(category, message);
            ReportItems.Add(entry);
            if (LogTextBox != null)
            {
                LogTextBox.AppendText($"[{entry.Timestamp}] [{category}] {message}{Environment.NewLine}");
                LogTextBox.ScrollToEnd();
            }
        }

        private void UpdateExtCombo<T>(ComboBox combo, ObservableCollection<T> items)
        {
            try
            {
                if (combo == null) return;
                var extensions = TableViewHelper.GetExtensions(items);
                var current = combo.SelectedItem?.ToString();

                combo.ItemsSource = extensions;
                if (!string.IsNullOrEmpty(current) && extensions.Contains(current))
                    combo.SelectedItem = current;
                else
                    combo.SelectedIndex = 0;
            }
            catch { }
        }

        private void ExportTableCsv<T>(ObservableCollection<T> items, string tableName)
        {
            try
            {
                if (items.Count == 0)
                {
                    AddReport("Экспорт", "Таблица пуста.");
                    return;
                }
                string path = TableViewHelper.ExportToCsv(items, tableName);
                AddReport("Экспорт", $"Сохранено: {Path.GetFileName(path)}");
            }
            catch (Exception ex)
            {
                AddReport("Экспорт", $"Ошибка: {ex.Message}");
                LoggerService.LogError($"Экспорт {tableName}", nameof(ExportTableCsv), ex);
            }
        }

        private void DeleteItems(List<object> allItems, bool toRecycle)
        {
            var selected = allItems.Where(x =>
            {
                var prop = x.GetType().GetProperty("IsSelected");
                return prop != null && prop.GetValue(x) is bool b && b;
            }).ToList();

            if (selected.Count == 0)
            {
                AddReport("Удаление", "Ничего не выбрано.");
                return;
            }

            int ok = 0;
            foreach (var obj in selected)
            {
                var type = obj.GetType();
                string? path = type.GetProperty("FullPath")?.GetValue(obj) as string;
                if (string.IsNullOrEmpty(path)) continue;

                try
                {
                    if (Directory.Exists(path))
                    {
                        if (toRecycle)
                            FileSystem.DeleteDirectory(path, UIOption.OnlyErrorDialogs, RecycleOption.SendToRecycleBin);
                        else
                            Directory.Delete(path, recursive: true);
                    }
                    else
                    {
                        if (toRecycle)
                            FileSystem.DeleteFile(path, UIOption.OnlyErrorDialogs, RecycleOption.SendToRecycleBin);
                        else
                            File.Delete(path);
                    }

                    if (obj is SystemFileItem sf) TempFiles.Remove(sf);
                    if (obj is SystemFileItem sf2) BrowserCacheFiles.Remove(sf2);
                    if (obj is SystemFileItem sf3) UnusedFiles.Remove(sf3);
                    if (obj is DuplicateItem di) DuplicateCandidates.Remove(di);
                    ok++;
                }
                catch (Exception ex)
                {
                    LoggerService.LogError($"Не удалось удалить {path}", nameof(DeleteItems), ex);
                }
            }
            AddReport("Удаление", $"Удалено: {ok}/{selected.Count}");
        }

        private void ArchiveItems(List<object> allItems)
        {
            var selected = allItems.Where(x =>
            {
                var prop = x.GetType().GetProperty("IsSelected");
                return prop != null && prop.GetValue(x) is bool b && b;
            }).ToList();

            if (selected.Count == 0)
            {
                AddReport("Архив", "Ничего не выбрано.");
                return;
            }

            string archiveDir = GetArchiveDir();
            int ok = 0;
            foreach (var obj in selected)
            {
                var type = obj.GetType();
                string? path = type.GetProperty("FullPath")?.GetValue(obj) as string;
                string? name = type.GetProperty("Name")?.GetValue(obj) as string;
                if (string.IsNullOrEmpty(path) || string.IsNullOrEmpty(name)) continue;
                try
                {
                    string dest = MakeUniquePath(archiveDir, name);

                    if (Directory.Exists(path))
                        Directory.Move(path, dest);
                    else
                        File.Move(path, dest);

                    if (obj is SystemFileItem sf) TempFiles.Remove(sf);
                    if (obj is SystemFileItem sf2) BrowserCacheFiles.Remove(sf2);
                    if (obj is SystemFileItem sf3) UnusedFiles.Remove(sf3);
                    if (obj is DuplicateItem di) DuplicateCandidates.Remove(di);
                    ok++;
                }
                catch (Exception ex)
                {
                    LoggerService.LogError($"Не удалось архивировать {path}", nameof(ArchiveItems), ex);
                }
            }
            AddReport("Архив", $"В архив: {ok}/{selected.Count}");
        }

        private static string GetArchiveDir()
        {
            string dir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "DigitalGardener_Archive");
            Directory.CreateDirectory(dir);
            return dir;
        }

        private static string MakeUniquePath(string dir, string fileName)
        {
            string dest = Path.Combine(dir, fileName);
            if (!File.Exists(dest) && !Directory.Exists(dest)) return dest;
            string name = Path.GetFileNameWithoutExtension(fileName);
            string ext = Path.GetExtension(fileName);
            int n = 1;
            while (File.Exists(dest) || Directory.Exists(dest))
                dest = Path.Combine(dir, $"{name}_{n++}{ext}");
            return dest;
        }
    }
}