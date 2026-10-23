using Ink_Canvas.Helpers;
using Ink_Canvas.Plugins;
using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

namespace Ink_Canvas
{
    /// <summary>
    /// 画笔调色板：内置 9 个普通颜色 + 6 个荧光笔颜色。
    ///
    /// 设计原则：
    /// - **不安装插件时行为与旧版完全一致**：没有任何自定义色时，颜色全部取自内置主题色表
    ///   （亮色/暗色主题各一套，随白板背景切换），即原来的写死行为。
    /// - 插件可通过 IPluginHost 的画笔颜色 API 覆盖任意槽位颜色、指定默认选中色；
    ///   覆盖只存在于运行时（内存表），插件禁用/卸载后自动回到内置颜色，
    ///   因此「不装插件默认颜色依然可用」由兜底逻辑保证，不需要把自定义色写进主设置。
    /// </summary>
    public partial class MainWindow
    {
        /// <summary>自定义画笔颜色：槽位索引 -> 颜色。由插件通过 IPluginHost 设置。</summary>
        private readonly Dictionary<int, Color> _customPenColors = new Dictionary<int, Color>();

        internal static readonly int[] PenColorNormalSlots = { 0, 1, 2, 3, 4, 5, 6, 7, 8 };
        internal static readonly int[] PenColorHighlighterSlots = { 101, 102, 103, 104, 105, 106 };

        /// <summary>是否为荧光笔槽位</summary>
        internal static bool IsHighlighterPenSlot(int index)
        {
            return index >= 101 && index <= 106;
        }

        /// <summary>是否为合法调色板槽位</summary>
        internal static bool IsValidPenColorSlot(int index)
        {
            return (index >= 0 && index <= 8) || IsHighlighterPenSlot(index);
        }

        /// <summary>槽位展示名</summary>
        internal static string GetPenColorSlotName(int index)
        {
            switch (index)
            {
                case 0: return "黑色";
                case 1: return "红色";
                case 2: return "绿色";
                case 3: return "蓝色";
                case 4: return "黄色";
                case 5: return "白色";
                case 6: return "粉色";
                case 7: return "青色";
                case 8: return "橙色";
                case 101: return "荧光笔红";
                case 102: return "荧光笔橙";
                case 103: return "荧光笔黄";
                case 104: return "荧光笔青";
                case 105: return "荧光笔蓝";
                case 106: return "荧光笔紫";
                default: return "颜色 " + index;
            }
        }

        /// <summary>
        /// 荧光笔内置笔色（旧版 CheckColorTheme 里直接写死的落笔颜色）。
        /// 注意：与色块显示色（XAML 里的 #ff5e53 一族）本来就不同，这里分开保留，
        /// 未自定义时完全维持旧版行为。
        /// </summary>
        private static Color GetBuiltInHighlighterInkColor(int index)
        {
            switch (index)
            {
                case 101: return Color.FromRgb(220, 38, 38);
                case 102: return Color.FromRgb(234, 88, 12);
                case 103: return Color.FromRgb(234, 179, 8);
                case 104: return Color.FromRgb(13, 148, 136);
                case 105: return Color.FromRgb(37, 99, 235);
                case 106: return Color.FromRgb(147, 51, 234);
                default: return Colors.Red;
            }
        }

        /// <summary>荧光笔内置色块显示色（与 XAML 一致，用于自定义后恢复默认）</summary>
        private static Color GetBuiltInHighlighterSwatchColor(int index)
        {
            switch (index)
            {
                case 101: return Color.FromRgb(0xFF, 0x5E, 0x53);
                case 102: return Color.FromRgb(0xFF, 0x70, 0x0D);
                case 103: return Color.FromRgb(0xFF, 0xD3, 0x08);
                case 104: return Color.FromRgb(0x10, 0xBA, 0xAB);
                case 105: return Color.FromRgb(0x4A, 0x76, 0xFF);
                case 106: return Color.FromRgb(0x93, 0x33, 0xEA);
                default: return Colors.Red;
            }
        }

        /// <summary>
        /// 解析槽位当前生效的"落笔颜色"：插件自定义色优先，其次内置色。
        /// 内置普通色按 isUselightThemeColor 取亮/暗两套映射（保持旧版主题切换行为）。
        /// </summary>
        internal Color ResolvePenColor(int index)
        {
            if (_customPenColors.TryGetValue(index, out var custom)) return custom;

            if (IsHighlighterPenSlot(index)) return GetBuiltInHighlighterInkColor(index);

            if (index < 0 || index > 8) index = 1;
            return isUselightThemeColor ? inkColorLightThemeMapping[index] : inkColorDarkThemeMapping[index];
        }

        /// <summary>
        /// 解析槽位色块应显示的颜色：普通色与笔色一致；荧光笔未自定义时用 XAML 里的
        /// 专用显示色（浅一档），自定义后与笔色一致（所见即所得）。
        /// </summary>
        private Color ResolvePenSwatchColor(int index)
        {
            if (_customPenColors.TryGetValue(index, out var custom)) return custom;
            if (IsHighlighterPenSlot(index)) return GetBuiltInHighlighterSwatchColor(index);
            return ResolvePenColor(index);
        }

        /// <summary>槽位是否已被自定义</summary>
        internal bool IsPenColorCustomized(int index)
        {
            return _customPenColors.ContainsKey(index);
        }

        /// <summary>构建槽位信息列表（普通色 0-8 在前，荧光笔 101-106 在后）</summary>
        internal IReadOnlyList<PenColorSlotInfo> BuildPenColorSlots()
        {
            var list = new List<PenColorSlotInfo>(15);
            foreach (var i in PenColorNormalSlots) list.Add(BuildPenColorSlot(i));
            foreach (var i in PenColorHighlighterSlots) list.Add(BuildPenColorSlot(i));
            return list;
        }

        private PenColorSlotInfo BuildPenColorSlot(int index)
        {
            return new PenColorSlotInfo
            {
                Index = index,
                Name = GetPenColorSlotName(index),
                IsHighlighter = IsHighlighterPenSlot(index),
                Color = ResolvePenColor(index),
                IsCustomized = IsPenColorCustomized(index)
            };
        }

        /// <summary>设置某槽位的自定义颜色（插件 API 入口，立即刷新色块与当前笔色）</summary>
        internal void SetCustomPenColor(int index, Color color)
        {
            if (!IsValidPenColorSlot(index)) return;
            _customPenColors[index] = color;
            RefreshPenPaletteAfterCustomChange(index);
        }

        /// <summary>恢复某槽位为内置主题色</summary>
        internal void ResetCustomPenColor(int index)
        {
            if (!IsValidPenColorSlot(index)) return;
            if (!_customPenColors.Remove(index)) return;
            RefreshPenPaletteAfterCustomChange(index);
        }

        /// <summary>恢复全部槽位为内置主题色（插件禁用/卸载时调用）</summary>
        internal void ResetAllCustomPenColors()
        {
            if (_customPenColors.Count == 0) return;
            _customPenColors.Clear();
            RefreshPenPaletteAfterCustomChange(-1);
        }

        /// <summary>
        /// 自定义色变化后刷新：色块、当前笔色（若改的正是正在用的颜色）、批注按钮颜色指示，
        /// 并通知插件。
        /// </summary>
        private void RefreshPenPaletteAfterCustomChange(int changedIndex)
        {
            try
            {
                ApplyPenPaletteColors();

                // 正在使用的颜色被改色时，笔色要立即跟着变（所见即所得）
                if (changedIndex < 0 || changedIndex == inkColor)
                {
                    inkCanvas.DefaultDrawingAttributes.Color = ResolvePenColor(inkColor);
                }

                UpdatePenIconColorIndicator();
                try { UpdateLaserPointerVisual(); } catch { }
                try { PluginHost.Instance?.RaisePenColorsChanged(); } catch { }
            }
            catch { }
        }

        /// <summary>
        /// 把调色板应用到两个面板的色块背景：
        /// 浮动栏 / 白板栏各 9 个普通色块 + 6 个荧光笔色块，共 30 个。
        /// 颜色统一走 ResolvePenColor，未自定义时即内置主题色（与旧版 CheckColorTheme 行为一致）。
        /// </summary>
        private void ApplyPenPaletteColors()
        {
            try
            {
                foreach (var i in PenColorNormalSlots) ApplyPenColorSwatch(i, ResolvePenSwatchColor(i));
                foreach (var i in PenColorHighlighterSlots) ApplyPenColorSwatch(i, ResolvePenSwatchColor(i));
                // 批注按钮的颜色指示与调色板同源刷新（选色/切主题/改色时一起变）
                UpdatePenIconColorIndicator();
            }
            catch { }
        }

        private void ApplyPenColorSwatch(int index, Color color)
        {
            var brush = new SolidColorBrush(color);
            if (brush.CanFreeze) brush.Freeze();

            switch (index)
            {
                case 0:
                    BorderPenColorBlack.Background = brush;
                    BoardBorderPenColorBlack.Background = brush;
                    break;
                case 1:
                    BorderPenColorRed.Background = brush;
                    BoardBorderPenColorRed.Background = brush;
                    break;
                case 2:
                    BorderPenColorGreen.Background = brush;
                    BoardBorderPenColorGreen.Background = brush;
                    break;
                case 3:
                    BorderPenColorBlue.Background = brush;
                    BoardBorderPenColorBlue.Background = brush;
                    break;
                case 4:
                    BorderPenColorYellow.Background = brush;
                    BoardBorderPenColorYellow.Background = brush;
                    break;
                case 5:
                    BorderPenColorWhite.Background = brush;
                    BoardBorderPenColorWhite.Background = brush;
                    break;
                case 6:
                    BorderPenColorPink.Background = brush;
                    BoardBorderPenColorPink.Background = brush;
                    break;
                case 7:
                    BorderPenColorTeal.Background = brush;
                    BoardBorderPenColorTeal.Background = brush;
                    break;
                case 8:
                    BorderPenColorOrange.Background = brush;
                    BoardBorderPenColorOrange.Background = brush;
                    break;
                case 101:
                    BorderHighlighterColorRed.Background = brush;
                    BoardBorderHighlighterColorRed.Background = brush;
                    break;
                case 102:
                    BorderHighlighterColorOrange.Background = brush;
                    BoardBorderHighlighterColorOrange.Background = brush;
                    break;
                case 103:
                    BorderHighlighterColorYellow.Background = brush;
                    BoardBorderHighlighterColorYellow.Background = brush;
                    break;
                case 104:
                    BorderHighlighterColorTeal.Background = brush;
                    BoardBorderHighlighterColorTeal.Background = brush;
                    break;
                case 105:
                    BorderHighlighterColorBlue.Background = brush;
                    BoardBorderHighlighterColorBlue.Background = brush;
                    break;
                case 106:
                    BorderHighlighterColorPurple.Background = brush;
                    BoardBorderHighlighterColorPurple.Background = brush;
                    break;
            }
        }

        /// <summary>
        /// 批注按钮显示画笔颜色：
        /// 1）按钮右下角的色点始终显示当前笔色（带自动对比描边，白色等浅色也看得见）；
        /// 2）画笔图标的字形同时染成该颜色，但颜色过浅（如白色）时不染色、保持主题前景色，
        ///    避免图标在浅色浮动栏上"消失"。
        /// </summary>
        private void UpdatePenIconColorIndicator()
        {
            try
            {
                var color = ResolvePenColor(inkColor);

                if (PenIconColorDot != null)
                {
                    var dotBrush = new SolidColorBrush(color);
                    if (dotBrush.CanFreeze) dotBrush.Freeze();
                    PenIconColorDot.Fill = dotBrush;

                    var strokeBrush = new SolidColorBrush(GetPenColorContrastStroke(color));
                    if (strokeBrush.CanFreeze) strokeBrush.Freeze();
                    PenIconColorDot.Stroke = strokeBrush;
                }

                if (Icon_Pen != null)
                {
                    if (GetRelativeLuminance(color) < 0.85)
                    {
                        Icon_Pen.Foreground = new SolidColorBrush(color);
                    }
                    else
                    {
                        // 浅色（白/黄等）：字形回落到主题前景色，真实颜色由色点表达
                        Icon_Pen.SetResourceReference(System.Windows.Controls.Control.ForegroundProperty, "FloatBarForeground");
                    }
                }
            }
            catch { }
        }

        /// <summary>色点描边色：亮色用深灰、暗色用浅灰，保证任意笔色下都清晰</summary>
        private static Color GetPenColorContrastStroke(Color color)
        {
            return GetRelativeLuminance(color) > 0.6
                ? Color.FromArgb(0xB0, 0x40, 0x40, 0x40)
                : Color.FromArgb(0xB0, 0xFF, 0xFF, 0xFF);
        }

        /// <summary>相对亮度（sRGB 近似），用于判断颜色深浅</summary>
        private static double GetRelativeLuminance(Color color)
        {
            return (0.2126 * color.R + 0.7152 * color.G + 0.0722 * color.B) / 255.0;
        }

        /// <summary>
        /// 设置默认画笔颜色槽位（插件 API）：-1 = 沿用内置默认；合法槽位 = 立即应用并持久化。
        /// 持久化在 Settings.Canvas.DefaultPenColorIndex，下次启动时由 ApplyStartupDefaultPenColor 生效。
        /// </summary>
        internal void ApplyDefaultPenColorSetting(int slotIndex)
        {
            try
            {
                if (slotIndex != -1 && !IsValidPenColorSlot(slotIndex)) return;
                if (Settings?.Canvas == null) return;

                // 值未变化时不落盘：插件每次启动初始化都会设置一次默认色，
                // 无脑保存会让"启动即保存"成为常态，放大其它早期保存的风险
                bool changed = Settings.Canvas.DefaultPenColorIndex != slotIndex;
                Settings.Canvas.DefaultPenColorIndex = slotIndex;

                if (IsValidPenColorSlot(slotIndex))
                {
                    lastDesktopInkColor = slotIndex;
                    lastBoardInkColor = slotIndex;
                    CheckColorTheme();
                }

                // 仅在真正改变时持久化；启动早期（设置尚未载入）的保存由
                // SaveSettingsToFile 内部的守卫拦截，避免覆盖用户已有的设置文件
                if (changed) SaveSettingsToFile();
                try { PluginHost.Instance?.RaisePenColorsChanged(); } catch { }
            }
            catch { }
        }

        /// <summary>
        /// 启动时应用「默认画笔颜色」设置。
        /// 未设置（-1）时不干预，保持内置默认：浮动栏红、白板随背景黑/白。
        /// </summary>
        internal void ApplyStartupDefaultPenColor()
        {
            try
            {
                int index = Settings?.Canvas?.DefaultPenColorIndex ?? -1;
                if (!IsValidPenColorSlot(index)) return;

                lastDesktopInkColor = index;
                lastBoardInkColor = index;
                CheckColorTheme();
            }
            catch { }
        }

        #region 色块修改颜色交互（鼠标右键 / 触摸长按）

        /// <summary>「自定义画笔颜色」插件 ID：安装后「墨迹选项」的深/浅色切换按钮变为设置颜色入口</summary>
        internal const string PenColorsPluginId = "ink-canvas.pen-colors";

        private bool _penSlotEditGesturesAttached = false;
        private int _penSlotEditGesturesAttachedCount = 0;

        private System.Windows.Threading.DispatcherTimer _penSlotLongPressTimer;
        private int _penSlotLongPressIndex = -1;
        private Point _penSlotLongPressOrigin;
        private const double PenSlotLongPressMoveTolerance = 20;
        private const int PenSlotLongPressMs = 500;

        /// <summary>
        /// 给「墨迹选项」全部色块（浮动栏 + 白板栏各 9 普通色 + 6 荧光笔色）挂上
        /// 修改颜色交互：鼠标右键直接触发；触摸长按 500ms（位移超 20px 或提前抬起则取消）。
        /// 触发后抛 PenSlotEditRequested 事件，由「自定义画笔颜色」插件弹取色器；
        /// 未安装插件（无人处理）时弹安装引导提示。
        /// 用 Preview（隧道）事件而非普通事件：浮动栏/面板祖先链路上一旦有人把
        /// 鼠标/触摸事件标记 Handled，普通事件就收不到，隧道事件先于冒泡执行不受影响。
        /// 构造时控件可能尚未就绪（挂载数为 0），主窗口 Loaded 后会再补一次。
        /// </summary>
        internal void InitializePenPaletteEditGestures()
        {
            if (_penSlotEditGesturesAttached) return;

            int attached = 0;
            try
            {
                attached += TryAttachPenSlotEditGestures(BorderPenColorBlack, 0);
                attached += TryAttachPenSlotEditGestures(BorderPenColorRed, 1);
                attached += TryAttachPenSlotEditGestures(BorderPenColorGreen, 2);
                attached += TryAttachPenSlotEditGestures(BorderPenColorBlue, 3);
                attached += TryAttachPenSlotEditGestures(BorderPenColorYellow, 4);
                attached += TryAttachPenSlotEditGestures(BorderPenColorWhite, 5);
                attached += TryAttachPenSlotEditGestures(BorderPenColorPink, 6);
                attached += TryAttachPenSlotEditGestures(BorderPenColorTeal, 7);
                attached += TryAttachPenSlotEditGestures(BorderPenColorOrange, 8);
                attached += TryAttachPenSlotEditGestures(BorderHighlighterColorRed, 101);
                attached += TryAttachPenSlotEditGestures(BorderHighlighterColorOrange, 102);
                attached += TryAttachPenSlotEditGestures(BorderHighlighterColorYellow, 103);
                attached += TryAttachPenSlotEditGestures(BorderHighlighterColorTeal, 104);
                attached += TryAttachPenSlotEditGestures(BorderHighlighterColorBlue, 105);
                attached += TryAttachPenSlotEditGestures(BorderHighlighterColorPurple, 106);

                attached += TryAttachPenSlotEditGestures(BoardBorderPenColorBlack, 0);
                attached += TryAttachPenSlotEditGestures(BoardBorderPenColorRed, 1);
                attached += TryAttachPenSlotEditGestures(BoardBorderPenColorGreen, 2);
                attached += TryAttachPenSlotEditGestures(BoardBorderPenColorBlue, 3);
                attached += TryAttachPenSlotEditGestures(BoardBorderPenColorYellow, 4);
                attached += TryAttachPenSlotEditGestures(BoardBorderPenColorWhite, 5);
                attached += TryAttachPenSlotEditGestures(BoardBorderPenColorPink, 6);
                attached += TryAttachPenSlotEditGestures(BoardBorderPenColorTeal, 7);
                attached += TryAttachPenSlotEditGestures(BoardBorderPenColorOrange, 8);
                attached += TryAttachPenSlotEditGestures(BoardBorderHighlighterColorRed, 101);
                attached += TryAttachPenSlotEditGestures(BoardBorderHighlighterColorOrange, 102);
                attached += TryAttachPenSlotEditGestures(BoardBorderHighlighterColorYellow, 103);
                attached += TryAttachPenSlotEditGestures(BoardBorderHighlighterColorTeal, 104);
                attached += TryAttachPenSlotEditGestures(BoardBorderHighlighterColorBlue, 105);
                attached += TryAttachPenSlotEditGestures(BoardBorderHighlighterColorPurple, 106);
            }
            catch (Exception ex)
            {
                LogHelper.WriteLogToFile("InitializePenPaletteEditGestures failed | " + ex.Message, LogHelper.LogType.Warning);
            }

            _penSlotEditGesturesAttachedCount += attached;
            if (_penSlotEditGesturesAttachedCount > 0)
            {
                _penSlotEditGesturesAttached = true;
                LogHelper.WriteLogToFile($"色块修改颜色交互已挂载：{_penSlotEditGesturesAttachedCount}/30 个色块", LogHelper.LogType.Info);
            }
            else
            {
                // 构造期控件未就绪：不置标志，等主窗口 Loaded 后再补
                LogHelper.WriteLogToFile("色块修改颜色交互挂载数为 0，等待主窗口 Loaded 后重试", LogHelper.LogType.Warning);
            }
        }

        /// <summary>挂载单个色块；返回 1 表示成功，0 表示控件为空或已挂载过</summary>
        private int TryAttachPenSlotEditGestures(dynamic borderOrNull, int slotIndex)
        {
            try
            {
                var border = borderOrNull as System.Windows.Controls.Border;
                if (border == null) return 0;
                if (border.Tag is int existing && existing == slotIndex) return 0; // 已挂载

                border.Tag = slotIndex;
                border.PreviewMouseRightButtonDown += PenSlotSwatch_MouseRightButtonDown;
                border.PreviewTouchDown += PenSlotSwatch_TouchDown;
                border.PreviewTouchMove += PenSlotSwatch_TouchMove;
                border.TouchUp += PenSlotSwatch_TouchUpOrLeave;
                border.TouchLeave += PenSlotSwatch_TouchUpOrLeave;
                return 1;
            }
            catch (Exception ex)
            {
                LogHelper.WriteLogToFile($"挂载色块手势失败 slot={slotIndex} | " + ex.Message, LogHelper.LogType.Warning);
                return 0;
            }
        }

        private void PenSlotSwatch_MouseRightButtonDown(object sender, MouseButtonEventArgs e)
        {
            try
            {
                var border = sender as System.Windows.Controls.Border;
                if (border == null) return;
                int slotIndex = border.Tag as int? ?? -1;
                LogHelper.WriteLogToFile($"色块右键请求改色 slot={slotIndex}", LogHelper.LogType.Info);
                TryRaisePenSlotEditRequested(slotIndex);
                e.Handled = true;
            }
            catch (Exception ex)
            {
                LogHelper.WriteLogToFile("色块右键处理异常 | " + ex.Message, LogHelper.LogType.Warning);
            }
        }

        private void PenSlotSwatch_TouchDown(object sender, TouchEventArgs e)
        {
            try
            {
                var border = sender as System.Windows.Controls.Border;
                if (border == null) return;

                _penSlotLongPressIndex = border.Tag as int? ?? -1;
                _penSlotLongPressOrigin = e.GetTouchPoint(border).Position;

                if (_penSlotLongPressTimer == null)
                {
                    _penSlotLongPressTimer = new System.Windows.Threading.DispatcherTimer
                    {
                        Interval = TimeSpan.FromMilliseconds(PenSlotLongPressMs)
                    };
                    _penSlotLongPressTimer.Tick += (s, args) =>
                    {
                        _penSlotLongPressTimer.Stop();
                        int index = _penSlotLongPressIndex;
                        _penSlotLongPressIndex = -1;
                        if (index >= 0)
                        {
                            LogHelper.WriteLogToFile($"色块长按请求改色 slot={index}", LogHelper.LogType.Info);
                            TryRaisePenSlotEditRequested(index);
                        }
                    };
                }
                _penSlotLongPressTimer.Start();
            }
            catch (Exception ex)
            {
                LogHelper.WriteLogToFile("色块触摸处理异常 | " + ex.Message, LogHelper.LogType.Warning);
                CancelPenSlotLongPress();
            }
        }

        private void PenSlotSwatch_TouchMove(object sender, TouchEventArgs e)
        {
            if (_penSlotLongPressIndex < 0) return;
            try
            {
                var border = sender as System.Windows.Controls.Border;
                if (border == null) return;

                var pos = e.GetTouchPoint(border).Position;
                if (InkStraightenDistance(_penSlotLongPressOrigin, pos) > PenSlotLongPressMoveTolerance)
                {
                    CancelPenSlotLongPress();
                }
            }
            catch { }
        }

        private void PenSlotSwatch_TouchUpOrLeave(object sender, TouchEventArgs e)
        {
            CancelPenSlotLongPress();
        }

        private void CancelPenSlotLongPress()
        {
            _penSlotLongPressIndex = -1;
            try { _penSlotLongPressTimer?.Stop(); } catch { }
        }

        private void TryRaisePenSlotEditRequested(int slotIndex)
        {
            if (slotIndex < 0) return;
            try
            {
                var host = PluginHost.Instance;
                if (host == null)
                {
                    LogHelper.WriteLogToFile("色块改色请求被忽略：PluginHost 未初始化", LogHelper.LogType.Warning);
                    ShowPenColorsPluginPrompt();
                    return;
                }

                bool handled = host.RaisePenSlotEditRequested(slotIndex);
                if (!handled)
                {
                    // 手势已识别但没有插件处理（未安装/未启用/宿主过旧）：
                    // 弹安装引导，避免用户以为“点了没反应”
                    ShowPenColorsPluginPrompt();
                }
            }
            catch (Exception ex)
            {
                LogHelper.WriteLogToFile("色块改色请求失败 | " + ex.Message, LogHelper.LogType.Warning);
            }
        }

        #endregion

        /// <summary>
        /// 「墨迹选项」原深/浅色切换按钮已直接替换为颜色调整入口（恒显，不再随插件状态变化）：
        /// 图标=设置齿轮，文字=「调整」（两字，避免窄按钮显示不下）。
        /// 点击行为见 ColorThemeSwitch_MouseUp（已装插件 → 直达插件设置；未装 → 弹安装提示）。
        /// 由 CheckColorTheme / PluginListChanged 驱动刷新。
        /// </summary>
        private void UpdateColorThemeSwitchButtonVisual()
        {
            try
            {
                ColorThemeSwitchIcon.Glyph = "\uE713"; // 设置齿轮
                BoardColorThemeSwitchIcon.Glyph = "\uE713";
                ColorThemeSwitchTextBlock.Text = "调整";
            }
            catch { }
        }
    }
}
