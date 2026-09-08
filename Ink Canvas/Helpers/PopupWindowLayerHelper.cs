using System;
using System.Collections.Generic;
using System.Linq;
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

        // 已登记窗口（弱引用，Closed 时移除），仅用于跟随主窗口同步置顶状态
        private static readonly List<WeakReference> RegisteredWindows = new List<WeakReference>();
        private static readonly object RegistryLock = new object();

        // 置顶状态由自己管理、不参与弹出层跟随的窗口：
        // 截图选择框/截图插入选项框是瞬态全屏遮罩，必须始终盖住一切（含画板），不能被“跟随”拉下来。
        private static readonly HashSet<Type> ExcludedWindowTypes = new HashSet<Type>
        {
            typeof(global::Ink_Canvas.ScreenshotSelectorWindow),
            typeof(global::Ink_Canvas.ScreenshotInsertOptionWindow)
        };

        private static readonly TimeSpan MaintenanceInterval = TimeSpan.FromMilliseconds(500);
        private static DispatcherTimer _maintenanceTimer;

        /// <summary>
        /// 把窗口登记进统一弹出层。应在窗口构造函数 InitializeComponent() 之后调用一次。
        /// </summary>
        public static void Register(Window window)
        {
            if (window == null) return;
            if (IsExcluded(window)) return;
            if (window.GetValue(RegisteredProperty) is bool already && already) return;
            window.SetValue(RegisteredProperty, true);

            // 置顶状态跟随主窗口，而不是固定为 true 或 false：
            // 主窗口置顶（屏幕/批注模式）时随之置顶 —— 否则会跌到 topmost 的主窗口（含浮动栏）之下，
            // 出现“设置窗口被浮动栏挡住、鼠标仍是批注光标”的情况；
            // 主窗口不置顶（黑板/白板模式）时随之让位 —— 外部窗口才能正常盖在它们上面。
            // 说明：Owner 关系只在同一个置顶层内保证 owned 在 owner 之上，跨层时会失效，故必须同步。
            try
            {
                var owner = ResolveOwnerWindow(window);
                window.Topmost = owner != null && owner.Topmost;

                // 已由调用方显式指定 Owner 的（如模态确认框指定为自己的调用者）保持原样。
                // IsLoaded 为 true 说明窗口已 Show，此时再设 Owner 会抛异常（自动扫描兜底路径会走到这里），
                // 这种情况下只同步 Topmost 即可，层级同样能得到保证。
                if (owner != null && window.Owner == null && !window.IsLoaded && !ReferenceEquals(owner, window))
                {
                    window.Owner = owner;
                }

                lock (RegistryLock)
                {
                    RegisteredWindows.Add(new WeakReference(window));
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
        /// 兜底扫描：把当前所有已打开、但尚未登记的窗口纳入管理。
        /// 目的：任何窗口（包括将来新增的、或漏写 Register 的）都不会掉到画板/浮动栏之下。
        /// 主窗口与截图遮罩类窗口不参与。
        /// </summary>
        public static void RegisterOpenWindows()
        {
            var app = Application.Current;
            if (app == null) return;

            try
            {
                // 先快照再处理：Register 不会增删窗口集合，但避免遍历期间集合被外部改动
                var windows = app.Windows.OfType<Window>().ToList();
                foreach (var w in windows)
                {
                    if (w == null) continue;
                    if (ReferenceEquals(w, app.MainWindow)) continue;
                    Register(w);
                }
            }
            catch { }
        }

        /// <summary>
        /// 启动兜底维护定时器：周期性扫描窗口并对齐置顶状态。
        /// 只做 Topmost 对齐，不做 Activate，不会抢焦点。
        /// </summary>
        public static void StartMaintenance()
        {
            try
            {
                if (_maintenanceTimer != null) return;
                _maintenanceTimer = new DispatcherTimer { Interval = MaintenanceInterval };
                _maintenanceTimer.Tick += (s, e) =>
                {
                    try
                    {
                        RegisterOpenWindows();
                        SyncTopmostToOwner();
                    }
                    catch { }
                };
                _maintenanceTimer.Start();
            }
            catch { }
        }

        private static bool IsExcluded(Window window)
        {
            try
            {
                return ExcludedWindowTypes.Contains(window.GetType());
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// 把所有已登记的弹出层窗口的置顶状态同步为主窗口的当前置顶状态。
        /// 应在主窗口 Topmost 发生变化时调用（MainWindow 构造函数里已挂监听，无需手动调用）。
        /// 值未变化时 WPF 不会重建窗口样式，因此可以安全地被高频触发。
        /// </summary>
        public static void SyncTopmostToOwner()
        {
            Window mw;
            try { mw = Application.Current?.MainWindow; } catch { return; }
            if (mw == null) return;

            // 先兜底扫描，保证尚未登记的窗口也能被同步到
            RegisterOpenWindows();

            bool topmost = mw.Topmost;
            List<WeakReference> snapshot;
            lock (RegistryLock)
            {
                RegisteredWindows.RemoveAll(r => !r.IsAlive);
                snapshot = new List<WeakReference>(RegisteredWindows);
            }

            foreach (var reference in snapshot)
            {
                var w = reference.Target as Window;
                if (w == null) continue;
                try
                {
                    if (w.Topmost != topmost) w.Topmost = topmost;
                }
                catch { }
            }
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
            try
            {
                lock (RegistryLock)
                {
                    RegisteredWindows.RemoveAll(r => !r.IsAlive || ReferenceEquals(r.Target, w));
                }
            }
            catch { }
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
