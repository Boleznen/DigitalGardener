using System;
using System.Drawing;
using System.IO;
using System.Windows;
using System.Windows.Forms;

namespace DigitalGardener
{
    public class TrayManager : IDisposable
    {
        private NotifyIcon? _notifyIcon;
        private Window? _mainWindow;
        private bool _disposed;

        public event Action? OnShowRequested;
        public event Action? OnSettingsRequested;
        public event Action? OnExitRequested;

        public bool IsVisible => _notifyIcon?.Visible == true;

        public void Initialize(Window mainWindow)
        {
            _mainWindow = mainWindow;

            try
            {
                var icon = LoadAppIcon();

                _notifyIcon = new NotifyIcon
                {
                    Icon = icon,
                    Text = "Digital Gardener — чистка Windows",
                    Visible = false
                };

                _notifyIcon.DoubleClick += (_, __) => OnShowRequested?.Invoke();
                _notifyIcon.ContextMenuStrip = BuildContextMenu();
            }
            catch (Exception ex)
            {
                LoggerService.LogError("Ошибка создания иконки в трее",
                    nameof(Initialize), ex);
            }
        }

        private ContextMenuStrip BuildContextMenu()
        {
            var menu = new ContextMenuStrip
            {
                BackColor = Color.FromArgb(40, 40, 56),
                ForeColor = Color.White,
                ShowImageMargin = false
            };

            var openItem = new ToolStripMenuItem("Открыть Digital Gardener");
            openItem.ForeColor = Color.White;
            openItem.Click += (_, __) => OnShowRequested?.Invoke();
            menu.Items.Add(openItem);

            var settingsItem = new ToolStripMenuItem("Настройки");
            settingsItem.ForeColor = Color.White;
            settingsItem.Click += (_, __) => OnSettingsRequested?.Invoke();
            menu.Items.Add(settingsItem);

            menu.Items.Add(new ToolStripSeparator());

            var exitItem = new ToolStripMenuItem("Выход");
            exitItem.ForeColor = Color.FromArgb(255, 120, 120);
            exitItem.Click += (_, __) => OnExitRequested?.Invoke();
            menu.Items.Add(exitItem);

            return menu;
        }

        /// <summary>
        /// Загружает иконку для трея. 4 попытки:
        /// 1) Из ресурсов приложения.
        /// 2) Из файла app.ico рядом с .exe.
        /// 3) Из встроенной в .exe.
        /// 4) Стандартная.
        /// </summary>
        private Icon LoadAppIcon()
        {
            // 1) Ресурсы приложения
            try
            {
                var uri = new Uri("pack://application:,,,/app.ico");
                var streamInfo = System.Windows.Application.GetResourceStream(uri);
                if (streamInfo?.Stream != null)
                {
                    using var s = streamInfo.Stream;
                    return new Icon(s);
                }
            }
            catch { }

            // 2) Файл рядом с .exe
            try
            {
                string icoPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "app.ico");
                if (File.Exists(icoPath))
                    return new Icon(icoPath);
            }
            catch { }

            // 3) Из встроенной в .exe иконки
            try
            {
                string? exePath = System.Reflection.Assembly.GetExecutingAssembly().Location;
                if (string.IsNullOrEmpty(exePath))
                    exePath = Environment.ProcessPath;

                if (!string.IsNullOrEmpty(exePath))
                {
                    var extracted = Icon.ExtractAssociatedIcon(exePath);
                    if (extracted != null) return extracted;
                }
            }
            catch { }

            return SystemIcons.Application;
        }

        public void Show()
        {
            if (_notifyIcon != null) _notifyIcon.Visible = true;
        }

        public void Hide()
        {
            if (_notifyIcon != null) _notifyIcon.Visible = false;
        }

        public void ShowNotification(string title, string text,
            ToolTipIcon icon = ToolTipIcon.Info, int timeoutMs = 3000)
        {
            try { _notifyIcon?.ShowBalloonTip(timeoutMs, title, text, icon); }
            catch { }
        }

        public void RestoreMainWindow()
        {
            try
            {
                if (_mainWindow == null) return;
                _mainWindow.Show();
                _mainWindow.WindowState = WindowState.Normal;
                _mainWindow.Activate();
                _mainWindow.Topmost = true;
                _mainWindow.Topmost = false;
                _mainWindow.Focus();
            }
            catch { }
        }

        public void MinimizeToTray()
        {
            try
            {
                _mainWindow?.Hide();
                Show();
            }
            catch { }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            try
            {
                if (_notifyIcon != null)
                {
                    _notifyIcon.Visible = false;
                    _notifyIcon.Dispose();
                    _notifyIcon = null;
                }
            }
            catch { }
        }
    }
}