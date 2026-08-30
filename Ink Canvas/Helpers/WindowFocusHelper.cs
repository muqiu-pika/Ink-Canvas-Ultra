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

            // 先比较再赋值：WPF 的 Topmost 每次真实变更都会走 SetWindowPos 重写扩展窗口样式
            // （WS_EX_TOPMOST）并触发合成层重排。本方法由 900ms 定时器高频调用，
            // 值未变化时直接跳过，避免每轮无意义的窗口样式重建。
            if (window.Topmost != isTopmost)
            {
                window.Topmost = isTopmost;
            }

            // 重新压 Z 序统一交给下面的 SetWindowPos(HWND_TOPMOST)。
            // 原先这里用「Topmost=false → true」翻转来强制刷新层级，属于重复劳动：
            // 翻转本身要付两次窗口样式变更，而紧随其后的 SetWindowPos 已经能保证置顶。
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
