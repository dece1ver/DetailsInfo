using MaterialDesignThemes.Wpf;
using System.IO;
using System.Linq;
using System.Windows;
using static DetailsInfo.Data.FileFormats;

namespace DetailsInfo.Data
{
    internal struct NcFile
    {
        public string FullPath { get; set; }
        public readonly string FileName => Path.GetFileName(FullPath);
        public readonly string Extension => MachineExtensions.Contains(value: Path.GetExtension(FullPath)?.ToLower()) ? Path.GetExtension(FullPath) : string.Empty;
        public Visibility OpenButtonVisibility { get; set; }
        public Visibility RenameButtonVisibility { get; set; }
        public Visibility CheckButtonVisibility { get; set; }
        public Visibility DeleteButtonVisibility { get; set; }
        public Visibility AnalyzeButtonVisibility { get; set; }
        public readonly PackIcon Icon => SetIcon(FullPath);


        public string NcName
        {
            get
            {
                // Mazatrol Smart
                if (MazatrolExtensions.Contains(Path.GetExtension(FullPath)?.ToLower()))
                {
                    return Reader.GetMazatrolSmartName(FullPath);
                }

                return Reader.GetFanucName(FullPath);
            }
        }
    }
}
