using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace Ink_Canvas.Helpers
{
    public static class WindowFocusHelper
    {
        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool SetForegroundWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

        [DllImport("user32.dll")]
        private static extern bool IsIconic(IntPtr hWnd);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);

        private static readonly IntPtr HWND_TOPMOST = new IntPtr(-1);
        private static readonly IntPtr HWND_NOTOPMOST = new IntPtr(-2);

        private const uint SWP_NOSIZE = 0x0001;
        private const uint SWP_NOMOVE = 0x0002;
        private const uint SWP_NOACTIVATE = 0x0010;
        private const uint SWP_SHOWWINDOW = 0x0040;

        private const int SW_RESTORE = 9;

        public static void EnsureWindowFocus(Window window)
        {
            if (window == null) return;

            var interopHelper = new WindowInteropHelper(window);
            IntPtr hWnd = interopHelper.Handle;

            // 如果窗口最小化，先恢复
            if (IsIconic(hWnd))
            {
                ShowWindow(hWnd, SW_RESTORE);
            }

            // 设置窗口为前台窗口
            SetForegroundWindow(hWnd);
        }

        public static void EnsureWindowTopmost(Window window, bool isTopmost)
        {
            if (window == null) return;

            window.Topmost = isTopmost;
            
            // 强制更新窗口层级
            if (isTopmost)
            {
                window.Topmost = false;
                window.Topmost = true;
            }

            IntPtr hWnd = new WindowInteropHelper(window).Handle;
            if (hWnd == IntPtr.Zero) return;
            SetWindowPos(
                hWnd,
                isTopmost ? HWND_TOPMOST : HWND_NOTOPMOST,
                0,
                0,
                0,
                0,
                SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE | SWP_SHOWWINDOW);
        }
    }
}
