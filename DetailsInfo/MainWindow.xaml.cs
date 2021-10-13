using DetailsInfo.Data;
using DetailsInfo.Properties;
using DetailsInfo.Utils;
using MailKit.Net.Pop3;
using Microsoft.VisualBasic;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using Application = System.Windows.Application;
using ListView = System.Windows.Controls.ListView;

namespace DetailsInfo
{
    /// TODO splash screen?

    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        private readonly bool debugMode = false;

        #region Поля
        private int _errors;
        private string _errorsList = string.Empty;
        private BindingList<ToolNote> _toolNotes;
        private List<ArchiveContent> _archiveContent;
        private List<NcFile> _machineContent = new();
        private string _currentArchiveFolder;

        private bool _archiveStatus = true;
        private bool needUpdateArchive;

        private bool _machineStatus = true;

        private bool _tempFolderStatus = true;

        private bool _transferFromArchive;
        private bool _transferFromMachine;
        private bool _renameOnMachine;
        private bool _deleteFromMachine;
        private bool _openFromArchive;
        private bool _openFromNcFolder;
        private bool _analyzeNcProgram;

        private bool _tabtipStatus;
        private bool _errorStatus;
        private enum FindStatus { DontNeed, Find, Finded }
        private FindStatus _findStatus = FindStatus.DontNeed;
        private string[] _findResult;
        private string _selectedArchiveFile;
        private string _selectedMachineFile;

        private readonly SolidColorBrush redBrush = Util.BrushFromHex("#FFF44336");
        private readonly SolidColorBrush greenBrush = Util.BrushFromHex("#FFAEEA00");
        #endregion

        private readonly List<string> _status = new();

        #region Статусы ошибок
        private readonly string _noArchiveLabel = "Архив недоступен. ";
        private readonly string _noMachineLabel = "Сетевая папка станка недоступна. ";
        private readonly string _noTempFolderLabel = "Промежуточная папка недоступна. ";
        private readonly string _noTableLabel = "Таблица недоступна. ";
        private readonly string _FindInProcess = "Архив заблокирован на время поиска. ";
        private readonly string _authenticationException = "Ошибка авторизации на сервере уведомлений. ";
        private readonly string _timeoutException = "Время ожидания подключения к серверу уведомлений истекло. ";
        private readonly string _pop3ProtocolException = "Ошибка протокола POP3. ";
        private readonly string _sslHandshakeException = "Неудачная проверка сертификата SSL. ";
        private readonly string _socketException = "Ошибка соединения с сервером уведомлений. ";
        #endregion


        public MainWindow()
        {
            InitializeComponent();
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        }


        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            if (debugMode) AddStatus(Reader.ReadConfig()); else Reader.ReadConfig();

            Topmost = Settings.Default.topMost;
            _currentArchiveFolder = Settings.Default.archivePath;
            archivePathTB.Text = _currentArchiveFolder;

            _ = LoadInfoAsync(start: true);

            try
            {
                _toolNotes = Reader.LoadTools();
                if (_toolNotes == null)
                    _toolNotes = new BindingList<ToolNote>();
            }
            catch (Exception exception)
            {
                AddError(exception);
            }

            notesDG.ItemsSource = _toolNotes;
            _toolNotes.ListChanged += ToolNotes_ListChanged;

            
        }

        private async Task LoadInfoAsync(bool start = false)
        {
            await Task.Run(() =>
            {
                if (!Reader.CheckPath(Settings.Default.archivePath)) TurnOffArchive();
                if (_archiveStatus) archiveConnectionIcon.Dispatcher.Invoke(() => archiveConnectionIcon.Foreground = greenBrush);
                LoadArchive();

                if (!Reader.CheckPath(Settings.Default.machinePath)) TurnOffMachine();
                if (_machineStatus) machineConnectionIcon.Dispatcher.Invoke(() => machineConnectionIcon.Foreground = greenBrush);
                LoadMachine();

                if (!Reader.CheckPath(Settings.Default.tempPath)) _tempFolderStatus = false;
                if (_tempFolderStatus) machineConnectionIcon.Dispatcher.Invoke(() => machineConnectionIcon.Foreground = greenBrush);

                RefreshStatus();
                if (start)
                {
                    Thread loadInfoThread = new(WatchInfo);
                    loadInfoThread.IsBackground = true;
                    loadInfoThread.Start();

                    Thread notifyThread = new(WatchMessages);
                    notifyThread.IsBackground = true;
                    notifyThread.Start();
                }
            });
        }

        private static void WriteLog(string message) => _ = Reader.WriteLogAsync(message);

        // событие при изменении инструмента
        private void ToolNotes_ListChanged(object sender, ListChangedEventArgs e)
        {
            if (e.ListChangedType == ListChangedType.ItemAdded || e.ListChangedType == ListChangedType.ItemDeleted || e.ListChangedType == ListChangedType.ItemChanged)
            {
                try
                {
                    Reader.SaveTools(sender);
                }
                catch (Exception exception)
                {
                    WriteLog($"Ошибка при сохранении списка инструмента.");
                    AddError(exception);
                }
            }
        }

        #region Уведомления
        private void WatchMessages()
        {
            emailConnectionIcon.Dispatcher.Invoke(() => emailConnectionIcon.Kind = MaterialDesignThemes.Wpf.PackIconKind.EmailOff);
            emailConnectionIcon.Dispatcher.Invoke(() => emailConnectionIcon.Foreground = redBrush);
            Pop3Client client = new();
            client.ServerCertificateValidationCallback = (s, c, h, e) => true;
            try
            {
                client.Connect(Settings.Default.popServer, Settings.Default.popPort, Settings.Default.useSsl);
                client.Authenticate(Settings.Default.emailLogin, Settings.Default.emailPass);
                client.DeleteAllMessages();
                client.Disconnect(true);

            }
            catch
            {

            }
            while (!string.IsNullOrEmpty(Settings.Default.emailLogin) &&
                !string.IsNullOrEmpty(Settings.Default.emailPass) &&
                !string.IsNullOrEmpty(Settings.Default.popServer) &&
                Settings.Default.popPort > 0)
            {
                try
                {
                    if (client.IsConnected)
                    {
                        client.Disconnect(true);
                    }
                    client.Connect(Settings.Default.popServer, Settings.Default.popPort, Settings.Default.useSsl);
                    if (debugMode) RemoveStatus(_timeoutException);
                    client.Authenticate(Settings.Default.emailLogin, Settings.Default.emailPass);

                    if (debugMode) AddStatus($"Connected: {client.IsConnected} ");
                    if (debugMode) AddStatus($"Auth: {client.IsAuthenticated} ");

                    if (client.IsConnected)
                    {
                        emailConnectionIcon.Dispatcher.Invoke(() => emailConnectionIcon.Kind = MaterialDesignThemes.Wpf.PackIconKind.Email);
                        emailConnectionIcon.Dispatcher.Invoke(() => emailConnectionIcon.Foreground = greenBrush);
                        if (debugMode) RemoveStatus(_authenticationException);
                        if (debugMode) RemoveStatus(_pop3ProtocolException);
                        if (debugMode) RemoveStatus(_sslHandshakeException);
                        if (debugMode) RemoveStatus(_socketException);
                        int currentMessagesCount = client.GetMessageCount();
                        if (debugMode) AddStatus($"Messages: {currentMessagesCount} ");
                        if (currentMessagesCount > 0)
                        {
                            var message = client.GetMessage(currentMessagesCount - 1);
                            if (ValidateEmailNotify(message))
                            {
                                Application.Current.Dispatcher.InvokeAsync(() => { emailMessageTextBox.Text = message.Subject; });
                                client.DeleteAllMessages();
                                client.Disconnect(true);
                                Application.Current.Dispatcher.InvokeAsync(() => { messageDialog.IsOpen = true; });
                            }
                        }
                    }
                }
                catch (MailKit.Security.AuthenticationException)
                {
                    if (debugMode) AddStatus(_authenticationException);
                    emailConnectionIcon.Dispatcher.Invoke(() => emailConnectionIcon.Kind = MaterialDesignThemes.Wpf.PackIconKind.EmailOff);
                    emailConnectionIcon.Dispatcher.Invoke(() => emailConnectionIcon.Foreground = redBrush);
                }
                catch (TimeoutException)
                {
                    if (debugMode) AddStatus(_timeoutException);
                    emailConnectionIcon.Dispatcher.Invoke(() => emailConnectionIcon.Kind = MaterialDesignThemes.Wpf.PackIconKind.EmailOff);
                    emailConnectionIcon.Dispatcher.Invoke(() => emailConnectionIcon.Foreground = redBrush);
                }
                catch (Pop3ProtocolException ex)
                {
                    if (debugMode) AddStatus(_pop3ProtocolException);
                    emailConnectionIcon.Dispatcher.Invoke(() => emailConnectionIcon.Kind = MaterialDesignThemes.Wpf.PackIconKind.EmailOff);
                    emailConnectionIcon.Dispatcher.Invoke(() => emailConnectionIcon.Foreground = redBrush);
                }
                catch (MailKit.Security.SslHandshakeException ex)
                {
                    if (debugMode) AddStatus(_sslHandshakeException);
                    emailConnectionIcon.Dispatcher.Invoke(() => emailConnectionIcon.Kind = MaterialDesignThemes.Wpf.PackIconKind.EmailOff);
                    emailConnectionIcon.Dispatcher.Invoke(() => emailConnectionIcon.Foreground = redBrush);
                }
                catch (System.Net.Sockets.SocketException)
                {
                    if (debugMode) AddStatus(_socketException);
                    emailConnectionIcon.Dispatcher.Invoke(() => emailConnectionIcon.Kind = MaterialDesignThemes.Wpf.PackIconKind.EmailOff);
                    emailConnectionIcon.Dispatcher.Invoke(() => emailConnectionIcon.Foreground = redBrush);
                }
                catch (Exception exception)
                {
                    AddError(exception);
                }
                finally
                {
                    RefreshStatus();
                    Thread.Sleep(10000);
                }
            }
        }

        /// <summary>
        /// Проверяет почту отпавителя на принадлежность к ареопагу
        /// </summary>
        /// <param name="message">Письмо</param>
        /// <returns>true если домен areopag-sbp</returns>
        private static bool ValidateEmailNotify(MimeKit.MimeMessage message)
        {
            foreach (MimeKit.MailboxAddress sender in message.From)
            {
                if (sender.Address.Contains("@areopag-spb.ru"))
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// Сообщение в снэкбар
        /// </summary>
        /// <param name="message"></param>
        private void SendMessage(string message) => snackBar.MessageQueue?.Enqueue(message,
            null,
            null,
            null,
            false,
            true, TimeSpan.FromSeconds(4));

        #endregion

        #region Статусная строка
        private void AddError(Exception e)
        {
            _errors++;
            _errorsList += $"[{DateTime.Now:dd.MM.yyyy HH:mm:ss}]: {e} {e.Message}" + Environment.NewLine;
            _errorsList += e.StackTrace;
            _errorsList += Environment.NewLine;
            _status.Add(!_errorStatus ? $"Необработанное исключение. " : string.Empty);
            WriteLog($"[{DateTime.Now:dd.MM.yyyy HH:mm:ss}]: {e} {e.Message}");
        }

        private void AddStatus(string message)
        {
            if (!_status.Contains(message)) _status.Add(message);
            RefreshStatus();
        }

        private void RemoveStatus(string message)
        {
            if (_status.Contains(message)) _status.Remove(message);
            RefreshStatus();
        }

        private void RefreshStatus()
        {
            statusTextBlock.Dispatcher.Invoke(() => statusTextBlock.Text = string.Join(string.Empty, _status.Distinct()));
            if (_errors > 0 && _errorStatus == false)
            {
                _errorStatus = true;
            }
        }
        #endregion

        #region Представление архива 

        /// <summary>
        /// Обновляет содержимое архива
        /// </summary>
        private async Task LoadArchive()
        {
            if (_archiveStatus)
            {
                try
                {
                    string[] dirs = Array.Empty<string>();
                    string[] files = Array.Empty<string>();
                    string findString = string.Empty;
                    await findTextBox.Dispatcher.InvokeAsync(() => findString = findTextBox.Text);
                    switch (_findStatus)
                    {
                        // просто сёрфим папки
                        case FindStatus.DontNeed:
                            _archiveContent = new();
                            dirs = Directory.EnumerateDirectories(_currentArchiveFolder).ToArray();
                            files = Directory.EnumerateFiles(_currentArchiveFolder).ToArray();
                            break;
                        // поиск
                        case FindStatus.Find:
                            _archiveContent = new();
                            await archiveLV.Dispatcher.InvokeAsync(() => archiveLV.Visibility = Visibility.Collapsed);
                            await findDialogButton.Dispatcher.InvokeAsync(() => findDialogButton.Visibility = Visibility.Collapsed);
                            await archivePathTB.Dispatcher.InvokeAsync(() => archivePathTB.Text = $"Поиск \"{findTextBox.Text}\"...");
                            if (debugMode) AddStatus(_FindInProcess);
                            await archiveProgressBar .Dispatcher.InvokeAsync(() => findProgressBar.Visibility = Visibility.Visible);
                            await Task.Run(() => 
                            {
                                files = Directory
                                .EnumerateFiles(_currentArchiveFolder, "*.*", SearchOption.AllDirectories)
                                .Where(
                                file => file.ToLower().Contains(findString, StringComparison.OrdinalIgnoreCase))
                                .ToArray();
                            });
                            break;
                        // возврат к результатам поиска
                        case FindStatus.Finded:
                            _archiveContent = new();
                            files = _findResult;
                            break;
                    }

                    // вносим папки
                    if (dirs.Length > 0)
                    {
                        foreach (string folder in dirs)
                        {
                            _archiveContent.Add(new ArchiveContent
                            {
                                Content = folder,
                                TransferButtonState = Visibility.Collapsed,
                                OpenButtonState = Visibility.Collapsed,
                                OpenFolderState = Visibility.Collapsed
                            });
                        }
                    }
                    // вносим файлы
                    if (files.Length > 0)
                    {
                        foreach (string file in files)
                        {
                            if (!FileFormats.SystemFiles.Contains(Path.GetFileName(file)))
                            {
                                if (Reader.CanBeTransfered(file))
                                {
                                    _archiveContent.Add(new ArchiveContent
                                    {
                                        Content = file,
                                        TransferButtonState = (_transferFromArchive && file == _selectedArchiveFile) ? Visibility.Visible : Visibility.Collapsed,
                                        OpenButtonState = (_openFromArchive && file == _selectedArchiveFile) ? Visibility.Visible : Visibility.Collapsed,
                                        OpenFolderState = (_findStatus == FindStatus.Finded && file == _selectedArchiveFile) ? Visibility.Visible : Visibility.Collapsed
                                    });
                                }
                                else
                                {
                                    _archiveContent.Add(new ArchiveContent
                                    {
                                        Content = file,
                                        TransferButtonState = Visibility.Collapsed,
                                        OpenButtonState = (_openFromArchive && file == _selectedArchiveFile) ? Visibility.Visible : Visibility.Collapsed,
                                        OpenFolderState = (_findStatus == FindStatus.Finded && file == _selectedArchiveFile) ? Visibility.Visible : Visibility.Collapsed
                                    });
                                }
                            }
                        }
                        if (_findStatus == FindStatus.Find)
                        {
                            _findResult = files;
                        }
                    }
                    await archiveLV.Dispatcher.InvokeAsync(() => archiveLV.IsEnabled = true);
                    await archiveLV.Dispatcher.InvokeAsync(() => archiveLV.ItemsSource = _archiveContent);

                }
                catch (UnauthorizedAccessException e)
                {
                    if (_findStatus == FindStatus.Find)
                    {
                        SendMessage("Ошибка доступа во время поиска.");
                        WriteLog($"Ошибка доступа во время поиска. {e} {e.Message} {e.StackTrace}");
                    }
                    else
                    {
                        SendMessage("Ошибка доступа.");
                        WriteLog($"Ошибка доступа. {e} {e.Message} {e.StackTrace}");
                    }
                }
                catch (DirectoryNotFoundException e)
                {
                    SendMessage("Указанное расположение больше не существует в архиве. Попробуйте вернуться в корень архива.");
                    WriteLog($"Ошибка при чтении архива. {e} {e.Message} {e.StackTrace}");
                }
                catch (IOException e)
                {
                    SendMessage("Не удается получить доступ к архиву.");
                    WriteLog($"Ошибка при чтении архива. {e} {e.Message} {e.StackTrace}");
                    TurnOffArchive();
                }
                catch (Exception e)
                {
                    AddError(e);
                }
                switch (_findStatus)
                {
                    case FindStatus.DontNeed:
                        await archivePathTB.Dispatcher.InvokeAsync(() => archivePathTB.Text = _currentArchiveFolder);
                        await archivePathTB.Dispatcher.InvokeAsync(() => archivePathTB.CaretIndex = archivePathTB.Text.Length);
                        await archivePathTB.Dispatcher.InvokeAsync(() => archivePathTB.ScrollToHorizontalOffset(double.MaxValue));
                        break;
                    case FindStatus.Finded:
                        break;
                    case FindStatus.Find:
                        await archiveLV.Dispatcher.InvokeAsync(() => archiveLV.Visibility = Visibility.Visible);
                        await archivePathTB.Dispatcher.InvokeAsync(() => archivePathTB.Text = $"Поиск \"{findTextBox.Text}\" завершен. Найдено {archiveLV.Items.Count} элементов.");
                        await findProgressBar.Dispatcher.InvokeAsync(() => findProgressBar.Visibility = Visibility.Collapsed);
                        await findDialogButton.Dispatcher.InvokeAsync(() => findDialogButton.Visibility = Visibility.Visible);
                        await archiveRootButton.Dispatcher.InvokeAsync(() => archiveRootButton.IsEnabled = true);
                        await archiveParentButton.Dispatcher.InvokeAsync(() => archiveParentButton.IsEnabled = true);
                        await findDialogButton.Dispatcher.InvokeAsync(() => findDialogButton.IsEnabled = true);
                        _findStatus = FindStatus.Finded;
                        await findTextBox.Dispatcher.InvokeAsync(() => findTextBox.Text = string.Empty);
                        break;
                }
            }
        }

        /// <summary>
        /// Выключает отображение архива
        /// </summary>
        private void TurnOffArchive()
        {
            _archiveStatus = false;
            archiveButtonsPanel.Dispatcher.Invoke(() => archiveButtonsPanel.Visibility = Visibility.Collapsed);
            archiveLV.Dispatcher.Invoke(() => archiveLV.Visibility = Visibility.Collapsed);
            archiveConnectionIcon.Dispatcher.Invoke(() => archiveConnectionIcon.Kind = MaterialDesignThemes.Wpf.PackIconKind.FolderRemove);
            archiveConnectionIcon.Dispatcher.Invoke(() => archiveConnectionIcon.Foreground = redBrush);
            if (!string.IsNullOrWhiteSpace(Settings.Default.archivePath))
                archiveProgressBar.Dispatcher.Invoke(() => archiveProgressBar.Visibility = Visibility.Visible);
            if (debugMode) AddStatus(_noArchiveLabel);
            needUpdateArchive = true;
        }

        /// <summary>
        /// Включает отображение архива
        /// </summary>
        private void TurnOnArchive()
        {
            _archiveStatus = true;
            archiveButtonsPanel.Dispatcher.Invoke(() => archiveButtonsPanel.Visibility = Visibility.Visible);
            archiveLV.Dispatcher.Invoke(() => archiveLV.Visibility = Visibility.Visible);
            archiveProgressBar.Dispatcher.Invoke(() => archiveProgressBar.Visibility = Visibility.Collapsed);
            archiveConnectionIcon.Dispatcher.Invoke(() => archiveConnectionIcon.Kind = MaterialDesignThemes.Wpf.PackIconKind.FolderHome);
            archiveConnectionIcon.Dispatcher.Invoke(() => archiveConnectionIcon.Foreground = greenBrush);
            if (debugMode) RemoveStatus(_noArchiveLabel);
            needUpdateArchive = false;
        }
        #endregion

        #region Представление сетевой папки 

        /// <summary>
        /// Обновляет содержимое сетевой папки
        /// </summary>
        private void LoadMachine()
        {
            if (_machineStatus)
            {
                _machineContent = new();
                foreach (string file in Directory.GetFiles(Settings.Default.machinePath))
                {
                    _machineContent.Add(new NcFile
                    {
                        FullPath = file,
                        CheckButtonVisibility = (_transferFromMachine && file == _selectedMachineFile) ? Visibility.Visible : Visibility.Collapsed,
                        RenameButtonVisibility = (_renameOnMachine && file == _selectedMachineFile) ? Visibility.Visible : Visibility.Collapsed,
                        OpenButtonVisibility = (_openFromNcFolder && file == _selectedMachineFile) ? Visibility.Visible : Visibility.Collapsed,
                        DeleteButtonVisibility = (_deleteFromMachine && file == _selectedMachineFile) ? Visibility.Visible : Visibility.Collapsed,
                        AnalyzeButtonVisibility = (_analyzeNcProgram && file == _selectedMachineFile) ? Visibility.Visible : Visibility.Collapsed,
                    });
                }

                if (machineDG.ItemsSource == null || !Enumerable.SequenceEqual(_machineContent, machineDG.ItemsSource as List<NcFile>))
                {
                    machineDG.Dispatcher.InvokeAsync(() => machineDG.ItemsSource = _machineContent);
                }
            }
        }

        /// <summary>
        /// Выключает отображение сетевой папки
        /// </summary>
        private void TurnOffMachine()
        {
            _machineStatus = false;
            machineDG.Dispatcher.Invoke(() => machineDG.Visibility = Visibility.Collapsed);
            if (!string.IsNullOrWhiteSpace(Settings.Default.machinePath))
                machineProgressBar.Dispatcher.Invoke(() => machineProgressBar.Visibility = Visibility.Visible);
            machineConnectionIcon.Dispatcher.Invoke(() => machineConnectionIcon.Kind = MaterialDesignThemes.Wpf.PackIconKind.ServerNetworkOff);
            machineConnectionIcon.Dispatcher.Invoke(() => machineConnectionIcon.Foreground = redBrush);
            if (debugMode) AddStatus(_noMachineLabel);
        }

        /// <summary>
        /// Включает отображение сетевой папки
        /// </summary>
        private void TurnOnMachine()
        {
            _machineStatus = true;
            machineDG.Dispatcher.Invoke(() => machineDG.Visibility = Visibility.Visible);
            machineProgressBar.Dispatcher.Invoke(() => machineProgressBar.Visibility = Visibility.Collapsed);
            machineConnectionIcon.Dispatcher.Invoke(() => machineConnectionIcon.Kind = MaterialDesignThemes.Wpf.PackIconKind.ServerNetwork);
            machineConnectionIcon.Dispatcher.Invoke(() => machineConnectionIcon.Foreground = greenBrush);
            if (debugMode) RemoveStatus(_noMachineLabel);
        }
        #endregion

        #region Наблюдатель доступности
        private void WatchInfo()
        {
            while (true)
            {
                if (Settings.Default.refreshInterval > 0)
                {
                    dateTimeTB.Dispatcher.Invoke(() => dateTimeTB.Text = DateTime.Now.ToString("d MMMM yyyy г.  HH:mm"));

                    #region Проверка архива
                    if (!Reader.CheckPath(Settings.Default.archivePath))
                    {
                        if (_archiveStatus) TurnOffArchive();
                    }
                    else
                    {
                        if (needUpdateArchive)
                        {
                            _archiveStatus = true;
                            TurnOnArchive();
                            LoadArchive();
                        }
                    }

                    #endregion

                    #region Проверка промежуточной папки
                    if (!Reader.CheckPath(Settings.Default.tempPath))
                    {
                        _tempFolderStatus = false;
                        tempFolderConnectionIcon.Dispatcher.Invoke(() => tempFolderConnectionIcon.Kind = MaterialDesignThemes.Wpf.PackIconKind.LanDisconnect);
                        tempFolderConnectionIcon.Dispatcher.Invoke(() => tempFolderConnectionIcon.Foreground = redBrush);
                        archiveGB.Dispatcher.InvokeAsync(() => archiveGB.Header = $"Архив управляющих программ");
                        if (debugMode) AddStatus(_noTempFolderLabel);
                    }
                    else
                    {
                        _tempFolderStatus = true;
                        tempFolderConnectionIcon.Dispatcher.Invoke(() => tempFolderConnectionIcon.Kind = MaterialDesignThemes.Wpf.PackIconKind.LanConnect);
                        tempFolderConnectionIcon.Dispatcher.Invoke(() => tempFolderConnectionIcon.Foreground = greenBrush);
                        archiveGB.Dispatcher.InvokeAsync(() => archiveGB.Header = $"Архив управляющих программ | На проверке: {Directory.GetFiles(Settings.Default.tempPath).Length}");
                        if (debugMode) RemoveStatus(_noTempFolderLabel);
                    }

                    #endregion

                    #region Проверка сетевой папки станка
                    if (!Reader.CheckPath(Settings.Default.machinePath))
                    {
                        if (_machineStatus) TurnOffMachine();
                    }
                    else
                    {
                        if (!_machineStatus)
                        {
                            _machineStatus = true;
                            TurnOnMachine();
                        }
                        LoadMachine();
                    }
                    #endregion

                    #region Проверка таблицы
                    if (!Reader.CheckPath(Settings.Default.tablePath))
                    {
                        tableConnectionIcon.Dispatcher.Invoke(() =>
                        tableConnectionIcon.Kind = MaterialDesignThemes.Wpf.PackIconKind.TableRemove);
                        tableConnectionIcon.Dispatcher.Invoke(() => tableConnectionIcon.Foreground = redBrush);
                        if (debugMode) AddStatus(_noTableLabel);
                    }
                    else
                    {
                        tableConnectionIcon.Dispatcher.Invoke(() =>
                        tableConnectionIcon.Kind = MaterialDesignThemes.Wpf.PackIconKind.Table);
                        tableConnectionIcon.Dispatcher.Invoke(() => tableConnectionIcon.Foreground = greenBrush);
                        if (debugMode) AddStatus(_noTableLabel);
                    }
                    #endregion

                    Thread.Sleep(Settings.Default.refreshInterval * 1000);
                }
            }
        }
        #endregion

        #region Кнопки на верхней панели
        private void closeButton_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        private void messageButton_Click(object sender, RoutedEventArgs e)
        {
            MessageWindow messageWindow = new();
            messageWindow.ShowDialog();
        }

        private void settingsButton_Click(object sender, RoutedEventArgs e)
        {
            SettingsWindow settingsWindow = new();
            settingsWindow.ShowDialog();
            _errorStatus = false;
            _status.Clear();
            if (Settings.Default.needUpdate)
            {
                _currentArchiveFolder = Settings.Default.archivePath;

                if (!Reader.CheckPath(Settings.Default.archivePath))
                {
                    TurnOffArchive();
                }
                else
                {
                    TurnOnArchive();
                }
                LoadArchive();

                if (!Reader.CheckPath(Settings.Default.machinePath))
                {
                    TurnOffMachine();
                }
                else
                {
                    TurnOnMachine();
                }
                LoadMachine();

                Settings.Default.needUpdate = false;
            }
        }

        private void helpButton_Click(object sender, RoutedEventArgs e)
        {
            HelpWindow helpWindow = new(_errorsList);
            helpWindow.ShowDialog();
            _status.Clear();
        }

        private void calcButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                List<IntPtr> wins = new();

                _ = WindowsUtils.EnumWindows((hWnd, _) =>
                {
                    if (WindowsUtils.IsWindowVisible(hWnd) && WindowsUtils.GetWindowTextLength(hWnd) != 0 && WindowsUtils.GetWindowText(hWnd) == "Калькулятор")
                    {
                        wins.Add(hWnd);
                    }
                    return true;
                }, IntPtr.Zero);

                if (wins.Count > 0)
                {
                    foreach (var win in wins)
                    {
                        if (WindowsUtils.IsIconic(win))
                        {
                            _ = WindowsUtils.ShowWindow(win, 1);
                        }
                        else if (win != IntPtr.Zero)
                        {
                            _ = WindowsUtils.ShowWindow(win, 1);
                            _ = WindowsUtils.SetForegroundWindow(win);
                        }
                    }
                }
                else
                {
                    _ = Process.Start(new ProcessStartInfo("calc") { UseShellExecute = true });
                }

            }
            catch (Exception exception)
            {
                AddError(exception);
            }
        }

        private void refreshButton_Click(object sender, RoutedEventArgs e)
        {
            _findStatus = FindStatus.DontNeed;
            LoadMachine();
            LoadArchive();
        }

        private void minimizeButton_Click(object sender, RoutedEventArgs e)
        {
            WindowState = WindowState.Minimized;
        }
        #endregion

        #region Архив

        private void archiveLV_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            object item = (sender as ListView)?.SelectedItem;
            if (item == null) return;
            ArchiveContent currentItem = (ArchiveContent)item;
            if (Directory.Exists(currentItem.Content))
            {
                //Stopwatch sw = Stopwatch.StartNew();
                _transferFromArchive = false;
                _transferFromMachine = false;
                _deleteFromMachine = false;
                _openFromArchive = false;
                _renameOnMachine = false;
                _openFromNcFolder = false;
                _analyzeNcProgram = false;
                _currentArchiveFolder = currentItem.Content;
                LoadArchive();
                //statusTextBlock.Text = $"Открыто за {sw.ElapsedMilliseconds} мс";
            }
            else if (File.Exists(currentItem.Content))
            {
                _transferFromArchive = true;
                _openFromArchive = true;
                _transferFromMachine = false;
                _deleteFromMachine = false;
                _renameOnMachine = false;
                _openFromNcFolder = false;
                _analyzeNcProgram = false;
                _selectedArchiveFile = currentItem.Content;
                LoadArchive();
                // LoadMachine() написать
            }
            else
            {
                SendMessage("Указанное расположение больше не существует в архиве. Попробуйте обновить информацию.");
            }
        }

        private void archiveParentButton_Click(object sender, RoutedEventArgs e)
        {
            findDialogButton.Visibility = Visibility.Visible;
            returnButton.Visibility = Visibility.Collapsed;
            _findStatus = FindStatus.DontNeed;
            if (Reader.CheckPath(_currentArchiveFolder) && Directory.GetParent(_currentArchiveFolder) != null)
            {
                _transferFromArchive = false;
                _transferFromMachine = false;
                _deleteFromMachine = false;
                _openFromArchive = false;
                _renameOnMachine = false;
                _openFromNcFolder = false;
                _analyzeNcProgram = false;
                _currentArchiveFolder = Directory.GetParent(_currentArchiveFolder)?.ToString();
            }
            LoadArchive();
        }

        private void archiveRootButton_Click(object sender, RoutedEventArgs e)
        {
            findDialogButton.Visibility = Visibility.Visible;
            returnButton.Visibility = Visibility.Collapsed;
            _findStatus = FindStatus.DontNeed;
            _transferFromArchive = false;
            _transferFromMachine = false;
            _deleteFromMachine = false;
            _openFromArchive = false;
            _renameOnMachine = false;
            _openFromNcFolder = false;
            _analyzeNcProgram = false;
            _currentArchiveFolder = Settings.Default.archivePath;
            LoadArchive();
        }


        private void findButton_Click(object sender, RoutedEventArgs e)
        {
            if (Reader.CheckPath(_currentArchiveFolder))
            {
                archiveRootButton.IsEnabled = false;
                archiveParentButton.IsEnabled = false;
                findDialogButton.IsEnabled = false;
                _findStatus = FindStatus.Find;
                LoadArchive();
            }
        }

        private void openFolderButton_Click(object sender, RoutedEventArgs e)
        {
            _currentArchiveFolder = Path.GetDirectoryName(_selectedArchiveFile);
            findDialogButton.Visibility = Visibility.Collapsed;
            returnButton.Visibility = Visibility.Visible;
            _findStatus = FindStatus.DontNeed;

            LoadArchive();
        }

        private void returnButton_Click(object sender, RoutedEventArgs e)
        {
            _findStatus = FindStatus.Finded;
            findDialogButton.Visibility = Visibility.Visible;
            returnButton.Visibility = Visibility.Collapsed;
            archivePathTB.Text = "Результаты поиска:";
            LoadArchive();
        }

        private void transferButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                string tranferFilePath;
                if (Settings.Default.autoRenameToMachine)
                {
                    tranferFilePath = Reader.FindFreeName(_selectedArchiveFile, Settings.Default.machinePath);
                }
                else
                {
                    tranferFilePath = Path.Combine(Settings.Default.machinePath, Path.GetFileName(_selectedArchiveFile)!);
                }


                File.Copy(_selectedArchiveFile!, tranferFilePath);
                _transferFromArchive = false;
                _transferFromMachine = false;
                _deleteFromMachine = false;
                _openFromArchive = false;
                _renameOnMachine = false;
                _openFromNcFolder = false;
                _analyzeNcProgram = false;
                WriteLog($"Отправлено на станок: \"{_selectedArchiveFile}\"");
                LoadMachine();
                LoadArchive();
                SendMessage(Settings.Default.autoRenameToMachine
                    ? $"Файл {Path.GetFileName(_selectedArchiveFile)} отправлен на станок и переименован в {Path.GetFileName(tranferFilePath)}"
                    : $"На станок отправлен файл: {Path.GetFileName(_selectedArchiveFile)}");
            }
            catch (IOException)
            {
                if (!Reader.CheckPath(Settings.Default.machinePath))
                {
                    SendMessage($"Сетевая папка станка недоступна.");
                }
                else
                {
                    SendMessage($"В сетевой папке уже есть такой файл: {Path.GetFileName(_selectedArchiveFile)}");
                }
            }
            catch (UnauthorizedAccessException)
            {
                SendMessage("Ошибка доступа. Вероятней всего отсутствуют права на запись в сетевую папку.");
            }
            catch (Exception exception)
            {
                WriteLog($"Ошибка при отправке на станок.");
                AddError(exception);
            }
        }

        private void openButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                Process.Start(new ProcessStartInfo(_selectedArchiveFile) { UseShellExecute = true });
                _transferFromArchive = false;
                _transferFromMachine = false;
                _deleteFromMachine = false;
                _openFromArchive = false;
                _renameOnMachine = false;
                _openFromNcFolder = false;
                _analyzeNcProgram = false;
                LoadMachine();
                LoadArchive();
            }
            catch (Win32Exception)
            {
                try
                {
                    Process.Start(new ProcessStartInfo("wordpad", $"\"{_selectedArchiveFile}\"") { UseShellExecute = true });
                    _transferFromArchive = false;
                    _transferFromMachine = false;
                    _deleteFromMachine = false;
                    _openFromArchive = false;
                    _renameOnMachine = false;
                    _openFromNcFolder = false;
                    _analyzeNcProgram = false;
                    LoadMachine();
                    LoadArchive();
                }
                catch (Exception exception)
                {
                    AddError(exception);
                }
            }
            catch (Exception exception)
            {
                AddError(exception);
            }

        }


        #endregion

        #region Сетевая папка

        private void machineDG_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            object item = (sender as ListView)?.SelectedItem;
            if (item == null) return;
            NcFile currentItem = (NcFile)item;
            if (File.Exists(currentItem.FullPath))
            {
                _transferFromMachine = true;
                _deleteFromMachine = true;
                _renameOnMachine = true;
                _transferFromArchive = false;
                _openFromArchive = false;
                _openFromNcFolder = true;
                _analyzeNcProgram = true;
                _selectedMachineFile = currentItem.FullPath;
                kostyl.Text = currentItem.Extension;
                LoadMachine();
                LoadArchive();
            }
        }

        #region Кнопки на программе в сетевой папке
        private void ApplyRenameButtonClick(object sender, RoutedEventArgs e)
        {
            try
            {
                if (string.IsNullOrEmpty(newNameTB.Text))
                {
                    throw new ArgumentException();
                }
                string newName = Path.Combine(Settings.Default.machinePath, newNameTB.Text) + kostyl.Text;
                FileSystem.Rename(_selectedMachineFile, newName);
                LoadMachine();
                LoadArchive();
                SendMessage($"Файл {Path.GetFileName(_selectedMachineFile)} переименован в {Path.GetFileName(newName)}");
                newNameTB.Text = string.Empty;
                kostyl.Text = string.Empty;
                WindowsUtils.KillTabTip(0);

            }
            catch (ArgumentException)
            {
                SendMessage("Недопустимое имя.");
            }
            catch (IOException)
            {
                SendMessage("Такой файл уже существует.");
            }
            catch (Exception exception)
            {
                AddError(exception);
            }
        }

        private void checkButton_Click(object sender, RoutedEventArgs e)
        {
            // формирование пути сохранения
            string tempName = Path.Combine(Settings.Default.tempPath,
                (Settings.Default.autoRenameToMachine
                    ? Reader.CreateTempName(_selectedMachineFile) // формирование нового имени файла
                    : Path.GetFileName(_selectedMachineFile))!);  // сохранение с текущим именем файла
            try
            {
                if (!_tempFolderStatus)
                {
                    throw new IOException();
                }
                File.Copy(_selectedMachineFile!, tempName, true);
                _transferFromMachine = false;
                LoadMachine();
                LoadArchive();
                if (File.Exists(tempName))
                {
                    File.Delete(_selectedMachineFile);
                    _transferFromArchive = false;
                    _transferFromMachine = false;
                    _deleteFromMachine = false;
                    _openFromArchive = false;
                    _renameOnMachine = false;
                    _openFromNcFolder = false;
                    _analyzeNcProgram = false;
                    LoadMachine();
                    SendMessage($"Отправлен на проверку и очищен из сетевой папки файл: {tempName}");
                    WriteLog($"Отправлено на проверку \"{_selectedMachineFile}\" -> \"{tempName}\"");
                }
            }
            catch (UnauthorizedAccessException)
            {
                WriteLog($"Ошибка доступа при отправке на проверку");
                SendMessage("Ошибка доступа. Вероятней всего отсутствуют права на запись в промежуточную папку.");
            }
            catch (DirectoryNotFoundException)
            {
                WriteLog($"Ошибка при отправке на проверку");
                SendMessage("Промежуточная папка недоступна.");
            }
            catch (IOException)
            {
                WriteLog($"Ошибка при отправке на проверку");
                SendMessage("Промежуточная папка недоступна.");
            }
            catch (Exception exception)
            {
                WriteLog($"Необработанное иссключение при отправке на проверку");
                AddError(exception);
            }
        }

        private void deleteButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                File.Delete(_selectedMachineFile);
                _deleteFromMachine = false;
                _transferFromArchive = false;
                _transferFromMachine = false;
                _openFromArchive = false;
                _renameOnMachine = false;
                _openFromNcFolder = false;
                _analyzeNcProgram = false;
                LoadMachine();
                LoadArchive();
                SendMessage($"Из сетевой папки станка удален файл: {Path.GetFileName(_selectedMachineFile)}");
            }
            catch (Exception exception)
            {
                WriteLog($"Ошибка при удалении файла");
                AddError(exception);
            }
        }

        private void openNCButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                Process.Start(new ProcessStartInfo(_selectedMachineFile) { UseShellExecute = true });
                _transferFromArchive = false;
                _transferFromMachine = false;
                _deleteFromMachine = false;
                _openFromArchive = false;
                _renameOnMachine = false;
                _openFromNcFolder = false;
                _analyzeNcProgram = false;
                LoadMachine();
                LoadArchive();
            }
            catch (Win32Exception)
            {
                try
                {
                    Process.Start(new ProcessStartInfo("wordpad", $"\"{_selectedMachineFile}\"") { UseShellExecute = true });
                    _transferFromArchive = false;
                    _transferFromMachine = false;
                    _deleteFromMachine = false;
                    _openFromArchive = false;
                    _renameOnMachine = false;
                    _openFromNcFolder = false;
                    _analyzeNcProgram = false;
                    LoadMachine();
                    LoadArchive();
                }
                catch (Exception exception)
                {
                    WriteLog($"Ошибка при открытии файла \"{Path.GetFileName(_selectedMachineFile)}\"");
                    AddError(exception);
                }
            }
            catch (Exception exception)
            {
                WriteLog($"Ошибка при открытии файла \"{Path.GetFileName(_selectedMachineFile)}\"");
                AddError(exception);
            }
        }

        private void analyzeNCButton_Click(object sender, RoutedEventArgs e)
        {
            analyzeDG.ItemsSource = Reader.AnalyzeProgram(_selectedMachineFile, out string programType, out string coordinates);
            analyzeProgramTypeTB.Text = programType;
            analyzeProgramCoordinatesTB.Text = coordinates;
            //List<string> temp = new();
            //foreach (var item in analyze)
            //{
            //    temp.Add($"T{item.Position} {item.Comment}{(item.LengthCompensation is null ? string.Empty : $" H{item.LengthCompensation}")}{(item.RadiusCompensation is null ? string.Empty : $" D{item.RadiusCompensation}")}");
            //}
            //MessageBox.Show($"{coordinates}\n{temp}", programType);
        }
        #endregion

        #endregion

        #region Список инструмента
        private void deleteToolButton_Click(object sender, RoutedEventArgs e)
        {
            if (notesDG.SelectedItem is ToolNote selectedItem)
            {
                _ = _toolNotes.Remove(selectedItem);
            }
        }

        private void addToolButton_Click(object sender, RoutedEventArgs e)
        {
            _toolNotes.Add(new ToolNote());
        }

        private void sortToolButton_Click(object sender, RoutedEventArgs e)
        {
            _toolNotes = new BindingList<ToolNote>(_toolNotes.OrderBy(x => x.ToolNo).ToList());
            notesDG.ItemsSource = _toolNotes;
            try
            {
                Reader.SaveTools(_toolNotes);
            }
            catch (Exception exception)
            {
                AddError(exception);
            }
        }
        #endregion

        #region Клавиатурная шляпа
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

        #endregion

        
    }
}