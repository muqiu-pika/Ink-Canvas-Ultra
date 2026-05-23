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
    public partial class MW_Selection : UserControl
    {
        public MW_Selection()
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

        private void BorderStrokeSelectionCloneToBoardOrNewPage_Click(object sender, RoutedEventArgs e)
        {
            InvokeMainWindowHandler(nameof(BorderStrokeSelectionCloneToBoardOrNewPage_Click), sender, e);
        }

        private void BorderStrokeSelectionClone_Click(object sender, RoutedEventArgs e)
        {
            InvokeMainWindowHandler(nameof(BorderStrokeSelectionClone_Click), sender, e);
        }

        private void BorderStrokeSelectionDelete_Click(object sender, RoutedEventArgs e)
        {
            InvokeMainWindowHandler(nameof(BorderStrokeSelectionDelete_Click), sender, e);
        }

        private void BtnAnticlockwiseRotate15_Click(object sender, RoutedEventArgs e)
        {
            InvokeMainWindowHandler(nameof(BtnAnticlockwiseRotate15_Click), sender, e);
        }

        private void BtnAnticlockwiseRotate45_Click(object sender, RoutedEventArgs e)
        {
            InvokeMainWindowHandler(nameof(BtnAnticlockwiseRotate45_Click), sender, e);
        }

        private void BtnAnticlockwiseRotate90_Click(object sender, RoutedEventArgs e)
        {
            InvokeMainWindowHandler(nameof(BtnAnticlockwiseRotate90_Click), sender, e);
        }

        private void BtnClockwiseRotate15_Click(object sender, RoutedEventArgs e)
        {
            InvokeMainWindowHandler(nameof(BtnClockwiseRotate15_Click), sender, e);
        }

        private void BtnClockwiseRotate45_Click(object sender, RoutedEventArgs e)
        {
            InvokeMainWindowHandler(nameof(BtnClockwiseRotate45_Click), sender, e);
        }

        private void BtnClockwiseRotate90_Click(object sender, RoutedEventArgs e)
        {
            InvokeMainWindowHandler(nameof(BtnClockwiseRotate90_Click), sender, e);
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

        private void BtnFlipHorizontal_Click(object sender, RoutedEventArgs e)
        {
            InvokeMainWindowHandler(nameof(BtnFlipHorizontal_Click), sender, e);
        }

        private void BtnFlipVertical_Click(object sender, RoutedEventArgs e)
        {
            InvokeMainWindowHandler(nameof(BtnFlipVertical_Click), sender, e);
        }

        private void BtnStrokeSelectionSaveToImage_Click(object sender, RoutedEventArgs e)
        {
            InvokeMainWindowHandler(nameof(BtnStrokeSelectionSaveToImage_Click), sender, e);
        }

        private void BtnVideoPlayPause_Click(object sender, RoutedEventArgs e)
        {
            InvokeMainWindowHandler(nameof(BtnVideoPlayPause_Click), sender, e);
        }

        private void GridInkCanvasSelectionCover_ManipulationCompleted(object sender, ManipulationCompletedEventArgs e)
        {
            InvokeMainWindowHandler(nameof(GridInkCanvasSelectionCover_ManipulationCompleted), sender, e);
        }

        private void GridInkCanvasSelectionCover_ManipulationDelta(object sender, ManipulationDeltaEventArgs e)
        {
            InvokeMainWindowHandler(nameof(GridInkCanvasSelectionCover_ManipulationDelta), sender, e);
        }

        private void GridInkCanvasSelectionCover_ManipulationStarting(object sender, ManipulationStartingEventArgs e)
        {
            InvokeMainWindowHandler(nameof(GridInkCanvasSelectionCover_ManipulationStarting), sender, e);
        }

        private void GridInkCanvasSelectionCover_MouseDown(object sender, MouseButtonEventArgs e)
        {
            InvokeMainWindowHandler(nameof(GridInkCanvasSelectionCover_MouseDown), sender, e);
        }

        private void GridInkCanvasSelectionCover_MouseMove(object sender, MouseEventArgs e)
        {
            InvokeMainWindowHandler(nameof(GridInkCanvasSelectionCover_MouseMove), sender, e);
        }

        private void GridInkCanvasSelectionCover_MouseUp(object sender, MouseButtonEventArgs e)
        {
            InvokeMainWindowHandler(nameof(GridInkCanvasSelectionCover_MouseUp), sender, e);
        }

        private void GridInkCanvasSelectionCover_MouseWheel(object sender, MouseWheelEventArgs e)
        {
            InvokeMainWindowHandler(nameof(GridInkCanvasSelectionCover_MouseWheel), sender, e);
        }

        private void GridInkCanvasSelectionCover_PreviewTouchDown(object sender, TouchEventArgs e)
        {
            InvokeMainWindowHandler(nameof(GridInkCanvasSelectionCover_PreviewTouchDown), sender, e);
        }

        private void GridInkCanvasSelectionCover_PreviewTouchUp(object sender, TouchEventArgs e)
        {
            InvokeMainWindowHandler(nameof(GridInkCanvasSelectionCover_PreviewTouchUp), sender, e);
        }

        private void GridPenWidthDecrease_Click(object sender, RoutedEventArgs e)
        {
            InvokeMainWindowHandler(nameof(GridPenWidthDecrease_Click), sender, e);
        }

        private void GridPenWidthIncrease_Click(object sender, RoutedEventArgs e)
        {
            InvokeMainWindowHandler(nameof(GridPenWidthIncrease_Click), sender, e);
        }

        private void GridPenWidthRestore_Click(object sender, RoutedEventArgs e)
        {
            InvokeMainWindowHandler(nameof(GridPenWidthRestore_Click), sender, e);
        }

        private void SliderVideoProgress_PreviewMouseDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            InvokeMainWindowHandler(nameof(SliderVideoProgress_PreviewMouseDown), sender, e);
        }

        private void SliderVideoProgress_PreviewMouseUp(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            InvokeMainWindowHandler(nameof(SliderVideoProgress_PreviewMouseUp), sender, e);
        }

        private void SliderVideoProgress_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            InvokeMainWindowHandler(nameof(SliderVideoProgress_ValueChanged), sender, e);
        }

        private void SliderVideoVolume_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            InvokeMainWindowHandler(nameof(SliderVideoVolume_ValueChanged), sender, e);
        }
    }
}
