using DetailsInfo.Data;
using DetailsInfo.Properties;
using DetailsInfo.Utils;
using Microsoft.Win32;
using Ookii.Dialogs.Wpf;
using System;
using System.Diagnostics;
using System.Windows;

namespace DetailsInfo
{
    /// <summary>
    /// Логика взаимодействия для SettingsWindow.xaml
    /// </summary>
    public partial class SettingsWindow : Window
    {
        public SettingsWindow()
        {
            InitializeComponent();
        }

        private bool _tabtipStatus;

        // диалоги
        private readonly OpenFileDialog _tableDialog = new OpenFileDialog();
        private readonly VistaFolderBrowserDialog _archiveDialog = new VistaFolderBrowserDialog();
        private readonly VistaFolderBrowserDialog _tempDialog = new VistaFolderBrowserDialog();
        private readonly VistaFolderBrowserDialog _machineDialog = new VistaFolderBrowserDialog();
        private readonly VistaFolderBrowserDialog _netLogPathDialog = new VistaFolderBrowserDialog();


        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            _tableDialog.FileName = Settings.Default.tablePath;
            fileNameTextBox.Text = Settings.Default.tablePath;

            fileEncodingTextBox.Text = Settings.Default.fileEncoding.ToString();

            _archiveDialog.SelectedPath = Settings.Default.archivePath;
            archiveTextBox.Text = Settings.Default.archivePath;

            _tempDialog.SelectedPath = Settings.Default.tempPath;
            tempTextBox.Text = Settings.Default.tempPath;

            _machineDialog.SelectedPath = Settings.Default.machinePath;
            machineTextBox.Text = Settings.Default.machinePath;

            _netLogPathDialog.SelectedPath = Settings.Default.netLogPath;
            netLogPathTextBox.Text = Settings.Default.netLogPath;

            refreshSlider.Value = Settings.Default.refreshInterval;

            autoRenameCheckBox.IsChecked = Settings.Default.autoRenameToMachine;

            emailLoginTextBox.Text = Settings.Default.emailLogin;
            emailPassTextBox.Password = Settings.Default.emailPass;

            serverTextBox.Text = Settings.Default.popServer;
            portTextBox.Text = Settings.Default.popPort.ToString();
            useSslCheckBox.IsChecked = Settings.Default.useSsl;

            Topmost = Settings.Default.topMost;
        }

        // обзор CSV таблички
        private void fileSetButton_Click(object sender, RoutedEventArgs e)
        {
            _tableDialog.Filter = "CSV Файл (*.CSV)|*.CSV|" +
                                "All files (*.*)|*.*";
            if (_tableDialog.ShowDialog() == true)
            {
                fileNameTextBox.Text = _tableDialog.FileName;
                statusBarTextBox.Text = "Файл изменен";
            }
            else
            {
                statusBarTextBox.Text = "Выбор файла отменен";
            }
        }

        // открывает ссылку на сайт MS
        private void encodingLinkButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                Process.Start(new ProcessStartInfo(Settings.Default.encodingsLink) { UseShellExecute = true });
                if (Settings.Default.topMost)
                {
                    statusBarTextBox.Text += "На заднем плане запущен браузер со списком кодировок";
                }
                else
                {
                    statusBarTextBox.Text += "Запущен браузер со списком кодировок";
                }
            }
            catch (Exception exception)
            {
                MessageBox.Show(exception.Message);
            }
        }

        private void archiveButton_Click(object sender, RoutedEventArgs e)
        {
            if (_archiveDialog.ShowDialog() == true)
            {
                archiveTextBox.Text = _archiveDialog.SelectedPath;
                statusBarTextBox.Text = "Путь к архиву изменен";
            }
            else
            {
                statusBarTextBox.Text = "Выбор директории отменен";
            }
        }

        private void tempButton_Click(object sender, RoutedEventArgs e)
        {
            if (_tempDialog.ShowDialog() == true)
            {
                tempTextBox.Text = _tempDialog.SelectedPath;
                statusBarTextBox.Text = "Путь к промежуточной папке изменен";
            }
            else
            {
                statusBarTextBox.Text = "Выбор директории отменен";
            }
        }

        private void machineButton_Click(object sender, RoutedEventArgs e)
        {
            if (_machineDialog.ShowDialog() == true)
            {
                machineTextBox.Text = _machineDialog.SelectedPath;
                statusBarTextBox.Text = "Путь к сетевой папке станка изменен";
            }
            else
            {
                statusBarTextBox.Text = "Выбор директории отменен";
            }
        }

        private void netLogPathButton_Click(object sender, RoutedEventArgs e)
        {
            if (_netLogPathDialog.ShowDialog() == true)
            {
                netLogPathTextBox.Text = _netLogPathDialog.SelectedPath;
                statusBarTextBox.Text = "DNC путь логгирования изменен";
            }
            else
            {
                statusBarTextBox.Text = "Выбор DNC пути логгирования отменен";
            }
        }

        private void closeButton_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        private void applyButton_Click(object sender, RoutedEventArgs e)
        {
            Settings.Default.tablePath = fileNameTextBox.Text;
            Settings.Default.archivePath = archiveTextBox.Text;
            Settings.Default.tempPath = tempTextBox.Text;
            Settings.Default.machinePath = machineTextBox.Text;
            Settings.Default.netLogPath = netLogPathTextBox.Text;
            Settings.Default.refreshInterval = (int)refreshSlider.Value;
            Settings.Default.autoRenameToMachine = (bool)autoRenameCheckBox.IsChecked;
            Settings.Default.emailLogin = emailLoginTextBox.Text;
            Settings.Default.emailPass = emailPassTextBox.Password;
            Settings.Default.popServer = serverTextBox.Text;
            Settings.Default.useSsl = (bool)useSslCheckBox.IsChecked;
            int encoding, port;
            if (int.TryParse(fileEncodingTextBox.Text, out encoding) && int.TryParse(portTextBox.Text, out port))
            {
                Settings.Default.popPort = port;
                Settings.Default.fileEncoding = encoding;
                Settings.Default.needUpdate = true;
                Settings.Default.Save();
                Reader.WriteConfig();
                Close();
            }
            else if (!int.TryParse(fileEncodingTextBox.Text, out encoding) && int.TryParse(portTextBox.Text, out port))
            {
                statusBarTextBox.Text = "Неверно указана кодировка";
            }
            else if (int.TryParse(fileEncodingTextBox.Text, out encoding) && !int.TryParse(portTextBox.Text, out port))
            {
                statusBarTextBox.Text = "Неверно указан порт";
            }
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
    }
}
