using DetailsInfo.Data;
using DetailsInfo.Properties;
using DetailsInfo.Utils;
using MailKit.Net.Pop3;
using Microsoft.VisualBasic;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Forms.VisualStyles;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using MaterialDesignThemes.Wpf;
using MimeKit;
using Application = System.Windows.Application;
using ListView = System.Windows.Controls.ListView;
using IWshRuntimeLibrary;
using File = System.IO.File;
#pragma warning disable CS0162

namespace DetailsInfo
{
    /// TODO splash screen?

    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        private const bool DebugMode = false;

        #region Поля
        private FileSystemWatcher archiveWatcher = new();
        private FileSystemWatcher machineWatcher = new();
        private const string NewOrFixProgramReason = "Новая программа/корректировка старой";
        private const string VariantProgramReason = "Другой вариант изготовления";
        private readonly string[] _reasons = new string[2] { NewOrFixProgramReason, VariantProgramReason };
        private int _errors;
        private string _errorsList = string.Empty;
        private BindingList<ToolNote> _toolNotes;
        private ArchiveContent _selectedArchiveFolder;
        private List<ArchiveContent> _archiveContent;
        private List<NcFile> _machineContent = new();
        private string _currentArchiveFolder;

        private bool _archiveStatus = true;
        private bool _needUpdateArchive;

        private bool _machineStatus = true;

        private bool _tempFolderStatus = true;
        private bool _tableStatus = true;

        private bool _transferFromArchive;
        private bool _transferFromMachine;
        private bool _renameOnMachine;
        private bool _deleteFromMachine;
        private bool _openFromArchive;
        private bool _openFromNcFolder;
        private bool _analyzeArchiveProgram;
        private bool _analyzeNcProgram;
        private bool _analyzeInfo;
        private bool _showWinExplorer;
        private bool _stopSearch;
        private bool _openFolderButton;

        private bool _tabtipStatus;
        private bool _errorStatus;
        private enum FindStatus { DontNeed, Find, Finded, InProgress }
        private FindStatus _findStatus = FindStatus.DontNeed;
        private List<string> _findResult;
        private string _selectedArchiveFile;
        private string _selectedForDeletionArchiveFile;
        private string _selectedForDeletionMachineFile;
        private string _selectedMachineFile;
        private bool _needArchiveScroll;

        private readonly SolidColorBrush _redBrush = Util.BrushFromHex("#FFF44336");
        private readonly SolidColorBrush _greenBrush = Util.BrushFromHex("#FFAEEA00");
        #endregion

        public string ProgramType { get; set; } = string.Empty;
        public string ProgramCoordinates { get; set; } = string.Empty;
        public List<NcToolInfo> ProgramTools { get; set; } = new();

        private List<string> _status = new();

        readonly bool _advancedMode = Settings.Default.advancedMode;

        #region Статусы ошибок

        private const string UnhandledException = "Необработанное исключение. ";
        private const string NoArchiveLabel = "Архив недоступен. ";
        private const string NoMachineLabel = "Сетевая папка станка недоступна. ";
        private const string NoTempFolderLabel = "Промежуточная папка недоступна. ";
        private const string NoTableLabel = "Таблица недоступна. ";
        private const string FindInProcess = "Архив заблокирован на время поиска. ";
        private const string AuthenticationException = "Ошибка авторизации на сервере уведомлений. ";
        private const string TimeoutException = "Время ожидания подключения к серверу уведомлений истекло. ";
        private const string Pop3ProtocolException = "Ошибка протокола POP3. ";
        private const string SslHandshakeException = "Неудачная проверка сертификата SSL. ";
        private const string SocketException = "Ошибка соединения с сервером уведомлений. ";
        private const string IoExceptionMail = "Разрыв соединения. ";

        #endregion

        #region Перерывы

        

        #endregion

        public MainWindow()
        {
            InitializeComponent();
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        }


        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            archiveWatcher.NotifyFilter = NotifyFilters.Attributes 
                                        | NotifyFilters.CreationTime
                                        | NotifyFilters.DirectoryName
                                        | NotifyFilters.FileName
                                        | NotifyFilters.LastAccess
                                        | NotifyFilters.LastWrite
                                        | NotifyFilters.Security
                                        | NotifyFilters.Size;
            archiveWatcher.Filter = "*.*";
            machineWatcher.NotifyFilter = NotifyFilters.Attributes 
                                        | NotifyFilters.CreationTime
                                        | NotifyFilters.DirectoryName
                                        | NotifyFilters.FileName
                                        | NotifyFilters.LastAccess
                                        | NotifyFilters.LastWrite
                                        | NotifyFilters.Security
                                        | NotifyFilters.Size;
            machineWatcher.Filter = "*.*";

            advancedModeIcon.Visibility = _advancedMode ? Visibility.Visible : Visibility.Collapsed;
            if (DebugMode) AddStatus(Reader.ReadConfig()); else Reader.ReadConfig();
            this.WindowStyle = _advancedMode ? WindowStyle.SingleBorderWindow : WindowStyle.None;
            this.ResizeMode = _advancedMode ? ResizeMode.CanResize : ResizeMode.CanMinimize;
            this.minimizeButton.Visibility = _advancedMode ? Visibility.Collapsed : Visibility.Visible;
            this.closeButton.Visibility = _advancedMode ? Visibility.Collapsed : Visibility.Visible;

            Topmost = Settings.Default.topMost;
            _currentArchiveFolder = Settings.Default.archivePath;
            archivePathTB.Text = _currentArchiveFolder;

            _ = LoadInfoAsync(start: true);

            try
            {
                _toolNotes = Reader.LoadTools() ?? new BindingList<ToolNote>();
            }
            catch (Exception exception)
            {
                AddError(exception);
            }

            notesDG.ItemsSource = _toolNotes;
            _toolNotes.ListChanged += ToolNotes_ListChanged;

            CheckReasonCB.ItemsSource = _reasons;
            CheckReasonCB.SelectedIndex = 0;
        }

        private void OnCreatedInArchive(object sender, FileSystemEventArgs eventArgs) => LoadArchive();
        private void OnDeletedInArchive(object sender, FileSystemEventArgs eventArgs) => LoadArchive();
        private void OnRenamedInArchive(object sender, RenamedEventArgs eventArgs) => LoadArchive();

        private void OnCreatedInMachine(object sender, FileSystemEventArgs eventArgs) => LoadMachine();
        private void OnChangedInMachine(object sender, FileSystemEventArgs eventArgs) => LoadMachine();
        private void OnDeletedInMachine(object sender, FileSystemEventArgs eventArgs) => LoadMachine();
        private void OnRenamedInMachine(object sender, RenamedEventArgs eventArgs) => LoadMachine();

        private async Task LoadInfoAsync(bool start = false)
        {
            await Task.Run(() =>
            {
                
                if (!Reader.CheckPath(Settings.Default.archivePath)) TurnOffArchive();
                if (_archiveStatus) 
                {
                    archiveConnectionIcon.Dispatcher.Invoke(() => archiveConnectionIcon.Foreground = _greenBrush);
                    archiveWatcher.Path = _currentArchiveFolder;
                    archiveWatcher.Created += new FileSystemEventHandler(OnCreatedInArchive);
                    archiveWatcher.Deleted += new FileSystemEventHandler(OnDeletedInArchive);
                    archiveWatcher.Renamed += new RenamedEventHandler(OnRenamedInArchive);
                    archiveWatcher.EnableRaisingEvents = true;
                }

                LoadArchive();

               
                if (!Reader.CheckPath(Settings.Default.machinePath)) TurnOffMachine();
                if (_machineStatus)
                {
                    machineConnectionIcon.Dispatcher.Invoke(() => machineConnectionIcon.Foreground = _greenBrush);
                    machineWatcher.Path = Settings.Default.machinePath;
                    machineWatcher.Created += new FileSystemEventHandler(OnCreatedInMachine);
                    machineWatcher.Changed += new FileSystemEventHandler(OnChangedInMachine);
                    machineWatcher.Deleted += new FileSystemEventHandler(OnDeletedInMachine);
                    machineWatcher.Renamed += new RenamedEventHandler(OnRenamedInMachine);
                    machineWatcher.EnableRaisingEvents = true;
                }
                LoadMachine();

                if (!Reader.CheckPath(Settings.Default.tempPath)) _tempFolderStatus = false;
                if (_tempFolderStatus) tempFolderConnectionIcon.Dispatcher.Invoke(() => tempFolderConnectionIcon.Foreground = _greenBrush);

                if (!Reader.CheckPath(Settings.Default.tablePath) && !System.IO.File.Exists(Settings.Default.tablePath)) TurnOffTable();
                if (_tableStatus) tableConnectionIcon.Dispatcher.Invoke(() => tableConnectionIcon.Foreground = _greenBrush);
                if (LoadTable())
                {
                    tableDG.Dispatcher.Invoke(() => tableDG.SortColumn(0));
                }

                RefreshStatus();
                if (!start) return;
                Thread loadInfoThread = new(WatchInfo)
                {
                    IsBackground = true
                };
                loadInfoThread.Start();

                Thread notifyThread = new(WatchMessages)
                {
                    IsBackground = true
                };
                notifyThread.Start();
            });
        }

        private static void WriteLog(string message) => _ = Reader.WriteLogAsync(message);

        // событие при изменении инструмента
        private void ToolNotes_ListChanged(object sender, ListChangedEventArgs e)
        {
            if (e.ListChangedType is not (ListChangedType.ItemAdded or ListChangedType.ItemDeleted
                or ListChangedType.ItemChanged)) return;
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

        #region Уведомления
        private void WatchMessages()
        {
            emailConnectionIcon.Dispatcher.Invoke(() => emailConnectionIcon.Kind = MaterialDesignThemes.Wpf.PackIconKind.EmailOff);
            emailConnectionIcon.Dispatcher.Invoke(() => emailConnectionIcon.Foreground = _redBrush);
            messageButton.Dispatcher.Invoke(() => messageButton.Visibility = Visibility.Collapsed);
            Pop3Client client = new()
            {
                ServerCertificateValidationCallback = (s, c, h, e) => true
            };
            try
            {
                client.Connect(Settings.Default.popServer, Settings.Default.popPort, Settings.Default.useSsl);
                client.Authenticate(Settings.Default.emailLogin, Util.Decrypt(Settings.Default.emailPass, "http://areopag"));
                client.DeleteAllMessages();
                client.Disconnect(true);
                messageButton.Dispatcher.Invoke(() => messageButton.Visibility = Visibility.Visible);

            }
            catch
            {
                
                // ignored
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
                    if (DebugMode) RemoveStatus(TimeoutException);
                    client.Authenticate(Settings.Default.emailLogin, Util.Decrypt(Settings.Default.emailPass, "http://areopag"));

                    if (DebugMode) AddStatus($"Connected: {client.IsConnected} ");
                    if (DebugMode) AddStatus($"Auth: {client.IsAuthenticated} ");

                    if (!client.IsConnected) continue;
                    messageButton.Dispatcher.Invoke(() => messageButton.Visibility = Visibility.Visible);
                    emailConnectionIcon.Dispatcher.Invoke(() => emailConnectionIcon.Kind = MaterialDesignThemes.Wpf.PackIconKind.Email);
                    emailConnectionIcon.Dispatcher.Invoke(() => emailConnectionIcon.Foreground = _greenBrush);
                    if (DebugMode) RemoveStatus(AuthenticationException);
                    if (DebugMode) RemoveStatus(Pop3ProtocolException);
                    if (DebugMode) RemoveStatus(SslHandshakeException);
                    if (DebugMode) RemoveStatus(SocketException);
                    if (DebugMode) RemoveStatus(IoExceptionMail);
                    var currentMessagesCount = client.GetMessageCount();
                    if (DebugMode) AddStatus($"Messages: {currentMessagesCount} ");
                    if (currentMessagesCount <= 0) continue;
                    var message = client.GetMessage(currentMessagesCount - 1);
                    if (!ValidateEmailNotify(message)) continue;
                    Application.Current.Dispatcher.InvokeAsync(() => { emailMessageTextBox.Text = message.Subject; });
                    client.DeleteAllMessages();
                    client.Disconnect(true);
                    Application.Current.Dispatcher.InvokeAsync(() => { messageDialog.IsOpen = true; });
                }
                catch (MailKit.Security.AuthenticationException ex)
                {
                    if (DebugMode) AddStatus(AuthenticationException);
                    messageButton.Dispatcher.Invoke(() => messageButton.Visibility = Visibility.Collapsed);
                    emailConnectionIcon.Dispatcher.Invoke(() => emailConnectionIcon.Kind = MaterialDesignThemes.Wpf.PackIconKind.EmailOff);
                    emailConnectionIcon.Dispatcher.Invoke(() => emailConnectionIcon.Foreground = _redBrush);
                    AddError(ex, AuthenticationException, false);
                }
                catch (TimeoutException ex)
                {
                    if (DebugMode) AddStatus(TimeoutException);
                    messageButton.Dispatcher.Invoke(() => messageButton.Visibility = Visibility.Collapsed);
                    emailConnectionIcon.Dispatcher.Invoke(() => emailConnectionIcon.Kind = MaterialDesignThemes.Wpf.PackIconKind.EmailOff);
                    emailConnectionIcon.Dispatcher.Invoke(() => emailConnectionIcon.Foreground = _redBrush);
                    AddError(ex, TimeoutException, false);
                }
                catch (Pop3ProtocolException ex)
                {
                    if (DebugMode) AddStatus(Pop3ProtocolException);
                    messageButton.Dispatcher.Invoke(() => messageButton.Visibility = Visibility.Collapsed);
                    emailConnectionIcon.Dispatcher.Invoke(() => emailConnectionIcon.Kind = MaterialDesignThemes.Wpf.PackIconKind.EmailOff);
                    emailConnectionIcon.Dispatcher.Invoke(() => emailConnectionIcon.Foreground = _redBrush);
                    AddError(ex, Pop3ProtocolException, false);
                }
                catch (MailKit.Security.SslHandshakeException ex)
                {
                    if (DebugMode) AddStatus(SslHandshakeException);
                    messageButton.Dispatcher.Invoke(() => messageButton.Visibility = Visibility.Collapsed);
                    emailConnectionIcon.Dispatcher.Invoke(() => emailConnectionIcon.Kind = MaterialDesignThemes.Wpf.PackIconKind.EmailOff);
                    emailConnectionIcon.Dispatcher.Invoke(() => emailConnectionIcon.Foreground = _redBrush);
                    AddError(ex, SslHandshakeException, false);
                }
                catch (System.Net.Sockets.SocketException ex)
                {
                    if (DebugMode) AddStatus(SocketException);
                    messageButton.Dispatcher.Invoke(() => messageButton.Visibility = Visibility.Collapsed);
                    emailConnectionIcon.Dispatcher.Invoke(() => emailConnectionIcon.Kind = MaterialDesignThemes.Wpf.PackIconKind.EmailOff);
                    emailConnectionIcon.Dispatcher.Invoke(() => emailConnectionIcon.Foreground = _redBrush);
                    AddError(ex, SocketException, false);
                }
                catch (System.IO.IOException ex)
                {
                    if (DebugMode) AddStatus(IoExceptionMail);
                    messageButton.Dispatcher.Invoke(() => messageButton.Visibility = Visibility.Collapsed);
                    emailConnectionIcon.Dispatcher.Invoke(() => emailConnectionIcon.Kind = MaterialDesignThemes.Wpf.PackIconKind.EmailOff);
                    emailConnectionIcon.Dispatcher.Invoke(() => emailConnectionIcon.Foreground = _redBrush);
                    AddError(ex, IoExceptionMail, false);
                }
                catch (Exception ex)
                {
                    messageButton.Dispatcher.Invoke(() => messageButton.Visibility = Visibility.Collapsed);
                    AddError(ex);
                }
                finally
                {
                    RefreshStatus();
                    Thread.Sleep(10000);
                }
            }
        }

        /// <summary>
        /// Проверяет почту отправителя на принадлежность к ареопагу
        /// </summary>
        /// <param name="message">Письмо</param>
        /// <returns>true если домен areopag-sbp</returns>
        private static bool ValidateEmailNotify(MimeKit.MimeMessage message)
        {
            return message.From.Cast<MailboxAddress>().Any(sender => sender.Address.Contains("@areopag-spb.ru"));
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
        private void AddError(Exception e, string message = UnhandledException, bool addStatus = true)
        {
            if (_errorsList.Length > 10000)
            {
                _errorsList = '[' + _errorsList[1..].Split('[', 2)[1];
            }
            _errors++;
            _errorsList += $"[{DateTime.Now:dd.MM.yyyy HH:mm:ss}]: {message}\n{e.Message}\n" + Environment.NewLine;
            if (message == UnhandledException) _errorsList += e.StackTrace;
            if (message == UnhandledException) _errorsList += Environment.NewLine;
            if (addStatus) _status.Add(!_errorStatus ? message : string.Empty);
            if (message == UnhandledException) WriteLog($"[{DateTime.Now:dd.MM.yyyy HH:mm:ss}]: {e} {e.Message}");
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
        /// Обновляет содержимое архива при серфинге
        /// </summary>
        private void LoadArchive()
        {
            if (!_archiveStatus) return;
            
            try
            {
                if (_findStatus == FindStatus.Finded)
                {
                    _archiveContent = new();
                    foreach (var file in _findResult)
                    {
                        if (FileFormats.SystemFiles.Contains(Path.GetFileName(file))) continue;

                        if (Reader.CanBeTransfered(file))
                        {
                            _archiveContent.Add(new ArchiveContent
                            {
                                Content = file,
                                TransferButtonState = (_transferFromArchive && file == _selectedArchiveFile) ? Visibility.Visible : Visibility.Collapsed,
                                OpenButtonState = (_openFromArchive && file == _selectedArchiveFile) ? Visibility.Visible : Visibility.Collapsed,
                                DeleteButtonState = (_openFromArchive && file == _selectedArchiveFile && Settings.Default.advancedMode) ? Visibility.Visible : Visibility.Collapsed,
                                OpenFolderState = (_findStatus == FindStatus.Finded && file == _selectedArchiveFile && _openFolderButton) ? Visibility.Visible : Visibility.Collapsed,
                                AnalyzeButtonState =
                                    ((!(FileFormats.MazatrolExtensions.Contains(Path.GetExtension(_selectedArchiveFile)?.ToLower(CultureInfo.InvariantCulture))
                                        || FileFormats.HeidenhainExtensions.Contains(Path.GetExtension(_selectedArchiveFile)?.ToLower(CultureInfo.InvariantCulture))
                                        || FileFormats.SinumerikExtensions.Contains(Path.GetExtension(_selectedArchiveFile)?.ToLower(CultureInfo.InvariantCulture))
                                         ) && Settings.Default.ncAnalyzer && Reader.CanBeTransfered(file))
                                     && _analyzeArchiveProgram && file == _selectedArchiveFile) ? Visibility.Visible : Visibility.Collapsed,
                                ShowWinExplorerButtonState = (_showWinExplorer && file == _selectedArchiveFile && _advancedMode) ? Visibility.Visible : Visibility.Collapsed,
                            });
                        }
                        else
                        {
                            _archiveContent.Add(new ArchiveContent
                            {
                                Content = file,
                                TransferButtonState = Visibility.Collapsed,
                                OpenButtonState = (_openFromArchive && file == _selectedArchiveFile) ? Visibility.Visible : Visibility.Collapsed,
                                DeleteButtonState = (_openFromArchive && file == _selectedArchiveFile && _advancedMode) ? Visibility.Visible : Visibility.Collapsed,
                                OpenFolderState = (_findStatus == FindStatus.Finded && file == _selectedArchiveFile) ? Visibility.Visible : Visibility.Collapsed,
                                AnalyzeButtonState = Visibility.Collapsed,
                                ShowWinExplorerButtonState = (_showWinExplorer && file == _selectedArchiveFile && _advancedMode) ? Visibility.Visible : Visibility.Collapsed,
                            });
                        }
                    }
                }
                else
                {
                    _archiveContent = new();
                    List<string> dirs = new();
                    List<string> files = new();
                    var findString = string.Empty;
                    dirs = Directory.EnumerateDirectories(_currentArchiveFolder).ToList();
                    files = Directory.EnumerateFiles(_currentArchiveFolder).ToList();

                    // вносим папки
                    if (dirs.Count > 0)
                    {
                        foreach (var folder in dirs)
                        {
                            var tempArchiveFolder = new ArchiveContent
                            {
                                Content = folder,
                                TransferButtonState = Visibility.Collapsed,
                                OpenButtonState = Visibility.Collapsed,
                                DeleteButtonState = Visibility.Collapsed,
                                OpenFolderState = Visibility.Collapsed,
                                AnalyzeButtonState = Visibility.Collapsed,
                                ShowWinExplorerButtonState = Visibility.Collapsed,
                            };
                            _archiveContent.Add(tempArchiveFolder);
                            if (tempArchiveFolder.Content == _selectedArchiveFile) _selectedArchiveFolder = tempArchiveFolder;
                        }
                    }
                    // вносим файлы
                    if (files.Count > 0)
                    {
                        foreach (var file in files)
                        {
                            if (FileFormats.SystemFiles.Contains(Path.GetFileName(file))) continue;

                            if (Reader.CanBeTransfered(file))
                            {
                                _archiveContent.Add(new ArchiveContent
                                {
                                    Content = file,
                                    TransferButtonState = (_transferFromArchive && file == _selectedArchiveFile) ? Visibility.Visible : Visibility.Collapsed,
                                    OpenButtonState = (_openFromArchive && file == _selectedArchiveFile) ? Visibility.Visible : Visibility.Collapsed,
                                    DeleteButtonState = (_openFromArchive && file == _selectedArchiveFile && Settings.Default.advancedMode) ? Visibility.Visible : Visibility.Collapsed,
                                    OpenFolderState = (_findStatus == FindStatus.Finded && file == _selectedArchiveFile && _openFolderButton) ? Visibility.Visible : Visibility.Collapsed,
                                    AnalyzeButtonState =
                                        ((!(FileFormats.MazatrolExtensions.Contains(Path.GetExtension(_selectedArchiveFile)?.ToLower(CultureInfo.InvariantCulture))
                                            || FileFormats.HeidenhainExtensions.Contains(Path.GetExtension(_selectedArchiveFile)?.ToLower(CultureInfo.InvariantCulture))
                                            || FileFormats.SinumerikExtensions.Contains(Path.GetExtension(_selectedArchiveFile)?.ToLower(CultureInfo.InvariantCulture))
                                             ) && Settings.Default.ncAnalyzer && Reader.CanBeTransfered(file))
                                         && _analyzeArchiveProgram && file == _selectedArchiveFile) ? Visibility.Visible : Visibility.Collapsed,
                                    ShowWinExplorerButtonState = (_showWinExplorer && file == _selectedArchiveFile && _advancedMode) ? Visibility.Visible : Visibility.Collapsed,
                                });
                            }
                            else
                            {
                                _archiveContent.Add(new ArchiveContent
                                {
                                    Content = file,
                                    TransferButtonState = Visibility.Collapsed,
                                    OpenButtonState = (_openFromArchive && file == _selectedArchiveFile) ? Visibility.Visible : Visibility.Collapsed,
                                    DeleteButtonState = (_openFromArchive && file == _selectedArchiveFile && _advancedMode) ? Visibility.Visible : Visibility.Collapsed,
                                    OpenFolderState = (_findStatus == FindStatus.Finded && file == _selectedArchiveFile) ? Visibility.Visible : Visibility.Collapsed,
                                    AnalyzeButtonState = Visibility.Collapsed,
                                    ShowWinExplorerButtonState = (_showWinExplorer && file == _selectedArchiveFile && _advancedMode) ? Visibility.Visible : Visibility.Collapsed,
                                });
                            }
                        }
                    }
                }

                if (archiveLV.ItemsSource == null || archiveLV.Items.Count == 0 || !_archiveContent.SequenceEqual(archiveLV.ItemsSource as List<ArchiveContent> ?? new List<ArchiveContent>()))
                {
                    archiveLV.Dispatcher.Invoke(() => archiveLV.ItemsSource = _archiveContent);
                } 
                    
                if (_needArchiveScroll)
                {
                    archiveLV.Dispatcher.Invoke(() => archiveLV.SelectedItem = _selectedArchiveFolder);
                    archiveLV.Dispatcher.Invoke(() => archiveLV.ScrollIntoView(_selectedArchiveFolder));
                    _needArchiveScroll = false;
                    archiveLV.Dispatcher.Invoke(() => archiveLV.SelectedItem = null);
                }
                archivePathTB.Dispatcher.Invoke(() => archivePathTB.Text = _currentArchiveFolder);
                archivePathTB.Dispatcher.Invoke(() => archivePathTB.ScrollToHorizontalOffset(double.MaxValue));

            }
            catch (UnauthorizedAccessException e)
            {
                SendMessage("Ошибка доступа.");
                WriteLog($"Ошибка доступа. {e} {e.Message} {e.StackTrace}");
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
        }

        /// <summary>
        /// Обновляет содержимое архива при поиске
        /// </summary>
        private async Task SearchInArchive()
        {
            
            if (_findStatus is FindStatus.InProgress) return;
            if (_archiveStatus)
            {
                try
                {
                    List<string> dirs = new();
                    List<string> files = new();
                    string[] allFiles;
                    var findString = string.Empty;
                    await findTextBox.Dispatcher.InvokeAsync(() => findString = findTextBox.Text);
                    switch (_findStatus)
                    {
                        case FindStatus.Find:
                            _stopSearch = false;
                            _findStatus = FindStatus.InProgress;
                            _archiveContent = new List<ArchiveContent>();
                            await archiveRootButton.Dispatcher.InvokeAsync(() => archiveRootButton.IsEnabled = false);
                            await archiveParentButton.Dispatcher.InvokeAsync(() => archiveParentButton.IsEnabled = false);
                            await findDialogButton.Dispatcher.InvokeAsync(() => findDialogButton.IsEnabled = false);
                            await settingsButton.Dispatcher.InvokeAsync(() => settingsButton.IsEnabled = false);
                            await refreshButton.Dispatcher.InvokeAsync(() => refreshButton.IsEnabled = false);
                            await cloudButton.Dispatcher.InvokeAsync(() => cloudButton.IsEnabled = false);
                            await archiveLV.Dispatcher.InvokeAsync(() => archiveLV.Visibility = Visibility.Collapsed);
                            await stopSearchButton.Dispatcher.InvokeAsync(() => stopSearchButton.Visibility = Visibility.Visible);
                            await findDialogButton.Dispatcher.InvokeAsync(() => findDialogButton.Visibility = Visibility.Collapsed);
                            var contentSearch = (bool)archiveContentSearchCB.Dispatcher.InvokeAsync(() => archiveContentSearchCB.IsChecked).Result;
                            
                            if (DebugMode) AddStatus(FindInProcess);
                            await archiveProgressBar.Dispatcher.InvokeAsync(() => archiveProgressBar.Visibility = Visibility.Visible);
                            if (contentSearch)
                            {
                                await archivePathTB.Dispatcher.InvokeAsync(() => archivePathTB.Text = $"Подсчет файлов...");
                                await Task.Run(() => 
                                {
                                    allFiles = Directory.GetFiles(_currentArchiveFolder, "*.*", SearchOption.AllDirectories);
                                    
                                    archiveProgressBar.Dispatcher.InvokeAsync(() => archiveProgressBar.Maximum = allFiles.Length);
                                    archiveProgressBar.Dispatcher.InvokeAsync(() => archiveProgressBar.Value = 0);
                                    archiveProgressBar.Dispatcher.InvokeAsync(() => archiveProgressBar.IsIndeterminate = false);
                                    for (var file = 0; file < allFiles.Length; file++)
                                    {
                                        archiveProgressBar.Dispatcher.InvokeAsync(() => archiveProgressBar.Value++);
                                        if (_stopSearch is true) break;
                                        if (!Reader.CanBeTransfered(allFiles[file])) continue;
                                        AddStatus($"Чтение: {allFiles[file]}");
                                        archivePathTB.Dispatcher.InvokeAsync(() => archivePathTB.Text = $"Поиск: \"{findString}\". {file + 1}/{allFiles.Length}/{files.Count}.");
                                        if (File.ReadLines(allFiles[file]).Any(line => line.Contains(findString)))
                                        {
                                            files.Add(allFiles[file]);
                                        }
                                        RemoveStatus($"Чтение: {allFiles[file]}");
                                    }
                                    archiveProgressBar.Dispatcher.InvokeAsync(() => archiveProgressBar.Maximum = 1);
                                    archiveProgressBar.Dispatcher.InvokeAsync(() => archiveProgressBar.Value = 0);
                                    archiveProgressBar.Dispatcher.InvokeAsync(() => archiveProgressBar.IsIndeterminate = true);
                                    
                                });
                            }
                            else
                            {
                                await archivePathTB.Dispatcher.InvokeAsync(() => archivePathTB.Text = $"Поиск файлов содержащих в названии \"{findTextBox.Text}\"...");
                                await Task.Run(() => 
                                {
                                    files = Directory
                                        .EnumerateFiles(_currentArchiveFolder, "*.*", SearchOption.AllDirectories)
                                        .Where(file => file.ToLower().Contains(findString, StringComparison.OrdinalIgnoreCase))
                                        .ToList();
                                });
                            }
                            break;
                        // возврат к результатам поиска
                        case FindStatus.Finded:
                            _archiveContent = new List<ArchiveContent>();
                            files = _findResult;
                            break;
                    }

                    await Task.Run(() =>
                    {
                        // вносим папки
                        if (dirs.Count > 0)
                        {
                            foreach (var folder in dirs)
                            {
                                var tempArchiveFolder = new ArchiveContent
                                {
                                    Content = folder,
                                    TransferButtonState = Visibility.Collapsed,
                                    OpenButtonState = Visibility.Collapsed,
                                    DeleteButtonState = Visibility.Collapsed,
                                    OpenFolderState = Visibility.Collapsed,
                                    AnalyzeButtonState = Visibility.Collapsed,
                                    ShowWinExplorerButtonState = Visibility.Collapsed,
                                };
                                _archiveContent.Add(tempArchiveFolder); 
                                if(tempArchiveFolder.Content == _selectedArchiveFile) _selectedArchiveFolder = tempArchiveFolder;
                            }
                        }
                        // вносим файлы
                        if (files.Count > 0)
                        {
                            foreach (var file in files)
                            {
                                if (FileFormats.SystemFiles.Contains(Path.GetFileName(file))) continue;
                            
                                if (Reader.CanBeTransfered(file))
                                {
                                    _archiveContent.Add(new ArchiveContent
                                    {
                                        Content = file,
                                        TransferButtonState = (_transferFromArchive && file == _selectedArchiveFile) ? Visibility.Visible : Visibility.Collapsed,
                                        OpenButtonState = (_openFromArchive && file == _selectedArchiveFile) ? Visibility.Visible : Visibility.Collapsed,
                                        DeleteButtonState = (_openFromArchive && file == _selectedArchiveFile && Settings.Default.advancedMode) ? Visibility.Visible : Visibility.Collapsed,
                                        OpenFolderState = (_findStatus == FindStatus.Finded && file == _selectedArchiveFile && _openFolderButton) ? Visibility.Visible : Visibility.Collapsed,
                                        AnalyzeButtonState =
                                            ((!(FileFormats.MazatrolExtensions.Contains(Path.GetExtension(_selectedArchiveFile)?.ToLower(CultureInfo.InvariantCulture))
                                                || FileFormats.HeidenhainExtensions.Contains(Path.GetExtension(_selectedArchiveFile)?.ToLower(CultureInfo.InvariantCulture))
                                                || FileFormats.SinumerikExtensions.Contains(Path.GetExtension(_selectedArchiveFile)?.ToLower(CultureInfo.InvariantCulture))
                                                 ) && Settings.Default.ncAnalyzer && Reader.CanBeTransfered(file))
                                             && _analyzeArchiveProgram && file == _selectedArchiveFile) ? Visibility.Visible : Visibility.Collapsed,
                                        ShowWinExplorerButtonState = (_showWinExplorer && file == _selectedArchiveFile && _advancedMode) ? Visibility.Visible : Visibility.Collapsed,
                                    });
                                }
                                else
                                {
                                    _archiveContent.Add(new ArchiveContent
                                    {
                                        Content = file,
                                        TransferButtonState = Visibility.Collapsed,
                                        OpenButtonState = (_openFromArchive && file == _selectedArchiveFile) ? Visibility.Visible : Visibility.Collapsed,
                                        DeleteButtonState = (_openFromArchive && file == _selectedArchiveFile && _advancedMode) ? Visibility.Visible : Visibility.Collapsed,
                                        OpenFolderState = (_findStatus == FindStatus.Finded && file == _selectedArchiveFile) ? Visibility.Visible : Visibility.Collapsed,
                                        AnalyzeButtonState = Visibility.Collapsed,
                                        ShowWinExplorerButtonState = (_showWinExplorer && file == _selectedArchiveFile && _advancedMode) ? Visibility.Visible : Visibility.Collapsed,
                                    });
                                }
                            }
                        }
                    });

                    if (_findStatus is FindStatus.InProgress)
                    {
                        _findResult = files;
                        _findStatus = FindStatus.Find;
                    }
                    await archiveLV.Dispatcher.InvokeAsync(() => archiveLV.IsEnabled = true);
                    await archiveLV.Dispatcher.InvokeAsync(() => archiveLV.ItemsSource = _archiveContent);
                    if (_needArchiveScroll)
                    {
                        await archiveLV.Dispatcher.InvokeAsync(() => archiveLV.SelectedItem = _selectedArchiveFolder);
                        await archiveLV.Dispatcher.InvokeAsync(() => archiveLV.ScrollIntoView(_selectedArchiveFolder));
                        _needArchiveScroll = false;
                        await archiveLV.Dispatcher.InvokeAsync(() => archiveLV.SelectedItem = null);
                    }

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
                        await archiveProgressBar.Dispatcher.InvokeAsync(() => archiveProgressBar.Visibility = Visibility.Collapsed);
                        await stopSearchButton.Dispatcher.InvokeAsync(() => stopSearchButton.Visibility = Visibility.Collapsed);
                        await findDialogButton.Dispatcher.InvokeAsync(() => findDialogButton.Visibility = Visibility.Visible);
                        await archiveRootButton.Dispatcher.InvokeAsync(() => archiveRootButton.IsEnabled = true);
                        await archiveParentButton.Dispatcher.InvokeAsync(() => archiveParentButton.IsEnabled = true);
                        await findDialogButton.Dispatcher.InvokeAsync(() => findDialogButton.IsEnabled = true);
                        await settingsButton.Dispatcher.InvokeAsync(() => settingsButton.IsEnabled = true);
                        await refreshButton.Dispatcher.InvokeAsync(() => refreshButton.IsEnabled = true);
                        await cloudButton.Dispatcher.InvokeAsync(() => cloudButton.IsEnabled = true);
                        
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
            if (_findStatus is FindStatus.InProgress) return;
            _archiveStatus = false;
            machineWatcher.EnableRaisingEvents = false;
            archiveWatcher.Created -= new FileSystemEventHandler(OnCreatedInArchive);
            archiveWatcher.Deleted -= new FileSystemEventHandler(OnDeletedInArchive);
            archiveWatcher.Renamed -= new RenamedEventHandler(OnRenamedInArchive);
            archiveButtonsPanel.Dispatcher.Invoke(() => archiveButtonsPanel.Visibility = Visibility.Collapsed);
            archiveLV.Dispatcher.Invoke(() => archiveLV.Visibility = Visibility.Collapsed);
            archiveConnectionIcon.Dispatcher.Invoke(() => archiveConnectionIcon.Kind = MaterialDesignThemes.Wpf.PackIconKind.FolderRemove);
            archiveConnectionIcon.Dispatcher.Invoke(() => archiveConnectionIcon.Foreground = _redBrush);
            if (!string.IsNullOrWhiteSpace(Settings.Default.archivePath))
                archiveProgressBar.Dispatcher.Invoke(() => archiveProgressBar.Visibility = Visibility.Visible);
            if (DebugMode) AddStatus(NoArchiveLabel);
            _needUpdateArchive = true;
        }

        /// <summary>
        /// Включает отображение архива
        /// </summary>
        private void TurnOnArchive()
        {
            if (_findStatus is FindStatus.InProgress) return;
            _archiveStatus = true;
            archiveWatcher.Path = _currentArchiveFolder;
            archiveWatcher.Created += new FileSystemEventHandler(OnCreatedInArchive);
            archiveWatcher.Deleted += new FileSystemEventHandler(OnDeletedInArchive);
            archiveWatcher.Renamed += new RenamedEventHandler(OnRenamedInArchive);
            archiveWatcher.EnableRaisingEvents = true;
            archiveButtonsPanel.Dispatcher.Invoke(() => archiveButtonsPanel.Visibility = Visibility.Visible);
            archiveLV.Dispatcher.Invoke(() => archiveLV.Visibility = Visibility.Visible);
            archiveProgressBar.Dispatcher.Invoke(() => archiveProgressBar.Visibility = Visibility.Collapsed);
            archiveConnectionIcon.Dispatcher.Invoke(() => archiveConnectionIcon.Kind = MaterialDesignThemes.Wpf.PackIconKind.FolderHome);
            archiveConnectionIcon.Dispatcher.Invoke(() => archiveConnectionIcon.Foreground = _greenBrush);
            if (DebugMode) RemoveStatus(NoArchiveLabel);
            _needUpdateArchive = false;
        }
        #endregion

        #region Представление сетевой папки 

        /// <summary>
        /// Обновляет содержимое сетевой папки
        /// </summary>
        private void LoadMachine()
        {
            if (!_machineStatus) return;
            _machineContent = new List<NcFile>();
            foreach (var file in Directory.GetFiles(Settings.Default.machinePath))
            {
                _machineContent.Add(new NcFile
                {
                    FullPath = file,
                    CheckButtonVisibility = (_transferFromMachine && file == _selectedMachineFile) ? Visibility.Visible : Visibility.Collapsed,
                    RenameButtonVisibility = (_renameOnMachine && file == _selectedMachineFile) ? Visibility.Visible : Visibility.Collapsed,
                    OpenButtonVisibility = (_openFromNcFolder && file == _selectedMachineFile) ? Visibility.Visible : Visibility.Collapsed,
                    DeleteButtonVisibility = (_deleteFromMachine && file == _selectedMachineFile) ? Visibility.Visible : Visibility.Collapsed,
                    AnalyzeButtonVisibility = 
                        ((!(FileFormats.MazatrolExtensions.Contains(Path.GetExtension(_selectedMachineFile)?.ToLower(CultureInfo.InvariantCulture))
                            || FileFormats.HeidenhainExtensions.Contains(Path.GetExtension(_selectedMachineFile)?.ToLower(CultureInfo.InvariantCulture))
                            || FileFormats.SinumerikExtensions.Contains(Path.GetExtension(_selectedMachineFile)?.ToLower(CultureInfo.InvariantCulture))
                             ) && Settings.Default.ncAnalyzer)
                            && _analyzeNcProgram && file == _selectedMachineFile ) 
                            && Reader.CanBeTransfered(_selectedMachineFile ?? string.Empty) 
                            ? Visibility.Visible : Visibility.Collapsed,
                });
            }

            //if (machineDG.ItemsSource == null || !_machineContent.SequenceEqual(machineDG.ItemsSource as List<NcFile> ?? new List<NcFile>()))
            //{
            //    machineDG.Dispatcher.InvokeAsync(() => machineDG.ItemsSource = _machineContent);
            //}
            machineDG.Dispatcher.InvokeAsync(() => machineDG.ItemsSource = _machineContent);
        }

        /// <summary>
        /// Выключает отображение сетевой папки
        /// </summary>
        private void TurnOffMachine()
        {
            _machineStatus = false;
            machineWatcher.EnableRaisingEvents = false;
            machineWatcher.Created -= new FileSystemEventHandler(OnCreatedInMachine);
            machineWatcher.Changed -= new FileSystemEventHandler(OnChangedInMachine);
            machineWatcher.Deleted -= new FileSystemEventHandler(OnDeletedInMachine);
            machineWatcher.Renamed -= new RenamedEventHandler(OnRenamedInMachine);
            machineDG.Dispatcher.Invoke(() => machineDG.Visibility = Visibility.Collapsed);
            if (!string.IsNullOrWhiteSpace(Settings.Default.machinePath))
                machineProgressBar.Dispatcher.Invoke(() => machineProgressBar.Visibility = Visibility.Visible);
            machineConnectionIcon.Dispatcher.Invoke(() => machineConnectionIcon.Foreground = _redBrush);
            machineConnectionIcon.Dispatcher.Invoke(() => machineConnectionIcon.Kind = MaterialDesignThemes.Wpf.PackIconKind.ServerNetworkOff);
            if (DebugMode) AddStatus(NoMachineLabel);
        }

        /// <summary>
        /// Включает отображение сетевой папки
        /// </summary>
        private void TurnOnMachine()
        {
            _machineStatus = true;
            machineWatcher.Created += new FileSystemEventHandler(OnCreatedInMachine);
            machineWatcher.Changed += new FileSystemEventHandler(OnChangedInMachine);
            machineWatcher.Deleted += new FileSystemEventHandler(OnDeletedInMachine);
            machineWatcher.Renamed += new RenamedEventHandler(OnRenamedInMachine);
            machineWatcher.EnableRaisingEvents = true;
            if (!_analyzeInfo) machineDG.Dispatcher.Invoke(() => machineDG.Visibility = Visibility.Visible);
            machineProgressBar.Dispatcher.Invoke(() => machineProgressBar.Visibility = Visibility.Collapsed);
            machineConnectionIcon.Dispatcher.Invoke(() => machineConnectionIcon.Kind = MaterialDesignThemes.Wpf.PackIconKind.ServerNetwork);
            machineConnectionIcon.Dispatcher.Invoke(() => machineConnectionIcon.Foreground = _greenBrush);
            if (DebugMode) RemoveStatus(NoMachineLabel);
        }
        #endregion

        #region Таблица деталей
        /// <summary>
        /// Выключает отображение таблицы
        /// </summary>
        private void TurnOffTable()
        {
            _tableStatus = false;
            tableConnectionIcon.Dispatcher.Invoke(() =>
            tableConnectionIcon.Kind = MaterialDesignThemes.Wpf.PackIconKind.TableRemove);
            tableConnectionIcon.Dispatcher.Invoke(() => tableConnectionIcon.Foreground = _redBrush);
            if (DebugMode) AddStatus(NoTableLabel);

        }

        /// <summary>
        /// Включает отображение таблицы
        /// </summary>
        private void TurnOnTable()
        {
            _tableStatus = true;
            tableConnectionIcon.Dispatcher.Invoke(() =>
            tableConnectionIcon.Kind = MaterialDesignThemes.Wpf.PackIconKind.Table);
            tableConnectionIcon.Dispatcher.Invoke(() => tableConnectionIcon.Foreground = _greenBrush);
            if (DebugMode) RemoveStatus(NoTableLabel);
        }

        private bool LoadTable()
        {
            if (_tableStatus)
            {
                try
                {
                    List<Detail> details = new();
                    //var statusOk = 0;
                    //var statusWarn = 0;

                    foreach (var line in Reader.ReadCsvTable(Settings.Default.tablePath).Skip(1))
                    {
                        var priority = int.TryParse(line.Split(';')[6].Split('.')[0], out var tPriority) ? tPriority : 1000;

                        //string time;
                        //if (double.TryParse(line.Replace('.', ',').Split(';')[8], out double tTime))
                        //{
                        //    time = $"{(int)tTime}м {(tTime - (int)tTime) * 60:N0}c";
                        //}
                        //else
                        //{
                        //    time = string.Empty;
                        //}

                        var order = line.Split(';')[5] + "   ";
                        var name = line.Split(';')[0];
                        var status = line.Split(';')[1];
                        var comment = "   " + line.Split(';')[4];

                        if (!string.IsNullOrEmpty(name))
                            details.Add(new Detail { Name = name, Status = status, Comment = comment, Order = order, Priority = priority});
                        switch (status)
                        {
                            case "Отработана":
                                //statusOk++;
                                break;
                            case "Требует проверки":
                                //statusWarn++;
                                break;
                        }
                    }
                    statusGB.Dispatcher.Invoke(() => statusGB.Header = $"Список заданий");
                    tableDG.Dispatcher.Invoke(() => tableDG.ItemsSource = details);
                    
                    return true;
                }
                catch (FileNotFoundException)
                {
                    return false;
                }
                catch (Exception ex)
                {
                    AddError(ex);
                    return false;
                }
            }
            return false;

        }
        #endregion

        #region Наблюдатель доступности
        private void WatchInfo()
        {
            while (true)
            {
                try
                {
                    if (Settings.Default.refreshInterval <= 0) continue;
                    dateTimeTB.Dispatcher.Invoke(() => dateTimeTB.Text = DateTime.Now.ToString("d MMMM yyyy г.  HH:mm"));

                    #region Проверка архива
                    if (!Reader.CheckPath(Settings.Default.archivePath))
                    {
                        if (_archiveStatus) TurnOffArchive();
                    }
                    else
                    {
                        if (_needUpdateArchive)
                        {
                            _archiveStatus = true;
                            archiveWatcher.EnableRaisingEvents = true;
                            TurnOnArchive();
                            LoadArchive();
                        } 
                        else if (_archiveStatus && !archiveWatcher.EnableRaisingEvents) archiveWatcher.EnableRaisingEvents = true; 
                    }

                    #endregion

                    #region Проверка промежуточной папки
                    if (!Reader.CheckPath(Settings.Default.tempPath))
                    {
                        _tempFolderStatus = false;
                        tempFolderConnectionIcon.Dispatcher.Invoke(() => tempFolderConnectionIcon.Kind = MaterialDesignThemes.Wpf.PackIconKind.LanDisconnect);
                        tempFolderConnectionIcon.Dispatcher.Invoke(() => tempFolderConnectionIcon.Foreground = _redBrush);
                        checkInfoLabel.Dispatcher.InvokeAsync(() => checkInfoLabel.Content = string.Empty);
                        if (DebugMode) AddStatus(NoTempFolderLabel);
                    }
                    else
                    {
                        _tempFolderStatus = true;
                        tempFolderConnectionIcon.Dispatcher.Invoke(() => tempFolderConnectionIcon.Kind = MaterialDesignThemes.Wpf.PackIconKind.LanConnect);
                        tempFolderConnectionIcon.Dispatcher.Invoke(() => tempFolderConnectionIcon.Foreground = _greenBrush);
                        checkInfoLabel.Dispatcher.InvokeAsync(() => checkInfoLabel.Content = $" | На проверке: {Directory.GetFiles(Settings.Default.tempPath, "*.info", SearchOption.AllDirectories).Length}");
                        if (DebugMode) RemoveStatus(NoTempFolderLabel);
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
                            machineWatcher.EnableRaisingEvents = true;
                            TurnOnMachine();
                            LoadMachine();
                        } 
                        else if (_machineStatus && !machineWatcher.EnableRaisingEvents) machineWatcher.EnableRaisingEvents = true;
                    }
                    #endregion

                    #region Проверка таблицы
                    if (!Reader.CheckPath(Settings.Default.tablePath) && !File.Exists(Settings.Default.tablePath))
                    {
                        TurnOffTable();
                    }
                    else
                    {
                        TurnOnTable();
                    }
                    #endregion

                    Thread.Sleep(Settings.Default.refreshInterval * 1000);
                }
                catch (Exception ex)
                {
                    AddError(ex);
                    Thread.Sleep(3000);
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
            MessageWindow messageWindow = new() {Owner = this};
            messageWindow.ShowDialog();
        }

        private void cloudButton_Click(object sender, RoutedEventArgs e)
        {
            const string cloudPath = @"\\hv-luga\nc\Резерв\Программы к станкам";
            if (!Directory.Exists(cloudPath)) return;
            _currentArchiveFolder = cloudPath;
            ChangeArchiveWatcher();
            LoadArchive();
        }

        private void settingsButton_Click(object sender, RoutedEventArgs e)
        {
            SettingsWindow settingsWindow = new() {Owner = this};
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
                    machineWatcher.Path = Settings.Default.machinePath;
                    TurnOnMachine();
                }
                LoadMachine();

                if (!Reader.CheckPath(Settings.Default.tablePath))
                {
                    TurnOffTable();
                }
                else
                {
                    TurnOnTable();
                }
                LoadTable();
                Settings.Default.needUpdate = false;
            }
        }

        private void helpButton_Click(object sender, RoutedEventArgs e)
        {
            HelpWindow helpWindow = new(_errorsList) {Owner = this};
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
            _transferFromArchive = false;
            _transferFromMachine = false;
            _renameOnMachine = false;
            _deleteFromMachine = false;
            _openFromArchive = false;
            _openFromNcFolder = false;
            _analyzeArchiveProgram = false;
            _analyzeNcProgram = false;
            _analyzeInfo = false;
            _showWinExplorer = false;
            _stopSearch = false;
            _openFolderButton = false;
            TurnOffArchive();
            TurnOnArchive();
            LoadArchive();
            TurnOffMachine();
            TurnOnMachine();
            LoadMachine();
        }

        private void minimizeButton_Click(object sender, RoutedEventArgs e)
        {
            WindowState = WindowState.Minimized;
        }
        #endregion

        private void ArchiveGroupBox_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (_advancedMode)
            {
                MessageBox.Show( $"archiveWatcher.EnableRaisingEvents = {archiveWatcher.EnableRaisingEvents}", "Молодой человек, это не для вас написано.", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }

        private void MachineGroupBox_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (_advancedMode)
            {
                MessageBox.Show( $"machineWatcher.EnableRaisingEvents = {machineWatcher.EnableRaisingEvents}", "Молодой человек, это не для вас написано.", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }

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
                _analyzeArchiveProgram = false;
                _showWinExplorer = false;
                _openFolderButton = false;
                _currentArchiveFolder = currentItem.Content;
                ChangeArchiveWatcher();
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
                _analyzeArchiveProgram = true;
                _showWinExplorer = true;
                _openFolderButton = true;
                _selectedArchiveFile = currentItem.Content;
                if (_findStatus is FindStatus.DontNeed)
                    LoadArchive();
                else
                {
                    Task.Run(SearchInArchive);
                }
                // LoadMachine() написать
            }
            else
            {
                SendMessage("Указанное расположение больше не существует в архиве. Попробуйте обновить информацию.");
            }
        }

        private void archiveGB_MouseLeftButtonDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (_advancedMode)
            {
                try
                {
                    Process.Start(new ProcessStartInfo("explorer", $"\"{Settings.Default.archivePath}\"") { UseShellExecute = true });
                    _transferFromArchive = false;
                    _transferFromMachine = false;
                    _deleteFromMachine = false;
                    _openFromArchive = false;
                    _renameOnMachine = false;
                    _openFromNcFolder = false;
                    _analyzeNcProgram = false;
                    _analyzeArchiveProgram = false;
                    _showWinExplorer = false;
                    _openFolderButton = false;
                    //LoadMachine();
                    //LoadArchive();
                }
                catch (Exception exception)
                {
                    AddError(exception);
                }
            }
        }

        private void archiveCheckGB_MouseLeftButtonDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (!_advancedMode) return;
            try
            {
                Process.Start(new ProcessStartInfo("explorer", $"\"{Settings.Default.tempPath}\"") { UseShellExecute = true });
                _transferFromArchive = false;
                _transferFromMachine = false;
                _deleteFromMachine = false;
                _openFromArchive = false;
                _renameOnMachine = false;
                _openFromNcFolder = false;
                _analyzeNcProgram = false;
                _analyzeArchiveProgram = false;
                _showWinExplorer = false;
                _openFolderButton = false;
                LoadMachine();
                LoadArchive();
            }
            catch (Exception exception)
            {
                AddError(exception);
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
                _analyzeArchiveProgram = false;
                _showWinExplorer = false;
                _selectedArchiveFile = _currentArchiveFolder;
                _needArchiveScroll = true;
                _openFolderButton = false;
                _currentArchiveFolder = Directory.GetParent(_currentArchiveFolder)?.ToString();
                ChangeArchiveWatcher();
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
            _analyzeArchiveProgram = false;
            _showWinExplorer = false;
            _openFolderButton = false;
            _currentArchiveFolder = Settings.Default.archivePath;
            ChangeArchiveWatcher();
            LoadArchive();
        }


        private void findButton_Click(object sender, RoutedEventArgs e)
        {
            if (!Reader.CheckPath(_currentArchiveFolder)) return;
            
            _findStatus = FindStatus.Find;
            Task.Run(SearchInArchive);
        }

        private void openFolderButton_Click(object sender, RoutedEventArgs e)
        {
            _currentArchiveFolder = Path.GetDirectoryName(_selectedArchiveFile);
            findDialogButton.Visibility = Visibility.Collapsed;
            returnButton.Visibility = Visibility.Visible;
            _findStatus = FindStatus.DontNeed;
            ChangeArchiveWatcher();
            LoadArchive();
        }

        private void returnButton_Click(object sender, RoutedEventArgs e)
        {
            _findStatus = FindStatus.Finded;
            findDialogButton.Visibility = Visibility.Visible;
            returnButton.Visibility = Visibility.Collapsed;
            archivePathTB.Text = "Результаты поиска";
            Task.Run(SearchInArchive);
        }

        private async void transferButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var transferFilePath = Settings.Default.autoRenameToMachine 
                    ? Reader.FindFreeName(_selectedArchiveFile, Settings.Default.machinePath) 
                    : Path.Combine(Settings.Default.machinePath, Path.GetFileName(_selectedArchiveFile)!);


                
                _transferFromArchive = false;
                _transferFromMachine = false;
                _deleteFromMachine = false;
                _openFromArchive = false;
                _renameOnMachine = false;
                _openFromNcFolder = false;
                _analyzeNcProgram = false;
                _analyzeArchiveProgram = false;
                _showWinExplorer = false;
                _openFolderButton = false;
                File.Copy(_selectedArchiveFile!, transferFilePath);
                WriteLog($"Отправлено на станок: \"{_selectedArchiveFile}\"");
                //Task.Run(() => LoadMachine()).GetAwaiter(); // почему не работает await ?
                LoadArchive();
                SendMessage(Settings.Default.autoRenameToMachine
                    ? $"Файл {Path.GetFileName(_selectedArchiveFile)} отправлен на станок и переименован в {Path.GetFileName(transferFilePath)}"
                    : $"На станок отправлен файл: {Path.GetFileName(_selectedArchiveFile)}");
            }
            catch (IOException)
            {
                SendMessage(!Reader.CheckPath(Settings.Default.machinePath)
                    ? $"Сетевая папка станка недоступна."
                    : $"В сетевой папке уже есть такой файл: {Path.GetFileName(_selectedArchiveFile)}");
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

        private void analyzeArchiveNCButton_Click(object sender, RoutedEventArgs e)
        {
            if (FileFormats.MazatrolExtensions.Contains(Path.GetExtension(_selectedArchiveFile)?.ToLower(CultureInfo.InvariantCulture)))
            {
                SendMessage("Файлы Mazatrol не поддерживаются");
            }
            else
            {
                _ = AnalyzeProgramAsync(_selectedArchiveFile);
            }
        }

        private void showWinExplorerButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                Process.Start(new ProcessStartInfo("explorer", $"\"{Directory.GetParent(_selectedArchiveFile)}\"") { UseShellExecute = true });
                _transferFromArchive = false;
                _transferFromMachine = false;
                _deleteFromMachine = false;
                _openFromArchive = false;
                _renameOnMachine = false;
                _openFromNcFolder = false;
                _analyzeNcProgram = false;
                _analyzeArchiveProgram = false;
                _showWinExplorer = false;
                _openFolderButton = false;
                //LoadMachine();
                //LoadArchive();
            }
            catch (Exception exception)
            {
                AddError(exception);
            }
        }

        private void openButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (Settings.Default.integratedImageViewer && FileFormats.ImageExtensions.Contains(Path.GetExtension(_selectedArchiveFile)?.ToLower()))
                {
                    image.Source = new BitmapImage(new Uri(_selectedArchiveFile!));
                    ImageDialogHost.IsOpen = true;
                }
                else if (Path.GetExtension(_selectedArchiveFile)?.ToLower() == ".lnk")
                {
                    IWshRuntimeLibrary.IWshShell shell = new IWshRuntimeLibrary.WshShell();
                    var shortcut = (IWshRuntimeLibrary.IWshShortcut)shell.CreateShortcut(_selectedArchiveFile);

                    if (Directory.Exists(shortcut.TargetPath))
                    {
                        _currentArchiveFolder = shortcut.TargetPath;
                        findDialogButton.Visibility = Visibility.Collapsed;
                        returnButton.Visibility = Visibility.Visible;
                        _findStatus = FindStatus.DontNeed;
                        ChangeArchiveWatcher();
                        LoadArchive();
                        
                    }
                    else
                    {
                        SendMessage("Путь по ссылке не существует.");
                    }
                }
                else
                {
                    Process.Start(new ProcessStartInfo(_selectedArchiveFile!) { UseShellExecute = true });
                }

                if (_findStatus is FindStatus.Finded) return;
                _transferFromArchive = false;
                _transferFromMachine = false;
                _deleteFromMachine = false;
                _openFromArchive = false;
                _renameOnMachine = false;
                _openFromNcFolder = false;
                _analyzeNcProgram = false;
                _analyzeArchiveProgram = false;
                _showWinExplorer = false;
                _openFolderButton = false;
                
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
                    _analyzeArchiveProgram = false;
                    _showWinExplorer = false;
                    _openFolderButton = false;
                    if (_findStatus is not FindStatus.Finded) return;
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

        private void confirmDeleteFromArchiveButton_Click(object sender, RoutedEventArgs e)
        {
            _selectedForDeletionArchiveFile = _selectedArchiveFile;
            confirmDeleteFromArchiveTB.Text = $"Удалить файл \"{Path.GetFileName(_selectedForDeletionArchiveFile)}\"?";
            ConfirmDeleteFromArchiveDialogHost.IsOpen = true;
        }
        private void closeDeleteFromArchiveDialogButton_Click(object sender, RoutedEventArgs e) => ConfirmDeleteFromArchiveDialogHost.IsOpen = false;
        private void deleteFromArchiveButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                ConfirmDeleteFromArchiveDialogHost.IsOpen = false;
                File.Delete(_selectedForDeletionArchiveFile);
                _deleteFromMachine = false;
                _transferFromArchive = false;
                _transferFromMachine = false;
                _openFromArchive = false;
                _renameOnMachine = false;
                _openFromNcFolder = false;
                _analyzeNcProgram = false;
                _analyzeArchiveProgram = false;
                _showWinExplorer = false;
                _openFolderButton = false;
                //LoadMachine();
                //LoadArchive();
                SendMessage($"Из архива удален файл: {Path.GetFileName(_selectedForDeletionArchiveFile)}");
            }
            catch (Exception exception)
            {
                WriteLog($"Ошибка при удалении файла");
                AddError(exception);
            }
        }

        private void closeCheckDialogButton_Click(object sender, RoutedEventArgs e)
        {
            ConfirmCheckDialog.IsOpen = false;
        }

        private void checkButton_Click(object sender, RoutedEventArgs e)
        {
            ConfirmCheckDialog.IsOpen = true;
        }

        private void archiveContentSearchCB_Checked(object sender, RoutedEventArgs e)
        {
            HintAssist.SetHint(findTextBox, "Содержимое");
            findTextBox.Focus();
        }

        private void archiveContentSearchCB_Unchecked(object sender, RoutedEventArgs e)
        {
            HintAssist.SetHint(findTextBox, "Название");
            findTextBox.Focus();
        }

        private void stopSearchButton_Click(object sender, RoutedEventArgs e)
        {
            _stopSearch = true;
        }

        private void findDialogButton_Click(object sender, RoutedEventArgs e)
        {
            findTextBox.Focus();
        }

        private void closeImageDialogButton_Click(object sender, RoutedEventArgs e)
        {
            ImageDialogHost.IsOpen = false;
        }
        #endregion

        #region Сетевая папка

        private async void machineDG_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            //var swMain = Stopwatch.StartNew();
            var item = (sender as ListView)?.SelectedItem;
            if (item is null) return;
            var currentItem = (NcFile)item;
            if (!File.Exists(currentItem.FullPath)) return;
            _transferFromMachine = true;
            _deleteFromMachine = true;
            _renameOnMachine = true;
            _transferFromArchive = false;
            _openFromArchive = false;
            _openFromNcFolder = true;
            _analyzeNcProgram = true;
            _analyzeArchiveProgram = false;
            _showWinExplorer = false;
            _openFolderButton = false;
            _selectedMachineFile = currentItem.FullPath;
            kostyl.Text = currentItem.Extension;
            await Task.Run(() => LoadMachine());
        }

        private void machineGB_MouseLeftButtonDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (!_advancedMode) return;
            try
            {
                Process.Start(new ProcessStartInfo("explorer", $"\"{Settings.Default.machinePath}\"") { UseShellExecute = true });
                _transferFromArchive = false;
                _transferFromMachine = false;
                _deleteFromMachine = false;
                _openFromArchive = false;
                _renameOnMachine = false;
                _openFromNcFolder = false;
                _analyzeNcProgram = false;
                _analyzeArchiveProgram = false;
                _showWinExplorer = false;
                _openFolderButton = false;
                LoadMachine();
                LoadArchive();
            }
            catch (Exception exception)
            {
                AddError(exception);
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
                var newName = Path.Combine(Settings.Default.machinePath, newNameTB.Text) + kostyl.Text;
                FileSystem.Rename(_selectedMachineFile, newName);
                //LoadMachine();
                //LoadArchive();
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

        private void applyCheckButton_Click(object sender, RoutedEventArgs e)
        {
            // формирование пути сохранения
            
            try
            {
                var tempProgramFolder = Path.Combine(Settings.Default.tempPath, Reader.CreateTempName(_selectedMachineFile, Reader.GetFileNameOptions.OnlyNcName));
                if (!Directory.Exists(tempProgramFolder))
                {
                    Directory.CreateDirectory(tempProgramFolder);
                }
                var tempName = Path.Combine(tempProgramFolder,
                    (Settings.Default.autoRenameToMachine
                        ? Reader.CreateTempName(_selectedMachineFile) // формирование нового имени файла
                        : Path.GetFileName(_selectedMachineFile))!);  // сохранение с текущим именем файла
                var infoFile = tempName + ".info";
                var info = CheckReasonCB.SelectedItem.ToString() == NewOrFixProgramReason ? "Замена" : "Вариант";
                if (!string.IsNullOrWhiteSpace(ReasonCommentTB.Text)) info += $"\nКомментарий: {ReasonCommentTB.Text}";

                if (!_tempFolderStatus)
                {
                    throw new IOException();
                }
                File.Copy(_selectedMachineFile!, tempName, true);
                using (var writer = File.CreateText(infoFile))
                {
                    writer.Write(info);
                }
                _transferFromMachine = false;
                //LoadMachine();
                //LoadArchive();
                if (!File.Exists(tempName)) return;
                ReasonCommentTB.Text = string.Empty;
                File.Delete(_selectedMachineFile);
                ConfirmCheckDialog.IsOpen = false;
                _transferFromArchive = false;
                _transferFromMachine = false;
                _deleteFromMachine = false;
                _openFromArchive = false;
                _renameOnMachine = false;
                _openFromNcFolder = false;
                _analyzeNcProgram = false;
                _analyzeArchiveProgram = false;
                _showWinExplorer = false;
                _openFolderButton = false;
                //LoadMachine();
                SendMessage($"Отправлен на проверку и очищен из сетевой папки файл: {tempName}");
                WriteLog($"Отправлено на проверку \"{_selectedMachineFile}\" -> \"{tempName}\"");
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
                WriteLog($"Необработанное исключение при отправке на проверку");
                AddError(exception);
            }
        }

        private void confirmDeleteFromMachineButton_Click(object sender, RoutedEventArgs e)
        {
            _selectedForDeletionMachineFile = _selectedMachineFile;
            confirmDeleteFromMachineTB.Text = $"Удалить файл \"{Path.GetFileName(_selectedForDeletionMachineFile)}\"?";
            ConfirmDeleteFromMachineDialogHost.IsOpen = true;
        }
        private void closeDeleteFromMachineDialogButton_Click(object sender, RoutedEventArgs e) => ConfirmDeleteFromMachineDialogHost.IsOpen = false;

        private void deleteFromMachineButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                ConfirmDeleteFromMachineDialogHost.IsOpen = false;
                File.Delete(_selectedForDeletionMachineFile);
                _deleteFromMachine = false;
                _transferFromArchive = false;
                _transferFromMachine = false;
                _openFromArchive = false;
                _renameOnMachine = false;
                _openFromNcFolder = false;
                _analyzeNcProgram = false;
                _analyzeArchiveProgram = false;
                _showWinExplorer = false;
                _openFolderButton = false;
                //Task.Run(() => LoadMachine()).GetAwaiter();
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
                if (Settings.Default.integratedImageViewer && FileFormats.ImageExtensions.Contains(Path.GetExtension(_selectedMachineFile)?.ToLower()))
                {
                    image.Source = new BitmapImage(new Uri(_selectedMachineFile!));
                    ImageDialogHost.IsOpen = true;
                }
                else
                {
                    Process.Start(new ProcessStartInfo(_selectedMachineFile!) { UseShellExecute = true });
                }
                
                _transferFromArchive = false;
                _transferFromMachine = false;
                _deleteFromMachine = false;
                _openFromArchive = false;
                _renameOnMachine = false;
                _openFromNcFolder = false;
                _analyzeNcProgram = false;
                _analyzeArchiveProgram = false;
                _showWinExplorer = false;
                _openFolderButton = false;
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
                    _analyzeArchiveProgram = false;
                    _showWinExplorer = false;
                    _openFolderButton = false;
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

            if (FileFormats.MazatrolExtensions.Contains(Path.GetExtension(_selectedMachineFile)?.ToLower(CultureInfo.InvariantCulture)))
            {
                SendMessage("Файлы Mazatrol не поддерживаются");
            }
            else
            {
                _ = AnalyzeProgramAsync(_selectedMachineFile);
            }
            

            //List<string> temp = new();
            //foreach (var item in analyze)
            //{
            //    temp.Add($"T{item.Position} {item.Comment}{(item.LengthCompensation is null ? string.Empty : $" H{item.LengthCompensation}")}{(item.RadiusCompensation is null ? string.Empty : $" D{item.RadiusCompensation}")}");
            //}
            //MessageBox.Show($"{coordinates}\n{temp}", programType);
        }

        private async Task AnalyzeProgramAsync(string program)
        {
            _analyzeInfo = true;
            machineDG.Visibility = Visibility.Collapsed;
            machineProgressBar.Visibility = Visibility.Visible;
            analyzeDG.Visibility = Visibility.Collapsed;
            analyzeGB.Visibility = Visibility.Collapsed;
            analyzeProgramTypeTB.Dispatcher.Invoke(() => analyzeProgramTypeTB.Text = string.Empty);
            analyzeProgramCoordinatesTB.Dispatcher.Invoke(() => analyzeProgramCoordinatesTB.Text = string.Empty);
            analyzeResultTB.Dispatcher.Invoke(() => analyzeResultTB.Text = string.Empty);
            await Task.Run(() =>
            {
                var analyze = Reader.AnalyzeProgram(
                    program, 
                    out var programType, 
                    out var coordinates, 
                    out var warningsH, 
                    out var warningsD,
                    out var warningsBracket,
                    out var warningsDots,
                    out var warningsEmptyAddress,
                    out var warningsCoolant,
                    out var warningsPolar,
                    out var warningsCyclesCancel,
                    out var warningsMacroCallCancel,
                    out var warningsCustomCyclesCancel,
                    out var warningsFeedType,
                    out var warningsIncrement,
                    out var warningStartPercent,
                    out var warningEndPercent,
                    out var warningEndProgram,
                    out var warningsExcessText
                    );
                analyzeDG.Dispatcher.Invoke(() => analyzeDG.ItemsSource = analyze);
                analyzeProgramTypeTB.Dispatcher.Invoke(() => analyzeProgramTypeTB.Text = programType);
                analyzeProgramCoordinatesTB.Dispatcher.Invoke(() => analyzeProgramCoordinatesTB.Text = coordinates);
                switch (warningStartPercent)
                {
                    case true when !warningEndPercent:
                        analyzeResultTB.Dispatcher.Invoke(() => analyzeResultTB.Text += $"Отсутствует процент в начале УП;\n\n");
                        break;
                    case false when warningEndPercent:
                        analyzeResultTB.Dispatcher.Invoke(() => analyzeResultTB.Text += $"Отсутствует процент в конце УП;\n\n");
                        break;
                    case true when warningEndPercent:
                        analyzeResultTB.Dispatcher.Invoke(() => analyzeResultTB.Text += $"Отсутствуют проценты в начале и в конце УП;\n\n");
                        break;
                }

                if (warningEndProgram)
                {
                    analyzeResultTB.Dispatcher.Invoke(() => analyzeResultTB.Text += $"Отсутствует команда завершения программы: ( M30 / M99 );\n\n");
                }
                if (warningsH.Count > 0)
                {
                    analyzeResultTB.Dispatcher.Invoke(() => analyzeResultTB.Text += $"Несовпадений корретора на длину: {warningsH.Count}\n{string.Join('\n', warningsH)}\n\n");
                }
                if (warningsD.Count > 0)
                {
                    analyzeResultTB.Dispatcher.Invoke(() => analyzeResultTB.Text += $"Несовпадений корретора на радиус: {warningsD.Count}\n{string.Join('\n', warningsD)}\n\n");
                }
                if (warningsBracket.Count > 0)
                {
                    analyzeResultTB.Dispatcher.Invoke(() => analyzeResultTB.Text += $"Несовпадений скобок: {warningsBracket.Count}\n{string.Join('\n', warningsBracket)}\n\n");
                }
                if (warningsDots.Count > 0)
                {
                    analyzeResultTB.Dispatcher.Invoke(() => analyzeResultTB.Text += $"Лишние точки: {warningsDots.Count}\n{string.Join('\n', warningsDots)}\n\n");
                }
                if (warningsEmptyAddress.Count > 0)
                {
                    analyzeResultTB.Dispatcher.Invoke(() => analyzeResultTB.Text += $"Пустые адреса: {warningsEmptyAddress.Count}\n{string.Join('\n', warningsEmptyAddress)}\n\n");
                }
                if (warningsCoolant.Count > 0)
                {
                    analyzeResultTB.Dispatcher.Invoke(() => analyzeResultTB.Text += $"Работа без охлаждения: {warningsCoolant.Count}\n{string.Join('\n', warningsCoolant)}\n\n");
                }
                if (warningsPolar.Count > 0)
                {
                    analyzeResultTB.Dispatcher.Invoke(() => analyzeResultTB.Text += $"Не выключен G16: {warningsPolar.Count}\n{string.Join('\n', warningsPolar)}\n\n");
                }
                if (warningsCyclesCancel.Count > 0)
                {
                    analyzeResultTB.Dispatcher.Invoke(() => analyzeResultTB.Text += $"Не отменен цикл: {warningsCyclesCancel.Count}\n{string.Join('\n', warningsCyclesCancel)}\n\n");
                }
                if (warningsMacroCallCancel.Count > 0)
                {
                    analyzeResultTB.Dispatcher.Invoke(() => analyzeResultTB.Text += $"Не отменен макро вызов: {warningsMacroCallCancel.Count}\n{string.Join('\n', warningsMacroCallCancel)}\n\n");
                }
                if (warningsCustomCyclesCancel.Count > 0)
                {
                    analyzeResultTB.Dispatcher.Invoke(() => analyzeResultTB.Text += $"Лишний текст в отмене макро вызова: {warningsCustomCyclesCancel.Count}\n{string.Join('\n', warningsCustomCyclesCancel)}\n\n");
                }
                if (warningsFeedType.Count > 0)
                {
                    analyzeResultTB.Dispatcher.Invoke(() => analyzeResultTB.Text += $"Оставлена подача мм/об: {warningsFeedType.Count}\n{string.Join('\n', warningsFeedType)}\n\n");
                }
                if (warningsIncrement.Count > 0)
                {
                    analyzeResultTB.Dispatcher.Invoke(() => analyzeResultTB.Text += $"Оставлено инкрементное движение: {warningsIncrement.Count}\n{string.Join('\n', warningsIncrement)}\n\n");
                }
                if (warningsExcessText.Count > 0)
                {
                    analyzeResultTB.Dispatcher.Invoke(() => analyzeResultTB.Text += $"Лишний текст: {warningsExcessText.Count}\n {string.Join('\n', warningsExcessText)}");
                }
            });
            analyzeDG.Visibility = Visibility.Visible;
            analyzeGB.Visibility = Visibility.Visible;
            machineProgressBar.Visibility = Visibility.Collapsed;
            analyzeGrid.Visibility = Visibility.Visible;
        }

        private void analyzeOkButton_Click(object sender, RoutedEventArgs e)
        {
            _analyzeInfo = false;
            analyzeDG.ItemsSource = new List<NcToolInfo>();
            analyzeResultTB.Text = string.Empty;
            analyzeProgramTypeTB.Text = string.Empty;
            analyzeProgramCoordinatesTB.Text = string.Empty;
            analyzeGrid.Visibility = Visibility.Collapsed;
            analyzeDG.Visibility = Visibility.Collapsed;
            analyzeGB.Visibility = Visibility.Collapsed;

            if (_machineStatus)
            {
                machineDG.Visibility = Visibility.Visible;
            }
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

        private void ChangeArchiveWatcher()
        {
            archiveWatcher.EnableRaisingEvents = false;
            archiveWatcher.Created -= new FileSystemEventHandler(OnCreatedInArchive);
            archiveWatcher.Deleted -= new FileSystemEventHandler(OnDeletedInArchive);
            archiveWatcher.Renamed -= new RenamedEventHandler(OnRenamedInArchive);
            archiveWatcher.Path = _currentArchiveFolder;
            archiveWatcher.Created += new FileSystemEventHandler(OnCreatedInArchive);
            archiveWatcher.Deleted += new FileSystemEventHandler(OnDeletedInArchive);
            archiveWatcher.Renamed += new RenamedEventHandler(OnRenamedInArchive);
            archiveWatcher.EnableRaisingEvents = true;
        }

        #region Нормативы

        private void SetupEndTp_SelectedTimeChanged(object sender, RoutedPropertyChangedEventArgs<DateTime?> e)
        {
            ProductionStartTp.SelectedTime = SetupEndTp.SelectedTime;
        }

        private void CalcSetupButton_Click(object sender, RoutedEventArgs e)
        {
            var dayShiftFirstBreak = DateTime.Today + TimeSpan.FromHours(9) ;
            var dayShiftSecondBreak = DateTime.Today + TimeSpan.FromHours(12) + TimeSpan.FromMinutes(30);
            var dayShiftThirdBreak = DateTime.Today + TimeSpan.FromHours(15) + TimeSpan.FromMinutes(15);
            var nightShiftFirstBreak = DateTime.Today + TimeSpan.FromHours(22) + TimeSpan.FromMinutes(30);
            var nightShiftSecondBreak = DateTime.Today + TimeSpan.FromHours(1) + TimeSpan.FromMinutes(30);
            var nightShiftThirdBreak = DateTime.Today + TimeSpan.FromHours(4) + TimeSpan.FromMinutes(30);

            var reduced = false;

            if (SetupStartTp.SelectedTime == null)
            {
                SetupInformationTb.Text = $"Некорректно указано начальное время наладки";
                return;
            }

            if (SetupEndTp.SelectedTime == null)
            {
                SetupInformationTb.Text = $"Некорректно указано конечное время наладки";
                return;
            }
            var startSetupTime = SetupStartTp.SelectedTime.Value;
            var endSetupTime = SetupEndTp.SelectedTime.Value;

            if (startSetupTime == endSetupTime)
            {
                SetupInformationTb.Text = $"Время начала и окончания совпадают.";
                return;
            }

            if (startSetupTime > endSetupTime)
            {
                nightShiftSecondBreak = nightShiftSecondBreak.AddDays(1);
                nightShiftThirdBreak = nightShiftThirdBreak.AddDays(1);
                endSetupTime = endSetupTime.AddDays(1);
            }

            var setupFullTime = (endSetupTime - startSetupTime).TotalMinutes;

            var message = string.Empty;

            message = $"Общее время наладки составило {setupFullTime} минут.\n";

            if (dayShiftFirstBreak > startSetupTime && dayShiftFirstBreak <= endSetupTime)
            {
                reduced = true;
                setupFullTime -= 15;
                message += "Наладка происходила во время утреннего перерыва на чай, вычтено 15 минут.\n";
            }

            if (dayShiftSecondBreak > startSetupTime && dayShiftSecondBreak <= endSetupTime)
            {
                reduced = true;
                setupFullTime -= 30;
                message += "Наладка происходила во время обеда, вычтено 30 минут.\n";
            }

            if (dayShiftThirdBreak > startSetupTime && dayShiftThirdBreak <= endSetupTime)
            {
                reduced = true;
                setupFullTime -= 15;
                message += "Наладка происходила во время дневного чая, вычтено 15 минут.\n";
            }

            if (nightShiftFirstBreak > startSetupTime && nightShiftFirstBreak <= endSetupTime)
            {
                reduced = true;
                setupFullTime -= 30;
                message += "Наладка происходила во время вечернего перерыва на чай, вычтено 30 минут.\n";
            }

            if (nightShiftSecondBreak > startSetupTime && nightShiftSecondBreak <= endSetupTime)
            {
                reduced = true;
                setupFullTime -= 30;
                message += "Наладка происходила во время ночного обеда, вычтено 30 минут.\n";
            }

            if (nightShiftThirdBreak > startSetupTime && nightShiftThirdBreak <= endSetupTime)
            {
                reduced = true;
                setupFullTime -= 30;
                message += "Наладка происходила во ночного чая, вычтено 30 минут.\n";
            }

            if (double.TryParse(SetupDowntimeTp.Text.Replace('.', ','), out var setupDowntime))
            {
                reduced = true;
                setupFullTime -= setupDowntime;
                message += $"Вычтен простой: {setupDowntime:N0} мин.\n";
            }


            //switch (FixJawsCb.IsChecked)
            //{
            //    case true when MakeAccessoriesCb.IsChecked is true:
            //        reduced = true;
            //        setupFullTime -= 60;
            //        message += "Из наладки вычтено 60 минут на расточку кулачков и изготовление оснастки.\n";
            //        break;
            //    case true when MakeAccessoriesCb.IsChecked is not true:
            //        reduced = true;
            //        setupFullTime -= 30;
            //        message += "Из наладки вычтено 30 минут на расточку кулачков.\n";
            //        break;
            //    default:
            //    {
            //        if (MakeAccessoriesCb.IsChecked is true && FixJawsCb.IsChecked is not true)
            //        {
            //            reduced = true;
            //            setupFullTime -= 30;
            //            message += "Из наладки вычтено 30 минут на изготовление оснастки.\n";
            //        }

            //        break;
            //    }
            //}

            if (reduced) message += $"Фактическое время наладки с учетом простоев составило {setupFullTime} минут.\n";


            if (!double.TryParse(SetupNormTp.Text.Replace('.', ','), out var setupNormTime))
            {
                SetupInformationTb.Text = "Некорректно указан норматив.";
                return;
            }
            var productivity = setupNormTime / setupFullTime * 100;
            message += $"Выполнение нормы: {productivity:N0}%.";

            SetupInformationTb.Text = message.Trim();
            SetupResultTb.Text = $"{productivity:N0}";
            SetupResultTb.Visibility = Visibility.Visible;
            SetupResultTb.Focus();
        }

        private void CalcProductionButton_Click(object sender, RoutedEventArgs e)
        {
            var dayShiftFirstBreak = DateTime.Today + TimeSpan.FromHours(9) ;
            var dayShiftSecondBreak = DateTime.Today + TimeSpan.FromHours(12) + TimeSpan.FromMinutes(30);
            var dayShiftThirdBreak = DateTime.Today + TimeSpan.FromHours(15) + TimeSpan.FromMinutes(15);
            var nightShiftFirstBreak = DateTime.Today + TimeSpan.FromHours(22) + TimeSpan.FromMinutes(30);
            var nightShiftSecondBreak = DateTime.Today + TimeSpan.FromHours(1) + TimeSpan.FromMinutes(30);
            var nightShiftThirdBreak = DateTime.Today + TimeSpan.FromHours(4) + TimeSpan.FromMinutes(30);

            var reduced = false;

            if (ProductionStartTp.SelectedTime == null)
            {
                ProductionInformationTb.Text = $"Некорректно указано начальное время изготовления";
                return;
            }

            if (ProductionEndTp.SelectedTime == null)
            {
                ProductionInformationTb.Text = $"Некорректно указано конечное время изготовления";
                return;
            }

            if (!int.TryParse(PartsCountTb.Text, out var partsCount))
            {
                ProductionInformationTb.Text = $"Некорректно указано количество деталей";
                return;
            }
            var startProductionTime = ProductionStartTp.SelectedTime.Value;
            var endProductionTime = ProductionEndTp.SelectedTime.Value;

            if (startProductionTime == endProductionTime)
            {
                ProductionInformationTb.Text = $"Время начала и окончания совпадают.";
                return;
            }

            if (startProductionTime > endProductionTime)
            {
                nightShiftSecondBreak = nightShiftSecondBreak.AddDays(1);
                nightShiftThirdBreak = nightShiftThirdBreak.AddDays(1);
                endProductionTime = endProductionTime.AddDays(1);
            }

            var productionFullTime = (endProductionTime - startProductionTime).TotalMinutes;

            var message = $"Общее время изготовления составило {productionFullTime} минут.\n";

            if (dayShiftFirstBreak > startProductionTime && dayShiftFirstBreak <= endProductionTime)
            {
                reduced = true;
                productionFullTime -= 15;
                message += "Изготовление происходило во время утреннего перерыва на чай, вычтено 15 минут.\n";
            }

            if (dayShiftSecondBreak > startProductionTime && dayShiftSecondBreak <= endProductionTime)
            {
                reduced = true;
                productionFullTime -= 30;
                message += "Изготовление происходило во время обеда, вычтено 30 минут.\n";
            }

            if (dayShiftThirdBreak > startProductionTime && dayShiftThirdBreak <= endProductionTime)
            {
                reduced = true;
                productionFullTime -= 15;
                message += "Изготовление происходило во дневного чая, вычтено 15 минут.\n";
            }

            if (nightShiftFirstBreak > startProductionTime && nightShiftFirstBreak <= endProductionTime)
            {
                reduced = true;
                productionFullTime -= 30;
                message += "Изготовление происходило во время вечернего перерыва на чай, вычтено 30 минут.\n";
            }

            if (nightShiftSecondBreak > startProductionTime && nightShiftSecondBreak <= endProductionTime)
            {
                reduced = true;
                productionFullTime -= 30;
                message += "Изготовление происходило во время ночного обеда, вычтено 30 минут.\n";
            }

            if (nightShiftThirdBreak > startProductionTime && nightShiftThirdBreak <= endProductionTime)
            {
                reduced = true;
                productionFullTime -= 30;
                message += "Изготовление происходило во ночного чая, вычтено 30 минут.\n";
            }

            if (double.TryParse(ProductionDowntimeTb.Text.Replace(',','.'), out var productionDowntime))
            {
                reduced = true;
                if (productionDowntime > productionFullTime)
                {
                    ProductionInformationTb.Text = $"Время простоя превышает время изготовления.";
                    return;
                }
                productionFullTime -= productionDowntime;
                message += $"Вычтен простой: {productionDowntime:N0} мин.\n";
            }

            //string[] machineTimeTbText = ProductionMachineTimeTb.Text.Split(':');
            //if (machineTimeTbText.Length != 2) return;
            //double machineTime = double.Parse(machineTimeTbText[0].Replace('_','0')) + double.Parse(machineTimeTbText[1].Replace('_','0')) / 60;
            //message = $"Машинное время: {machineTime.ToString("F2").Replace(",00","").Replace(',','.')}\n";


            if (!double.TryParse(ProductionNormTb.Text.Replace('.', ','), out var productionNormTime))
            {
                ProductionInformationTb.Text = $"Некорректно указан норматив.";
                return;
            }

            //double setupTime = 0;

            //if (Settings.Default.machinePath.EndsWith("GS-1500"))
            //{
            //    message += $"Базовое время на замену 2 минуты.";
            //    setupTime += 2;
            //}
            //else
            //{
            //    message += $"Базовое время на замену 1 минута.";
            //    setupTime += 1;
            //}

            //if (DirectlyCheckingCb.IsChecked is true)
            //{
            //    message += $"На установку детали добавлено 30 секунд для обмеров в станке.";
            //    setupTime += 0.5;
            //}

            //if (UsingAccessoriesCb.IsChecked is true)
            //{
            //    message += $"На установку детали добавлено 30 секунд для установки в оснастку.";
            //    setupTime += 0.5;
            //}

            //if (SetupEveryPartCb.IsChecked is true)
            //{
            //    message += $"На установку детали добавлено 5 минут для привязки каждой детали.";
            //    setupTime += 5;
            //}

            //if (UsingCraneCb.IsChecked is true)
            //{
            //    message += $"На установку детали добавлено 5 минут для установки детали тельфером.";
            //    setupTime += 5;
            //}

            var productionNormFullTime = productionNormTime * partsCount;
            message += $"Норматив на выполнение партии: {productionNormFullTime:N0} минут.\n";
            if (reduced) message += $"Фактическое время изготовления с учетом простоев составило {productionFullTime:N0} минут.\n";


            var productivity = productionNormFullTime / productionFullTime * 100;
            message += $"Выполнение нормы: {productivity:N0}%";

            ProductionInformationTb.Text = message.Trim();
            ProductionResultTb.Text = $"{productivity:N0}";
            ProductionResultTb.Visibility = Visibility.Visible;
            ProductionResultTb.Focus();
        }


        #endregion

        
    }
}