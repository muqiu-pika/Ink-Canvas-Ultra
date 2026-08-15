using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Ink_Canvas.Helpers
{
    /// <summary>
    /// 窗口内存回收辅助。
    ///
    /// 背景：WPF 窗口关闭后，其托管对象在 GC 运行前不会被回收，托管堆会保留已申请的内存，
    /// 因此任务管理器里的进程占用不会在关闭窗口后立刻下降（这是正常的 GC 行为，并非都是泄漏）。
    /// 本工具在窗口关闭后：
    ///   1) 先断开可视化树中对“大对象”（位图 Image.Source、媒体 MediaElement）的强引用，
    ///      因为 WPF 的渲染上下文（MediaContext）常会延后持有这些资源，导致即便窗口关闭、
    ///      已无业务引用，gen-2 的大对象也无法被回收；
    ///   2) 再在后台线程触发一次“阻塞式完整回收”（连续两次 GC.Collect + WaitForPendingFinalizers），
    ///      让确实已无引用的对象真正回收，工作集回落，从而能观察到“反复打开-关闭窗口后内存释放”。
    ///
    /// 注意：仅对真正不再被引用的窗口有效；若窗口仍被静态字段 / 事件订阅持有，则属泄漏，
    /// 需要先断开引用（见各窗口 OnClosed 中的计时器停止、事件反订阅等），GC 才能将其回收。
    /// </summary>
    internal static class WindowMemoryHelper
    {
        /// <summary>订阅窗口的 Closed 事件，关闭后清理可视化树并延时后台 GC 回收其内存。</summary>
        public static void ReleaseOnClose(Window window)
        {
            if (window == null) return;
            try
            {
                window.Closed += (s, e) =>
                {
                    var w = s as Window;
                    CleanupVisualTree(w);
                    ScheduleRelease();
                };
            }
            catch { }
        }

        /// <summary>
        /// 断开窗口可视化树中对大对象（位图 / 媒体）的强引用，帮助 GC 真正回收。
        /// 在窗口已关闭（Closed）时调用是安全的：窗口即将被销毁，置空这些引用不会影响任何可见内容。
        /// </summary>
        public static void CleanupVisualTree(Window window)
        {
            if (window == null) return;
            try
            {
                WalkAndClean(window);
            }
            catch { }
        }

        private static void WalkAndClean(DependencyObject parent)
        {
            if (parent == null) return;

            int count;
            try { count = VisualTreeHelper.GetChildrenCount(parent); }
            catch { return; }

            for (int i = 0; i < count; i++)
            {
                DependencyObject child;
                try { child = VisualTreeHelper.GetChild(parent, i); }
                catch { continue; }
                if (child == null) continue;

                if (child is Image image)
                {
                    try { image.Source = null; } catch { }
                }
                else if (child is MediaElement media)
                {
                    try { media.Stop(); } catch { }
                    try { media.Close(); } catch { }
                }

                WalkAndClean(child);
            }
        }

        /// <summary>
        /// 延时后台 GC：延迟 300ms 让窗口关闭回调完整执行，且在后台线程以“阻塞式”做完整回收，
        /// 避免 UI 卡顿的同时确保 gen-2 大对象（位图等）被真正回收。
        /// </summary>
        public static void ScheduleRelease()
        {
            try
            {
                System.Threading.Tasks.Task.Run(() =>
                {
                    try
                    {
                        System.Threading.Thread.Sleep(300);
                        // 非阻塞收集（blocking:false）对 WPF 大对象往往无效，这里用阻塞式并回收两次。
                        GC.Collect(2, GCCollectionMode.Forced, blocking: true);
                        GC.WaitForPendingFinalizers();
                        GC.Collect(2, GCCollectionMode.Forced, blocking: true);
                    }
                    catch { }
                });
            }
            catch { }
        }
    }
}
