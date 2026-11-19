using System;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Ink;
using System.Windows.Media;
using iNKORE.UI.WPF.Modern;

namespace Ink_Canvas
{
    public partial class MW_FloatBar : UserControl
    {
        public MW_FloatBar()
        {
            InitializeComponent();
        }

        public dynamic EnableTwoFingerGestureBtnBridge => FindName("EnableTwoFingerGestureBtn");
        public dynamic TwoFingerGestureSimpleStackPanelBridge => FindName("TwoFingerGestureSimpleStackPanel");
        public dynamic InkWidthSliderBridge => FindName("InkWidthSlider");
        public dynamic ColorThemeSwitchTextBlockBridge => FindName("ColorThemeSwitchTextBlock");
        public dynamic IconUndoBridge => FindName("Icon_Undo");
        public dynamic IconRedoBridge => FindName("Icon_Redo");

        private void InvokeMainWindowHandler(string handlerName, params object[] args)
        {
            var window = Window.GetWindow(this) as MainWindow;
            if (window == null)
            {
                return;
            }

            var method = typeof(MainWindow).GetMethod(handlerName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (method == null)
            {
                throw new MissingMethodException(typeof(MainWindow).FullName, handlerName);
            }

            method.Invoke(window, args);
        }

        #region 墨迹选项底边对齐到图形面板

        /// <summary>墨迹面板 XAML 初始左偏移（首次对齐时捕获，之后按图形面板宽度补偿量同步左移）</summary>
        private double _paletteBaseLeft = double.NaN;

        private void PenPaletteSizeChanged(object sender, SizeChangedEventArgs e)
        {
            // 不能在 SizeChanged（布局过程中）直接改 Margin，会触发布局循环保护
            Dispatcher.BeginInvoke(new Action(AlignPenPaletteBottomToDrawShape),
                System.Windows.Threading.DispatcherPriority.Loaded);
        }

        private void PenPaletteIsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            // 面板隐藏时 WPF 不参与布局（ActualHeight=0），SizeChanged 不会触发；
            // 每次变为可见时补一次对齐，保证首次打开就是正确位置
            if (!(bool)e.NewValue) return;
            Dispatcher.BeginInvoke(new Action(AlignPenPaletteBottomToDrawShape),
                System.Windows.Threading.DispatcherPriority.Loaded);
        }

        /// <summary>
        /// 把「墨迹选项」面板底边对齐到「图形」面板底边。
        ///
        /// 两个面板各自高度不同（墨迹由内容撑开、图形原为固定高），历史上的 Margin.Top 是分别写死的，
        /// 任何一侧内容变化都会让底边错位。这里直接读图形面板的实际底边反算墨迹面板的 Margin.Top，
        /// 不再依赖任何估算像素值。
        /// 水平方向同理：图形面板宽度被插件补偿后是向左扩展的（右缘保持原位），墨迹面板紧贴其左侧，
        /// 需要同步左移相同量，否则两块面板会重叠。
        /// </summary>
        private void AlignPenPaletteBottomToDrawShape()
        {
            try
            {
                Helpers.LogHelper.WriteLogToFile(
                    "[面板对齐] 检查 浮动栏 palette=" + (PenPalette == null ? "null" : PenPalette.ActualHeight.ToString("F1"))
                    + " shape=" + (BorderDrawShape == null ? "null" : BorderDrawShape.ActualHeight.ToString("F1"))
                    + " host=" + (PenPaletteScaleHost == null ? "null" : "ok")
                    + " shapeHost=" + (BorderDrawShapeScaleHost == null ? "null" : "ok"),
                    Helpers.LogHelper.LogType.Info);

                if (PenPalette == null || BorderDrawShape == null) return;
                if (PenPaletteScaleHost == null || BorderDrawShapeScaleHost == null) return;

                double paletteHeight = PenPalette.ActualHeight;
                double shapeHeight = BorderDrawShape.ActualHeight;
                if (paletteHeight < 80 || shapeHeight < 80) return;

                // 窗口高度跟随图形面板（底边本就对齐，等高后顶边同时对齐），不低于 XAML 基线 212
                double targetHeight = Math.Max(212, shapeHeight);
                if (Math.Abs(PenPalette.Height - targetHeight) > 0.5)
                {
                    PenPalette.Height = targetHeight;
                }

                double shapeBottom = BorderDrawShapeScaleHost.Margin.Top + shapeHeight;
                double targetTop = shapeBottom - targetHeight;

                var hostMargin = PenPaletteScaleHost.Margin;
                if (double.IsNaN(_paletteBaseLeft)) _paletteBaseLeft = hostMargin.Left;
                // 图形面板 XAML 基线宽 280（与 MW_FloatBar.xaml 中 Width 一致），超出部分即插件宽度补偿量。
                // 图形与墨迹面板各自的缩放系数可能不同（图形面板缩放是独立设置项），
                // 需要按各自的 ScaleX 换算，保证两块面板的渲染位移一致、间距与未补偿时相同
                double shapeDeltaWidth = Math.Max(0, BorderDrawShape.Width - 280);
                double shapeScaleX = GetScaleX(BorderDrawShapeScaleHost);
                double paletteScaleX = GetScaleX(PenPaletteScaleHost);
                double leftShift = paletteScaleX <= 0.01
                    ? shapeDeltaWidth
                    : shapeDeltaWidth * shapeScaleX / paletteScaleX;
                double targetLeft = _paletteBaseLeft - leftShift;

                bool changed = Math.Abs(hostMargin.Top - targetTop) >= 0.5
                               || Math.Abs(hostMargin.Left - targetLeft) >= 0.5;

                // 内容在窗口内垂直居中（顶栏不参与，窗口尺寸/位置由上面的高度与对齐决定）
                CenterPaletteContent(PenPalette, targetHeight);

                Helpers.LogHelper.WriteLogToFile(
                    "[面板对齐] 浮动栏 墨迹h=" + targetHeight.ToString("F1")
                    + " 图形h=" + shapeHeight.ToString("F1")
                    + " 图形底边=" + shapeBottom.ToString("F1")
                    + " 目标Top=" + targetTop.ToString("F1")
                    + " 当前Top=" + hostMargin.Top.ToString("F1")
                    + " 目标Left=" + targetLeft.ToString("F1")
                    + " 当前Left=" + hostMargin.Left.ToString("F1")
                    + (changed ? " -> 应用" : " -> 无需调整"),
                    Helpers.LogHelper.LogType.Info);

                if (!changed) return;

                PenPaletteScaleHost.Margin = new Thickness(targetLeft, targetTop, hostMargin.Right, hostMargin.Bottom);

                // 缩放锚点：水平跟随面板左缘（= -Margin.Left），垂直保持在面板底边，缩放时不会相对浮动栏漂移
                if (PenPaletteScaleHost.RenderTransform is TransformGroup group && group.Children.Count >= 3)
                {
                    if (group.Children[0] is TranslateTransform top)
                    {
                        top.X = -targetLeft;
                        top.Y = -targetHeight;
                    }
                    if (group.Children[2] is TranslateTransform bottom)
                    {
                        bottom.X = targetLeft;
                        bottom.Y = targetHeight;
                    }
                }
            }
            catch (Exception ex)
            {
                Helpers.LogHelper.WriteLogToFile("[面板对齐] 浮动栏 失败: " + ex.Message, Helpers.LogHelper.LogType.Warning);
            }
        }

        /// <summary>
        /// 把「墨迹选项」顶栏之外的内容在窗口内垂直居中：
        /// 顶栏固定在顶部，内容容器的顶边距 =（标题以下的可用区 − 内容高）/ 2。
        /// </summary>
        private static void CenterPaletteContent(Border palette, double paletteHeight)
        {
            if (!(palette.Child is Panel stack) || stack.Children.Count < 2) return;
            if (!(stack.Children[0] is FrameworkElement title)) return;
            if (!(stack.Children[1] is FrameworkElement content)) return;

            double innerHeight = paletteHeight - palette.BorderThickness.Top - palette.BorderThickness.Bottom;
            double titleSlot = title.ActualHeight + title.Margin.Top + title.Margin.Bottom;
            double areaHeight = innerHeight - titleSlot;
            if (title.ActualHeight <= 0 || content.ActualHeight <= 0 || areaHeight <= 0) return;

            double topMargin = Math.Max(0, (areaHeight - content.ActualHeight) / 2);
            if (Math.Abs(content.Margin.Top - topMargin) > 0.5)
            {
                content.Margin = new Thickness(content.Margin.Left, topMargin, content.Margin.Right, content.Margin.Bottom);
            }
        }

        /// <summary>供主程序在面板显示后调用（隐藏状态下 SizeChanged 不触发，需要在显示时补一次对齐）</summary>
        public void AlignPaletteNow()
        {
            AlignPenPaletteBottomToDrawShape();
        }

        /// <summary>读取缩放宿主 RenderTransform 里的横向缩放系数（面板缩放设置项的运行值）</summary>
        private static double GetScaleX(FrameworkElement host)
        {
            if (host?.RenderTransform is TransformGroup group && group.Children.Count >= 3 &&
                group.Children[1] is ScaleTransform scale && Math.Abs(scale.ScaleX) > 0.01)
            {
                return scale.ScaleX;
            }
            return 1.0;
        }

        #endregion

        private void Border_MouseDown(object sender, MouseButtonEventArgs e)
        {
            InvokeMainWindowHandler(nameof(Border_MouseDown), sender, e);
        }

        private void ShapeTab_MouseUp(object sender, MouseButtonEventArgs e)
        {
            InvokeMainWindowHandler(nameof(ShapeTab_MouseUp), sender, e);
        }

        private void SymbolIconEmoji_MouseDown(object sender, MouseButtonEventArgs e)
        {
            InvokeMainWindowHandler(nameof(SymbolIconEmoji_MouseDown), sender, e);
        }

        private void SymbolIconEmoji_MouseUp(object sender, MouseButtonEventArgs e)
        {
            InvokeMainWindowHandler(nameof(SymbolIconEmoji_MouseUp), sender, e);
        }

        private void SymbolIconEmoji_PreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
        {
            InvokeMainWindowHandler(nameof(SymbolIconEmoji_PreviewMouseRightButtonDown), sender, e);
        }

        private void SymbolIconEmoji_PreviewTouchDown(object sender, TouchEventArgs e)
        {
            InvokeMainWindowHandler(nameof(SymbolIconEmoji_PreviewTouchDown), sender, e);
        }

        private void SymbolIconEmoji_PreviewTouchMove(object sender, TouchEventArgs e)
        {
            InvokeMainWindowHandler(nameof(SymbolIconEmoji_PreviewTouchMove), sender, e);
        }

        private void SymbolIconEmoji_PreviewTouchUp(object sender, TouchEventArgs e)
        {
            InvokeMainWindowHandler(nameof(SymbolIconEmoji_PreviewTouchUp), sender, e);
        }

        private void BtnColorBlack_Click(object sender, RoutedEventArgs e)
        {
            InvokeMainWindowHandler(nameof(BtnColorBlack_Click), sender, e);
        }

        private void BtnColorBlue_Click(object sender, RoutedEventArgs e)
        {
            InvokeMainWindowHandler(nameof(BtnColorBlue_Click), sender, e);
        }

        private void BtnColorGreen_Click(object sender, RoutedEventArgs e)
        {
            InvokeMainWindowHandler(nameof(BtnColorGreen_Click), sender, e);
        }

        private void BtnColorOrange_Click(object sender, RoutedEventArgs e)
        {
            InvokeMainWindowHandler(nameof(BtnColorOrange_Click), sender, e);
        }

        private void BtnColorPink_Click(object sender, RoutedEventArgs e)
        {
            InvokeMainWindowHandler(nameof(BtnColorPink_Click), sender, e);
        }

        private void BtnColorRed_Click(object sender, RoutedEventArgs e)
        {
            InvokeMainWindowHandler(nameof(BtnColorRed_Click), sender, e);
        }

        private void BtnColorTeal_Click(object sender, RoutedEventArgs e)
        {
            InvokeMainWindowHandler(nameof(BtnColorTeal_Click), sender, e);
        }

        private void BtnColorWhite_Click(object sender, RoutedEventArgs e)
        {
            InvokeMainWindowHandler(nameof(BtnColorWhite_Click), sender, e);
        }

        private void BtnColorYellow_Click(object sender, RoutedEventArgs e)
        {
            InvokeMainWindowHandler(nameof(BtnColorYellow_Click), sender, e);
        }

        private void BtnDrawArrow_Click(object sender, MouseButtonEventArgs e)
        {
            InvokeMainWindowHandler(nameof(BtnDrawArrow_Click), sender, e);
        }

        private void BtnDrawCenterEllipseWithFocalPoint_Click(object sender, MouseButtonEventArgs e)
        {
            InvokeMainWindowHandler(nameof(BtnDrawCenterEllipseWithFocalPoint_Click), sender, e);
        }

        private void BtnDrawCenterEllipse_Click(object sender, MouseButtonEventArgs e)
        {
            InvokeMainWindowHandler(nameof(BtnDrawCenterEllipse_Click), sender, e);
        }

        private void BtnDrawCircle_Click(object sender, MouseButtonEventArgs e)
        {
            InvokeMainWindowHandler(nameof(BtnDrawCircle_Click), sender, e);
        }

        private void BtnDrawCone_Click(object sender, MouseButtonEventArgs e)
        {
            InvokeMainWindowHandler(nameof(BtnDrawCone_Click), sender, e);
        }

        private void BtnDrawCoordinate1_Click(object sender, MouseButtonEventArgs e)
        {
            InvokeMainWindowHandler(nameof(BtnDrawCoordinate1_Click), sender, e);
        }

        private void BtnDrawCoordinate2_Click(object sender, MouseButtonEventArgs e)
        {
            InvokeMainWindowHandler(nameof(BtnDrawCoordinate2_Click), sender, e);
        }

        private void BtnDrawCoordinate3_Click(object sender, MouseButtonEventArgs e)
        {
            InvokeMainWindowHandler(nameof(BtnDrawCoordinate3_Click), sender, e);
        }

        private void BtnDrawCoordinate4_Click(object sender, MouseButtonEventArgs e)
        {
            InvokeMainWindowHandler(nameof(BtnDrawCoordinate4_Click), sender, e);
        }

        private void BtnDrawCoordinate5_Click(object sender, MouseButtonEventArgs e)
        {
            InvokeMainWindowHandler(nameof(BtnDrawCoordinate5_Click), sender, e);
        }

        private void BtnDrawCuboid_Click(object sender, MouseButtonEventArgs e)
        {
            InvokeMainWindowHandler(nameof(BtnDrawCuboid_Click), sender, e);
        }

        private void BtnDrawTetrahedron_Click(object sender, MouseButtonEventArgs e)
        {
            InvokeMainWindowHandler(nameof(BtnDrawTetrahedron_Click), sender, e);
        }

        private void BtnDrawCylinder_Click(object sender, MouseButtonEventArgs e)
        {
            InvokeMainWindowHandler(nameof(BtnDrawCylinder_Click), sender, e);
        }

        private void BtnDrawDashedCircle_Click(object sender, MouseButtonEventArgs e)
        {
            InvokeMainWindowHandler(nameof(BtnDrawDashedCircle_Click), sender, e);
        }

        private void BtnDrawDashedLine_Click(object sender, MouseButtonEventArgs e)
        {
            InvokeMainWindowHandler(nameof(BtnDrawDashedLine_Click), sender, e);
        }

        private void BtnDrawDotLine_Click(object sender, MouseButtonEventArgs e)
        {
            InvokeMainWindowHandler(nameof(BtnDrawDotLine_Click), sender, e);
        }

        private void BtnDrawEllipse_Click(object sender, MouseButtonEventArgs e)
        {
            InvokeMainWindowHandler(nameof(BtnDrawEllipse_Click), sender, e);
        }

        private void BtnDrawHyperbolaWithFocalPoint_Click(object sender, MouseButtonEventArgs e)
        {
            InvokeMainWindowHandler(nameof(BtnDrawHyperbolaWithFocalPoint_Click), sender, e);
        }

        private void BtnDrawHyperbola_Click(object sender, MouseButtonEventArgs e)
        {
            InvokeMainWindowHandler(nameof(BtnDrawHyperbola_Click), sender, e);
        }

        private void BtnDrawLine_Click(object sender, MouseButtonEventArgs e)
        {
            InvokeMainWindowHandler(nameof(BtnDrawLine_Click), sender, e);
        }

        private void BtnDrawParabola1_Click(object sender, MouseButtonEventArgs e)
        {
            InvokeMainWindowHandler(nameof(BtnDrawParabola1_Click), sender, e);
        }

        private void BtnDrawParabola2_Click(object sender, MouseButtonEventArgs e)
        {
            InvokeMainWindowHandler(nameof(BtnDrawParabola2_Click), sender, e);
        }

        private void BtnDrawParabolaWithFocalPoint_Click(object sender, MouseButtonEventArgs e)
        {
            InvokeMainWindowHandler(nameof(BtnDrawParabolaWithFocalPoint_Click), sender, e);
        }

        private void BtnDrawParallelLine_Click(object sender, MouseButtonEventArgs e)
        {
            InvokeMainWindowHandler(nameof(BtnDrawParallelLine_Click), sender, e);
        }

        private void BtnDrawRectangleCenter_Click(object sender, MouseButtonEventArgs e)
        {
            InvokeMainWindowHandler(nameof(BtnDrawRectangleCenter_Click), sender, e);
        }

        private void BtnDrawRectangle_Click(object sender, MouseButtonEventArgs e)
        {
            InvokeMainWindowHandler(nameof(BtnDrawRectangle_Click), sender, e);
        }

        private void BtnExit_Click(object sender, RoutedEventArgs e)
        {
            InvokeMainWindowHandler(nameof(BtnExit_Click), sender, e);
        }

        private void BtnHighlighterColorBlue_Click(object sender, RoutedEventArgs e)
        {
            InvokeMainWindowHandler(nameof(BtnHighlighterColorBlue_Click), sender, e);
        }

        private void BtnHighlighterColorOrange_Click(object sender, RoutedEventArgs e)
        {
            InvokeMainWindowHandler(nameof(BtnHighlighterColorOrange_Click), sender, e);
        }

        private void BtnHighlighterColorPurple_Click(object sender, RoutedEventArgs e)
        {
            InvokeMainWindowHandler(nameof(BtnHighlighterColorPurple_Click), sender, e);
        }

        private void BtnHighlighterColorRed_Click(object sender, RoutedEventArgs e)
        {
            InvokeMainWindowHandler(nameof(BtnHighlighterColorRed_Click), sender, e);
        }

        private void BtnHighlighterColorTeal_Click(object sender, RoutedEventArgs e)
        {
            InvokeMainWindowHandler(nameof(BtnHighlighterColorTeal_Click), sender, e);
        }

        private void BtnHighlighterColorYellow_Click(object sender, RoutedEventArgs e)
        {
            InvokeMainWindowHandler(nameof(BtnHighlighterColorYellow_Click), sender, e);
        }

        private void BtnMediaInsertUnified_Click(object sender, RoutedEventArgs e)
        {
            InvokeMainWindowHandler(nameof(BtnMediaInsertUnified_Click), sender, e);
        }

        private void BtnLaserPointer_Click(object sender, RoutedEventArgs e)
        {
            InvokeMainWindowHandler(nameof(BtnLaserPointer_Click), sender, e);
        }

        private void BtnPPTSlideShowEnd_Click(object sender, RoutedEventArgs e)
        {
            InvokeMainWindowHandler(nameof(BtnPPTSlideShowEnd_Click), sender, e);
        }

        private void ColorThemeSwitch_MouseUp(object sender, RoutedEventArgs e)
        {
            InvokeMainWindowHandler(nameof(ColorThemeSwitch_MouseUp), sender, e);
        }

        private void ComboBoxPenStyle_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            InvokeMainWindowHandler(nameof(ComboBoxPenStyle_SelectionChanged), sender, e);
        }

        private void CursorIcon_Click(object sender, RoutedEventArgs e)
        {
            InvokeMainWindowHandler(nameof(CursorIcon_Click), sender, e);
        }

        private void CursorWithDelIcon_Click(object sender, RoutedEventArgs e)
        {
            InvokeMainWindowHandler(nameof(CursorWithDelIcon_Click), sender, e);
        }

        private void Element_IsEnabledChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            InvokeMainWindowHandler(nameof(Element_IsEnabledChanged), sender, e);
        }

        private void EraserIconByStrokes_Click(object sender, RoutedEventArgs e)
        {
            InvokeMainWindowHandler(nameof(EraserIconByStrokes_Click), sender, e);
        }

        private void EraserIcon_Click(object sender, RoutedEventArgs e)
        {
            InvokeMainWindowHandler(nameof(EraserIcon_Click), sender, e);
        }

        private void FoldFloatingBar_Click(object sender, RoutedEventArgs e)
        {
            InvokeMainWindowHandler(nameof(FoldFloatingBar_Click), sender, e);
        }

        private void GridInkReplayButton_Click(object sender, RoutedEventArgs e)
        {
            InvokeMainWindowHandler(nameof(GridInkReplayButton_Click), sender, e);
        }

        private void ImageBlackboard_Click(object sender, RoutedEventArgs e)
        {
            InvokeMainWindowHandler(nameof(ImageBlackboard_Click), sender, e);
        }

        private void ImageCountdownTimer_Click(object sender, RoutedEventArgs e)
        {
            InvokeMainWindowHandler(nameof(ImageCountdownTimer_Click), sender, e);
        }

        private void ImageDrawShape_Click(object sender, RoutedEventArgs e)
        {
            InvokeMainWindowHandler(nameof(ImageDrawShape_Click), sender, e);
        }

        private void Image_MouseDown(object sender, MouseButtonEventArgs e)
        {
            InvokeMainWindowHandler(nameof(Image_MouseDown), sender, e);
        }

        private void InkAlphaSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            InvokeMainWindowHandler(nameof(InkAlphaSlider_ValueChanged), sender, e);
        }

        private void InkAnnotationAlphaSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            InvokeMainWindowHandler(nameof(InkAnnotationAlphaSlider_ValueChanged), sender, e);
        }

        private void InkWidthSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            InvokeMainWindowHandler(nameof(InkWidthSlider_ValueChanged), sender, e);
        }

        private void PenIcon_Click(object sender, RoutedEventArgs e)
        {
            InvokeMainWindowHandler(nameof(PenIcon_Click), sender, e);
        }

        private void SymbolIconDelete_MouseUp(object sender, RoutedEventArgs e)
        {
            InvokeMainWindowHandler(nameof(SymbolIconDelete_MouseUp), sender, e);
        }

        private void SymbolIconOpenInkCanvasFile_Click(object sender, RoutedEventArgs e)
        {
            InvokeMainWindowHandler(nameof(SymbolIconOpenInkCanvasFile_Click), sender, e);
        }

        private void SymbolIconPinBorderDrawShape_MouseUp(object sender, MouseButtonEventArgs e)
        {
            InvokeMainWindowHandler(nameof(SymbolIconPinBorderDrawShape_MouseUp), sender, e);
        }

        private void ShapePanelDragHandle_MouseDown(object sender, MouseButtonEventArgs e)
        {
            InvokeMainWindowHandler(nameof(ShapePanelDragHandle_MouseDown), sender, e);
        }

        private void ShapePanelDragHandle_MouseMove(object sender, MouseEventArgs e)
        {
            InvokeMainWindowHandler(nameof(ShapePanelDragHandle_MouseMove), sender, e);
        }

        private void ShapePanelDragHandle_MouseUp(object sender, MouseButtonEventArgs e)
        {
            InvokeMainWindowHandler(nameof(ShapePanelDragHandle_MouseUp), sender, e);
        }

        private void ShapePanelDragHandle_PreviewTouchDown(object sender, TouchEventArgs e)
        {
            InvokeMainWindowHandler(nameof(ShapePanelDragHandle_PreviewTouchDown), sender, e);
        }

        private void ShapePanelDragHandle_PreviewTouchMove(object sender, TouchEventArgs e)
        {
            InvokeMainWindowHandler(nameof(ShapePanelDragHandle_PreviewTouchMove), sender, e);
        }

        private void ShapePanelDragHandle_PreviewTouchUp(object sender, TouchEventArgs e)
        {
            InvokeMainWindowHandler(nameof(ShapePanelDragHandle_PreviewTouchUp), sender, e);
        }

        private void ShapePanelRestore_MouseDown(object sender, MouseButtonEventArgs e)
        {
            InvokeMainWindowHandler(nameof(ShapePanelRestore_MouseDown), sender, e);
        }

        private void ShapePanelRestore_MouseUp(object sender, MouseButtonEventArgs e)
        {
            InvokeMainWindowHandler(nameof(ShapePanelRestore_MouseUp), sender, e);
        }

        private void ShapePanelRestore_TouchDown(object sender, TouchEventArgs e)
        {
            InvokeMainWindowHandler(nameof(ShapePanelRestore_TouchDown), sender, e);
        }

        private void ShapePanelRestore_TouchUp(object sender, TouchEventArgs e)
        {
            InvokeMainWindowHandler(nameof(ShapePanelRestore_TouchUp), sender, e);
        }

        private void SymbolIconPinPenPalette_MouseUp(object sender, MouseButtonEventArgs e)
        {
            InvokeMainWindowHandler(nameof(SymbolIconPinPenPalette_MouseUp), sender, e);
        }

        private void PenPaletteDragHandle_MouseDown(object sender, MouseButtonEventArgs e)
        {
            InvokeMainWindowHandler(nameof(PenPaletteDragHandle_MouseDown), sender, e);
        }

        private void PenPaletteDragHandle_MouseMove(object sender, MouseEventArgs e)
        {
            InvokeMainWindowHandler(nameof(PenPaletteDragHandle_MouseMove), sender, e);
        }

        private void PenPaletteDragHandle_MouseUp(object sender, MouseButtonEventArgs e)
        {
            InvokeMainWindowHandler(nameof(PenPaletteDragHandle_MouseUp), sender, e);
        }

        private void PenPaletteDragHandle_PreviewTouchDown(object sender, TouchEventArgs e)
        {
            InvokeMainWindowHandler(nameof(PenPaletteDragHandle_PreviewTouchDown), sender, e);
        }

        private void PenPaletteDragHandle_PreviewTouchMove(object sender, TouchEventArgs e)
        {
            InvokeMainWindowHandler(nameof(PenPaletteDragHandle_PreviewTouchMove), sender, e);
        }

        private void PenPaletteDragHandle_PreviewTouchUp(object sender, TouchEventArgs e)
        {
            InvokeMainWindowHandler(nameof(PenPaletteDragHandle_PreviewTouchUp), sender, e);
        }

        private void PenPaletteRestore_MouseDown(object sender, MouseButtonEventArgs e)
        {
            InvokeMainWindowHandler(nameof(PenPaletteRestore_MouseDown), sender, e);
        }

        private void PenPaletteRestore_MouseUp(object sender, MouseButtonEventArgs e)
        {
            InvokeMainWindowHandler(nameof(PenPaletteRestore_MouseUp), sender, e);
        }

        private void PenPaletteRestore_TouchDown(object sender, TouchEventArgs e)
        {
            InvokeMainWindowHandler(nameof(PenPaletteRestore_TouchDown), sender, e);
        }

        private void PenPaletteRestore_TouchUp(object sender, TouchEventArgs e)
        {
            InvokeMainWindowHandler(nameof(PenPaletteRestore_TouchUp), sender, e);
        }

        private void SymbolIconRandOne_Click(object sender, RoutedEventArgs e)
        {
            InvokeMainWindowHandler(nameof(SymbolIconRandOne_Click), sender, e);
        }

        private void SymbolIconRand_Click(object sender, RoutedEventArgs e)
        {
            InvokeMainWindowHandler(nameof(SymbolIconRand_Click), sender, e);
        }

        private void SymbolIconRedo_Click(object sender, RoutedEventArgs e)
        {
            InvokeMainWindowHandler(nameof(SymbolIconRedo_Click), sender, e);
        }

        private void SymbolIconSaveStrokes_Click(object sender, RoutedEventArgs e)
        {
            InvokeMainWindowHandler(nameof(SymbolIconSaveStrokes_Click), sender, e);
        }

        private void SymbolIconScreenshot_Click(object sender, RoutedEventArgs e)
        {
            InvokeMainWindowHandler(nameof(SymbolIconScreenshot_Click), sender, e);
        }

        private void SymbolIconSelect_Click(object sender, RoutedEventArgs e)
        {
            InvokeMainWindowHandler(nameof(SymbolIconSelect_Click), sender, e);
        }

        private void SymbolIconSettings_Click(object sender, RoutedEventArgs e)
        {
            InvokeMainWindowHandler(nameof(SymbolIconSettings_Click), sender, e);
        }

        private void SymbolIconTools_Click(object sender, RoutedEventArgs e)
        {
            InvokeMainWindowHandler(nameof(SymbolIconTools_Click), sender, e);
        }

        private void SymbolIconUndo_Click(object sender, RoutedEventArgs e)
        {
            InvokeMainWindowHandler(nameof(SymbolIconUndo_Click), sender, e);
        }

        private void ToggleSwitchEnableInkToShape_Toggled(object sender, RoutedEventArgs e)
        {
            InvokeMainWindowHandler(nameof(ToggleSwitchEnableInkToShape_Toggled), sender, e);
        }

        private void ToggleSwitchEnableMultiTouchMode_Toggled(object sender, RoutedEventArgs e)
        {
            InvokeMainWindowHandler(nameof(ToggleSwitchEnableMultiTouchMode_Toggled), sender, e);
        }

        private void ToggleSwitchEnableNibMode_Toggled(object sender, RoutedEventArgs e)
        {
            InvokeMainWindowHandler(nameof(ToggleSwitchEnableNibMode_Toggled), sender, e);
        }

        private void ToggleSwitchEnableTwoFingerRotation_Toggled(object sender, RoutedEventArgs e)
        {
            InvokeMainWindowHandler(nameof(ToggleSwitchEnableTwoFingerRotation_Toggled), sender, e);
        }

        private void ToggleSwitchEnableTwoFingerTranslate_Toggled(object sender, RoutedEventArgs e)
        {
            InvokeMainWindowHandler(nameof(ToggleSwitchEnableTwoFingerTranslate_Toggled), sender, e);
        }

        private void ToggleSwitchEnableTwoFingerZoom_Toggled(object sender, RoutedEventArgs e)
        {
            InvokeMainWindowHandler(nameof(ToggleSwitchEnableTwoFingerZoom_Toggled), sender, e);
        }

        private void TwoFingerGestureBorder_Click(object sender, RoutedEventArgs e)
        {
            InvokeMainWindowHandler(nameof(TwoFingerGestureBorder_Click), sender, e);
        }
    }
}
