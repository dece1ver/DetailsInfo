using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Forms;
using UserControl = System.Windows.Controls.UserControl;

namespace DetailsInfo
{
    /// <summary>
    /// Логика взаимодействия для NumericKeyboardControl.xaml
    /// </summary>
    public partial class DateTimeKeyboardControl : UserControl
    {
        public DateTimeKeyboardControl()
        {
            InitializeComponent();
        }

        [DllImport("user32.dll")]
        private static extern void keybd_event(byte bVk, byte bScan, int dwFlags, int dwExtraInfo);

        private const int KeyeventfExtendedkey = 1;
        private const int KeyeventfKeyup = 2;

        public static new void KeyDown(Keys vKey)
        {
            keybd_event((byte)vKey, 0, KeyeventfExtendedkey, 0);
        }

        public static new void KeyUp(Keys vKey)
        {
            keybd_event((byte)vKey, 0, KeyeventfExtendedkey | KeyeventfKeyup, 0);
        }

        public static void KeyPress(Keys vKey)
        {
            KeyDown(vKey);
            KeyUp(vKey);
        }

        private void button7_Click(object sender, RoutedEventArgs e)
        {
            KeyPress(Keys.D7);
        }

        private void button8_Click(object sender, RoutedEventArgs e)
        {
            KeyPress(Keys.D8);
        }

        private void button9_Click(object sender, RoutedEventArgs e)
        {
            KeyPress(Keys.D9);
        }

        private void button4_Click(object sender, RoutedEventArgs e)
        {
            KeyPress(Keys.D4);
        }

        private void button5_Click(object sender, RoutedEventArgs e)
        {
            KeyPress(Keys.D5);
        }

        private void button6_Click(object sender, RoutedEventArgs e)
        {
            KeyPress(Keys.D6);
        }

        private void button1_Click(object sender, RoutedEventArgs e)
        {
            KeyPress(Keys.D1);
        }

        private void button2_Click(object sender, RoutedEventArgs e)
        {
            KeyPress(Keys.D2);
        }

        private void button3_Click(object sender, RoutedEventArgs e)
        {
            KeyPress(Keys.D3);
        }

        private void button0_Click(object sender, RoutedEventArgs e)
        {
            KeyPress(Keys.D0);
        }

        private void buttonDot_Click(object sender, RoutedEventArgs e)
        {
            if (InputLanguage.CurrentInputLanguage.Culture.IetfLanguageTag == "en-US")
            {
                KeyPress(Keys.OemPeriod);
            }
            else
            {
                KeyPress(Keys.Oem2);
            }
        }

        private void buttonYes_Click(object sender, RoutedEventArgs e)
        {
            KeyPress(Keys.Enter);
        }

        private void buttonNo_Click(object sender, RoutedEventArgs e)
        {
            KeyPress(Keys.Escape);
        }

        private void buttonBackspace_Click(object sender, RoutedEventArgs e)
        {
            KeyPress(Keys.Back);
        }

        private void buttonPlus_Click(object sender, RoutedEventArgs e)
        {
            KeyPress(Keys.Oemplus);
        }

        private void buttonColon_Click(object sender, RoutedEventArgs e)
        {
            if (InputLanguage.CurrentInputLanguage.Culture.IetfLanguageTag == "en-US")
            {
                KeyDown(Keys.LShiftKey);
                KeyPress(Keys.Oem1);
                KeyUp(Keys.LShiftKey);
            }
            else
            {
                KeyDown(Keys.LShiftKey);
                KeyPress(Keys.D6);
                KeyUp(Keys.LShiftKey);
            }
        }

        private void buttonDel_Click(object sender, RoutedEventArgs e)
        {
            KeyPress(Keys.Delete);
        }

        private void buttonClear_Click(object sender, RoutedEventArgs e)
        {
            KeyDown(Keys.LControlKey);
            KeyPress(Keys.A);
            KeyUp(Keys.LControlKey);
            KeyPress(Keys.Delete);
        }
    }
}
