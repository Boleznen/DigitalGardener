using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Microsoft.VisualBasic.FileIO;
using SearchOption = System.IO.SearchOption;

namespace DigitalGardener
{
    public partial class MainWindow : Window
    {
        public ObservableCollection<SystemFileItem> TempFiles { get; set; } = new();
        public ObservableCollection<SystemFileItem> BrowserCacheFiles { get; set; } = new();
        public ObservableCollection<SystemFileItem> UnusedFiles { get; set; } = new();
        public ObservableCollection<DuplicateItem> DuplicateCandidates { get; set; } = new();
        public ObservableCollection<StartupItem> StartupItems { get; set; } = new();
        public ObservableCollection<ProcessItem> RunningProcesses { get; set; } = new();
        public ObservableCollection<ReportItem> ReportItems { get; set; } = new();
        public ObservableCollection<LockedFileItem> LockedFiles { get; set; } = new();
        public ObservableCollection<SystemInfoItem> SystemInfoItems { get; set; } = new();
        public ObservableCollection<ProgressSlot> ProgressSlots { get; set; } = new();

        // Итоговые сводки
        public CollectionSummary TempSummary { get; set; } = new();
        public CollectionSummary BrowserSummary { get; set; } = new();
        public CollectionSummary UnusedSummary { get; set; } = new();
        public CollectionSummary DuplicatesSummary { get; set; } = new();
        public CollectionSummary StartupSummary { get; set; } = new();
        public CollectionSummary ProcessesSummary { get; set; } = new();
        public CollectionSummary LockedSummary { get; set; } = new();

        private readonly StartupManager _startupManager = new();
        private DispatcherTimer? _processTimer;
        private bool _initializing = true;

        public MainWindow()
        {
            InitializeComponent();
            DataContext = this;

            // Подписки на изменения коллекций для подсчёта итогов
            HookSummary(TempFiles, TempSummary, "TempFiles");
            HookSummary(BrowserCacheFiles, BrowserSummary, "BrowserCacheFiles");
            HookSummary(UnusedFiles, UnusedSummary, "UnusedFiles");
            HookSummary(DuplicateCandidates, DuplicatesSummary, "DuplicateCandidates");
            HookSummary(StartupItems, StartupSummary, "StartupItems");
            HookSummary(RunningProcesses, ProcessesSummary, "RunningProcesses");
            HookSummary(LockedFiles, LockedSummary, "LockedFiles");

            if (!ProcessManager.IsElevated())
                AddReport("Внимание", "Приложение запущено БЕЗ прав администратора. " +
                    "HKLM-автозагрузка и системные процессы могут не отключаться.");

            // Применяем настройки к UI
            ApplySettingsToUi();

            LoadStartupItems();
            LoadSystemInfo();

            _initializing = false;
        }

        // ==================== SETTINGS ====================
        private void ApplySettingsToUi()
        {
            var s = SettingsService.Current;

            // ComboBox «дни»
            int idx = s.UnusedDays switch
            {
                30 => 0,
                60 => 1,
                90 => 2,
                _ => 3
            };
            if (UnusedDaysCombo != null) UnusedDaysCombo.SelectedIndex = idx;

            // Авто-обновление процессов
            if (AutoRefreshProcessesCheck != null)
                AutoRefreshProcessesCheck.IsChecked = s.AutoRefreshProcesses;

            int intervalIdx = s.AutoRefreshIntervalSec switch
            {
                3 => 0,
                5 => 1,
                10 => 2,
                30 => 3,
                _ => 1
            };
            if (AutoRefreshIntervalCombo != null) AutoRefreshIntervalCombo.SelectedIndex = intervalIdx;

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
                    // сохраняем выделение
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

        // ==================== SUMMARY HOOK ====================
        private void HookSummary<T>(ObservableCollection<T> collection,
            CollectionSummary summary, string name) where T : class
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

            collection.CollectionChanged += (_, args) => Recalc();
            Recalc();
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
                var list = await Task.Run(() => TempCleaner.Scan());
                TempFiles.Clear();
                foreach (var f in list) TempFiles.Add(f);
                AddReport("Очистка", $"Найдено {list.Count} файлов.");
            });
        }

        private void DeleteSelected_Click(object sender, RoutedEventArgs e) =>
            DeleteItems(TempFiles.Cast<object>().ToList(), toRecycle: false);

        private void DeleteToRecycleBin_Click(object sender, RoutedEventArgs e) =>
            DeleteItems(TempFiles.Cast<object>().ToList(), toRecycle: true);

        private void ArchiveFile_Click(object sender, RoutedEventArgs e) =>
            ArchiveItems(TempFiles.Cast<object>().ToList());

        private void SelectAllTemp_Click(object sender, RoutedEventArgs e)
        {
            bool all = TempFiles.All(x => x.IsSelected);
            foreach (var f in TempFiles) f.IsSelected = !all;
        }

        private void TempDataGrid_MouseDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e) =>
            OpenSelectedInExplorer(TempDataGrid);

        // ==================== BROWSER ====================
        private async void ScanBrowserCache_Click(object sender, RoutedEventArgs e)
        {
            await RunWithProgress("Кэш", async () =>
            {
                AddReport("Кэш браузеров", "Сканирование...");
                var list = await Task.Run(() => BrowserCacheCleaner.Scan());
                BrowserCacheFiles.Clear();
                foreach (var f in list) BrowserCacheFiles.Add(f);
                AddReport("Кэш браузеров", $"Найдено {list.Count} файлов.");
            });
        }

        private void DeleteSelectedBrowser_Click(object sender, RoutedEventArgs e) =>
            DeleteItems(BrowserCacheFiles.Cast<object>().ToList(), toRecycle: false);

        private void RecycleSelectedBrowser_Click(object sender, RoutedEventArgs e) =>
            DeleteItems(BrowserCacheFiles.Cast<object>().ToList(), toRecycle: true);

        private void ArchiveSelectedBrowser_Click(object sender, RoutedEventArgs e) =>
            ArchiveItems(BrowserCacheFiles.Cast<object>().ToList());

        private void SelectAllBrowser_Click(object sender, RoutedEventArgs e)
        {
            bool all = BrowserCacheFiles.All(x => x.IsSelected);
            foreach (var f in BrowserCacheFiles) f.IsSelected = !all;
        }

        private void BrowserDataGrid_MouseDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e) =>
            OpenSelectedInExplorer(BrowserDataGrid);

        // ==================== UNUSED ====================
        private async void ScanUnusedFiles_Click(object sender, RoutedEventArgs e)
        {
            int days = SettingsService.Current.UnusedDays;

            await RunWithProgress($"Старые ({days}д)", async () =>
            {
                AddReport("Неиспользуемые", $"Сканирование (>{days} дней)...");
                var roots = SettingsService.Current.ScanRoots;
                var list = await Task.Run(() => UnusedFileFinder.Scan(roots, days));
                UnusedFiles.Clear();
                foreach (var f in list) UnusedFiles.Add(f);
                AddReport("Неиспользуемые", $"Найдено {list.Count} объектов (>{days} дней).");
            });
        }

        private void DeleteSelectedUnused_Click(object sender, RoutedEventArgs e) =>
            DeleteItems(UnusedFiles.Cast<object>().ToList(), toRecycle: false);

        private void RecycleSelectedUnused_Click(object sender, RoutedEventArgs e) =>
            DeleteItems(UnusedFiles.Cast<object>().ToList(), toRecycle: true);

        private void ArchiveSelectedUnused_Click(object sender, RoutedEventArgs e) =>
            ArchiveItems(UnusedFiles.Cast<object>().ToList());

        private void UnusedDataGrid_MouseDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e) =>
            OpenSelectedInExplorer(UnusedDataGrid);

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
                DuplicateCandidates.Clear();
                foreach (var d in dups) DuplicateCandidates.Add(d);
                AddReport("Дубликаты", $"Найдено {dups.Count} дубликатов.");
            });
        }

        private void DeleteDuplicateToRecycleBin_Click(object sender, RoutedEventArgs e)
        {
            var selected = DuplicateCandidates.Where(x => x.IsSelected).ToList();
            if (selected.Count == 0)
            {
                AddReport("Дубликаты", "Ничего не выбрано.");
                return;
            }
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
                    LoggerService.LogError("Ошибка удаления дубликата",
                        nameof(DeleteDuplicateToRecycleBin_Click), ex);
                }
            }
            AddReport("Дубликаты", $"В корзину отправлено: {ok}/{selected.Count}");
        }

        private void ArchiveDuplicate_Click(object sender, RoutedEventArgs e)
        {
            var selected = DuplicateCandidates.Where(x => x.IsSelected).ToList();
            if (selected.Count == 0)
            {
                AddReport("Дубликаты", "Ничего не выбрано.");
                return;
            }
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
                    LoggerService.LogError("Ошибка архивации дубликата",
                        nameof(ArchiveDuplicate_Click), ex);
                }
            }
            AddReport("Дубликаты", $"В архив: {ok}/{selected.Count}");
        }

        private void DuplicatesDataGrid_MouseDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e) =>
            OpenSelectedInExplorer(DuplicatesDataGrid);

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
            if (selected.Count == 0)
            {
                AddReport("Автозагрузка", "Ничего не выбрано.");
                return;
            }

            int ok = 0;
            foreach (var item in selected)
            {
                var (success, message) = _startupManager.DisableWithMessage(item);
                AddReport("Автозагрузка", message);

                if (success)
                {
                    item.IsEnabled = false;
                    item.Recommendation = "Отключено";
                    ok++;
                }
            }

            AddReport("Автозагрузка", $"Итого отключено: {ok}/{selected.Count}");
        }

        private void OptimizeStartup_Click(object sender, RoutedEventArgs e)
        {
            var bad = StartupItems.Where(x => x.IsEnabled &&
                (x.Recommendation.Contains("Проверить") ||
                 x.Recommendation.Contains("необходимость") ||
                 x.Path.ToLower().Contains("temp"))).ToList();

            if (bad.Count == 0)
            {
                AddReport("Автозагрузка", "Нечего оптимизировать.");
                return;
            }

            int ok = 0;
            foreach (var item in bad)
            {
                var (success, message) = _startupManager.DisableWithMessage(item);
                AddReport("Автозагрузка", message);
                if (success)
                {
                    item.IsEnabled = false;
                    item.Recommendation = "Отключено";
                    ok++;
                }
            }
            AddReport("Автозагрузка", $"Оптимизировано: {ok}/{bad.Count}");
        }

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
            if (selected.Count == 0)
            {
                AddReport("Процессы", "Ничего не выбрано.");
                return;
            }
            int ok = 0;
            foreach (var p in selected)
            {
                if (ProcessManager.Kill(p.Id))
                {
                    ok++;
                    RunningProcesses.Remove(p);
                }
                else
                {
                    AddReport("Процессы", $"Не удалось завершить {p.ProcessName} (PID {p.Id}). " +
                        "Возможно, это системный процесс или нужно запустить от админа.");
                }
            }
            AddReport("Процессы", $"Завершено: {ok}/{selected.Count}");
        }

        private void ProcessesDataGrid_MouseDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e) =>
            OpenSelectedInExplorer(ProcessesDataGrid);

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
                if (ReportItems.Count == 0)
                {
                    AddReport("Экспорт", "Отчёт пуст.");
                    return;
                }
                string path = ReportExporter.ExportToTxt(ReportItems);
                AddReport("Экспорт", $"Сохранено в TXT: {Path.GetFileName(path)}");
            }
            catch (Exception ex)
            {
                AddReport("Экспорт", $"Ошибка: {ex.Message}");
                LoggerService.LogError("Экспорт TXT", nameof(ExportTxt_Click), ex);
            }
        }

        private void ExportCsv_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (ReportItems.Count == 0)
                {
                    AddReport("Экспорт", "Отчёт пуст.");
                    return;
                }
                string path = ReportExporter.ExportToCsv(ReportItems);
                AddReport("Экспорт", $"Сохранено в CSV: {Path.GetFileName(path)}");
            }
            catch (Exception ex)
            {
                AddReport("Экспорт", $"Ошибка: {ex.Message}");
                LoggerService.LogError("Экспорт CSV", nameof(ExportCsv_Click), ex);
            }
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
            using var dlg = new System.Windows.Forms.FolderBrowserDialog
            {
                Description = "Выберите папку для анализа блокировок",
                UseDescriptionForTitle = true,
                ShowNewFolderButton = false
            };
            if (dlg.ShowDialog() == System.Windows.Forms.DialogResult.OK)
            {
                string folder = dlg.SelectedPath;
                int added = 0;
                try
                {
                    foreach (var f in Directory.GetFiles(folder, "*", SearchOption.TopDirectoryOnly))
                    {
                        try
                        {
                            foreach (var it in FileUnlocker.BuildLockedItem(f))
                            {
                                LockedFiles.Add(it);
                                added++;
                            }
                        }
                        catch { }
                    }
                    AddReport("Заблокированные", $"Из папки {folder} добавлено файлов: {added}");
                }
                catch (Exception ex)
                {
                    AddReport("Заблокированные", $"Ошибка: {ex.Message}");
                    LoggerService.LogError("Ошибка добавления папки", nameof(AddLockedFolder_Click), ex);
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
                catch (Exception ex)
                {
                    AddReport("Заблокированные", $"Не удалось удалить {lf.FileName}: {ex.Message}");
                }
            }
        }

        private void LockedDataGrid_MouseDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e) =>
            OpenSelectedInExplorer(LockedDataGrid);

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
                LoggerService.LogError("Ошибка открытия проводника",
                    nameof(OpenSelectedInExplorer), ex);
            }
        }

        // ==================== ARCHIVE FOLDER ====================
        private void OpenArchiveFolder_Click(object sender, RoutedEventArgs e)
        {
            string dir = GetArchiveDir();
            FileOpener.OpenFolder(dir);
            AddReport("Архив", $"Открыта папка: {dir}");
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
            AddReport("Удаление", $"Удалено: {ok}/{selected.Count} (корзина: {toRecycle})");
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