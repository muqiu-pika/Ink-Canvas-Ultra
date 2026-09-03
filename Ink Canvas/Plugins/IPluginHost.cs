using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Media.Imaging;

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

        /// <summary>照片清晰度 DPI（Settings.Automation.PhotoClarityDpi）</summary>
        int PhotoClarityDpi { get; }

        /// <summary>
        /// 将图片添加到主程序照片列表。
        /// filePath 为原始文件路径（可选），会复制到 File Dependency 目录。
        /// </summary>
        void AddCapturedPhoto(BitmapImage image, string filePath = null);

        /// <summary>
        /// 更新照片列表中对应 filePath 的文档照片。
        /// 如果存在则替换图片并保留时间戳等元数据；不存在则新增。
        /// </summary>
        void UpdateCapturedPhoto(string filePath, BitmapImage newImage);

        /// <summary>
        /// 替换画板上对应 filePath 的文档图片源，保留 Left/Top 位置、宽度及其他内容不变。
        /// 返回是否成功找到并替换。
        /// </summary>
        bool ReplaceDocumentImageOnCanvas(string filePath, BitmapImage newImage);

        /// <summary>获取当前页码（白板模式为 CurrentWhiteboardIndex，批注模式为 0）</summary>
        int GetCurrentPageIndex();

        /// <summary>
        /// 如果 sourceFilePath 对应的文档此前有保存的笔迹/元素，
        /// 则自动恢复到当前页。返回是否成功找到并恢复了保存的内容。
        /// </summary>
        bool RestoreDocumentPageIfSaved(string sourceFilePath);

        /// <summary>
        /// 检查照片列表中是否已经存在对应 sourceFilePath 的文档照片。
        /// </summary>
        bool HasCapturedPhotoForFile(string sourceFilePath);

        // ===== 选择控制条插槽 =====

        /// <summary>
        /// 将 plugin 自定义的选择控制条 UI 注册到主程序的选择浮条容器中。
        /// 主程序会根据当前选中元素类型决定显示/隐藏，由 plugin 自行处理 Visibility。
        /// </summary>
        void RegisterSelectionControlBar(UIElement controlBar);

        /// <summary>注销之前注册的选择控制条</summary>
        void UnregisterSelectionControlBar(UIElement controlBar);

        // ===== 快捷键重绑定 =====

        /// <summary>
        /// 获取软件内置快捷动作的当前信息列表。
        /// 插件可用它做「自定义快捷键」设置界面。
        /// </summary>
        IReadOnlyList<HotkeyActionInfo> GetHotkeyActions();

        /// <summary>
        /// 为一个内置快捷动作重新绑定组合键。
        /// </summary>
        /// <param name="actionId">动作标识（来自 GetHotkeyActions 的 Id）</param>
        /// <param name="combo">组合键文本，如 "Ctrl+Shift+V"；空串/空值表示禁用该动作。</param>
        /// <returns>是否设置成功。</returns>
        bool SetHotkey(string actionId, string combo);

        /// <summary>将一个内置快捷动作恢复为软件默认按键。</summary>
        bool ResetHotkey(string actionId);

        /// <summary>将所有内置快捷动作恢复为软件默认按键。</summary>
        void ResetAllHotkeys();

        /// <summary>
        /// 返回与指定组合键冲突的其他快捷键动作（供「自定义快捷键」在设置前检测重复）。
        /// 不包含 actionId 本身；combo 非法或没有冲突时返回空列表。
        /// </summary>
        IReadOnlyList<HotkeyActionInfo> GetConflictingHotkeys(string actionId, string combo);

        /// <summary>
        /// 暂时挂起软件的全部快捷键（全局注销 + 窗口级移除 KeyBinding）。
        /// 用于「捕获新快捷键」期间，避免按下与现有键冲突的按键时执行对应功能。
        /// 长时间未调用 ResumeHotkeys 会由宿主自动恢复，防止卡死。
        /// </summary>
        void SuspendHotkeys();

        /// <summary>恢复被 SuspendHotkeys 挂起的所有快捷键并按当前配置重新注册。</summary>
        void ResumeHotkeys();

        // ===== 插件工坊设置面板 =====

        /// <summary>
        /// 注册一个「工厂方法」用来构建插件在插件工坊里显示的设置面板 UI。
        /// 插件被禁用/卸载时自动移除。工坊在该插件条目下展示，并用同一位置的图标展开/折叠。
        /// 每次展开工坊会重新调用该工厂以刷新 UI。
        /// </summary>
        void RegisterSettingsPanel(Func<UIElement> panelFactory);

        /// <summary>注销之前注册的设置面板工厂。</summary>
        void UnregisterSettingsPanel();

        // ===== 工具栏按钮排序 =====

        /// <summary>
        /// 获取当前可重排的工具栏分组（浮动工具栏 / 白板工具栏）。
        /// 每个分组列出可在一个条带内调整顺序的功能按钮。
        /// </summary>
        IReadOnlyList<ToolbarReorderGroup> GetReorderableToolbarGroups();

        /// <summary>
        /// 将指定工具栏的功能按钮调整为给定的顺序（立即生效）。
        /// orderedItemIds 必须是该分组当前按钮标识的一个完整排列（不增不减、不重复），
        /// 否则视为无效并返回 false（不产生任何改动）。
        /// </summary>
        bool ApplyToolbarOrder(string placement, IReadOnlyList<string> orderedItemIds);

        /// <summary>将指定工具栏恢复为默认按钮顺序。</summary>
        void ResetToolbarPlacement(string placement);

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

    /// <summary>
    /// 工具栏分组内可重排的单个功能按钮。
    /// </summary>
    public class ToolbarReorderItem
    {
        /// <summary>稳定标识（功能按钮的唯一 id，跨重启保持不变）。</summary>
        public string Id { get; set; }

        /// <summary>用户可见名称（优先取按钮 ToolTip，否则用主程序映射的中文名）。</summary>
        public string DisplayName { get; set; }

        /// <summary>默认（原始）顺序下标，用于「恢复默认」排序。</summary>
        public int DefaultIndex { get; set; }
    }

    /// <summary>
    /// 一个可重排的工具栏分组（一个条带条）。
    /// </summary>
    public class ToolbarReorderGroup
    {
        /// <summary>分组成员标识：<c>float-bar</c>（浮动工具栏）或 <c>board-toolbar</c>（白板工具栏）。</summary>
        public string Placement { get; set; }

        /// <summary>分组展示名称（如「浮动工具栏」）。</summary>
        public string Name { get; set; }

        /// <summary>分组内可重排的功能按钮列表（按当前顺序）。</summary>
        public IReadOnlyList<ToolbarReorderItem> Items { get; set; }
    }
}
