using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace DigitalGardener
{
    public static class SettingsService
    {
        private static readonly string SettingsDir =
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "DigitalGardener_Settings");

        private static readonly string SettingsFile =
            Path.Combine(SettingsDir, "settings.json");

        private static AppSettings? _current;

        public static AppSettings Current
        {
            get
            {
                if (_current == null) _current = Load();
                return _current;
            }
        }

        public static AppSettings Load()
        {
            try
            {
                if (File.Exists(SettingsFile))
                {
                    string json = File.ReadAllText(SettingsFile);
                    var s = JsonSerializer.Deserialize<AppSettings>(json);
                    if (s != null)
                    {
                        if (s.ScanRoots.Count == 0)
                            s.ScanRoots = GetDefaultScanRoots();
                        return s;
                    }
                }
            }
            catch (Exception ex)
            {
                LoggerService.LogError("Не удалось загрузить настройки",
                    nameof(Load), ex);
            }

            var fresh = new AppSettings { ScanRoots = GetDefaultScanRoots() };
            Save(fresh);
            return fresh;
        }

        public static void Save(AppSettings settings)
        {
            try
            {
                Directory.CreateDirectory(SettingsDir);
                string json = JsonSerializer.Serialize(settings,
                    new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(SettingsFile, json);
                _current = settings;
            }
            catch (Exception ex)
            {
                LoggerService.LogError("Не удалось сохранить настройки",
                    nameof(Save), ex);
            }
        }

        public static void SaveCurrent() => Save(Current);

        public static List<string> GetDefaultScanRoots()
        {
            return new List<string>
            {
                Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                Environment.GetFolderPath(Environment.SpecialFolder.MyPictures),
                Environment.GetFolderPath(Environment.SpecialFolder.MyVideos),
                Environment.GetFolderPath(Environment.SpecialFolder.MyMusic),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads"),
                Environment.GetFolderPath(Environment.SpecialFolder.Desktop)
            };
        }
    }
}