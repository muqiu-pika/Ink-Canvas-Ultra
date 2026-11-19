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
            public double HostMarginLeft;
        }

        private readonly Dictionary<string, PluginShapeToolEntry> _pluginShapeToolEntries =
            new Dictionary<string, PluginShapeToolEntry>(StringComparer.Ordinal);
        /// <summary>本插件机制新建的行（基线测量时需整行摘除；原始行是 XAML 定义的）</summary>
        private readonly HashSet<Panel> _pluginShapeToolRows = new HashSet<Panel>();
        private ShapePanelSizeBaseline _floatSizeBaseline;
        private ShapePanelSizeBaseline _boardSizeBaseline;
        // 图形面板高度自适应：XAML 基线高度与底边偏移（首次计算时从布局捕获）
        private double _floatShapeBaselineHeight = double.NaN;
        private double _boardShapeBaselineHeight = double.NaN;
        private double _floatShapeBottomOffset = double.NaN;
        private double _boardShapeBottomOffset = double.NaN;
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

        #region 面板高度自适应（内容不被底边裁切，底边位置保持不变）

        /// <summary>
        /// 图形面板高度自适应：面板在 XAML 里是固定高（249），而内容实际需求更高
        /// （标题栏 + Viewbox 渲染高 + 边框，约 270）——Viewbox 会把内容等比放大后溢出面板底边，
        /// 表现为最底下一行图形被裁切（未安装插件时同样存在）。
        /// 这里按内容实际需求设置面板高度，并把缩放宿主的 Margin.Top 反算成
        /// 「原底边偏移 - 新高度」，让面板只向上长、底边始终停在原位（不压住浮动栏/白板栏），
        /// 同时缩放锚点跟随底边，设置项「图形面板缩放」与墨迹选项底边对齐均不受影响。
        /// 与插件是否安装无关，主窗口构造时即挂接 SizeChanged。
        /// </summary>
        public void InitShapePanelAutoHeight()
        {
            HookShapePanelAutoHeight(isBoard: false);
            HookShapePanelAutoHeight(isBoard: true);
            UpdateShapePanelAutoHeight(isBoard: false);
            UpdateShapePanelAutoHeight(isBoard: true);
        }

        private void HookShapePanelAutoHeight(bool isBoard)
        {
            var border = GetShapePanelBorder(isBoard);
            if (border == null) return;
            border.SizeChanged += (s, e) => UpdateShapePanelAutoHeight(isBoard);
        }

        /// <summary>「默认图形 / 我的图库」页切换后补刷（Visibility 切换不改面板高度，SizeChanged 不触发）。</summary>
        public void RefreshShapePanelAutoHeight()
        {
            UpdateShapePanelAutoHeight(isBoard: false);
            UpdateShapePanelAutoHeight(isBoard: true);

            // 图形面板高度可能因页面内容变化，墨迹选项窗口高度跟随它，需同步重新对齐
            try
            {
                (MWFloatBarHost as MW_FloatBar)?.AlignPaletteNow();
                (MWBoardHost as MW_Board)?.AlignPaletteNow();
            }
            catch { }
        }

        private Border GetShapePanelBorder(bool isBoard)
        {
            if (isBoard) return BoardBorderDrawShape as Border;
            return BorderDrawShape as Border;
        }

        private void UpdateShapePanelAutoHeight(bool isBoard)
        {
            try
            {
                var border = GetShapePanelBorder(isBoard);
                FrameworkElement scaleHost = isBoard
                    ? BoardBorderDrawShapeScaleHost as FrameworkElement
                    : BorderDrawShapeScaleHost as FrameworkElement;
                if (border == null || scaleHost == null) return;
                if (!(border.Child is Panel stack)) return;
                double currentHeight = double.IsNaN(border.ActualHeight) || border.ActualHeight <= 0
                    ? border.Height
                    : border.ActualHeight;
                if (double.IsNaN(currentHeight) || double.IsInfinity(currentHeight)) return;

                // 首次计算时从布局捕获 XAML 基线高度与底边偏移（底边 = Margin.Top + 基线高度）
                if (isBoard)
                {
                    if (double.IsNaN(_boardShapeBaselineHeight)) _boardShapeBaselineHeight = currentHeight;
                    if (double.IsNaN(_boardShapeBottomOffset))
                        _boardShapeBottomOffset = scaleHost.Margin.Top + _boardShapeBaselineHeight;
                    currentHeight = _boardShapeBaselineHeight;
                }
                else
                {
                    if (double.IsNaN(_floatShapeBaselineHeight)) _floatShapeBaselineHeight = currentHeight;
                    if (double.IsNaN(_floatShapeBottomOffset))
                        _floatShapeBottomOffset = scaleHost.Margin.Top + _floatShapeBaselineHeight;
                    currentHeight = _floatShapeBaselineHeight;
                }
                double baselineHeight = currentHeight;
                double bottomOffset = isBoard ? _boardShapeBottomOffset : _floatShapeBottomOffset;

                // 内容需求：直接 Measure 内部纵向容器（不受祖先固定高度约束），
                // 加上 BorderThickness 才是面板需要的总高
                stack.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
                double needed = stack.DesiredSize.Height +
                                border.BorderThickness.Top + border.BorderThickness.Bottom;
                // 不低于 XAML 基线高度（内容更少时保持原尺寸）
                double target = Math.Max(baselineHeight, Math.Ceiling(needed));

                if (Math.Abs(border.Height - target) > 0.5)
                {
                    border.Height = target;
                }

                // 底边锚定：Margin.Top = 原底边偏移 - 当前高度；缩放锚点恒为 -Margin.Top
                double newTop = bottomOffset - border.Height;
                if (Math.Abs(scaleHost.Margin.Top - newTop) > 0.5)
                {
                    var margin = scaleHost.Margin;
                    scaleHost.Margin = new Thickness(margin.Left, newTop, margin.Right, margin.Bottom);
                    SetShapeScaleAnchorY(scaleHost, -newTop);
                }
            }
            catch (Exception ex)
            {
                LogHelper.WriteLogToFile("图形面板高度自适应失败: " + ex.Message, LogHelper.LogType.Warning);
            }
        }

        #endregion

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

                // 「墨迹选项」面板与「图形」面板并排，宽度补偿后同步刷新其水平位置
                try
                {
                    if (MWFloatBarHost is MW_FloatBar floatBar) floatBar.AlignPaletteNow();
                    if (MWBoardHost is MW_Board board) board.AlignPaletteNow();
                }
                catch { }
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
                    HostMarginLeft = scaleHost.Margin.Left,
                };
                if (isBoard) _boardSizeBaseline = baseline;
                else _floatSizeBaseline = baseline;
            }

            MeasureShapePanel(panel, isBoard,
                out double baseWidth, out double baseHeight,
                out double currentWidth, out double currentHeight);
            if (baseWidth <= 0 || currentWidth <= 0) return;

            bool fixedMaxHeight = !double.IsNaN(baseline.ViewboxMaxHeight) && !double.IsInfinity(baseline.ViewboxMaxHeight);

            // 基线缩放比 = Viewbox 实际会采用的比例：内容被等比放大/缩小以塞进
            // 「槽宽(235) × MaxHeight」，取二者中较小者。新增内容后只要把槽宽/上限
            // 按同一比例补齐，图标缩放就与未装插件时完全一致（不会整体变小）
            double scaleW = baseWidth > 0 ? baseline.ViewboxWidth / baseWidth : 1.0;
            double scaleH = fixedMaxHeight && baseHeight > 0 ? baseline.ViewboxMaxHeight / baseHeight : 1.0;
            double scale = Math.Max(0.01, Math.Min(scaleW, scaleH));

            // 宽度：槽宽补到「内容宽 × 基线比例」
            double deltaWidth = Math.Max(0, currentWidth * scale - baseline.ViewboxWidth);
            // 高度上限：内容行数变多时抬到「内容高 × 基线比例」，否则上限会反过来压小图标
            double deltaHeight = Math.Max(0, currentHeight * scale - baseline.ViewboxMaxHeight);

            double newViewboxWidth = baseline.ViewboxWidth + deltaWidth;
            double newBorderWidth = baseline.BorderWidth + deltaWidth;

            if (Math.Abs(viewbox.Width - newViewboxWidth) > 0.01) viewbox.Width = newViewboxWidth;
            if (Math.Abs(border.Width - newBorderWidth) > 0.01) border.Width = newBorderWidth;

            // 宽度补偿向左扩展：面板右缘保持原位、左缘左移 delta。
            // 面板左缘原本就位于工具栏锚点左侧（-200），向左长更安全；
            // 若向右扩，浮动栏/白板栏拖到屏幕右侧时面板会超出屏幕显示范围。
            double newLeft = baseline.HostMarginLeft - deltaWidth;
            if (Math.Abs(scaleHost.Margin.Left - newLeft) > 0.01)
            {
                var hostMargin = scaleHost.Margin;
                scaleHost.Margin = new Thickness(newLeft, hostMargin.Top, hostMargin.Right, hostMargin.Bottom);
                SetShapeScaleAnchorX(scaleHost, -newLeft);
            }

            if (deltaHeight > 0)
            {
                // 内容行数增加：抬高 Viewbox 上限，避免新行触发整体缩小。
                // 面板 Border 的高度与底边锚定由 UpdateShapePanelAutoHeight 统一按内容需求接管
                double newMaxHeight = baseline.ViewboxMaxHeight + deltaHeight;
                if (Math.Abs(viewbox.MaxHeight - newMaxHeight) > 0.01) viewbox.MaxHeight = newMaxHeight;
            }

            // 内容（含 MaxHeight 变化）重新核算面板高度与底边
            UpdateShapePanelAutoHeight(isBoard);
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

        /// <summary>设置缩放宿主的底边锚点（RenderTransform 中两个 Translate 互为相反数，锚点 = -Margin.Top）。</summary>
        private static void SetShapeScaleAnchorY(FrameworkElement scaleHost, double anchorY)
        {
            if (scaleHost.RenderTransform is TransformGroup group && group.Children.Count >= 3 &&
                group.Children[0] is TranslateTransform top && group.Children[2] is TranslateTransform bottom)
            {
                top.Y = -anchorY;
                bottom.Y = anchorY;
            }
        }

        /// <summary>设置缩放宿主的水平锚点（锚点 = -Margin.Left，与 XAML 的 translate(-L)/translate(L) 结构一致）。</summary>
        private static void SetShapeScaleAnchorX(FrameworkElement scaleHost, double anchorX)
        {
            if (scaleHost.RenderTransform is TransformGroup group && group.Children.Count >= 3 &&
                group.Children[0] is TranslateTransform top && group.Children[2] is TranslateTransform bottom)
            {
                top.X = -anchorX;
                bottom.X = anchorX;
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
