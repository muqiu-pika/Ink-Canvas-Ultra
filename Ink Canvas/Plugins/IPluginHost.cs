using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

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

        Window MainWindow { get; }

        void Log(string message, PluginLogLevel logLevel = PluginLogLevel.Info);

        /// <summary>显示一条主程序通知（轻量级、自动消失）</summary>
        void ShowNotification(string message);

        /// <summary>
        /// 触发路由。优先调用 plugin 通过 RegisterRouteHandler 注册的处理器，
        /// 其次调用主程序内建路由处理器。
        /// </summary>
        bool TriggerRoute(string route, object parameter = null);

        /// <summary>plugin 注册自定义路由处理器，响应主程序或其他 plugin 触发的路由</summary>
        void RegisterRouteHandler(string route, Func<object, bool> handler);

        /// <summary>注销 plugin 之前注册的路由处理器</summary>
        void UnregisterRouteHandler(string route);

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

        /// <summary>
        /// 将 plugin 自定义的选择控制条 UI 注册到主程序的选择浮条容器中。
        /// 主程序会根据当前选中元素类型决定显示/隐藏，由 plugin 自行处理 Visibility。
        /// </summary>
        void RegisterSelectionControlBar(UIElement controlBar);

        void UnregisterSelectionControlBar(UIElement controlBar);

        IReadOnlyList<HotkeyActionInfo> GetHotkeyActions();

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

        void RegisterSettingsPanel(Func<UIElement> panelFactory);

        void UnregisterSettingsPanel();

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

        void SetToolbarHidden(string placement, IReadOnlyList<string> hiddenItemIds);

        void ResetToolbarPlacement(string placement);

        // ===== 画笔颜色调色板（18 个内置色块：9 普通色 + 6 荧光笔色） =====

        /// <summary>
        /// 获取当前调色板全部槽位（普通色索引 0-8 在前，荧光笔索引 101-106 在后）。
        /// 未自定义的槽位返回内置主题色，因此不装插件时也能正常读取。
        /// </summary>
        IReadOnlyList<PenColorSlotInfo> GetPenColorSlots();

        bool SetPenColorSlot(int slotIndex, Color color);

        void ResetPenColorSlot(int slotIndex);

        void ResetPenColors();

        /// <summary>当前选中的画笔颜色槽位索引（0-8 / 101-106）。</summary>
        int CurrentPenColorIndex { get; }

        bool SelectPenColor(int slotIndex);

        /// <summary>
        /// 默认画笔颜色槽位（启动时初始选中色，会持久化到主设置）。
        /// 取值为 0-8 / 101-106；设为 -1 表示沿用内置默认（批注=红、白板=黑/白随背景）。
        /// </summary>
        int DefaultPenColorIndex { get; set; }

        /// <summary>调色板发生变化（自定义色、恢复默认、选色或主题切换）。</summary>
        event EventHandler PenColorsChanged;

        event EventHandler<PenSlotEditRequestedEventArgs> PenSlotEditRequested;

        /// <summary>
        /// 供主程序内部触发色块修改请求（一般插件无需调用）。
        /// 返回是否有插件处理了该请求（弹过修改界面）；false 表示未安装/未启用相关插件。
        /// </summary>
        bool RaisePenSlotEditRequested(int slotIndex);

        bool IsPluginActive(string pluginId);

        bool IsPluginInstalled(string pluginId);

        event EventHandler ApplicationExiting;

        /// <summary>白板模式变化（0=批注模式 / 1=白板模式）</summary>
        event EventHandler<BoardModeChangedEventArgs> BoardModeChanged;

        event EventHandler<PluginElementSelectionChangedEventArgs> ElementSelectionChanged;

        /// <summary>元素被从画布移除（如用户删除笔迹/元素）</summary>
        event EventHandler<PluginElementEventArgs> ElementRemoved;

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

    public class PluginElementEventArgs : EventArgs
    {
        /// <summary>受影响的元素（若批量操作则为首个元素）</summary>
        public UIElement Element { get; set; }
    }

    /// <summary>
    /// 调色板单个颜色槽位。
    /// 槽位索引沿用主程序既有约定：0-8 为普通颜色，101-106 为荧光笔颜色。
    /// </summary>
    public class PenColorSlotInfo
    {
        /// <summary>槽位索引（0-8 普通色，101-106 荧光笔色）</summary>
        public int Index { get; set; }

        /// <summary>槽位展示名（黑/红/绿/蓝/黄/白/粉/青/橙，荧光笔红…）</summary>
        public string Name { get; set; }

        public bool IsHighlighter { get; set; }

        /// <summary>当前生效颜色（自定义优先，否则为内置主题色）</summary>
        public Color Color { get; set; }

        /// <summary>是否已被插件自定义（false = 使用内置主题色）</summary>
        public bool IsCustomized { get; set; }
    }

    /// <summary>
    /// 「墨迹选项」色块上请求修改颜色的事件参数（鼠标右键 / 触摸长按触发）。
    /// 插件处理完后应把 Handled 置 true；未处理时主程序按"未安装该功能"静默忽略。
    /// </summary>
    public class PenSlotEditRequestedEventArgs : EventArgs
    {
        /// <summary>被操作的槽位索引（0-8 普通色，101-106 荧光笔色）</summary>
        public int SlotIndex { get; set; }

        public bool Handled { get; set; }
    }

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

        public string Name { get; set; }

        /// <summary>分组内可重排的功能按钮列表（按当前顺序）。</summary>
        public IReadOnlyList<ToolbarReorderItem> Items { get; set; }
    }
}
