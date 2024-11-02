using Microsoft.Win32;
using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows;
using DetailsInfo.Properties;

namespace DetailsInfo
{
    public partial class App : Application
    {
        private const string UniqueMutexName = "{80100B51-4C86-4513-831E-67071DFE9D6D}";
        private const string UniqueEventName = $"{UniqueMutexName}_Event";
        private const string PipeName = $"{UniqueMutexName}_Pipe";
        public const string UriProtocol = "dinfo";
        public const string UriPrefix = $"{UriProtocol}://";

        private Mutex _singleInstanceMutex;
        private EventWaitHandle _eventWaitHandle;
        private bool _isMainInstance;
        private static string _logPath;

        public App()
        {
            // Инициализируем путь к лог-файлу в папке с приложением
            _logPath = Path.Combine(
                Path.GetDirectoryName(Process.GetCurrentProcess().MainModule.FileName),
                "app.log"
            );
            LogMessage("Приложение запущено");
        }

        public static void LogMessage(string message)
        {
            var logMessage = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {message}";
            try
            {
                File.AppendAllText(_logPath, logMessage + Environment.NewLine);
                Console.WriteLine(logMessage); // Дублируем в консоль
            }
            catch { } // Игнорируем ошибки записи лога
        }

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            LogMessage($"Запуск с аргументами: {string.Join(" ", e.Args)}");

            // Сброс advanced режима при каждом запуске
            Settings.Default.advancedMode = false;

            // Обработка аргумента -advanced
            if (e.Args.Contains("-advanced"))
            {
                Settings.Default.advancedMode = true;
                LogMessage("Включен advanced режим");
            }
            Settings.Default.Save();

            _singleInstanceMutex = new Mutex(true, UniqueMutexName, out _isMainInstance);

            if (!_isMainInstance)
            {
                LogMessage("Обнаружен существующий экземпляр приложения");
                // Если это не основной экземпляр, передаем аргументы существующему и закрываемся
                try
                {
                    _eventWaitHandle = EventWaitHandle.OpenExisting(UniqueEventName);
                    string urlArgument = e.Args.FirstOrDefault(arg => arg.StartsWith(UriPrefix));
                    if (!string.IsNullOrEmpty(urlArgument))
                    {
                        using (var pipeClient = new System.IO.Pipes.NamedPipeClientStream(".", PipeName, System.IO.Pipes.PipeDirection.Out))
                        {
                            pipeClient.Connect(1000); // Timeout 1 second
                            using (var writer = new StreamWriter(pipeClient))
                            {
                                writer.WriteLine(urlArgument);
                                LogMessage($"Отправлен URL: {urlArgument}");
                            }
                        }
                        _eventWaitHandle.Set();
                    }
                    Shutdown();
                    return;
                }
                catch (Exception ex)
                {
                    LogMessage($"Ошибка передачи данных: {ex.Message}");
                    MessageBox.Show("Не удалось передать параметры существующему экземпляру приложения.");
                    Shutdown();
                    return;
                }
            }

            try
            {
                // Регистрируем протокол только для основного экземпляра
                string appPath = Process.GetCurrentProcess().MainModule.FileName;
                RegisterUrlProtocol(UriProtocol, appPath);

                // Создаем event handle для коммуникации между экземплярами
                _eventWaitHandle = new EventWaitHandle(false, EventResetMode.AutoReset, UniqueEventName);

                // Запускаем именованный канал для получения данных
                StartPipeServer();

                // Обрабатываем начальные аргументы
                string urlArgument = e.Args.FirstOrDefault(arg => arg.StartsWith(UriPrefix));
                if (!string.IsNullOrEmpty(urlArgument))
                {
                    LogMessage($"Обработка начального URL: {urlArgument}");
                    Dispatcher.BeginInvoke(new Action(() =>
                    {
                        if (MainWindow is MainWindow mw)
                        {
                            mw.HandleIncomingUrl(urlArgument);
                        }
                    }));
                }

                // Запускаем поток для обработки последующих вызовов
                StartMessageLoop();
            }
            catch (Exception ex)
            {
                LogMessage($"Ошибка инициализации: {ex.Message}");
                MessageBox.Show($"Ошибка инициализации: {ex.Message}");
                Shutdown();
            }
        }

        private void StartPipeServer()
        {
            Thread pipeThread = new Thread(() =>
            {
                while (true)
                {
                    try
                    {
                        using (var pipeServer = new System.IO.Pipes.NamedPipeServerStream(PipeName, System.IO.Pipes.PipeDirection.In))
                        {
                            pipeServer.WaitForConnection();
                            using (var reader = new StreamReader(pipeServer))
                            {
                                string url = reader.ReadLine();
                                LogMessage($"Получен URL через pipe: {url}");
                                if (!string.IsNullOrEmpty(url))
                                {
                                    Dispatcher.BeginInvoke(new Action(() =>
                                    {
                                        if (MainWindow is MainWindow mw)
                                        {
                                            mw.HandleIncomingUrl(url);
                                        }
                                    }));
                                }
                            }
                        }
                    }
                    catch (ObjectDisposedException)
                    {
                        break;
                    }
                    catch (Exception ex)
                    {
                        LogMessage($"Ошибка в pipe сервере: {ex.Message}");
                    }
                }
            })
            {
                IsBackground = true
            };
            pipeThread.Start();
        }

        private void StartMessageLoop()
        {
            Thread messageThread = new Thread(() =>
            {
                while (true)
                {
                    try
                    {
                        _eventWaitHandle.WaitOne();
                        LogMessage("Получен сигнал от другого экземпляра");

                        Dispatcher.BeginInvoke(new Action(() =>
                        {
                            try
                            {
                                if (MainWindow is MainWindow mw)
                                {
                                    if (mw.WindowState == WindowState.Minimized)
                                        mw.WindowState = WindowState.Normal;

                                    mw.Activate();
                                    mw.Focus();
                                    LogMessage("Окно активировано");
                                }
                            }
                            catch (Exception ex)
                            {
                                LogMessage($"Ошибка обработки сообщения: {ex.Message}");
                                MessageBox.Show($"Ошибка обработки сообщения: {ex.Message}");
                            }
                        }));
                    }
                    catch (ObjectDisposedException)
                    {
                        break;
                    }
                }
            })
            {
                IsBackground = true
            };
            messageThread.Start();
        }

        private void RegisterUrlProtocol(string scheme, string applicationPath)
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.CreateSubKey($@"SOFTWARE\Classes\{scheme}"))
                {
                    key.SetValue("", $"URL:{scheme}");
                    key.SetValue("URL Protocol", "");

                    using (RegistryKey shellKey = key.CreateSubKey(@"shell\open\command"))
                    {
                        shellKey.SetValue("", $"\"{applicationPath}\" \"%1\"");
                    }
                }
                LogMessage($"Протокол {scheme} зарегистрирован");
            }
            catch (Exception ex)
            {
                LogMessage($"Ошибка регистрации протокола: {ex.Message}");
                MessageBox.Show($"Ошибка регистрации протокола: {ex.Message}");
            }
        }

        protected override void OnExit(ExitEventArgs e)
        {
            LogMessage("Приложение завершает работу");
            _eventWaitHandle?.Dispose();
            _singleInstanceMutex?.Dispose();
            base.OnExit(e);
        }
    }
}