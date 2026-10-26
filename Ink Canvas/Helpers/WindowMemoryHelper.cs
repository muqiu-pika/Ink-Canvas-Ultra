using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Ink_Canvas.Helpers
{
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
