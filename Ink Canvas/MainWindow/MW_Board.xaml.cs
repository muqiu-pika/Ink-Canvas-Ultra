using System;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Ink;
using iNKORE.UI.WPF.Modern;

namespace Ink_Canvas
{
    public partial class MW_Board : UserControl
    {
        public MW_Board()
        {
            InitializeComponent();
        }

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

        private void BoardChangeBackgroundColorBtn_Click(object sender, RoutedEventArgs e)
        {
            InvokeMainWindowHandler(nameof(BoardChangeBackgroundColorBtn_Click), sender, e);
        }

        private void BoardEraserIconByStrokes_Click(object sender, RoutedEventArgs e)
        {
            InvokeMainWindowHandler(nameof(BoardEraserIconByStrokes_Click), sender, e);
        }

        private void BoardEraserIcon_Click(object sender, RoutedEventArgs e)
        {
            InvokeMainWindowHandler(nameof(BoardEraserIcon_Click), sender, e);
        }

        private void BoardImageDrawShape_Click(object sender, RoutedEventArgs e)
        {
            InvokeMainWindowHandler(nameof(BoardImageDrawShape_Click), sender, e);
        }

        private void BoardLaunchDesmos_Click(object sender, RoutedEventArgs e)
        {
            InvokeMainWindowHandler(nameof(BoardLaunchDesmos_Click), sender, e);
        }

        private void BoardLaunchEasiCamera_Click(object sender, RoutedEventArgs e)
        {
            InvokeMainWindowHandler(nameof(BoardLaunchEasiCamera_Click), sender, e);
        }

        private void BoardPenIcon_Click(object sender, RoutedEventArgs e)
        {
            InvokeMainWindowHandler(nameof(BoardPenIcon_Click), sender, e);
        }

        private void BoardSelectIcon_Click(object sender, RoutedEventArgs e)
        {
            InvokeMainWindowHandler(nameof(BoardSelectIcon_Click), sender, e);
        }

        private void BoardSymbolIconDelete_Click(object sender, RoutedEventArgs e)
        {
            InvokeMainWindowHandler(nameof(BoardSymbolIconDelete_Click), sender, e);
        }

        private void Border_MouseDown(object sender, MouseButtonEventArgs e)
        {
            InvokeMainWindowHandler(nameof(Border_MouseDown), sender, e);
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

        private void BtnVideoPresenter_Click(object sender, RoutedEventArgs e)
        {
            InvokeMainWindowHandler(nameof(BtnVideoPresenter_Click), sender, e);
        }

        private void BtnWhiteBoardAdd_Click(object sender, EventArgs e)
        {
            InvokeMainWindowHandler(nameof(BtnWhiteBoardAdd_Click), sender, e);
        }

        private void BtnWhiteBoardSwitchNext_Click(object sender, EventArgs e)
        {
            InvokeMainWindowHandler(nameof(BtnWhiteBoardSwitchNext_Click), sender, e);
        }

        private void BtnWhiteBoardSwitchPrevious_Click(object sender, EventArgs e)
        {
            InvokeMainWindowHandler(nameof(BtnWhiteBoardSwitchPrevious_Click), sender, e);
        }

        private void ColorThemeSwitch_MouseUp(object sender, RoutedEventArgs e)
        {
            InvokeMainWindowHandler(nameof(ColorThemeSwitch_MouseUp), sender, e);
        }

        private void ComboBoxPenStyle_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            InvokeMainWindowHandler(nameof(ComboBoxPenStyle_SelectionChanged), sender, e);
        }

        private void Element_IsEnabledChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            InvokeMainWindowHandler(nameof(Element_IsEnabledChanged), sender, e);
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

        private void Image_MouseDown(object sender, MouseButtonEventArgs e)
        {
            InvokeMainWindowHandler(nameof(Image_MouseDown), sender, e);
        }

        private void InkAlphaSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            InvokeMainWindowHandler(nameof(InkAlphaSlider_ValueChanged), sender, e);
        }

        private void InkWidthSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            InvokeMainWindowHandler(nameof(InkWidthSlider_ValueChanged), sender, e);
        }

        private void SymbolIconOpenInkCanvasFile_Click(object sender, RoutedEventArgs e)
        {
            InvokeMainWindowHandler(nameof(SymbolIconOpenInkCanvasFile_Click), sender, e);
        }

        private void SymbolIconPinBorderDrawShape_MouseUp(object sender, MouseButtonEventArgs e)
        {
            InvokeMainWindowHandler(nameof(SymbolIconPinBorderDrawShape_MouseUp), sender, e);
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
