using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;

namespace Ink_Canvas.Helpers
{
    /// <summary>
    /// ICU 弹出层窗口的统一图层（Z 序）管理。
    /// 适用窗口：设置、插件工坊、倒计时、抽奖、快捷键指南、初始化设置、更新日志、名单导入、确认框等
    /// （不包含截图选择框这类瞬态全屏遮罩窗口，也不包含主窗口内部的浮动栏/工具栏/侧栏面板）。
    ///
    /// 需要同时满足两条规则：
    ///   1. 这些窗口永远位于笔迹、画板、浮动栏、工具栏、侧栏之上；
    ///   2. 这些窗口彼此之间没有固定层级 —— 鼠标点了哪个、新开了哪个，哪个就在最前。
    ///
    /// 实现依据（Windows 窗口管理语义）：
    ///   · 统一把 Owner 设为主窗口。Windows 保证 owned window 永远显示在其 owner 之上，
    ///     因此主窗口（承载笔迹/画板/浮动栏/工具栏/侧栏，且会被浮动栏 Z 序修复定时器周期性
    ///     SetWindowPos 到最前）无论如何被激活或置顶，都不可能盖住这些弹出窗口。
    ///   · 共享同一个 owner 的窗口之间，层级完全由激活顺序决定。点击某个窗口即被激活并排到
    ///     同组最前，于是“点谁谁在前 / 新开谁在前”是系统默认行为，
    ///     不会再出现“插件工坊始终遮挡设置”这类固定层级问题。
    ///   · 所有弹出窗口保持 Topmost=True，使主窗口切换到非置顶模式（黑板/批注等）时仍在其之上。
    ///
    /// 事件订阅全部挂在窗口自身并随 Closed 反订阅，不持有跨窗口生命期的静态引用，避免内存泄漏。
    /// </summary>
    public static class PopupWindowLayerHelper
    {
        #region Win32

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);

        private static readonly IntPtr HWND_TOPMOST = new IntPtr(-1);

        private const uint SWP_NOSIZE = 0x0001;
        private const uint SWP_NOMOVE = 0x0002;
        private const uint SWP_NOACTIVATE = 0x0010;

        #endregion

        // 用附加属性标记“已登记”，避免重复订阅；同时不产生任何静态集合引用，随窗口一起回收
        private static readonly DependencyProperty RegisteredProperty =
            DependencyProperty.RegisterAttached(
                "PopupLayerRegistered",
                typeof(bool),
                typeof(PopupWindowLayerHelper),
                new PropertyMetadata(false));

        /// <summary>
        /// 把窗口登记进统一弹出层。应在窗口构造函数 InitializeComponent() 之后调用一次。
        /// </summary>
        public static void Register(Window window)
        {
            if (window == null) return;
            if (window.GetValue(RegisteredProperty) is bool already && already) return;
            window.SetValue(RegisteredProperty, true);

            try { window.Topmost = true; } catch { }

            try
            {
                var owner = ResolveOwnerWindow(window);
                // 已由调用方显式指定 Owner 的（如模态确认框指定为自己的调用者）保持原样
                if (owner != null && window.Owner == null && !ReferenceEquals(owner, window))
                {
                    window.Owner = owner;
                }
            }
            catch { }

            window.Loaded += OnWindowLoaded;
            window.IsVisibleChanged += OnWindowIsVisibleChanged;
            window.PreviewMouseDown += OnWindowPreviewMouseDown;
            window.Activated += OnWindowActivated;
            window.Closed += OnWindowClosed;
        }

        /// <summary>
        /// 把窗口提到最前：恢复最小化、激活、并压到 Topmost 组最前。
        /// 供“打开/复用窗口”的调用点在 Show() 之后显式调用。
        /// </summary>
        public static void BringToFront(Window window)
        {
            if (window == null) return;
            try
            {
                if (!window.IsVisible) return;
                if (window.WindowState == WindowState.Minimized)
                    window.WindowState = WindowState.Normal;
                if (!window.IsActive) window.Activate();
                PushToTopOfTopmostBand(window);
            }
            catch { }
        }

        /// <summary>弹出层窗口的统一 Owner：应用主窗口（承载画板/笔迹/浮动栏）。</summary>
        private static Window ResolveOwnerWindow(Window self)
        {
            try
            {
                var mw = Application.Current?.MainWindow;
                if (mw == null) return null;
                if (ReferenceEquals(mw, self)) return null;
                // 主窗口必须已经显示出来才能作为 Owner，否则设置 Owner 会抛异常
                if (!mw.IsLoaded && !mw.IsVisible) return null;
                return mw;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// 把窗口压到 Topmost 组最前（不激活、不移动、不改变尺寸）。
        /// Owner 约束由系统强制维持，因此不会把窗口压到主窗口之下。
        /// </summary>
        private static void PushToTopOfTopmostBand(Window window)
        {
            try
            {
                var hwnd = new WindowInteropHelper(window).Handle;
                if (hwnd == IntPtr.Zero) return;
                SetWindowPos(hwnd, HWND_TOPMOST, 0, 0, 0, 0,
                    SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
            }
            catch { }
        }

        private static void OnWindowLoaded(object sender, RoutedEventArgs e)
        {
            var w = sender as Window;
            if (w == null) return;
            w.Loaded -= OnWindowLoaded;
            if (IsReallyShown(w)) ScheduleBringToFront(w);
        }

        private static void OnWindowIsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            var w = sender as Window;
            if (w == null) return;
            // 覆盖“复用缓存实例 / Hide 后重新 Show”的场景：重新可见即置前
            if (w.IsVisible && IsReallyShown(w)) ScheduleBringToFront(w);
        }

        private static void OnWindowPreviewMouseDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            var w = sender as Window;
            if (w == null) return;
            // 已激活的窗口无需重复激活，避免每次点击都走一次 SetForegroundWindow / SetWindowPos
            if (!w.IsActive) BringToFront(w);
        }

        private static void OnWindowActivated(object sender, EventArgs e)
        {
            var w = sender as Window;
            if (w == null) return;
            ScheduleBringToFront(w);
        }

        private static void OnWindowClosed(object sender, EventArgs e)
        {
            var w = sender as Window;
            if (w == null) return;
            w.Loaded -= OnWindowLoaded;
            w.IsVisibleChanged -= OnWindowIsVisibleChanged;
            w.PreviewMouseDown -= OnWindowPreviewMouseDown;
            w.Activated -= OnWindowActivated;
            w.Closed -= OnWindowClosed;
        }

        /// <summary>
        /// 判断是否“真正呈现给用户”。设置窗口在空闲预构建阶段会 Show 到屏幕外且 Opacity=0，
        /// 此时不能抢焦点，否则启动时会把焦点从用户当前应用抢走。
        /// </summary>
        private static bool IsReallyShown(Window w)
        {
            try
            {
                return w.IsVisible && w.Opacity > 0.01;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// 延后一拍再压 Z 序：Show() 触发 IsVisibleChanged / Activated 时窗口句柄可能刚创建、
        /// 激活动作尚未收尾，立即 SetWindowPos 可能被随后的动作覆盖。
        /// 这里只调整 Z 序、不调用 Activate：避免窗口在后台被显示时把焦点从用户当前应用抢走。
        /// </summary>
        private static void ScheduleBringToFront(Window w)
        {
            try
            {
                w.Dispatcher.BeginInvoke(new Action(() =>
                {
                    try
                    {
                        if (!w.IsVisible) return;
                        PushToTopOfTopmostBand(w);
                    }
                    catch { }
                }), DispatcherPriority.Background);
            }
            catch { }
        }
    }
}
