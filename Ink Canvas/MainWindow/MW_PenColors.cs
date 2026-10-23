using Ink_Canvas.Helpers;
using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Ink;
using System.Windows.Media;

namespace Ink_Canvas
{
    public partial class MainWindow : Window
    {
        int inkColor = 1;

        private void InkWidthSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (!isLoaded || _isLoadingSettings) return;
            if (sender == BoardInkWidthSlider) InkWidthSlider.Value = ((Slider)sender).Value;
            if (sender == InkWidthSlider) BoardInkWidthSlider.Value = ((Slider)sender).Value;
            Settings.Canvas.InkWidth = ((Slider)sender).Value / 2;
            if (inkColor > 100)
            {
                drawingAttributes.Height = 30 + ((Slider)sender).Value;
                drawingAttributes.Width = 30 + ((Slider)sender).Value;
            }
            else
            {
                drawingAttributes.Height = ((Slider)sender).Value / 2;
                drawingAttributes.Width = ((Slider)sender).Value / 2;
            }
            SaveSettingsToFile();
        }

        private void ColorSwitchCheck()
        {
            forceEraser = false;
            // 激光笔开启时不收起面板：激光笔开关就在「墨迹选项」内，HideSubPanels 会把它一起藏起来，
            // 导致选色后必须重新打开面板才能关闭激光笔；颜色本身已生效，无需收起
            if (!isLaserPointerEnabled) HideSubPanels("color");
            if (Main_Grid.Background == Brushes.Transparent)
            {
                if (currentMode == 1)
                {
                    currentMode = 0;
                    GridBackgroundCover.Visibility = Visibility.Collapsed;
                    AnimationsHelper.HideWithSlideAndFade(BlackboardLeftSide);
                    AnimationsHelper.HideWithSlideAndFade(BlackboardCenterSide);
                    AnimationsHelper.HideWithSlideAndFade(BlackboardRightSide);
                }
                BtnHideInkCanvas_Click(null, null);
            }

            StrokeCollection strokes = inkCanvas.GetSelectedStrokes();
            if (strokes.Count != 0)
            {
                foreach (Stroke stroke in strokes)
                {
                    try
                    {
                        stroke.DrawingAttributes.Color = inkCanvas.DefaultDrawingAttributes.Color;
                    }
                    catch { }
                }
            }
            if (DrawingAttributesHistory.Count > 0)
            {
                timeMachine.CommitStrokeDrawingAttributesHistory(DrawingAttributesHistory);
                DrawingAttributesHistory = new Dictionary<Stroke, Tuple<DrawingAttributes, DrawingAttributes>>();
                foreach (var item in DrawingAttributesHistoryFlag)
                {
                    item.Value.Clear();
                }
            }
            else
            {
                inkCanvas.IsManipulationEnabled = true;
                drawingShapeMode = 0;
                // 激光笔开启期间不改编辑模式：激光态要求 EditingMode 保持 None，
                // 若在此改回 Ink，触摸输入的「触笔」事件（激光笔只拦截 Touch 事件）会被
                // InkCanvas 收集成真笔迹，出现"激光轨迹淡出后残留普通笔迹线条"。
                // 选色本身已生效（画笔与激光笔共用同一份选色）。
                if (!isLaserPointerEnabled)
                {
                    inkCanvas.EditingMode = InkCanvasEditingMode.Ink;
                }
                CancelSingleFingerDragMode();
                forceEraser = false;
                CheckColorTheme();
            }

            isLongPressSelected = false;
        }

        bool isUselightThemeColor = false, isDesktopUselightThemeColor = false;
        int lastDesktopInkColor = 1, lastBoardInkColor = 5;
        Dictionary<int, Color> inkColorLightThemeMapping = new Dictionary<int, Color>
        {
            { 0, Colors.Black }, // Black
            { 1, Color.FromRgb(239, 68, 68) }, // Red
            { 2, Color.FromRgb(34, 197, 94) }, // Green
            { 3, Color.FromRgb(59, 130, 246) }, // Blue
            { 4, Color.FromRgb(250, 204, 21) }, // Yellow
            { 5, Colors.White }, // White
            { 6, Color.FromRgb(236, 72, 153) }, // Pink
            { 7, Color.FromRgb(20, 184, 166) }, // Teal
            { 8, Color.FromRgb(249, 115, 22) }, // Orange
        };
        Dictionary<int, Color> inkColorDarkThemeMapping = new Dictionary<int, Color>
        {
            { 0, Colors.Black }, // Black
            { 1, Color.FromRgb(220, 38, 38) }, // Red
            { 2, Color.FromRgb(22, 163, 74) }, // Green
            { 3, Color.FromRgb(37, 99, 235) }, // Blue
            { 4, Color.FromRgb(234, 179, 8) }, // Yellow
            { 5, Colors.White }, // White
            { 6, Color.FromRgb(147, 51, 234) }, // Pink (Purple)
            { 7, Color.FromRgb(13, 148, 136) }, // Teal
            { 8, Color.FromRgb(234, 88, 12) }  // Orange
        };

        private void CheckColorTheme(bool changeColorTheme = false)
        {
            if (changeColorTheme)
            {
                if (currentMode != 0)
                {
                    if (Settings.Canvas.UsingWhiteboard)
                    {
                        GridBackgroundCover.Background = new SolidColorBrush(StringToColor("#FFF2F2F2"));
                        isUselightThemeColor = false;
                    }
                    else
                    {
                        GridBackgroundCover.Background = new SolidColorBrush(StringToColor("#FF1F1F1F"));
                        isUselightThemeColor = true;
                    }
                }
            }

            if (currentMode == 0)
            {
                isUselightThemeColor = isDesktopUselightThemeColor;
                inkColor = lastDesktopInkColor;
            }
            else
            {
                inkColor = lastBoardInkColor;
            }

            // 统一解析当前落笔颜色：插件自定义色优先，其次内置主题色
            // （荧光笔内置笔色、普通色亮/暗主题映射都收敛到 ResolvePenColor，见 MW_PenColorPalette.cs）
            inkCanvas.DefaultDrawingAttributes.Color = ResolvePenColor(inkColor);

            // 主题切换按钮的图标/文字（亮系时提示切到"暗系"，反之亦然）
            if (isUselightThemeColor)
            {
                ColorThemeSwitchIcon.Glyph = "\uE708";
                BoardColorThemeSwitchIcon.Glyph = "\uE708";
                ColorThemeSwitchTextBlock.Text = "暗系";
            }
            else
            {
                ColorThemeSwitchIcon.Glyph = "\uE706";
                BoardColorThemeSwitchIcon.Glyph = "\uE706";
                ColorThemeSwitchTextBlock.Text = "亮系";
            }

            // 应用调色板：浮动栏/白板栏各 9 个普通色块 + 6 个荧光笔色块，
            // 并同步「批注」按钮的画笔颜色指示（色点 + 图标着色）
            ApplyPenPaletteColors();

            // 安装了「自定义画笔颜色」插件时，深/浅色切换按钮整体变为「设置颜色」入口
            UpdateColorThemeSwitchButtonVisual();
            if (drawingAttributes != null && isLoaded)
            {
                if (inkColor > 100)
                {
                    drawingAttributes.Height = 30 + Settings.Canvas.InkWidth;
                    drawingAttributes.Width = 30 + Settings.Canvas.InkWidth;
                    byte NowR = drawingAttributes.Color.R;
                    byte NowG = drawingAttributes.Color.G;
                    byte NowB = drawingAttributes.Color.B;
                    drawingAttributes.Color = Color.FromArgb((byte)Settings.Canvas.InkAlpha, NowR, NowG, NowB);
                    drawingAttributes.IsHighlighter = true;
                }
                else
                {
                    drawingAttributes.Height = Settings.Canvas.InkWidth;
                    drawingAttributes.Width = Settings.Canvas.InkWidth;
                    drawingAttributes.IsHighlighter = false;
                }
            }

            // 改变选中提示
            ViewboxBtnColorBlackContent.Visibility = Visibility.Collapsed;
            ViewboxBtnColorBlueContent.Visibility = Visibility.Collapsed;
            ViewboxBtnColorGreenContent.Visibility = Visibility.Collapsed;
            ViewboxBtnColorRedContent.Visibility = Visibility.Collapsed;
            ViewboxBtnColorYellowContent.Visibility = Visibility.Collapsed;
            ViewboxBtnColorWhiteContent.Visibility = Visibility.Collapsed;
            ViewboxBtnColorPinkContent.Visibility = Visibility.Collapsed;
            ViewboxBtnColorTealContent.Visibility = Visibility.Collapsed;
            ViewboxBtnColorOrangeContent.Visibility = Visibility.Collapsed;
            BoardViewboxBtnColorBlackContent.Visibility = Visibility.Collapsed;
            BoardViewboxBtnColorBlueContent.Visibility = Visibility.Collapsed;
            BoardViewboxBtnColorGreenContent.Visibility = Visibility.Collapsed;
            BoardViewboxBtnColorRedContent.Visibility = Visibility.Collapsed;
            BoardViewboxBtnColorYellowContent.Visibility = Visibility.Collapsed;
            BoardViewboxBtnColorWhiteContent.Visibility = Visibility.Collapsed;
            BoardViewboxBtnColorPinkContent.Visibility = Visibility.Collapsed;
            BoardViewboxBtnColorTealContent.Visibility = Visibility.Collapsed;
            BoardViewboxBtnColorOrangeContent.Visibility = Visibility.Collapsed;

            ViewboxBtnHighlighterColorRedContent.Visibility = Visibility.Collapsed;
            BoardViewboxBtnHighlighterColorRedContent.Visibility = Visibility.Collapsed;
            ViewboxBtnHighlighterColorOrangeContent.Visibility = Visibility.Collapsed;
            BoardViewboxBtnHighlighterColorOrangeContent.Visibility = Visibility.Collapsed;
            ViewboxBtnHighlighterColorYellowContent.Visibility = Visibility.Collapsed;
            BoardViewboxBtnHighlighterColorYellowContent.Visibility = Visibility.Collapsed;
            ViewboxBtnHighlighterColorTealContent.Visibility = Visibility.Collapsed;
            BoardViewboxBtnHighlighterColorTealContent.Visibility = Visibility.Collapsed;
            ViewboxBtnHighlighterColorBlueContent.Visibility = Visibility.Collapsed;
            BoardViewboxBtnHighlighterColorBlueContent.Visibility = Visibility.Collapsed;
            ViewboxBtnHighlighterColorPurpleContent.Visibility = Visibility.Collapsed;
            BoardViewboxBtnHighlighterColorPurpleContent.Visibility = Visibility.Collapsed;
            switch (inkColor)
            {
                case 0:
                    ViewboxBtnColorBlackContent.Visibility = Visibility.Visible;
                    BoardViewboxBtnColorBlackContent.Visibility = Visibility.Visible;
                    break;
                case 1:
                    ViewboxBtnColorRedContent.Visibility = Visibility.Visible;
                    BoardViewboxBtnColorRedContent.Visibility = Visibility.Visible;
                    break;
                case 2:
                    ViewboxBtnColorGreenContent.Visibility = Visibility.Visible;
                    BoardViewboxBtnColorGreenContent.Visibility = Visibility.Visible;
                    break;
                case 3:
                    ViewboxBtnColorBlueContent.Visibility = Visibility.Visible;
                    BoardViewboxBtnColorBlueContent.Visibility = Visibility.Visible;
                    break;
                case 4:
                    ViewboxBtnColorYellowContent.Visibility = Visibility.Visible;
                    BoardViewboxBtnColorYellowContent.Visibility = Visibility.Visible;
                    break;
                case 5:
                    ViewboxBtnColorWhiteContent.Visibility = Visibility.Visible;
                    BoardViewboxBtnColorWhiteContent.Visibility = Visibility.Visible;
                    break;
                case 6:
                    ViewboxBtnColorPinkContent.Visibility = Visibility.Visible;
                    BoardViewboxBtnColorPinkContent.Visibility = Visibility.Visible;
                    break;
                case 7:
                    ViewboxBtnColorTealContent.Visibility = Visibility.Visible;
                    BoardViewboxBtnColorTealContent.Visibility = Visibility.Visible;
                    break;
                case 8:
                    ViewboxBtnColorOrangeContent.Visibility = Visibility.Visible;
                    BoardViewboxBtnColorOrangeContent.Visibility = Visibility.Visible;
                    break;
                case 101:
                    ViewboxBtnHighlighterColorRedContent.Visibility = Visibility.Visible;
                    BoardViewboxBtnHighlighterColorRedContent.Visibility = Visibility.Visible;
                    break;
                case 102:
                    ViewboxBtnHighlighterColorOrangeContent.Visibility = Visibility.Visible;
                    BoardViewboxBtnHighlighterColorOrangeContent.Visibility = Visibility.Visible;
                    break;
                case 103:
                    ViewboxBtnHighlighterColorYellowContent.Visibility = Visibility.Visible;
                    BoardViewboxBtnHighlighterColorYellowContent.Visibility = Visibility.Visible;
                    break;
                case 104:
                    ViewboxBtnHighlighterColorTealContent.Visibility = Visibility.Visible;
                    BoardViewboxBtnHighlighterColorTealContent.Visibility = Visibility.Visible;
                    break;
                case 105:
                    ViewboxBtnHighlighterColorBlueContent.Visibility = Visibility.Visible;
                    BoardViewboxBtnHighlighterColorBlueContent.Visibility = Visibility.Visible;
                    break;
                case 106:
                    ViewboxBtnHighlighterColorPurpleContent.Visibility = Visibility.Visible;
                    BoardViewboxBtnHighlighterColorPurpleContent.Visibility = Visibility.Visible;
                    break;
            }

            // 激光笔颜色与画笔共用同一份选色：这里画笔颜色刚被应用，需同步刷新激光笔按钮的
            // 指示点/描边，否则只有开关切换（SetLaserPointerEnabled）时才会刷新，
            // 表现为「开启激光笔后点颜色，按钮颜色不变，要关一次再开才变」。
            // 放在本方法末尾可一并覆盖明暗主题切换（映射色值随之变化）与模式切换。
            try { UpdateLaserPointerVisual(); } catch { }
        }

        /// <summary>
        /// 按调色板索引取实际颜色：0-8 为普通颜色（随明暗主题映射），101-106 为荧光笔颜色。
        /// 索引非法时退化为 1（红）。激光笔颜色也复用这套索引，保证与用户点选的色块所见即所得。
        /// 自定义色优先（见 MW_PenColorPalette.ResolvePenColor）。
        /// </summary>
        private Color GetInkColorByIndex(int index)
        {
            return ResolvePenColor(index);
        }

        private void CheckLastColor(int inkColor)
        {
            StrokeCollection strokes = inkCanvas.GetSelectedStrokes();
            if (strokes.Count > 0)
            {
                // 选中笔迹的改色也用当前生效颜色（自定义色优先，与色块显示一致）
                Color targetedColor = ResolvePenColor(inkColor);
                if (!isUselightThemeColor)
                {
                    inkCanvas.DefaultDrawingAttributes.Color = ResolvePenColor(inkColor);
                }
                foreach (Stroke stroke in strokes)
                {
                    stroke.DrawingAttributes.Color = targetedColor;
                }
                return;
            }

            // 画笔颜色与激光笔颜色共用同一份选色（按模式分开：浮动栏记 lastDesktopInkColor，白板记 lastBoardInkColor），
            // 激光笔开启时在这里选色会同时生效于两者，无需单独处理
            if (currentMode == 0)
            {
                lastDesktopInkColor = inkColor;
            }
            else
            {
                lastBoardInkColor = inkColor;
            }
            ColorSwitchCheck();
        }

        private void BtnColorBlack_Click(object sender, RoutedEventArgs e)
        {
            CheckLastColor(0);
        }

        private void BtnColorRed_Click(object sender, RoutedEventArgs e)
        {
            CheckLastColor(1);
        }

        private void BtnColorGreen_Click(object sender, RoutedEventArgs e)
        {
            CheckLastColor(2);
        }

        private void BtnColorBlue_Click(object sender, RoutedEventArgs e)
        {
            CheckLastColor(3);
        }

        private void BtnColorYellow_Click(object sender, RoutedEventArgs e)
        {
            CheckLastColor(4);
        }

        private void BtnColorWhite_Click(object sender, RoutedEventArgs e)
        {
            CheckLastColor(5);
        }

        private void BtnColorPink_Click(object sender, RoutedEventArgs e)
        {
            CheckLastColor(6);
        }

        private void BtnColorTeal_Click(object sender, RoutedEventArgs e)
        {
            CheckLastColor(7);
        }

        private void BtnColorOrange_Click(object sender, RoutedEventArgs e)
        {
            CheckLastColor(8);
        }

        private Color StringToColor(string colorStr)
        {
            Byte[] argb = new Byte[4];
            for (int i = 0; i < 4; i++)
            {
                char[] charArray = colorStr.Substring(i * 2 + 1, 2).ToCharArray();
                Byte b1 = toByte(charArray[0]);
                Byte b2 = toByte(charArray[1]);
                argb[i] = (Byte)(b2 | (b1 << 4));
            }
            return Color.FromArgb(argb[0], argb[1], argb[2], argb[3]);//#FFFFFFFF
        }

        private static byte toByte(char c)
        {
            byte b = (byte)"0123456789ABCDEF".IndexOf(c);
            return b;
        }
    }
}