using System.Windows;
using System.Windows.Controls;

namespace DigitalGardener
{
    public partial class SettingsWindow : Window
    {
        private readonly AppSettings _settings;

        public SettingsWindow(AppSettings settings)
        {
            InitializeComponent();
            _settings = settings;
            LoadSettings();
        }

        private void LoadSettings()
        {
            // Дни для неиспользуемых
            int idx = _settings.UnusedDays switch
            {
                30 => 0,
                60 => 1,
                90 => 2,
                _ => 3
            };
            if (UnusedDaysComboSettings != null)
                UnusedDaysComboSettings.SelectedIndex = idx;

            // Авто-обновление процессов
            if (AutoRefreshProcessesCheckSettings != null)
                AutoRefreshProcessesCheckSettings.IsChecked = _settings.AutoRefreshProcesses;

            int intervalIdx = _settings.AutoRefreshIntervalSec switch
            {
                3 => 0,
                5 => 1,
                10 => 2,
                30 => 3,
                _ => 1
            };
            if (AutoRefreshIntervalComboSettings != null)
                AutoRefreshIntervalComboSettings.SelectedIndex = intervalIdx;

            // Отображение
            if (ShowHiddenFilesCheck != null)
                ShowHiddenFilesCheck.IsChecked = _settings.ShowHiddenFiles;

            if (ShowMonitorCheckSettings != null)
                ShowMonitorCheckSettings.IsChecked = _settings.ShowSystemMonitor;

            // Трей
            if (MinimizeToTrayCheck != null)
                MinimizeToTrayCheck.IsChecked = _settings.MinimizeToTray;

            if (CloseToTrayCheck != null)
                CloseToTrayCheck.IsChecked = _settings.CloseToTray;
        }

        private void Save_Click(object sender, RoutedEventArgs e)
        {
            // Сохраняем обратно в объект настроек
            if (UnusedDaysComboSettings?.SelectedItem is ComboBoxItem daysItem &&
                int.TryParse(daysItem.Content?.ToString()?.Split(' ')[0], out int days))
            {
                _settings.UnusedDays = days;
            }

            _settings.AutoRefreshProcesses = AutoRefreshProcessesCheckSettings?.IsChecked == true;

            if (AutoRefreshIntervalComboSettings?.SelectedItem is ComboBoxItem intItem &&
                int.TryParse(intItem.Content?.ToString()?.Split(' ')[0], out int sec))
            {
                _settings.AutoRefreshIntervalSec = sec;
            }

            _settings.ShowHiddenFiles = ShowHiddenFilesCheck?.IsChecked == true;
            _settings.ShowSystemMonitor = ShowMonitorCheckSettings?.IsChecked == true;
            _settings.MinimizeToTray = MinimizeToTrayCheck?.IsChecked == true;
            _settings.CloseToTray = CloseToTrayCheck?.IsChecked == true;

            DialogResult = true;
            Close();
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }
}