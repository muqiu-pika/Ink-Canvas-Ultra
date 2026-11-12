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
    /// ShapePageShapes）图形面板中；未注册任何工具时面板 XAML 与旧版完全一致。
    ///
    /// 摆放策略：面板内容是"若干 50 高的水平行"，各面板外层 Viewbox 带 MaxHeight 限制，
    /// 追加新行会触发整体等比缩小并在右侧留出空列。因此优先把工具元素
    /// <see cref="RegisterShapeTool"/> 追加进最后一行的空位（最后一行的子项数少于
    /// 最宽行时视为有空位）；只有在没有空位时才新建一行，并按宽度缩放比相应抬高
    /// Viewbox 的 MaxHeight，保证不触发整体缩小。工厂会为两个面板分别调用一次
    /// （UIElement 不能跨视觉树共享）。
    /// </summary>
    public partial class MainWindow
    {
        private sealed class PluginShapeToolEntry
        {
            public string Id;
            public int Order;
            public Func<UIElement> Factory;
            public FrameworkElement FloatRow;
            public UIElement FloatTool;
            public FrameworkElement BoardRow;
            public UIElement BoardTool;
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

                AttachShapeTool(entry, floatPanel, boardPanel);
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
                    DetachShapeTool(entry);
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
                            AttachShapeTool(entry, floatPanel, boardPanel);
                        }
                    }
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

        private void AttachShapeTool(PluginShapeToolEntry entry, Panel floatPanel, Panel boardPanel)
        {
            if (entry.Attached) return;

            AttachToolInPanel(entry, floatPanel, isBoard: false, isFloat: true);
            AttachToolInPanel(entry, boardPanel, isBoard: true, isFloat: false);

            entry.Attached = entry.FloatTool != null || entry.BoardTool != null;
        }

        private void AttachToolInPanel(PluginShapeToolEntry entry, Panel panel, bool isBoard, bool isFloat)
        {
            if (panel == null) return;
            try
            {
                var tool = BuildShapeToolElement(entry, isBoard);
                if (tool == null) return;

                // 1) 最后一行有空位（子项数少于最宽行）：直接追加进该行，不新增行高
                var lastRow = panel.Children.Count > 0 ? panel.Children[panel.Children.Count - 1] as Panel : null;
                if (lastRow != null)
                {
                    int widest = 0;
                    foreach (var child in panel.Children)
                    {
                        if (child is Panel row && row.Children.Count > widest) widest = row.Children.Count;
                    }
                    if (lastRow.Children.Count < widest)
                    {
                        lastRow.Children.Add(tool);
                        if (isFloat) { entry.FloatRow = lastRow; entry.FloatTool = tool; }
                        else { entry.BoardRow = lastRow; entry.BoardTool = tool; }
                        return;
                    }
                }

                // 2) 无空位：新建一行，并按宽度缩放比抬高 Viewbox 的 MaxHeight，避免触发整体缩小
                var newRow = new StackPanel { Height = 50, Orientation = Orientation.Horizontal };
                newRow.SetResourceReference(TextElement.ForegroundProperty,
                    isBoard ? "BoardBarForeground" : "FloatBarForeground");
                newRow.Children.Add(tool);
                panel.Children.Add(newRow);
                GrowShapePageMaxHeight(panel, isBoard);
                if (isFloat) { entry.FloatRow = newRow; entry.FloatTool = tool; }
                else { entry.BoardRow = newRow; entry.BoardTool = tool; }
            }
            catch (Exception ex)
            {
                LogHelper.WriteLogToFile("挂载图形面板工具失败 [" + entry.Id + "]: " + ex.Message, LogHelper.LogType.Warning);
            }
        }

        /// <summary>新建行后按内容宽度缩放比抬高外层 Viewbox 的 MaxHeight（行高 50 在缩放后占的高度）。</summary>
        private void GrowShapePageMaxHeight(Panel panel, bool isBoard)
        {
            try
            {
                var viewbox = panel.Parent as Viewbox;
                if (viewbox == null || double.IsNaN(viewbox.MaxHeight) || double.IsInfinity(viewbox.MaxHeight)) return;
                double naturalWidth = panel.DesiredSize.Width;
                if (naturalWidth <= 0 || double.IsNaN(viewbox.Width) || double.IsInfinity(viewbox.Width)) return;
                double widthScale = viewbox.Width / naturalWidth;
                if (widthScale <= 0 || widthScale > 1) widthScale = 1;
                viewbox.MaxHeight += 50 * widthScale;
            }
            catch
            {
                // 抬高失败不影响功能
            }
        }

        /// <summary>构建工具元素（每面板独立调用工厂，前景色随面板主题继承宿主行级资源）。</summary>
        private UIElement BuildShapeToolElement(PluginShapeToolEntry entry, bool isBoard)
        {
            try
            {
                var tool = entry.Factory();
                if (tool is FrameworkElement fe)
                {
                    // 通过 TextElement.Foreground 继承，让插件工具内的文本自动跟随当前面板主题
                    fe.SetResourceReference(TextElement.ForegroundProperty,
                        isBoard ? "BoardBarForeground" : "FloatBarForeground");
                }
                return tool;
            }
            catch (Exception ex)
            {
                LogHelper.WriteLogToFile("生成图形面板工具元素失败 [" + entry.Id + "]: " + ex.Message, LogHelper.LogType.Warning);
                return null;
            }
        }

        private void DetachShapeTool(PluginShapeToolEntry entry)
        {
            try
            {
                RemoveTool(entry.FloatRow, entry.FloatTool);
                RemoveTool(entry.BoardRow, entry.BoardTool);
            }
            catch { }
            entry.FloatRow = null;
            entry.FloatTool = null;
            entry.BoardRow = null;
            entry.BoardTool = null;
            entry.Attached = false;
        }

        private static void RemoveTool(FrameworkElement row, UIElement tool)
        {
            if (row == null || tool == null) return;
            if (row is Panel rowPanel && rowPanel.Children.Contains(tool))
            {
                rowPanel.Children.Remove(tool);
            }
            // 行是自己动态创建且已空时一并移除（空行只可能是新建行；原始行由 XAML 定义，不动）
            if (row is StackPanel sp && sp.Children.Count == 0 && sp.Parent is Panel parent &&
                !(parent is System.Windows.Controls.Viewbox) && parent.Children.Contains(row))
            {
                // 仅为本插件新建的行（末尾追加的空行）才移除，XAML 原始行保持不动
                parent.Children.Remove(row);
            }
        }
    }
}
