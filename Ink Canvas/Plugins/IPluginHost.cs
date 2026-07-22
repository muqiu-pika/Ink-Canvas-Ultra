using System;
using System.Collections.Generic;
using System.Windows;

namespace Ink_Canvas.Plugins
{
    /// <summary>
    /// 主程序暴露给 plugin 的 API 表面。
    /// plugin 通过 host 访问主程序功能、注册 UI、订阅事件。
    /// </summary>
    public interface IPluginHost
    {
        // ===== 基础能力 =====

        /// <summary>plugin 自身所在的绝对目录（以 \ 结尾）</summary>
        string PluginDirectory { get; }

        /// <summary>主程序根目录（App.RootPath）</summary>
        string HostRootPath { get; }

        /// <summary>主程序主窗口实例</summary>
        Window MainWindow { get; }

        /// <summary>写日志到主程序 Log.txt</summary>
        void Log(string message, PluginLogLevel logLevel = PluginLogLevel.Info);

        /// <summary>显示一条主程序通知（轻量级、自动消失）</summary>
        void ShowNotification(string message);

        // ===== 路由 =====

        /// <summary>
        /// 触发路由。优先调用 plugin 通过 RegisterRouteHandler 注册的处理器，
        /// 其次调用主程序内建路由处理器。
        /// </summary>
        bool TriggerRoute(string route, object parameter = null);

        /// <summary>plugin 注册自定义路由处理器，响应主程序或其他 plugin 触发的路由</summary>
        void RegisterRouteHandler(string route, Func<object, bool> handler);

        /// <summary>注销 plugin 之前注册的路由处理器</summary>
        void UnregisterRouteHandler(string route);

        // ===== 主程序画布与元素 API =====

        /// <summary>获取主程序 InkCanvas（白板/批注画布）</summary>
        System.Windows.Controls.InkCanvas GetInkCanvas();

        /// <summary>获取当前在 InkCanvas 上被选中的元素列表（含 Stroke 与 UIElement）</summary>
        IReadOnlyList<UIElement> GetSelectedElements();

        /// <summary>提交一次元素插入历史，供主程序撤销栈记录</summary>
        void CommitElementInsertHistory(UIElement element);

        /// <summary>主程序存放媒体依赖文件的目录（Settings.Automation.AutoSavedStrokesLocation）</summary>
        string AutoSavedStrokesLocation { get; }

        // ===== 选择控制条插槽 =====

        /// <summary>
        /// 将 plugin 自定义的选择控制条 UI 注册到主程序的选择浮条容器中。
        /// 主程序会根据当前选中元素类型决定显示/隐藏，由 plugin 自行处理 Visibility。
        /// </summary>
        void RegisterSelectionControlBar(UIElement controlBar);

        /// <summary>注销之前注册的选择控制条</summary>
        void UnregisterSelectionControlBar(UIElement controlBar);

        // ===== 事件订阅 =====

        /// <summary>主程序即将退出</summary>
        event EventHandler ApplicationExiting;

        /// <summary>白板模式变化（0=批注模式 / 1=白板模式）</summary>
        event EventHandler<BoardModeChangedEventArgs> BoardModeChanged;

        /// <summary>InkCanvas 选择集变化</summary>
        event EventHandler<PluginElementSelectionChangedEventArgs> ElementSelectionChanged;

        /// <summary>元素被从画布移除（如用户删除笔迹/元素）</summary>
        event EventHandler<PluginElementEventArgs> ElementRemoved;

        /// <summary>元素被变换（移动/缩放/旋转）</summary>
        event EventHandler<PluginElementEventArgs> ElementTransformed;
    }

    /// <summary>
    /// plugin 写日志的级别。主程序内部映射到 LogHelper.LogType。
    /// </summary>
    public enum PluginLogLevel
    {
        Info,
        Trace,
        Warning,
        Error,
        Event
    }

    public class BoardModeChangedEventArgs : EventArgs
    {
        public int NewMode { get; set; }
    }

    /// <summary>InkCanvas 选择集变化事件参数</summary>
    public class PluginElementSelectionChangedEventArgs : EventArgs
    {
        /// <summary>当前选中的 UIElement 列表（不含 Stroke）</summary>
        public IReadOnlyList<UIElement> SelectedElements { get; set; }
    }

    /// <summary>元素被移除或变换的事件参数</summary>
    public class PluginElementEventArgs : EventArgs
    {
        /// <summary>受影响的元素（若批量操作则为首个元素）</summary>
        public UIElement Element { get; set; }
    }
}
