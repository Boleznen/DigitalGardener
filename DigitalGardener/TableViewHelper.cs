using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Collections.ObjectModel;
using System.ComponentModel;

namespace DigitalGardener
{
    public static class TableViewHelper
    {
        // ==================== СОХРАНЕНИЕ ВЫДЕЛЕНИЯ ====================

        /// <summary>
        /// Сохраняет выделение по уникальному полю (FullPath, Id, Name).
        /// Возвращает HashSet сохранённых ключей.
        /// </summary>
        public static HashSet<string> SaveSelection<T>(IEnumerable<T> items, string keyField)
        {
            var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            try
            {
                foreach (var item in items)
                {
                    var prop = item?.GetType().GetProperty(keyField);
                    if (prop == null) continue;

                    var selProp = item!.GetType().GetProperty("IsSelected");
                    if (selProp == null) continue;

                    if (selProp.GetValue(item) is bool isSelected && isSelected)
                    {
                        var value = prop.GetValue(item)?.ToString();
                        if (!string.IsNullOrEmpty(value))
                            result.Add(value);
                    }
                }
            }
            catch { }

            return result;
        }

        /// <summary>
        /// Восстанавливает выделение после пересканирования.
        /// </summary>
        public static void RestoreSelection<T>(IEnumerable<T> items,
            HashSet<string> savedKeys, string keyField)
        {
            if (savedKeys == null || savedKeys.Count == 0) return;

            try
            {
                foreach (var item in items)
                {
                    if (item == null) continue;

                    var prop = item.GetType().GetProperty(keyField);
                    if (prop == null) continue;

                    var selProp = item.GetType().GetProperty("IsSelected");
                    if (selProp == null) continue;

                    var value = prop.GetValue(item)?.ToString();
                    if (!string.IsNullOrEmpty(value) && savedKeys.Contains(value))
                    {
                        selProp.SetValue(item, true);
                    }
                }
            }
            catch { }
        }

        // ==================== ПОИСК И ФИЛЬТР ====================

        /// <summary>
        /// Применяет фильтр + поиск к ICollectionView коллекции.
        /// </summary>
        public static void ApplyFilter(ICollectionView view,
            string searchText, string extensionFilter)
        {
            if (view == null) return;

            view.Filter = (obj) =>
            {
                if (obj == null) return false;

                // Расширение
                if (!string.IsNullOrEmpty(extensionFilter) &&
                    extensionFilter != "Все файлы")
                {
                    var name = GetPropertyValue(obj, "Name") ??
                               GetPropertyValue(obj, "DisplayName") ??
                               GetPropertyValue(obj, "ProcessName") ?? "";

                    string ext = Path.GetExtension(name);
                    if (!ext.Equals(extensionFilter, StringComparison.OrdinalIgnoreCase))
                        return false;
                }

                // Поиск
                if (!string.IsNullOrWhiteSpace(searchText))
                {
                    string lower = searchText.ToLowerInvariant();
                    var found = false;

                    foreach (var field in new[] { "Name", "FullPath", "DisplayName", "ProcessName", "Publisher", "Reason" })
                    {
                        var val = GetPropertyValue(obj, field);
                        if (!string.IsNullOrEmpty(val) &&
                            val.ToLowerInvariant().Contains(lower))
                        {
                            found = true;
                            break;
                        }
                    }

                    if (!found) return false;
                }

                return true;
            };
        }

        private static string? GetPropertyValue(object obj, string propertyName)
        {
            try
            {
                var prop = obj.GetType().GetProperty(propertyName);
                return prop?.GetValue(obj)?.ToString();
            }
            catch { return null; }
        }

        // ==================== ПОЛУЧЕНИЕ СПИСКА РАСШИРЕНИЙ ====================

        /// <summary>
        /// Возвращает уникальные расширения файлов из коллекции (для фильтра).
        /// </summary>
        public static List<string> GetExtensions<T>(IEnumerable<T> items)
        {
            var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "Все файлы"
            };

            try
            {
                foreach (var item in items)
                {
                    if (item == null) continue;

                    var name = GetPropertyValue(item, "Name");
                    if (string.IsNullOrEmpty(name)) continue;

                    string ext = Path.GetExtension(name);
                    if (!string.IsNullOrEmpty(ext))
                        set.Add(ext.ToLowerInvariant());
                }
            }
            catch { }

            return set.OrderBy(x => x == "Все файлы" ? "" : x).ToList();
        }

        // ==================== ЭКСПОРТ В CSV ====================

        /// <summary>
        /// Экспортирует любую коллекцию в CSV-файл.
        /// Автоматически определяет колонки по публичным свойствам.
        /// </summary>
        public static string ExportToCsv<T>(IEnumerable<T> items, string tableName)
        {
            string dir = ReportExporter.GetReportsDir();
            string file = Path.Combine(dir,
                $"table_{tableName}_{DateTime.Now:yyyy-MM-dd_HH-mm-ss}.csv");

            var sb = new StringBuilder();

            // Заголовки
            var props = typeof(T).GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .Where(p => p.CanRead && IsSimpleType(p.PropertyType))
                .ToList();

            sb.AppendLine(string.Join(";", props.Select(p => EscapeCsv(p.Name))));

            // Данные
            foreach (var item in items)
            {
                var row = new List<string>();
                foreach (var p in props)
                {
                    try
                    {
                        var val = p.GetValue(item)?.ToString() ?? "";
                        row.Add(EscapeCsv(val));
                    }
                    catch
                    {
                        row.Add("");
                    }
                }
                sb.AppendLine(string.Join(";", row));
            }

            File.WriteAllText(file, sb.ToString(), Encoding.UTF8);
            return file;
        }

        private static bool IsSimpleType(Type t)
        {
            return t.IsPrimitive
                || t == typeof(string)
                || t == typeof(decimal)
                || t == typeof(DateTime)
                || t == typeof(DateTime?)
                || (t.IsValueType && !t.IsGenericType);
        }

        private static string EscapeCsv(string value)
        {
            if (string.IsNullOrEmpty(value)) return "";

            // Экранируем кавычки
            value = value.Replace("\"", "\"\"");
            return $"\"{value}\"";
        }

        // ==================== ПОЛУЧЕНИЕ КЛЮЧЕВОГО ПОЛЯ ====================

        /// <summary>
        /// Определяет ключевое поле для сохранения выделения в зависимости от типа модели.
        /// </summary>
        public static string GetKeyFieldForType<T>()
        {
            var type = typeof(T).Name;

            return type switch
            {
                nameof(SystemFileItem) => "FullPath",
                nameof(DuplicateItem) => "FullPath",
                nameof(ProcessItem) => "Id",
                nameof(StartupItem) => "Name",
                nameof(LockedFileItem) => "FilePath",
                nameof(ProgramItem) => "RegistryKeyPath",
                _ => "Name"
            };
        }
    }
}