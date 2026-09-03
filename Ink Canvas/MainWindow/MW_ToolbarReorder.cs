using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using Ink_Canvas.Plugins;

namespace Ink_Canvas
{
    /// <summary>
    /// 工具栏按钮排序（宿主实现）。
    /// 浮动工具栏按显隐行为分为三组：
    ///   - float-bar:fixed      固定组   （鼠标/画笔/清空，永不被折叠）
    ///   - float-bar:collapsible 折叠组   （内嵌 StackPanelCanvasControls，随折叠显隐）
    ///   - float-bar:trailing    右侧组   （画板/工具/隐藏，只能在本组内调整）
    /// 白板工具栏为单组，但“选择/画布/更多/退出”以及两侧边缘按钮（手势/重做，其边框需保持位置固定）
    /// 固定不参与排序。每组内只调整自身按钮顺序，绝不跨容器移动，因此折叠/边框等行为不变。
    /// </summary>
    public partial class MainWindow
    {
        private const string PlaceFloatFixed = "float-bar:fixed";
        private const string PlaceFloatCollapsible = "float-bar:collapsible";
        private const string PlaceFloatTrailing = "float-bar:trailing";
        private const string PlaceBoard = "board-toolbar";

        // 白板工具栏固定按钮（不参与排序）：手势 / 画布 / 更多 / 退出。
        // 中间的工具按钮（含左边缘“选择”与右边缘“重做”）都可排序；
        // 排序后用 ApplyBoardEdgeBorders 自动把圆角边框加到位于中间区两端的按钮上。
        private static readonly HashSet<string> BoardPinnedHandlers = new HashSet<string>(StringComparer.Ordinal)
        {
            "TwoFingerGestureBorder_Click",      // 手势（左端固定）
            "BoardChangeBackgroundColorBtn_Click", // 画布
            "SymbolIconTools_Click",             // 更多
            "BtnExit_Click"                      // 退出
        };
        // 白板工具栏固定按钮：可见文字标签（反射失败时 id 为标签）
        private static readonly HashSet<string> BoardPinnedLabels = new HashSet<string>(StringComparer.Ordinal)
        {
            "手势", "画布", "更多", "退出"
        };

        // 中间工具按钮的三种边框样式（左边缘 / 右边缘 / 内部）
        private static readonly CornerRadius BoardEdgeLeftRadius = new CornerRadius(5, 0, 0, 5);
        private static readonly Thickness BoardEdgeLeftThickness = new Thickness(1, 1, 0.25, 1);
        private static readonly CornerRadius BoardEdgeRightRadius = new CornerRadius(0, 5, 5, 0);
        private static readonly Thickness BoardEdgeRightThickness = new Thickness(0, 1, 1, 1);
        private static readonly CornerRadius BoardInteriorRadius = new CornerRadius(0);
        private static readonly Thickness BoardInteriorThickness = new Thickness(0, 1, 0.25, 1);

        // ===== 枚举 =====

        private IReadOnlyList<ToolbarReorderGroup> BuildToolbarReorderGroups()
        {
            var groups = new List<ToolbarReorderGroup>();
            try
            {
                groups.AddRange(BuildFloatBarGroups());
                var bb = BuildBoardToolbarGroup();
                if (bb != null && bb.Items.Count > 0) groups.Add(bb);
            }
            catch (Exception ex)
            {
                Helpers.LogHelper.WriteLogToFile($"BuildToolbarReorderGroups 异常: {ex.Message}", Helpers.LogHelper.LogType.Warning);
            }
            return groups;
        }

        private IEnumerable<ToolbarReorderGroup> BuildFloatBarGroups()
        {
            var top = GetFloatBarTopPanel();
            if (top == null) yield break;
            var collapsible = GetFloatBarCollapsiblePanel();
            var trailing = GetFloatBarTrailingPanel(top, collapsible);

            var g1 = BuildGroupFromContainer(PlaceFloatFixed, "浮动工具栏 · 固定", top);
            if (g1 != null) yield return g1;
            var g2 = BuildGroupFromContainer(PlaceFloatCollapsible, "浮动工具栏 · 可折叠", collapsible);
            if (g2 != null) yield return g2;
            var g3 = BuildGroupFromContainer(PlaceFloatTrailing, "浮动工具栏 · 右侧", trailing);
            if (g3 != null) yield return g3;
        }

        private ToolbarReorderGroup BuildGroupFromContainer(string placement, string name, Panel panel)
        {
            if (panel == null) return null;
            var items = new List<ToolbarReorderItem>();
            int idx = 0;
            foreach (var child in panel.Children.Cast<UIElement>())
            {
                if (!(child is Button btn)) continue;
                string id = GetButtonId(btn, idx);
                if (string.IsNullOrWhiteSpace(id)) { idx++; continue; }
                items.Add(new ToolbarReorderItem
                {
                    Id = id,
                    DisplayName = GetButtonDisplayName(btn, id),
                    DefaultIndex = idx
                });
                idx++;
            }
            if (items.Count == 0) return null;
            return new ToolbarReorderGroup { Placement = placement, Name = name, Items = items };
        }

        private ToolbarReorderGroup BuildBoardToolbarGroup()
        {
            var panel = GetBoardToolsPanel();
            if (panel == null) return null;

            var items = new List<ToolbarReorderItem>();
            int idx = 0;
            foreach (var child in panel.Children.Cast<UIElement>())
            {
                if (!(child is Button btn)) continue;
                if (IsBoardPinned(btn)) continue; // 固定按钮不参与排序、不予展示
                string id = GetButtonId(btn, idx);
                if (string.IsNullOrWhiteSpace(id)) { idx++; continue; }
                items.Add(new ToolbarReorderItem
                {
                    Id = id,
                    DisplayName = GetButtonDisplayName(btn, id),
                    DefaultIndex = idx
                });
                idx++;
            }
            if (items.Count == 0) return null;

            return new ToolbarReorderGroup
            {
                Placement = PlaceBoard,
                Name = "白板工具栏",
                Items = items
            };
        }

        private bool IsBoardPinned(Button btn)
        {
            try
            {
                var h = GetClickHandlerName(btn);
                if (!string.IsNullOrWhiteSpace(h) && BoardPinnedHandlers.Contains(h)) return true;
            }
            catch { }
            var lbl = GetVisibleLabel(btn);
            if (!string.IsNullOrWhiteSpace(lbl) && BoardPinnedLabels.Contains(lbl)) return true;
            return false;
        }

        // ===== 应用 / 重置 =====

        private bool ApplyToolbarOrderInternal(string placement, IReadOnlyList<string> orderedItemIds)
        {
            try
            {
                if (string.Equals(placement, PlaceBoard, StringComparison.OrdinalIgnoreCase))
                {
                    var boardPanel = GetBoardToolsPanel();
                    bool ok = ApplyContainerOrder(boardPanel, orderedItemIds, usePinned: true);
                    if (ok) ApplyBoardEdgeBorders(boardPanel); // 排序后把圆角边框加到中间区两端的按钮上
                    return ok;
                }
                var floatPanel = ResolveFloatPanel(placement);
                if (floatPanel == null) return false;
                return ApplyContainerOrder(floatPanel, orderedItemIds, usePinned: false);
            }
            catch (Exception ex)
            {
                Helpers.LogHelper.WriteLogToFile($"ApplyToolbarOrder 异常 [{placement}]: {ex.Message}", Helpers.LogHelper.LogType.Warning);
                return false;
            }
        }

        private void ResetToolbarPlacementInternal(string placement)
        {
            try
            {
                var groups = BuildToolbarReorderGroups();
                var g = groups?.FirstOrDefault(x => string.Equals(x.Placement, placement, StringComparison.OrdinalIgnoreCase));
                if (g == null) return;
                var defaultIds = g.Items.OrderBy(i => i.DefaultIndex).Select(i => i.Id).ToList();
                ApplyToolbarOrderInternal(placement, defaultIds);
            }
            catch (Exception ex)
            {
                Helpers.LogHelper.WriteLogToFile($"ResetToolbarPlacement 异常 [{placement}]: {ex.Message}", Helpers.LogHelper.LogType.Warning);
            }
        }

        // ===== 浮动工具栏容器解析 =====

        private Panel GetFloatBarTopPanel()
        {
            try { return MWFloatBarHost?.StackPanelFloatingBar as Panel; } catch { return null; }
        }

        private Panel GetFloatBarCollapsiblePanel()
        {
            try { return MWFloatBarHost?.StackPanelCanvasControls as Panel; } catch { return null; }
        }

        private Panel GetFloatBarTrailingPanel(Panel top, Panel collapsible)
        {
            if (top == null) return null;
            foreach (var child in top.Children.Cast<UIElement>())
            {
                if (child is Panel p && p != top && p != collapsible && p.Children.OfType<Button>().Any())
                    return p;
            }
            return null;
        }

        private Panel ResolveFloatPanel(string placement)
        {
            var top = GetFloatBarTopPanel();
            var collapsible = GetFloatBarCollapsiblePanel();
            if (string.Equals(placement, PlaceFloatFixed, StringComparison.OrdinalIgnoreCase)) return top;
            if (string.Equals(placement, PlaceFloatCollapsible, StringComparison.OrdinalIgnoreCase)) return collapsible;
            if (string.Equals(placement, PlaceFloatTrailing, StringComparison.OrdinalIgnoreCase)) return GetFloatBarTrailingPanel(top, collapsible);
            return null;
        }

        // ===== 白板工具栏容器 =====

        private Panel GetBoardToolsPanel()
        {
            try { return MWBoardHost?.BoardToolsPanel as Panel; } catch { return null; }
        }

        /// <summary>
        /// 白板中间工具按钮的边框自动跟随位置：位于中间区最左的按钮加左圆角边框，
        /// 最右的按钮加右圆角边框，其余中间按钮用平直内部边框；固定按钮不改动。
        /// 这样无论按钮怎么重排，两端圆角始终留在中间工具栏的边缘按钮上。
        /// </summary>
        private void ApplyBoardEdgeBorders(Panel panel)
        {
            try
            {
                if (panel == null) return;
                var children = panel.Children.Cast<UIElement>().ToList();

                // 中间在最左 / 最右的可排序按钮
                Button left = null, right = null;
                for (int i = 0; i < children.Count; i++)
                    if (children[i] is Button b && !IsBoardPinned(b)) { left = b; break; }
                for (int i = children.Count - 1; i >= 0; i--)
                    if (children[i] is Button b && !IsBoardPinned(b)) { right = b; break; }

                foreach (var child in children)
                {
                    if (!(child is Button btn) || IsBoardPinned(btn)) continue; // 固定按钮保持默认边框
                    var br = FindInnerBorder(btn);
                    if (br == null) continue;
                    if (btn == left)
                    {
                        br.CornerRadius = BoardEdgeLeftRadius;
                        br.BorderThickness = BoardEdgeLeftThickness;
                    }
                    else if (btn == right)
                    {
                        br.CornerRadius = BoardEdgeRightRadius;
                        br.BorderThickness = BoardEdgeRightThickness;
                    }
                    else
                    {
                        br.CornerRadius = BoardInteriorRadius;
                        br.BorderThickness = BoardInteriorThickness;
                    }
                }
            }
            catch (Exception ex)
            {
                Helpers.LogHelper.WriteLogToFile($"ApplyBoardEdgeBorders 异常: {ex.Message}", Helpers.LogHelper.LogType.Warning);
            }
        }

        /// <summary>查找按钮内容里的首个 Border（用于调整圆角与边框）。</summary>
        private static Border FindInnerBorder(Button btn)
        {
            try
            {
                if (btn.Content is Border b) return b;
                var stack = new Stack<DependencyObject>();
                foreach (var c in LogicalTreeHelper.GetChildren(btn)) if (c is DependencyObject d) stack.Push(d);
                while (stack.Count > 0)
                {
                    var node = stack.Pop();
                    if (node is Border bb) return bb;
                    foreach (var c in LogicalTreeHelper.GetChildren(node))
                        if (c is DependencyObject d) stack.Push(d);
                }
            }
            catch { }
            return null;
        }

        // ===== 通用单容器重排 =====

        /// <summary>
        /// 重排某容器内的功能按钮。usePinned=true 时（白板工具栏）仅重排“非固定”按钮，
        /// 固定按钮（选择/画布/更多/退出/手势/重做）和分隔条等都保持原位；false 时该容器内所有按钮都参与重排。
        /// </summary>
        private bool ApplyContainerOrder(Panel panel, IReadOnlyList<string> orderedItemIds, bool usePinned)
        {
            if (panel == null || orderedItemIds == null) return false;

            var children = panel.Children.Cast<UIElement>().ToList();
            var allButtons = children.OfType<Button>().ToList();

            // 参与排序的按钮集合
            var reorderable = usePinned ? allButtons.Where(b => !IsBoardPinned(b)).ToList() : allButtons;
            if (reorderable.Count == 0) return false;

            var btnToId = new Dictionary<Button, string>();
            var idToBtn = new Dictionary<string, Button>(StringComparer.Ordinal);
            var currentIds = new List<string>();
            int idx = 0;
            foreach (var b in reorderable)
            {
                string id = GetButtonId(b, idx);
                if (string.IsNullOrWhiteSpace(id)) return false;
                btnToId[b] = id;
                idToBtn[id] = b;
                currentIds.Add(id);
                idx++;
            }

            if (!IsValidPermutation(currentIds, orderedItemIds)) return false;

            var desired = new List<Button>();
            foreach (var id in orderedItemIds) desired.Add(idToBtn[id]);

            var pinnedSet = usePinned ? new HashSet<Button>(allButtons.Where(IsBoardPinned)) : new HashSet<Button>();

            var result = new List<UIElement>();
            int di = 0;
            foreach (var c in children)
            {
                if (c is Button b)
                {
                    if (pinnedSet.Contains(b)) result.Add(b);       // 固定按钮原位不动
                    else { result.Add(desired[di]); di++; }          // 可排序按钮按期望顺序填充
                }
                else result.Add(c);                                   // 分隔条等占位原位
            }

            panel.Children.Clear();
            foreach (var r in result) panel.Children.Add(r);
            return true;
        }

        // ===== id / 名称解析 =====

        private static string GetButtonId(Button btn, int fallbackOrdinal)
        {
            try
            {
                var h = GetClickHandlerName(btn);
                if (!string.IsNullOrWhiteSpace(h)) return h;
            }
            catch { }

            if (btn.ToolTip is string s && !string.IsNullOrWhiteSpace(s))
                return s.Trim();

            var label = GetVisibleLabel(btn);
            if (!string.IsNullOrWhiteSpace(label)) return label;

            var named = FindFirstNamedDescendant(btn);
            if (!string.IsNullOrWhiteSpace(named)) return named;

            return "item" + (fallbackOrdinal + 1);
        }

        private static string GetButtonDisplayName(Button btn, string id)
        {
            if (btn.ToolTip is string s && !string.IsNullOrWhiteSpace(s))
                return s.Trim();
            if (TryMapHandlerName(id, out var name))
                return name;
            var label = GetVisibleLabel(btn);
            if (!string.IsNullOrWhiteSpace(label)) return label;
            if (!string.IsNullOrEmpty(id) && id.Any(c => c > 127)) return id;
            return id;
        }

        private static string GetVisibleLabel(Button btn)
        {
            try
            {
                var stack = new Stack<DependencyObject>();
                foreach (var c in LogicalTreeHelper.GetChildren(btn)) if (c is DependencyObject d) stack.Push(d);
                while (stack.Count > 0)
                {
                    var node = stack.Pop();
                    if (node is TextBlock tb && !string.IsNullOrWhiteSpace(tb.Text))
                        return tb.Text.Trim();
                    foreach (var c in LogicalTreeHelper.GetChildren(node))
                        if (c is DependencyObject d) stack.Push(d);
                }
            }
            catch { }
            return null;
        }

        private static readonly Dictionary<string, string> HandlerNameMap = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            { "CursorIcon_Click", "鼠标" },
            { "CursorWithDelIcon_Click", "清空并切换鼠标" },
            { "PenIcon_Click", "画笔" },
            { "SymbolIconDelete_MouseUp", "清空画布" },
            { "EraserIcon_Click", "圆形橡皮擦" },
            { "EraserIconByStrokes_Click", "墨迹橡皮擦" },
            { "SymbolIconSelect_Click", "选择" },
            { "ImageDrawShape_Click", "绘制图形" },
            { "BtnMediaInsertUnified_Click", "插入媒体" },
            { "SymbolIconUndo_Click", "撤销" },
            { "SymbolIconRedo_Click", "重做" },
            { "ImageBlackboard_Click", "画板" },
            { "SymbolIconTools_Click", "工具" },
            { "FoldFloatingBar_Click", "隐藏工具栏" },
            { "BoardSelectIcon_Click", "选择" },
            { "BoardPenIcon_Click", "画笔" },
            { "BoardEraserIcon_Click", "橡皮擦" },
            { "BoardEraserIconByStrokes_Click", "墨迹橡皮擦" },
            { "BoardImageDrawShape_Click", "绘制图形" },
            { "BtnVideoPresenter_Click", "视频展台" },
            { "ImageCountdownTimer_Click", "计时器" },
            { "SymbolIconRand_Click", "随机点名" },
            { "SymbolIconRandOne_Click", "随机单选" },
            { "SymbolIconSaveStrokes_Click", "保存批注" },
            { "SymbolIconOpenInkCanvasFile_Click", "打开文件" },
            { "GridInkReplayButton_Click", "笔迹回放" },
            { "SymbolIconScreenshot_Click", "截图" },
            { "BtnExit_Click", "退出" },
            { "SymbolIconSettings_Click", "设置" },
            { "BoardLaunchEasiCamera_Click", "EasiCamera" },
            { "BoardLaunchDesmos_Click", "Desmos" },
            { "BtnWhiteBoardAdd_Click", "新建白板" },
            { "TwoFingerGestureBorder_Click", "手势" },
            { "BoardChangeBackgroundColorBtn_Click", "画布" }
        };

        private static bool TryMapHandlerName(string handler, out string name)
            => HandlerNameMap.TryGetValue(handler ?? "", out name);

        private static string GetClickHandlerName(Button btn)
        {
            try
            {
                var storeField = typeof(UIElement).GetField("_eventHandlersStore",
                    BindingFlags.Instance | BindingFlags.NonPublic);
                var store = storeField?.GetValue(btn);
                if (store == null)
                    store = TryGetEventHandlersStoreDynamic(btn);
                if (store == null) return null;

                var clickEventField = typeof(ButtonBase).GetField("ClickEvent",
                    BindingFlags.Public | BindingFlags.Static);
                var clickEvent = clickEventField?.GetValue(null) as RoutedEvent;
                if (clickEvent == null) return null;

                var storeType = store.GetType();
                var getter = storeType.GetMethod("GetRoutedEventHandler",
                    BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
                if (getter == null) return null;

                var handler = getter.Invoke(store, new object[] { clickEvent }) as Delegate;
                var invokers = handler?.GetInvocationList();
                if (invokers != null && invokers.Length > 0)
                {
                    var m = invokers[0].Method;
                    return m?.Name;
                }
            }
            catch { }
            return null;
        }

        private static object TryGetEventHandlersStoreDynamic(DependencyObject element)
        {
            try
            {
                foreach (var name in new[] { "_eventHandlersStore", "_handlersStore", "_route" })
                {
                    var f = typeof(UIElement).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic);
                    var v = f?.GetValue(element);
                    if (v != null) return v;
                }
            }
            catch { }
            return null;
        }

        private static string FindFirstNamedDescendant(Button btn)
        {
            try
            {
                var stack = new Stack<DependencyObject>();
                foreach (var c in LogicalTreeHelper.GetChildren(btn)) if (c is DependencyObject d) stack.Push(d);
                while (stack.Count > 0)
                {
                    var node = stack.Pop();
                    if (node is FrameworkElement fe && !string.IsNullOrWhiteSpace(fe.Name))
                        return fe.Name;
                    foreach (var c in LogicalTreeHelper.GetChildren(node))
                        if (c is DependencyObject d) stack.Push(d);
                }
            }
            catch { }
            return null;
        }

        private static bool IsValidPermutation(List<string> currentIds, IReadOnlyList<string> orderedIds)
        {
            if (currentIds == null || orderedIds == null) return false;
            if (currentIds.Count != orderedIds.Count) return false;

            var currentSet = new HashSet<string>(currentIds, StringComparer.Ordinal);
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var id in orderedIds)
            {
                if (string.IsNullOrWhiteSpace(id)) return false;
                if (!currentSet.Contains(id)) return false;
                if (!seen.Add(id)) return false;
            }
            return true;
        }
    }
}