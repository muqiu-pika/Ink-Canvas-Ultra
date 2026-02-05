using Ink_Canvas.Helpers;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Ink;
using System.Windows.Media;
using Application = System.Windows.Application;
using System.Diagnostics;

namespace Ink_Canvas
{
    public partial class MainWindow : Window
    {
        private void BoardChangeBackgroundColorBtn_Click(object sender, RoutedEventArgs e)
        {
            if (!isLoaded) return;
            Settings.Canvas.UsingWhiteboard = !Settings.Canvas.UsingWhiteboard;
            SaveSettingsToFile();
            if (Settings.Canvas.UsingWhiteboard)
            {
                if (inkColor == 5) lastBoardInkColor = 0;
            }
            else
            {
                if (inkColor == 0) lastBoardInkColor = 5;
            }
            ComboBoxTheme_SelectionChanged(null, null);
            CheckColorTheme(true);
            if (BoardPen.Opacity == 1)
            {
                BoardPen.Background = (Brush)Application.Current.FindResource("BoardBarBackground");
            }
            if (BoardEraser.Opacity == 1)
            {
                BoardEraser.Background = (Brush)Application.Current.FindResource("BoardBarBackground");
            }
            if (BoardSelect.Opacity == 1)
            {
                BoardSelect.Background = (Brush)Application.Current.FindResource("BoardBarBackground");
            }
            if (BoardEraserByStrokes.Opacity == 1)
            {
                BoardEraserByStrokes.Background = (Brush)Application.Current.FindResource("BoardBarBackground");
            }
        }

        // 白板模式画笔按钮点击事件 - 独立处理，不影响浮动栏按钮
        private void BoardPenIcon_Click(object sender, RoutedEventArgs e)
        {
            if (BoardPen.Opacity != 1)
            {
                AnimationsHelper.ShowWithSlideFromBottomAndFade(BoardPenPalette);
            }
            else
            {
                inkCanvas.EditingMode = InkCanvasEditingMode.Ink;

                Main_Grid.Background = new SolidColorBrush(StringToColor("#01FFFFFF"));

                inkCanvas.IsHitTestVisible = true;
                inkCanvas.Visibility = Visibility.Visible;

                GridBackgroundCoverHolder.Visibility = Visibility.Visible;
                GridInkCanvasSelectionCover.Visibility = Visibility.Collapsed;

                StackPanelCanvasControls.Visibility = Visibility.Visible;

                CheckEnableTwoFingerGestureBtnVisibility(true);
                inkCanvas.EditingMode = InkCanvasEditingMode.Ink;
                ColorSwitchCheck();
                HideSubPanels("pen", false, true);
            }
        }

        // 白板模式选择按钮点击事件 - 独立处理，不影响浮动栏按钮
        private void BoardSelectIcon_Click(object sender, RoutedEventArgs e)
        {
            BtnSelect_Click(null, null);
            HideSubPanels("select", false, true);
        }

        private void BoardEraserIcon_Click(object sender, RoutedEventArgs e)
        {
            if (BoardEraser.Opacity != 1)
            {
                AnimationsHelper.ShowWithSlideFromBottomAndFade(BoardDeleteIcon);
            }
            else
            {
                forceEraser = true;
                forcePointEraser = true;
                double k = 1;
                switch (Settings.Canvas.EraserSize)
                {
                    case 0:
                        k = 0.5;
                        break;
                    case 1:
                        k = 0.8;
                        break;
                    case 3:
                        k = 1.25;
                        break;
                    case 4:
                        k = 1.8;
                        break;
                }
                inkCanvas.EraserShape = new EllipseStylusShape(k * 90, k * 90);
                inkCanvas.EditingMode = InkCanvasEditingMode.EraseByPoint;
                drawingShapeMode = 0;

                InkCanvas_EditingModeChanged(inkCanvas, null);
                CancelSingleFingerDragMode();

                HideSubPanels("eraser", false, true);
            }
        }

        private void BoardEraserIconByStrokes_Click(object sender, RoutedEventArgs e)
        {
            if (BoardEraserByStrokes.Opacity != 1)
            {
                AnimationsHelper.ShowWithSlideFromBottomAndFade(BoardDeleteIcon);
            }
            else
            {
                forceEraser = true;
                forcePointEraser = false;

                inkCanvas.EraserShape = new EllipseStylusShape(5, 5);
                inkCanvas.EditingMode = InkCanvasEditingMode.EraseByStroke;
                drawingShapeMode = 0;

                InkCanvas_EditingModeChanged(inkCanvas, null);
                CancelSingleFingerDragMode();

                HideSubPanels("eraserByStrokes", false, true);
            }
        }

        private void BoardSymbolIconDelete_Click(object sender, RoutedEventArgs e)
        {
            BoardPenIcon_Click(null, null);
            SymbolIconDelete_MouseUp(sender, e);
        }

        private void BoardLaunchEasiCamera_Click(object sender, RoutedEventArgs e)
        {
            ImageBlackboard_Click(null, null);
            SoftwareLauncher.LaunchEasiCamera("希沃视频展台");
        }

        private void BoardLaunchDesmos_Click(object sender, RoutedEventArgs e)
        {
            HideSubPanelsImmediately();
            ImageBlackboard_Click(null, null);
            Process.Start("https://www.desmos.com/calculator?lang=zh-CN");
        }

    }
}