using Ink_Canvas.Helpers;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;

namespace Ink_Canvas
{
    /// <summary>
    /// 「图形」面板通用插件扩展点：插件通过 IPluginHost.RegisterShapeTool 注册的工具项
    /// 会同时出现在浮动栏（ShapePageShapes）与白板栏（BoardBorderDrawShape 内同名
    /// ShapePageShapes）图形面板中；未注册任何工具时面板 XAML 与旧版完全一致。
    ///
    /// 摆放策略：面板内容是"若干 50 高的水平行"，各面板外层 Viewbox 带固定 Width/MaxHeight，
    /// 外层 Border 也是固定 Width/Height —— 内容一变宽，Viewbox 只能等比缩小，表现为
    /// 「面板不长大、图标反而变小」。因此注册/移除工具后会走 <see cref="RefreshShapePanelSizing"/>：
    /// 先摘掉插件内容测出「基线（纯 XAML 内容）」的自然尺寸，再按基线缩放比把多出来的
    /// 宽/高补到 Viewbox 与 Border 上，使图标保持原尺寸、面板刚好容纳新内容；插件全部
    /// 移除后各项自动回到 XAML 初始值。高度方向向上扩展（Margin.Top 同步上移），
    /// 面板底边始终贴住工具栏，不会向下压住浮动栏/白板栏，墨迹选项底边对齐也保持不变。
    ///
    /// 工具优先 <see cref="RegisterShapeTool"/> 追加进最后一行的空位（最后一行的子项数少于
    /// 最宽行时视为有空位）；只有在没有空位时才新建一行（新行高度同样由 Refresh 计入）。
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
            public UIElement FloatTool;
            public FrameworkElement BoardRow;
            public UIElement BoardTool;
            public bool Attached;
        }

        /// <summary>面板 XAML 初始尺寸快照（首次 Refresh 时抓取，之后始终以它为基准回算）</summary>
        private sealed class ShapePanelSizeBaseline
        {
            public double ViewboxWidth;
            public double ViewboxMaxHeight;
            public double BorderWidth;
            public double BorderHeight;
            public double HostMarginTop;
            public double AnchorY = double.NaN;
        }

        private readonly Dictionary<string, PluginShapeToolEntry> _pluginShapeToolEntries =
            new Dictionary<string, PluginShapeToolEntry>(StringComparer.Ordinal);
        /// <summary>本插件机制新建的行（基线测量时需整行摘除；原始行是 XAML 定义的）</summary>
        private readonly HashSet<Panel> _pluginShapeToolRows = new HashSet<Panel>();
        private ShapePanelSizeBaseline _floatSizeBaseline;
        private ShapePanelSizeBaseline _boardSizeBaseline;
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
                    RefreshShapePanelSizing();
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
            if (entry.Attached) RefreshShapePanelSizing();
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

                // 2) 无空位：新建一行（面板整体的宽高补偿由 RefreshShapePanelSizing 统一计算）
                var newRow = new StackPanel { Height = 50, Orientation = Orientation.Horizontal };
                newRow.SetResourceReference(TextElement.ForegroundProperty,
                    isBoard ? "BoardBarForeground" : "FloatBarForeground");
                newRow.Children.Add(tool);
                panel.Children.Add(newRow);
                _pluginShapeToolRows.Add(newRow);
                if (isFloat) { entry.FloatRow = newRow; entry.FloatTool = tool; }
                else { entry.BoardRow = newRow; entry.BoardTool = tool; }
            }
            catch (Exception ex)
            {
                LogHelper.WriteLogToFile("挂载图形面板工具失败 [" + entry.Id + "]: " + ex.Message, LogHelper.LogType.Warning);
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
                // 空的插件新建行已被移除，同步清理标记，避免基线测量残留
                if (entry.FloatRow is Panel fr && fr.Parent == null) _pluginShapeToolRows.Remove(fr);
                if (entry.BoardRow is Panel br && br.Parent == null) _pluginShapeToolRows.Remove(br);
            }
            catch { }
            entry.FloatRow = null;
            entry.FloatTool = null;
            entry.BoardRow = null;
            entry.BoardTool = null;
            entry.Attached = false;
        }

        #region 面板尺寸自适应（插件工具增减后自动扩/还原）

        /// <summary>
        /// 插件工具挂载或摘除后，把两个「图形」面板的尺寸按内容重算一遍：
        /// 以 XAML 初始值为基线，插件内容多出来的自然宽/高按基线缩放比补偿到 Viewbox 与 Border，
        /// 使图标保持原尺寸、面板刚好容纳新内容；插件内容为空时各项回到 XAML 初始值。
        /// </summary>
        private void RefreshShapePanelSizing()
        {
            try
            {
                RefreshShapePanelSizing(MWFloatBarHost, isBoard: false);
                RefreshShapePanelSizing(MWBoardHost, isBoard: true);
            }
            catch (Exception ex)
            {
                LogHelper.WriteLogToFile("刷新图形面板尺寸失败: " + ex.Message, LogHelper.LogType.Warning);
            }
        }

        private void RefreshShapePanelSizing(FrameworkElement host, bool isBoard)
        {
            if (host == null) return;

            var viewbox = FindXamlPart(host, "ShapePageShapes") as Viewbox;
            if (viewbox == null || !(viewbox.Child is Panel panel)) return;
            if (double.IsNaN(viewbox.Width) || double.IsInfinity(viewbox.Width)) return;

            FrameworkElement border;
            FrameworkElement scaleHost;
            if (isBoard)
            {
                border = BoardBorderDrawShape as FrameworkElement;
                scaleHost = BoardBorderDrawShapeScaleHost as FrameworkElement;
            }
            else
            {
                border = BorderDrawShape as FrameworkElement;
                scaleHost = BorderDrawShapeScaleHost as FrameworkElement;
            }
            if (border == null || scaleHost == null) return;
            if (double.IsNaN(border.Width) || double.IsInfinity(border.Width)) return;

            var baseline = isBoard ? _boardSizeBaseline : _floatSizeBaseline;
            if (baseline == null)
            {
                baseline = new ShapePanelSizeBaseline
                {
                    ViewboxWidth = viewbox.Width,
                    ViewboxMaxHeight = viewbox.MaxHeight,
                    BorderWidth = border.Width,
                    BorderHeight = border.Height,
                    HostMarginTop = scaleHost.Margin.Top,
                    AnchorY = GetShapeScaleAnchorY(scaleHost),
                };
                if (isBoard) _boardSizeBaseline = baseline;
                else _floatSizeBaseline = baseline;
            }

            MeasureShapePanel(panel, isBoard,
                out double baseWidth, out double baseHeight,
                out double currentWidth, out double currentHeight);
            if (baseWidth <= 0 || currentWidth <= 0) return;

            bool fixedHeight = !double.IsNaN(baseline.ViewboxMaxHeight) && !double.IsInfinity(baseline.ViewboxMaxHeight) &&
                               !double.IsNaN(baseline.BorderHeight) && !double.IsInfinity(baseline.BorderHeight);

            // 内容自然宽度与槽宽之比即基线缩放比；内容比槽宽时（本项目两个面板的现状）
            // 新增内容会触发 Viewbox 整体等比缩小，把多出的自然宽按该比例补进槽宽即可保持图标原尺寸
            double scale = baseWidth > baseline.ViewboxWidth
                ? baseline.ViewboxWidth / baseWidth
                : 1.0;
            double deltaWidth = baseWidth > baseline.ViewboxWidth
                ? Math.Max(0, (currentWidth - baseWidth) * scale)
                : Math.Max(0, currentWidth - baseline.ViewboxWidth);
            double deltaHeight = fixedHeight ? Math.Max(0, (currentHeight - baseHeight) * scale) : 0;

            double newViewboxWidth = baseline.ViewboxWidth + deltaWidth;
            double newBorderWidth = baseline.BorderWidth + deltaWidth;

            if (Math.Abs(viewbox.Width - newViewboxWidth) > 0.01) viewbox.Width = newViewboxWidth;
            if (Math.Abs(border.Width - newBorderWidth) > 0.01) border.Width = newBorderWidth;

            if (deltaHeight > 0)
            {
                double newMaxHeight = baseline.ViewboxMaxHeight + deltaHeight;
                double newBorderHeight = baseline.BorderHeight + deltaHeight;
                if (Math.Abs(viewbox.MaxHeight - newMaxHeight) > 0.01) viewbox.MaxHeight = newMaxHeight;
                if (Math.Abs(border.Height - newBorderHeight) > 0.01) border.Height = newBorderHeight;

                // 高度向上扩展：Margin.Top 同步上移，面板底边（Margin.Top + Height）保持不动，
                // 不会向下压住浮动栏/白板栏；墨迹选项底边对齐按同一式子计算，无需改写
                double newTop = baseline.HostMarginTop - deltaHeight;
                if (Math.Abs(scaleHost.Margin.Top - newTop) > 0.01)
                {
                    var margin = scaleHost.Margin;
                    scaleHost.Margin = new Thickness(margin.Left, newTop, margin.Right, margin.Bottom);
                }
                // 缩放锚点跟随面板底边，设置项「图形面板缩放」仍以底边为基准，不产生漂移
                if (!double.IsNaN(baseline.AnchorY)) SetShapeScaleAnchorY(scaleHost, baseline.AnchorY + deltaHeight);
            }
            else if (deltaHeight == 0 && Math.Abs(scaleHost.Margin.Top - baseline.HostMarginTop) > 0.01)
            {
                // 插件新建行已移除：还原底边锚点
                var margin = scaleHost.Margin;
                scaleHost.Margin = new Thickness(margin.Left, baseline.HostMarginTop, margin.Right, margin.Bottom);
                if (!double.IsNaN(baseline.AnchorY)) SetShapeScaleAnchorY(scaleHost, baseline.AnchorY);
            }
        }

        private sealed class RemovedShapeToolItem
        {
            public Panel Row;
            public int Index;
            public UIElement Tool;
        }

        /// <summary>
        /// 测量面板内容的自然尺寸：先量含插件内容的当前尺寸，再临时摘掉插件工具与插件新建行
        /// 量出基线（纯 XAML 内容），随后原位还原。摘除与还原在同一调用内完成、不经过渲染，无闪烁。
        /// 面板可能处于 Collapsed（展开前不参与布局），因此逐个元素主动 Measure 而不依赖 DesiredSize 缓存。
        /// </summary>
        private void MeasureShapePanel(Panel panel, bool isBoard,
            out double baseWidth, out double baseHeight, out double currentWidth, out double currentHeight)
        {
            baseWidth = baseHeight = currentWidth = currentHeight = 0;
            var infinite = new Size(double.PositiveInfinity, double.PositiveInfinity);

            // 行间垂直间距（面板为 SimpleStackPanel，Spacing 取负以压缩行距）
            double rowGap = 0;
            try { rowGap = (double)((dynamic)panel).Spacing; } catch { /* 非 SimpleStackPanel 时按 0 处理 */ }

            // 不能用 panel.DesiredSize：面板在展开前处于 Collapsed，且摘除子项后祖先链不一定重算，
            // 实测它会一直是陈旧值。逐行 Measure 取“最宽行宽 / 行高合计+行距”才可靠
            MeasureShapeRows(panel, infinite, rowGap, out currentWidth, out currentHeight);
            if (currentWidth <= 0) return;

            // 1) 摘除插件新建的整行（按索引从后往前，放回时倒序即可恢复原位置）
            var removedRows = new List<KeyValuePair<int, UIElement>>();
            for (int i = panel.Children.Count - 1; i >= 0; i--)
            {
                if (panel.Children[i] is Panel row && _pluginShapeToolRows.Contains(row))
                {
                    removedRows.Add(new KeyValuePair<int, UIElement>(i, row));
                    panel.Children.RemoveAt(i);
                }
            }

            // 2) 摘除挂在原始行里的插件工具
            var removedTools = new List<RemovedShapeToolItem>();
            foreach (var entry in _pluginShapeToolEntries.Values)
            {
                var row = (isBoard ? entry.BoardRow : entry.FloatRow) as Panel;
                var tool = isBoard ? entry.BoardTool : entry.FloatTool;
                if (row == null || tool == null) continue;
                int index = row.Children.IndexOf(tool);
                if (index < 0) continue;
                removedTools.Add(new RemovedShapeToolItem { Row = row, Index = index, Tool = tool });
                row.Children.RemoveAt(index);
            }

            try
            {
                MeasureShapeRows(panel, infinite, rowGap, out baseWidth, out baseHeight);
            }
            finally
            {
                foreach (var removed in removedTools)
                {
                    removed.Row.Children.Insert(removed.Index, removed.Tool);
                }
                for (int i = removedRows.Count - 1; i >= 0; i--)
                {
                    panel.Children.Insert(removedRows[i].Key, removedRows[i].Value);
                }
                // 恢复子项会让各行 Measure 失效，交由真实布局重算，避免缓存被测量结果污染
                panel.InvalidateMeasure();
            }

            if (baseWidth <= 0 || baseHeight <= 0)
            {
                baseWidth = currentWidth;
                baseHeight = currentHeight;
            }
        }

        /// <summary>逐行 Measure：宽度取最宽行，高度为各行高合计加行间距。</summary>
        private static void MeasureShapeRows(Panel panel, Size infinite, double rowGap,
            out double width, out double height)
        {
            width = 0;
            height = 0;
            int rows = 0;
            foreach (var child in panel.Children)
            {
                if (!(child is Panel row)) continue;
                row.Measure(infinite);
                rows++;
                if (row.DesiredSize.Width > width) width = row.DesiredSize.Width;
                height += row.DesiredSize.Height;
            }
            if (rows > 0) height += rowGap * (rows - 1);
        }

        /// <summary>取缩放宿主的底边锚点（RenderTransform 中第二个 Translate 的绝对值）。</summary>
        private static double GetShapeScaleAnchorY(FrameworkElement scaleHost)
        {
            if (scaleHost.RenderTransform is TransformGroup group && group.Children.Count >= 3 &&
                group.Children[2] is TranslateTransform bottom)
            {
                return Math.Abs(bottom.Y);
            }
            return double.NaN;
        }

        private static void SetShapeScaleAnchorY(FrameworkElement scaleHost, double anchorY)
        {
            if (scaleHost.RenderTransform is TransformGroup group && group.Children.Count >= 3 &&
                group.Children[0] is TranslateTransform top && group.Children[2] is TranslateTransform bottom)
            {
                top.Y = -anchorY;
                bottom.Y = anchorY;
            }
        }

        #endregion

        private static void RemoveTool(FrameworkElement row, UIElement tool)
        {
            if (row == null || tool == null) return;
            if (row is Panel rowPanel && rowPanel.Children.Contains(tool))
            {
                rowPanel.Children.Remove(tool);
            }
            // 行是自己动态创建且已空时一并移除（空行只可能是新建行；原始行由 XAML 定义，不动）
            // 注：这里只判断 Parent 是否为 Panel —— Viewbox 等非 Panel 容器本来就不满足该模式，
            // 无需再额外排除（原写法 `!(parent is Viewbox)` 恒为真，会触发 CS0184）
            if (row is StackPanel sp && sp.Children.Count == 0 && sp.Parent is Panel parent &&
                parent.Children.Contains(row))
            {
                // 仅为本插件新建的行（末尾追加的空行）才移除，XAML 原始行保持不动
                parent.Children.Remove(row);
            }
        }
    }
}
