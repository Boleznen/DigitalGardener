using System;
using System.Collections.Concurrent;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace DigitalGardener
{
    public static class FileIconHelper
    {
        // Кэш иконок по расширению — чтобы не дёргать Windows API каждый раз
        private static readonly ConcurrentDictionary<string, ImageSource?> _iconCache = new();

        // Иконка по умолчанию (если ничего не нашли)
        private static ImageSource? _defaultFileIcon;

        #region WinAPI

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
        private struct SHFILEINFO
        {
            public IntPtr hIcon;
            public int iIcon;
            public uint dwAttributes;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
            public string szDisplayName;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)]
            public string szTypeName;
        }

        [DllImport("shell32.dll", CharSet = CharSet.Auto)]
        private static extern IntPtr SHGetFileInfo(
            string pszPath,
            uint dwFileAttributes,
            ref SHFILEINFO psfi,
            uint cbFileInfo,
            uint uFlags);

        [DllImport("user32.dll")]
        private static extern bool DestroyIcon(IntPtr hIcon);

        private const uint SHGFI_ICON = 0x000000100;
        private const uint SHGFI_SMALLICON = 0x000000001;
        private const uint SHGFI_LARGEICON = 0x000000000;
        private const uint SHGFI_USEFILEATTRIBUTES = 0x000000010;
        private const uint FILE_ATTRIBUTE_NORMAL = 0x00000080;

        #endregion

        /// <summary>
        /// Возвращает иконку для файла по его расширению. Кэширует результат.
        /// Возвращает null, если иконку получить не удалось.
        /// </summary>
        public static ImageSource? GetIconForExtension(string? extension)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(extension))
                    return GetDefaultFileIcon();

                extension = extension.ToLowerInvariant();
                if (!extension.StartsWith("."))
                    extension = "." + extension;

                // Проверяем кэш
                if (_iconCache.TryGetValue(extension, out var cached))
                    return cached;

                // Запрашиваем иконку у Windows
                var shinfo = new SHFILEINFO();
                IntPtr result = SHGetFileInfo(
                    "file" + extension,
                    FILE_ATTRIBUTE_NORMAL,
                    ref shinfo,
                    (uint)Marshal.SizeOf(shinfo),
                    SHGFI_ICON | SHGFI_SMALLICON | SHGFI_USEFILEATTRIBUTES);

                if (result == IntPtr.Zero || shinfo.hIcon == IntPtr.Zero)
                {
                    _iconCache[extension] = null;
                    return null;
                }

                ImageSource? icon = IconToImageSource(shinfo.hIcon);
                DestroyIcon(shinfo.hIcon);

                _iconCache[extension] = icon;
                return icon;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// Возвращает иконку для конкретного файла (полный путь).
        /// Использует реальный файл, поэтому иконка точнее (например, для .exe).
        /// </summary>
        public static ImageSource? GetIconForFile(string? fullPath)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(fullPath))
                    return GetDefaultFileIcon();

                // Если файла нет — используем иконку по расширению
                if (!File.Exists(fullPath))
                    return GetIconForExtension(Path.GetExtension(fullPath));

                var shinfo = new SHFILEINFO();
                IntPtr result = SHGetFileInfo(
                    fullPath,
                    0,
                    ref shinfo,
                    (uint)Marshal.SizeOf(shinfo),
                    SHGFI_ICON | SHGFI_SMALLICON);

                if (result == IntPtr.Zero || shinfo.hIcon == IntPtr.Zero)
                    return GetIconForExtension(Path.GetExtension(fullPath));

                ImageSource? icon = IconToImageSource(shinfo.hIcon);
                DestroyIcon(shinfo.hIcon);
                return icon;
            }
            catch
            {
                return GetDefaultFileIcon();
            }
        }

        /// <summary>Иконка по умолчанию для неизвестных файлов.</summary>
        public static ImageSource? GetDefaultFileIcon()
        {
            if (_defaultFileIcon != null) return _defaultFileIcon;

            try
            {
                var shinfo = new SHFILEINFO();
                IntPtr result = SHGetFileInfo(
                    "unknown.xyz",
                    FILE_ATTRIBUTE_NORMAL,
                    ref shinfo,
                    (uint)Marshal.SizeOf(shinfo),
                    SHGFI_ICON | SHGFI_SMALLICON | SHGFI_USEFILEATTRIBUTES);

                if (result != IntPtr.Zero && shinfo.hIcon != IntPtr.Zero)
                {
                    _defaultFileIcon = IconToImageSource(shinfo.hIcon);
                    DestroyIcon(shinfo.hIcon);
                }
            }
            catch { }

            return _defaultFileIcon;
        }

        /// <summary>Иконка папки.</summary>
        public static ImageSource? GetFolderIcon()
        {
            try
            {
                var shinfo = new SHFILEINFO();
                IntPtr result = SHGetFileInfo(
                    @"C:\",
                    0,
                    ref shinfo,
                    (uint)Marshal.SizeOf(shinfo),
                    SHGFI_ICON | SHGFI_SMALLICON);

                if (result == IntPtr.Zero || shinfo.hIcon == IntPtr.Zero)
                    return GetDefaultFileIcon();

                var icon = IconToImageSource(shinfo.hIcon);
                DestroyIcon(shinfo.hIcon);
                return icon;
            }
            catch
            {
                return GetDefaultFileIcon();
            }
        }

        /// <summary>HICON (WinAPI) → ImageSource (WPF).</summary>
        private static ImageSource? IconToImageSource(IntPtr hIcon)
        {
            try
            {
                var icon = Icon.FromHandle(hIcon);
                var bitmap = icon.ToBitmap();

                IntPtr hBitmap = bitmap.GetHbitmap();
                try
                {
                    var imageSource = Imaging.CreateBitmapSourceFromHBitmap(
                        hBitmap,
                        IntPtr.Zero,
                        Int32Rect.Empty,
                        BitmapSizeOptions.FromEmptyOptions());

                    // Замораживаем, чтобы можно было использовать в разных потоках
                    imageSource.Freeze();
                    return imageSource;
                }
                finally
                {
                    DeleteObject(hBitmap);
                }
            }
            catch
            {
                return null;
            }
        }

        [DllImport("gdi32.dll")]
        private static extern bool DeleteObject(IntPtr hObject);

        /// <summary>Очистить кэш (если нужно перезагрузить иконки).</summary>
        public static void ClearCache()
        {
            _iconCache.Clear();
            _defaultFileIcon = null;
        }
    }
}