using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace Ink_Canvas.Helpers
{
    public static class TouchLockFix
    {
        [DllImport("user32.dll")]
        private static extern bool RegisterTouchWindow(IntPtr hWnd, uint ulFlags);

        [DllImport("user32.dll")]
        private static extern bool UnregisterTouchWindow(IntPtr hWnd);

        private const uint TWF_FINETOUCH = 0x00000001;
        private const uint TWF_WANTPALM = 0x00000002;

        public static void ReRegisterTouchWindow(Window window)
        {
            if (window == null) return;

            var interopHelper = new WindowInteropHelper(window);
            IntPtr hWnd = interopHelper.Handle;

            try
            {
                UnregisterTouchWindow(hWnd);
                RegisterTouchWindow(hWnd, TWF_FINETOUCH | TWF_WANTPALM);
            }
            catch (Exception ex)
            {
                LogHelper.WriteLogToFile(ex.ToString(), LogHelper.LogType.Error);
            }
        }
    }
}