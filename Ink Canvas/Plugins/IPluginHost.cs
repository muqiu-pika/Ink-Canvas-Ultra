using System;
using System.Windows;

namespace Ink_Canvas.Plugins
{
    /// <summary>
    /// 主程序暴露给 plugin 的 API 表面。
    /// plugin 通过 host 访问主程序功能、注册 UI、订阅事件。
    /// </summary>
    public interface IPluginHost
    {
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

        // ===== 声明式入口点路由 =====

        /// <summary>
        /// 触发主程序内建路由（与 PluginEntryPoint.Route 对应）。
        /// 例如 plugin 自定义 UI 中点击按钮时调用 host.TriggerRoute("video-presenter")。
        /// </summary>
        bool TriggerRoute(string route, object parameter = null);

        // ===== 事件订阅 =====

        /// <summary>主程序即将退出</summary>
        event EventHandler ApplicationExiting;

        /// <summary>白板模式变化（0=批注模式 / 1=白板模式）</summary>
        event EventHandler<BoardModeChangedEventArgs> BoardModeChanged;
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
}
