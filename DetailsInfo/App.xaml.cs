using DetailsInfo.Properties;
using System;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;

namespace DetailsInfo
{
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App : Application
    {
        private void App_Startup(object sender, StartupEventArgs e)
        {
            Settings.Default.advancedMode = false;
            for (var i = 0; i != e.Args.Length; ++i)
            {
                if (e.Args[i] == "-advanced")
                {
                    Settings.Default.advancedMode = true;
                }
            }
            Settings.Default.Save();
        }
        public App()
        {
            // initiate it. Call it first.
            SingleInstanceWatcher();
        }

        private const string UniqueEventName = "{80100B51-4C86-4513-831E-67071DFE9D6D}";
        private EventWaitHandle _eventWaitHandle;
        private void SingleInstanceWatcher()
        {
            // check if it is already open.
            try
            {
                // try to open it - if another instance is running, it will exist , if not it will throw
                _eventWaitHandle = EventWaitHandle.OpenExisting(UniqueEventName);

                // Notify other instance so it could bring itself to foreground.
                _eventWaitHandle.Set();

                // Terminate this instance.
                Shutdown();
            }
            catch (WaitHandleCannotBeOpenedException)
            {
                // listen to a new event (this app instance will be the new "master")
                _eventWaitHandle = new EventWaitHandle(false, EventResetMode.AutoReset, UniqueEventName);
            }

            // if this instance gets the signal to show the main window
            new Task(() =>
            {
                while (_eventWaitHandle.WaitOne())
                {
                    _ = Current.Dispatcher.BeginInvoke((Action)(() =>
                      {
                        // could be set or removed anytime
                        if (!Current.MainWindow.Equals(null))
                          {
                              var mw = Current.MainWindow;

                              if (mw.WindowState == WindowState.Minimized || mw.Visibility != Visibility.Visible)
                              {
                                  mw.Show();
                                  mw.WindowState = WindowState.Maximized;
                              }

                            // According to some sources these steps are required to be sure it went to foreground.
                            mw.Activate();
                              mw.Topmost = true;
                              mw.Topmost = false;
                              mw.Focus();
                          }
                      }));
                }
            })
            .Start();
        }
    }
}
