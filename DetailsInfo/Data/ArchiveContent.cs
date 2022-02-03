using MaterialDesignThemes.Wpf;
using System.IO;
using System.Linq;
using System.Windows;
using static DetailsInfo.Data.FileFormats;

namespace DetailsInfo
{
    public struct ArchiveContent
    {
        public string Content { get; set; }
        public Visibility TransferButtonState { get; set; }
        public Visibility OpenButtonState { get; set; }
        public Visibility OpenFolderState { get; set; }
        public Visibility DeleteButtonState { get; set; }
        public bool CanBeTransfered => !NonTransferableExtensions.Contains(Path.GetExtension(Content).ToLower());
        public PackIcon Icon => SetIcon(Content);
    }
}
