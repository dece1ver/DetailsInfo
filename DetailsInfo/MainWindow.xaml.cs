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
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using MaterialDesignThemes.Wpf;
using MimeKit;
using Application = System.Windows.Application;
using ListView = System.Windows.Controls.ListView;
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

        private readonly List<string> _status = new();

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


        public MainWindow()
        {
            InitializeComponent();
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        }


        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            
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

        private async Task LoadInfoAsync(bool start = false)
        {
            await Task.Run(() =>
            {
                if (!Reader.CheckPath(Settings.Default.archivePath)) TurnOffArchive();
                if (_archiveStatus) archiveConnectionIcon.Dispatcher.Invoke(() => archiveConnectionIcon.Foreground = _greenBrush);
                _ = LoadArchive();

                if (!Reader.CheckPath(Settings.Default.machinePath)) TurnOffMachine();
                if (_machineStatus) machineConnectionIcon.Dispatcher.Invoke(() => machineConnectionIcon.Foreground = _greenBrush);
                LoadMachine();

                if (!Reader.CheckPath(Settings.Default.tempPath)) _tempFolderStatus = false;
                if (_tempFolderStatus) tempFolderConnectionIcon.Dispatcher.Invoke(() => tempFolderConnectionIcon.Foreground = _greenBrush);

                if (!Reader.CheckPath(Settings.Default.tablePath) && !File.Exists(Settings.Default.tablePath)) TurnOffTable();
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
                    emailConnectionIcon.Dispatcher.Invoke(() => emailConnectionIcon.Kind = MaterialDesignThemes.Wpf.PackIconKind.EmailOff);
                    emailConnectionIcon.Dispatcher.Invoke(() => emailConnectionIcon.Foreground = _redBrush);
                    AddError(ex, AuthenticationException, false);
                }
                catch (TimeoutException ex)
                {
                    if (DebugMode) AddStatus(TimeoutException);
                    emailConnectionIcon.Dispatcher.Invoke(() => emailConnectionIcon.Kind = MaterialDesignThemes.Wpf.PackIconKind.EmailOff);
                    emailConnectionIcon.Dispatcher.Invoke(() => emailConnectionIcon.Foreground = _redBrush);
                    AddError(ex, TimeoutException, false);
                }
                catch (Pop3ProtocolException ex)
                {
                    if (DebugMode) AddStatus(Pop3ProtocolException);
                    emailConnectionIcon.Dispatcher.Invoke(() => emailConnectionIcon.Kind = MaterialDesignThemes.Wpf.PackIconKind.EmailOff);
                    emailConnectionIcon.Dispatcher.Invoke(() => emailConnectionIcon.Foreground = _redBrush);
                    AddError(ex, Pop3ProtocolException, false);
                }
                catch (MailKit.Security.SslHandshakeException ex)
                {
                    if (DebugMode) AddStatus(SslHandshakeException);
                    emailConnectionIcon.Dispatcher.Invoke(() => emailConnectionIcon.Kind = MaterialDesignThemes.Wpf.PackIconKind.EmailOff);
                    emailConnectionIcon.Dispatcher.Invoke(() => emailConnectionIcon.Foreground = _redBrush);
                    AddError(ex, SslHandshakeException, false);
                }
                catch (System.Net.Sockets.SocketException ex)
                {
                    if (DebugMode) AddStatus(SocketException);
                    emailConnectionIcon.Dispatcher.Invoke(() => emailConnectionIcon.Kind = MaterialDesignThemes.Wpf.PackIconKind.EmailOff);
                    emailConnectionIcon.Dispatcher.Invoke(() => emailConnectionIcon.Foreground = _redBrush);
                    AddError(ex, SocketException, false);
                }
                catch (System.IO.IOException ex)
                {
                    if (DebugMode) AddStatus(IoExceptionMail);
                    emailConnectionIcon.Dispatcher.Invoke(() => emailConnectionIcon.Kind = MaterialDesignThemes.Wpf.PackIconKind.EmailOff);
                    emailConnectionIcon.Dispatcher.Invoke(() => emailConnectionIcon.Foreground = _redBrush);
                    AddError(ex, IoExceptionMail, false);
                }
                catch (Exception ex)
                {
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
        /// Обновляет содержимое архива
        /// </summary>
        private async Task LoadArchive()
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
                        // просто сёрфим папки
                        case FindStatus.DontNeed:
                            _archiveContent = new List<ArchiveContent>();
                            dirs = Directory.EnumerateDirectories(_currentArchiveFolder).ToList();
                            files = Directory.EnumerateFiles(_currentArchiveFolder).ToList();
                            break;
                        // поиск
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
                            
                            if (DebugMode) AddStatus(FindInProcess);
                            await archiveProgressBar .Dispatcher.InvokeAsync(() => archiveProgressBar.Visibility = Visibility.Visible);
                            if (archiveContentSearchCB.IsChecked != null && (bool)archiveContentSearchCB.IsChecked)
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
                                        archivePathTB.Dispatcher.InvokeAsync(() => archivePathTB.Text = $"Поиск УП содержащих \"{findString}\". Проверено: {file + 1}/{allFiles.Length}. Найдено: {files.Count}.");
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

            if (machineDG.ItemsSource == null || !_machineContent.SequenceEqual(machineDG.ItemsSource as List<NcFile> ?? new List<NcFile>()))
            {
                machineDG.Dispatcher.InvokeAsync(() => machineDG.ItemsSource = _machineContent);
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
                            TurnOnArchive();
                            _ = LoadArchive();
                        }
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
                            TurnOnMachine();
                        }
                        LoadMachine();
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
            MessageWindow messageWindow = new();
            messageWindow.ShowDialog();
        }

        private void cloudButton_Click(object sender, RoutedEventArgs e)
        {
            _currentArchiveFolder = @"\\DESKTOP-DB65KJ0\Users\Zvyagin_VM\Desktop\Программы к станкам";
            _ = LoadArchive();
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
                _ = LoadArchive();

                if (!Reader.CheckPath(Settings.Default.machinePath))
                {
                    TurnOffMachine();
                }
                else
                {
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
            _ = LoadArchive();
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
                _analyzeArchiveProgram = false;
                _showWinExplorer = false;
                _openFolderButton = false;
                _currentArchiveFolder = currentItem.Content;
                _ = LoadArchive();
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
                _ = LoadArchive();
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
                    LoadMachine();
                    _ = LoadArchive();
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
                _ = LoadArchive();
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
            }
            _ = LoadArchive();
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
            _ = LoadArchive();
        }


        private void findButton_Click(object sender, RoutedEventArgs e)
        {
            if (!Reader.CheckPath(_currentArchiveFolder)) return;
            
            _findStatus = FindStatus.Find;
            _ = LoadArchive();
        }

        private void openFolderButton_Click(object sender, RoutedEventArgs e)
        {
            _currentArchiveFolder = Path.GetDirectoryName(_selectedArchiveFile);
            findDialogButton.Visibility = Visibility.Collapsed;
            returnButton.Visibility = Visibility.Visible;
            _findStatus = FindStatus.DontNeed;

            _ = LoadArchive();
        }

        private void returnButton_Click(object sender, RoutedEventArgs e)
        {
            _findStatus = FindStatus.Finded;
            findDialogButton.Visibility = Visibility.Visible;
            returnButton.Visibility = Visibility.Collapsed;
            archivePathTB.Text = "Результаты поиска";
            _ = LoadArchive();
        }

        private void transferButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var transferFilePath = Settings.Default.autoRenameToMachine 
                    ? Reader.FindFreeName(_selectedArchiveFile, Settings.Default.machinePath) 
                    : Path.Combine(Settings.Default.machinePath, Path.GetFileName(_selectedArchiveFile)!);


                File.Copy(_selectedArchiveFile!, transferFilePath);
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
                WriteLog($"Отправлено на станок: \"{_selectedArchiveFile}\"");
                LoadMachine();
                _ = LoadArchive();
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
                LoadMachine();
                _ = LoadArchive();
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
                else
                {
                    Process.Start(new ProcessStartInfo(_selectedArchiveFile!) { UseShellExecute = true });
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
                _ = LoadArchive();
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
                    LoadMachine();
                    _ = LoadArchive();
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
                LoadMachine();
                _ = LoadArchive();
                SendMessage($"Из архива удален файл: {Path.GetFileName(_selectedForDeletionArchiveFile)}");
                ConfirmDeleteFromArchiveDialogHost.IsOpen = false;
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

        private void machineDG_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            var item = (sender as ListView)?.SelectedItem;
            if (item == null) return;
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
            LoadMachine();
            _ = LoadArchive();
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
                _ = LoadArchive();
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
                LoadMachine();
                _ = LoadArchive();
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
                var tempProgramFolder = Path.Combine(Settings.Default.tempPath, Reader.CreateTempName(_selectedMachineFile, Reader.GetFileNameOptions.OnlyNCName));
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
                LoadMachine();
                _ = LoadArchive();
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
                LoadMachine();
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
                LoadMachine();
                _ = LoadArchive();
                ConfirmDeleteFromMachineDialogHost.IsOpen = false;
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
                _ = LoadArchive();
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
                    _ = LoadArchive();
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
                    analyzeResultTB.Dispatcher.Invoke(() => analyzeResultTB.Text += $"Несовпадений СОЖ: {warningsCoolant.Count}\n{string.Join('\n', warningsCoolant)}\n\n");
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

        
    }
}