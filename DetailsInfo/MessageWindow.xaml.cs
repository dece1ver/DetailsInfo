using DetailsInfo.Utils;
using System;
using MailKit.Net.Smtp;
using MailKit;
using MimeKit;
using System.Windows;
using DetailsInfo.Properties;
using System.Threading.Tasks;
using DetailsInfo.Data;

namespace DetailsInfo
{
    /// <summary>
    /// Логика взаимодействия для MessageWindow.xaml
    /// </summary>
    public partial class MessageWindow : Window
    {
        private bool _tabtipStatus;

        public MessageWindow()
        {
            InitializeComponent();
        }

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {

            emailLoginTextBox.Text = Settings.Default.emailLogin;
            emailPassTextBox.Password = Settings.Default.emailPass;

            toTextBox.Text = Settings.Default.toAdress;
            fromTextBox.Text = Settings.Default.fromAdress;

            serverTextBox.Text = Settings.Default.smtpServer;
            portTextBox.Text = Settings.Default.smtpPort.ToString();

        }

        private void closeButton_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        private void applyButton_Click(object sender, RoutedEventArgs e)
        {
            _ = SendMessage();
        }

        private void tabtipButton_Click(object sender, RoutedEventArgs e)
        {
            if (_tabtipStatus)
            {
                WindowsUtils.KillTabTip(0);
                _tabtipStatus = false;
            }
            else
            {

                WindowsUtils.RunTabTip();
                _tabtipStatus = true;
            }
        }

        private async Task SendMessage()
        {
            Settings.Default.emailLogin = emailLoginTextBox.Text;
            Settings.Default.emailPass = emailPassTextBox.Password;

            Settings.Default.toAdress = toTextBox.Text;
            Settings.Default.fromAdress = fromTextBox.Text;

            Settings.Default.smtpServer = serverTextBox.Text;

            if (int.TryParse(portTextBox.Text, out int port))
            {
                Settings.Default.smtpPort = port;
            }
            Settings.Default.Save();
            Reader.WriteConfig();

            var message = new MimeMessage();
            message.From.Add(new MailboxAddress(Environment.MachineName, Settings.Default.fromAdress));
            message.To.Add(new MailboxAddress(string.Empty, Settings.Default.toAdress));
            message.Subject = $"Сообщение с {Environment.MachineName}";

            message.Body = new TextPart("plain")
            {
                Text = messageTextBox.Text
            };
            messageTextBox.Visibility = Visibility.Collapsed;
            progressBar.Visibility = Visibility.Visible;
            statusBarTextBox.Dispatcher.Invoke(() => statusBarTextBox.Text = string.Empty);
            await Task.Run(() =>
            {
                try
                {
                    using (var client = new SmtpClient())
                    {
                        client.ServerCertificateValidationCallback = (s, c, h, e) => true;
                        client.Connect(Settings.Default.smtpServer, Settings.Default.smtpPort, false);
                        client.Authenticate(Settings.Default.emailLogin, Settings.Default.emailPass);
                        client.Send(message);
                        client.Disconnect(true);
                    }
                    progressBar.Dispatcher.Invoke(() => progressBar.Visibility = Visibility.Collapsed);
                    messageTextBox.Dispatcher.Invoke(() => messageTextBox.Visibility = Visibility.Visible);
                    Dispatcher.Invoke(() => Close());
                }
                catch (Exception ex)
                {
                    progressBar.Dispatcher.Invoke(() => progressBar.Visibility = Visibility.Collapsed);
                    messageTextBox.Dispatcher.Invoke(() => messageTextBox.Visibility = Visibility.Visible);
                    statusBarTextBox.Dispatcher.Invoke(() => statusBarTextBox.Text = ex.Message);
                    //MessageBox.Show($"Исключение: {ex.GetType()}\n" +
                    //    $"Сообщение: {ex.Message}\n");
                }
            });
        }
    }
}
