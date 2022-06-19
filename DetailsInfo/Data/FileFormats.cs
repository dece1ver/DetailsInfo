using MaterialDesignThemes.Wpf;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace DetailsInfo.Data
{
    public static class FileFormats
    {
        public static int IconSize => 44;
        public static string[] MazatrolExtensions { get; } = { ".pbg", ".pbd", ".eia" };
        public static string[] HeidenhainExtensions { get; } = { ".h" };
        public static string[] SinumerikExtensions { get; } = { ".mpf", ".spf" };
        public static string[] OtherNcExtensions { get; } = { ".nc", ".tap" };
        public static string[] ImageExtensions { get; set; } = { ".jpg", ".jpeg", ".png", ".gif", ".svg", ".tiff", ".tif", ".bmp", ".dib", ".dxf" };
        public static string[] VideoExtensions { get; set; } = { ".mp4", ".mpeg", ".wmv", ".webm", ".mkv" };
        public static string[] DocumentExtensions { get; set; } = { ".pdf", ".xml", ".doc", ".docx", ".docm", ".txt", ".rtf" };
        public static string[] MiscExtensions { get; set; } = { ".sys", ".tmp", ".lnk", ".rdp", ".ini", ".exe", ".bat", ".cmd", ".com", ".ezd" };
        public static string[] SystemFiles { get; set; } = { "Thumbs.db" };

        private static string[] _machineExtensions = Array.Empty<string>();

        public static string[] MachineExtensions
        {
            get
            {
                _machineExtensions = _machineExtensions
                    .Concat(HeidenhainExtensions)
                    .Concat(MazatrolExtensions)
                    .Concat(SinumerikExtensions)
                    .ToArray();
                return _machineExtensions;
            }
        }

        private static string[] _infoExtensions = Array.Empty<string>();

        public static string[] InfoExtensions
        {
            get
            {
                _infoExtensions = _infoExtensions
                    .Concat(DocumentExtensions)
                    .Concat(ImageExtensions)
                    .Concat(VideoExtensions)
                    .ToArray();
                return _infoExtensions;
            }
        }

        private static string[] _nonTransferableExtensions = Array.Empty<string>();

        public static string[] NonTransferableExtensions
        {
            get
            {
                _nonTransferableExtensions = _nonTransferableExtensions
                    .Concat(InfoExtensions)
                    .Concat(MiscExtensions)
                    .ToArray();
                return _nonTransferableExtensions;
            }
        }

        public static HashSet<string> NonTransferableHash = new(NonTransferableExtensions);

        public static PackIcon SetIcon(string file)
        {
            PackIconKind iconKind;
            if (Directory.Exists(file))
            {
                iconKind = PackIconKind.Folder;
            }
            // Mazatrol Smart
            else if (MazatrolExtensions.Contains(Path.GetExtension(file).ToLower()))
            {
                iconKind = PackIconKind.AlphaM;
            }
            // Sinumerik
            else if (SinumerikExtensions.Contains(Path.GetExtension(file).ToLower()))
            {
                iconKind = PackIconKind.AlphaS;
            }
            // Heidenhain
            else if (HeidenhainExtensions.Contains(Path.GetExtension(file).ToLower()))
            {
                iconKind = PackIconKind.AlphaH;
            }
            // Документы
            else if (DocumentExtensions.Contains(Path.GetExtension(file).ToLower()))
            {
                iconKind = PackIconKind.FileDocument;
            }
            // Картинки
            else if (ImageExtensions.Contains(Path.GetExtension(file).ToLower()))
            {
                iconKind = PackIconKind.Image;
            }
            // Видосики
            else if (VideoExtensions.Contains(Path.GetExtension(file).ToLower()))
            {
                iconKind = PackIconKind.FileVideo;
            }
            else
            {
                iconKind = PackIconKind.Text;
            }
            return new PackIcon { Kind = iconKind, Height = IconSize, Width = IconSize };
        }
    }
}
