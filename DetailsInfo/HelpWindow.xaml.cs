using DetailsInfo.Properties;
using System.Reflection;
using System.Windows;

namespace DetailsInfo
{
    /// <summary>
    /// Логика взаимодействия для HelpWindow.xaml
    /// </summary>
    public partial class HelpWindow : Window
    {
        private readonly string _exceptions;
        public string Version { get; set; } = $"ver. {Assembly.GetExecutingAssembly().GetName().Version}";

        public HelpWindow(string errors)
        {
            InitializeComponent();
            _exceptions = errors;
        }


        private void applyButton_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            if (_exceptions != string.Empty)
            {
                infoTextBox.Text += _exceptions;
            }
            else
            {
                infoTextBox.Text = "Все норм.";
            }
            Topmost = Settings.Default.topMost;
        }

        private void updateButton_Click(object sender, RoutedEventArgs e)
        {

        }
    }
}
