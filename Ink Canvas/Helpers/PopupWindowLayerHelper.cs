using System;
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
    /// 需要同时满足三条规则：
    ///   1. 这些窗口位于笔迹、画板、浮动栏、工具栏、侧栏之上；
    ///   2. 它们彼此之间没有固定层级 —— 鼠标点了哪个、新开了哪个，哪个就在最前；
    ///   3. 它们不能始终压在其它应用之上 —— 打开资源管理器、文件对话框等外部窗口时，
    ///      外部窗口必须能正常显示（尤其在黑板/白板模式下主窗口本身就不是置顶窗口）。
    ///
    /// 实现依据（Windows 窗口管理语义）：
    ///   · 统一把 Owner 设为主窗口。Windows 保证 owned window 永远显示在其 owner 之上，
    ///     因此主窗口（承载笔迹/画板/浮动栏/工具栏/侧栏，且会被浮动栏 Z 序修复定时器周期性
    ///     置顶）无论如何被激活，都不会盖住这些弹出窗口。
    ///   · 共享同一个 owner 的窗口之间，层级完全由激活顺序决定：点击谁谁就在最前，
    ///     不会再出现“插件工坊始终遮挡设置”这类固定层级问题。
    ///   · 关键：这里刻意**不**给这些窗口设置 Topmost（取消 WS_EX_TOPMOST）。
    ///     置顶状态由 owner 传递 —— 主窗口置顶（屏幕/批注模式）时它们随主窗口一起位于其它应用之上；
    ///     主窗口不置顶（黑板/白板模式）时它们也随之让位，外部窗口可以正常盖在上面，
    ///     不会出现“打开资源管理器却被设置窗口挡住”的情况。
    ///
    /// 事件订阅全部挂在窗口自身并随 Closed 反订阅，不持有跨窗口生命期的静态引用，避免内存泄漏。
    /// </summary>
    public static class PopupWindowLayerHelper
    {
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

            // 明确取消置顶：层级完全交给 Owner 关系，使这些窗口跟随主窗口的置顶状态，
            // 而不是永远压在资源管理器、文件对话框等外部窗口之上。
            try { window.Topmost = false; } catch { }

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
            window.Closed += OnWindowClosed;
        }

        /// <summary>
        /// 把窗口提到最前：恢复最小化并激活。
        /// 注意：这里刻意不使用 SetWindowPos(HWND_TOPMOST) —— 那会给窗口加上 WS_EX_TOPMOST，
        /// 使其变成“永远置顶”，正是需要避免的行为。激活已足以让它在同组窗口中排到最前。
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
            }
            catch { }
        }

        /// <summary>
        /// 为 Win32/WinForms 通用对话框（FolderBrowserDialog 等）提供一个 IWin32Window 宿主，
        /// 使对话框成为指定窗口的 owned window —— 否则在置顶的主窗口下对话框会显示不出来。
        /// </summary>
        public static System.Windows.Forms.IWin32Window AsWin32Owner(Window window)
        {
            try
            {
                var hwnd = new WindowInteropHelper(window).Handle;
                if (hwnd == IntPtr.Zero) return null;
                return new Win32WindowWrapper(hwnd);
            }
            catch
            {
                return null;
            }
        }

        private sealed class Win32WindowWrapper : System.Windows.Forms.IWin32Window
        {
            private readonly IntPtr _handle;

            public Win32WindowWrapper(IntPtr handle)
            {
                _handle = handle;
            }

            public IntPtr Handle
            {
                get { return _handle; }
            }
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
            if (w == null || w.IsActive) return;
            // 延后一拍再激活：避免抢在本次点击命中测试之前切换前台窗口，导致这一下点击被吞掉
            ScheduleBringToFront(w);
        }

        private static void OnWindowClosed(object sender, EventArgs e)
        {
            var w = sender as Window;
            if (w == null) return;
            w.Loaded -= OnWindowLoaded;
            w.IsVisibleChanged -= OnWindowIsVisibleChanged;
            w.PreviewMouseDown -= OnWindowPreviewMouseDown;
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
        /// 延后一拍再激活：Show() 触发 IsVisibleChanged 时激活流程可能尚未收尾，
        /// 立即 Activate 可能被随后的动作覆盖；同时也避免窗口在后台被显示时抢走用户焦点。
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
                        if (w.IsActive) return;
                        if (w.WindowState == WindowState.Minimized) w.WindowState = WindowState.Normal;
                        w.Activate();
                    }
                    catch { }
                }), DispatcherPriority.Background);
            }
            catch { }
        }
    }
}
