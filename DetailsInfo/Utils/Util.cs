using System.Windows.Media;

namespace DetailsInfo.Utils
{
    public static class Util
    {
        public static SolidColorBrush BrushFromHex(string hexColorString)
        {
            return (SolidColorBrush)new BrushConverter().ConvertFrom(hexColorString);
        }
    }
}
