using Ink_Canvas.Helpers;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;

namespace Ink_Canvas
{
    /// <summary>
    /// 「图形」面板通用插件扩展点：插件通过 IPluginHost.RegisterShapeTool 注册的工具项
    /// 会同时出现在浮动栏（ShapePageShapes）与白板栏（BoardBorderDrawShape 内同名
    /// ShapePageShapes）图形面板的末尾；未注册任何工具时面板 XAML 与旧版完全一致。
    /// 工厂会为两个面板分别调用一次（UIElement 不能跨视觉树共享）。
    /// </summary>
    public partial class MainWindow
    {
        private sealed class PluginShapeToolEntry
        {
            public string Id;
            public int Order;
            public Func<UIElement> Factory;
            public FrameworkElement FloatRow;
            public FrameworkElement BoardRow;
            public bool Attached;
        }

        private readonly Dictionary<string, PluginShapeToolEntry> _pluginShapeToolEntries =
            new Dictionary<string, PluginShapeToolEntry>(StringComparer.Ordinal);
        private bool _pluginShapeToolsLoadedHooked;

        /// <summary>注册（宿主经 PluginHostOptions 调用，已在 UI 线程）。</summary>
        public void RegisterPluginShapeTool(string id, int order, Func<UIElement> toolFactory)
        {
            try
            {
                if (string.IsNullOrEmpty(id) || toolFactory == null) return;

                // 同 id 重复注册：先摘除旧条目再挂新的
                UnregisterPluginShapeTool(id);

                var entry = new PluginShapeToolEntry { Id = id, Order = order, Factory = toolFactory };
                _pluginShapeToolEntries[id] = entry;

                var floatPanel = GetShapeToolsInnerPanel(MWFloatBarHost);
                var boardPanel = GetShapeToolsInnerPanel(MWBoardHost);
                if (floatPanel == null || boardPanel == null)
                {
                    // 构造期插件加载可能早于子面板就绪：挂 Window.Loaded 补挂一次
                    HookShapeToolsLoadedFlush();
                    return;
                }

                AttachShapeToolRows(entry, floatPanel, boardPanel);
                SortPluginShapeToolRows(floatPanel, boardPanel);
            }
            catch (Exception ex)
            {
                LogHelper.WriteLogToFile("注册图形面板工具失败 [" + id + "]: " + ex.Message, LogHelper.LogType.Warning);
            }
        }

        /// <summary>移除注册（宿主经 PluginHostOptions 调用，已在 UI 线程）。</summary>
        public void UnregisterPluginShapeTool(string id)
        {
            try
            {
                if (string.IsNullOrEmpty(id)) return;
                if (_pluginShapeToolEntries.TryGetValue(id, out var entry))
                {
                    DetachShapeToolRows(entry);
                    _pluginShapeToolEntries.Remove(id);
                }
            }
            catch
            {
                // 面板结构异常时忽略，保持宿主可用
            }
        }

        private void HookShapeToolsLoadedFlush()
        {
            if (_pluginShapeToolsLoadedHooked) return;
            _pluginShapeToolsLoadedHooked = true;
            Loaded += (s, e) =>
            {
                try
                {
                    var floatPanel = GetShapeToolsInnerPanel(MWFloatBarHost);
                    var boardPanel = GetShapeToolsInnerPanel(MWBoardHost);
                    if (floatPanel == null || boardPanel == null) return;

                    foreach (var entry in _pluginShapeToolEntries.Values.ToList())
                    {
                        if (!entry.Attached)
                        {
                            AttachShapeToolRows(entry, floatPanel, boardPanel);
                        }
                    }
                    SortPluginShapeToolRows(floatPanel, boardPanel);
                }
                catch (Exception ex)
                {
                    LogHelper.WriteLogToFile("补挂图形面板工具失败: " + ex.Message, LogHelper.LogType.Warning);
                }
            };
        }

        /// <summary>取两个图形面板的内层容器（Viewbox 下的 SimpleStackPanel，即图形按钮行所在 Panel）。</summary>
        private Panel GetShapeToolsInnerPanel(FrameworkElement host)
        {
            try
            {
                if (host == null) return null;
                var viewbox = FindXamlPart(host, "ShapePageShapes") as Viewbox;
                return viewbox?.Child as Panel;
            }
            catch
            {
                return null;
            }
        }

        private void AttachShapeToolRows(PluginShapeToolEntry entry, Panel floatPanel, Panel boardPanel)
        {
            if (entry.Attached) return;

            entry.FloatRow = BuildShapeToolRow(entry, floatPanel, isBoard: false);
            if (entry.FloatRow != null)
            {
                floatPanel.Children.Add(entry.FloatRow);
            }

            entry.BoardRow = BuildShapeToolRow(entry, boardPanel, isBoard: true);
            if (entry.BoardRow != null)
            {
                boardPanel.Children.Add(entry.BoardRow);
            }

            entry.Attached = entry.FloatRow != null || entry.BoardRow != null;
        }

        /// <summary>为单个面板构建工具行：50 高的水平行，元素由插件工厂生成（每面板独立实例）。</summary>
        private FrameworkElement BuildShapeToolRow(PluginShapeToolEntry entry, Panel panel, bool isBoard)
        {
            if (panel == null) return null;
            try
            {
                var row = new StackPanel
                {
                    Height = 50,
                    Orientation = Orientation.Horizontal
                };
                // 通过 TextElement.Foreground 继承，让插件工具内的文本自动跟随当前面板主题
                row.SetResourceReference(TextElement.ForegroundProperty,
                    isBoard ? "BoardBarForeground" : "FloatBarForeground");
                var tool = entry.Factory();
                if (tool == null) return null;
                row.Children.Add(tool);
                return row;
            }
            catch (Exception ex)
            {
                LogHelper.WriteLogToFile("生成图形面板工具元素失败 [" + entry.Id + "]: " + ex.Message, LogHelper.LogType.Warning);
                return null;
            }
        }

        private void DetachShapeToolRows(PluginShapeToolEntry entry)
        {
            try
            {
                if (entry.FloatRow?.Parent is Panel fp) fp.Children.Remove(entry.FloatRow);
                if (entry.BoardRow?.Parent is Panel bp) bp.Children.Remove(entry.BoardRow);
            }
            catch { }
            entry.FloatRow = null;
            entry.BoardRow = null;
            entry.Attached = false;
        }

        /// <summary>按 Order 重排已注册的工具行（只影响插件行之间的相对顺序，始终位于面板末尾）。</summary>
        private void SortPluginShapeToolRows(Panel floatPanel, Panel boardPanel)
        {
            try
            {
                var ordered = _pluginShapeToolEntries.Values
                    .Where(t => t.Attached)
                    .OrderBy(t => t.Order)
                    .ThenBy(t => t.Id, StringComparer.Ordinal)
                    .ToList();

                foreach (var entry in ordered)
                {
                    if (entry.FloatRow?.Parent == floatPanel) floatPanel.Children.Remove(entry.FloatRow);
                    if (entry.BoardRow?.Parent == boardPanel) boardPanel.Children.Remove(entry.BoardRow);
                }
                foreach (var entry in ordered)
                {
                    if (entry.FloatRow != null) floatPanel.Children.Add(entry.FloatRow);
                    if (entry.BoardRow != null) boardPanel.Children.Add(entry.BoardRow);
                }
            }
            catch
            {
                // 排序失败不影响功能
            }
        }
    }
}
