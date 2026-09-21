using Ink_Canvas.Helpers;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Ink;
using System.Windows.Media;
using System.Threading.Tasks;
using Application = System.Windows.Application;
using System.Diagnostics;

namespace Ink_Canvas
{
    public partial class MainWindow : Window
    {
        private void BoardChangeBackgroundColorBtn_Click(object sender, RoutedEventArgs e)
        {
            // 如果长按已触发，则不执行点击操作
            if (_canvasBtnLongPressFired)
            {
                _canvasBtnLongPressFired = false;
                return;
            }

            if (!isLoaded || _isLoadingSettings) return;
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
            // 直接触发主题刷新，避免依赖 ComboBoxTheme（设置窗口未打开时为 null 导致闪退）
            SystemEvents_UserPreferenceChanged(null, null);
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
            // 刷新方格纸颜色
            RefreshGridPaper();
        }

        // 白板模式画笔按钮点击事件 - 独立处理，不影响浮动栏按钮
        private async void BoardPenIcon_Click(object sender, RoutedEventArgs e)
        {
            if (BoardPen.Opacity != 1)
            {
                // 再次点击“墨迹”时收起已展开的墨迹选项面板（与浮动栏“批注”按钮行为一致），
                // 否则面板一旦展开将无法在白板模式下收起
                if (BoardPenPalette.Visibility == Visibility.Visible)
                {
                    AnimationsHelper.HideWithSlideAndFade(BoardPenPalette);
                }
                else
                {
                    AnimationsHelper.ShowWithSlideFromBottomAndFade(BoardPenPalette);
                    // 首次展示时动画会重置 RenderTransform，等待动画结束后重新应用已保存的拖动偏移
                    await Task.Delay(300);
                    GetPenPaletteDragOffset(BoardPenPalette as FrameworkElement, out double bx, out double by);
                    if (bx != 0 || by != 0) ApplyPenPaletteDragOffset(BoardPenPalette as FrameworkElement, bx, by);
                }
            }
            else
            {
                // 切回画笔时自动关闭激光笔，避免“激光笔开启时切回笔迹书写”出现普通笔与激光笔叠加
                if (isLaserPointerEnabled) SetLaserPointerEnabled(false);
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
                // 切橡皮时自动关闭激光笔，否则激光笔仍在 Preview 阶段拦截输入并画出激光轨迹
                if (isLaserPointerEnabled) SetLaserPointerEnabled(false);
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
                // 切墨迹擦时自动关闭激光笔，避免仍残留激光轨迹
                if (isLaserPointerEnabled) SetLaserPointerEnabled(false);
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