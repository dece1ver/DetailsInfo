using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Text;

namespace DetailsInfo.Utils
{
    internal class WindowsUtils
    {
        #region DLL'ки

        const string User32 = "user32.dll";
        const string Shell32 = "shell32.dll";

        public delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

        [DllImport(User32, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

        [DllImport(User32, SetLastError = true)]
        public static extern int GetWindowText(IntPtr hWnd, StringBuilder lpString, int nMaxCount);

        [DllImport(User32, SetLastError = true)]
        public static extern int GetWindowTextLength(IntPtr hWnd);

        [DllImport(User32, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool IsWindowVisible(IntPtr hWnd);

        [DllImport(User32)]
        public static extern IntPtr FindWindow(string lpClassName, string lpWindowName);

        [DllImport(User32, CharSet = CharSet.Unicode)]
        public static extern IntPtr FindWindowEx(IntPtr parentHandle, IntPtr childAfter, string lclassName, string windowTitle);

        [DllImport(User32, EntryPoint = "FindWindow")]
        public static extern IntPtr FindWindowByCaption(IntPtr zeroOnly, string lpWindowName);

        [DllImport(User32)]
        public static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

        [DllImport(User32)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool IsIconic(IntPtr hWnd);

        [DllImport(User32)]
        public static extern bool SetForegroundWindow(IntPtr hWnd);

        [DllImport(User32)]
        public static extern int SendMessage(IntPtr hWnd, uint msg, int wParam, int lParam);
        public const int WmSyscommand = 0x0112;
        public const int ScClose = 0xF060;

        [DllImport(User32, EntryPoint = "PostMessageA")]
        public static extern bool PostMessage(IntPtr hWnd, uint msg, int wParam, int lParam);
        public enum WMessages
        {
            WmLbuttondown = 0x201,
            WmLbuttonup = 0x202,
            WmKeydown = 0x100,
            WmKeyup = 0x101,
            WhKeyboardLl = 13,
            WhMouseLl = 14,
        }


        [DllImport(Shell32, CharSet = CharSet.Auto)]
        static extern bool ShellExecuteEx(ref SHELLEXECUTEINFO lpExecInfo);

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
        public struct SHELLEXECUTEINFO
        {
            public int cbSize;
            public uint fMask;
            public IntPtr hwnd;
            [MarshalAs(UnmanagedType.LPTStr)]
            public string lpVerb;
            [MarshalAs(UnmanagedType.LPTStr)]
            public string lpFile;
            [MarshalAs(UnmanagedType.LPTStr)]
            public string lpParameters;
            [MarshalAs(UnmanagedType.LPTStr)]
            public string lpDirectory;
            public int nShow;
            public IntPtr hInstApp;
            public IntPtr lpIDList;
            [MarshalAs(UnmanagedType.LPTStr)]
            public string lpClass;
            public IntPtr hkeyClass;
            public uint dwHotKey;
            public IntPtr hIcon;
            public IntPtr hProcess;
        }
        #endregion

        public static string GetWindowText(IntPtr hWnd)
        {
            int len = GetWindowTextLength(hWnd) + 1;
            StringBuilder sb = new(len);
            len = GetWindowText(hWnd, sb, len);
            return sb.ToString(0, len);
        }

        public static void KillTabTip(int killType)
        {
            if (killType == 0)
            {
                var tabtip = FindWindow("IPTIP_Main_Window", "");
                if (tabtip != IntPtr.Zero)
                {
                    _ = SendMessage(tabtip, WmSyscommand, ScClose, 0);
                }
            }
            else if (killType == 1)
            {
                foreach (var process in Process.GetProcessesByName("tabtip"))
                {
                    process.Kill();
                }
            }

        }

        public static void RunTabTip()
        {
            if (Process.GetProcessesByName("tabtip").Length == 0)
            {
                Process.Start(new ProcessStartInfo("tabtip") { UseShellExecute = true });
            }
            var trayWnd = FindWindow("Shell_TrayWnd", null);
            IntPtr nullIntPtr = new(0);

            if (trayWnd != nullIntPtr)
            {
                var trayNotifyWnd = FindWindowEx(trayWnd, nullIntPtr, "TrayNotifyWnd", null);
                if (trayNotifyWnd != nullIntPtr)
                {
                    var tIpBandWnd = FindWindowEx(trayNotifyWnd, nullIntPtr, "TIPBand", null);

                    if (tIpBandWnd != nullIntPtr)
                    {
                        PostMessage(tIpBandWnd, (uint)WMessages.WmLbuttondown, 1, 65537);
                        PostMessage(tIpBandWnd, (uint)WMessages.WmLbuttonup, 1, 65537);
                    }
                }
            }
        }

        /// <summary>
        /// Проверяет, запущено ли приложение с правами администратора.
        /// </summary>
        /// <returns>True, если запущено с правами администратора; иначе False.</returns>
        public static bool IsRunAsAdministrator()
        {
            using (var identity = WindowsIdentity.GetCurrent())
            {
                var principal = new WindowsPrincipal(identity);
                return principal.IsInRole(WindowsBuiltInRole.Administrator);
            }
        }

        public static void OpenFileWithDefaultApp(string filePath)
        {
            SHELLEXECUTEINFO info = new SHELLEXECUTEINFO();
            info.cbSize = Marshal.SizeOf(info);
            info.lpVerb = "open";
            info.lpFile = filePath;
            info.nShow = 1; // SW_NORMAL
            info.fMask = 0x0000000C; // SEE_MASK_FLAG_NO_UI | SEE_MASK_NOCLOSEPROCESS
            info.lpDirectory = System.IO.Path.GetDirectoryName(filePath);

            if (!ShellExecuteEx(ref info))
            {
                throw new Win32Exception(Marshal.GetLastWin32Error());
            }
        }

    }
}
