using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using Ink_Canvas.Helpers;

namespace Ink_Canvas
{
    public partial class MainWindow : Window
    {
        private bool _isGridPaperEnabled = false;
        private double _gridSpacingX = 30.0; // 水平方格间距
        private double _gridSpacingY = 30.0; // 垂直方格间距

        /// <summary>
        /// 画布按钮右键按下事件 - 显示方格纸开关弹窗
        /// </summary>
        private void BoardCanvasBtn_PreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (BoardGridPaperBorder == null) return;

            // 切换弹窗显示状态
            if (BoardGridPaperBorder.Visibility == Visibility.Visible)
            {
                AnimationsHelper.HideWithSlideAndFade(BoardGridPaperBorder);
            }
            else
            {
                AnimationsHelper.ShowWithSlideFromBottomAndFade(BoardGridPaperBorder);
            }

            e.Handled = true; // 阻止事件继续传播，避免触发其他事件
        }

        /// <summary>
        /// 方格纸开关切换事件
        /// </summary>
        private void ToggleSwitchGridPaper_Toggled(object sender, RoutedEventArgs e)
        {
            if (!isLoaded || _isLoadingSettings) return;

            _isGridPaperEnabled = BoardToggleSwitchGridPaper.IsOn;
            UpdateGridPaperVisibility();
        }

        /// <summary>
        /// 水平间距滑动条值改变事件
        /// </summary>
        private void GridSpacingXSlider_ValueChanged(object sender, System.Windows.RoutedPropertyChangedEventArgs<double> e)
        {
            if (!isLoaded || _isLoadingSettings) return;

            _gridSpacingX = BoardGridSpacingXSlider.Value;
            DrawGridPaper();
        }

        /// <summary>
        /// 垂直间距滑动条值改变事件
        /// </summary>
        private void GridSpacingYSlider_ValueChanged(object sender, System.Windows.RoutedPropertyChangedEventArgs<double> e)
        {
            if (!isLoaded || _isLoadingSettings) return;

            _gridSpacingY = BoardGridSpacingYSlider.Value;
            DrawGridPaper();
        }

        /// <summary>
        /// 更新方格纸显示状态
        /// </summary>
        private void UpdateGridPaperVisibility()
        {
            if (GridPaperCanvas == null) return;

            if (_isGridPaperEnabled && GridBackgroundCoverHolder.Visibility == Visibility.Visible)
            {
                GridPaperCanvas.Visibility = Visibility.Visible;
                DrawGridPaper();
            }
            else
            {
                GridPaperCanvas.Visibility = Visibility.Collapsed;
            }
        }

        /// <summary>
        /// 绘制方格纸
        /// </summary>
        private void DrawGridPaper()
        {
            if (GridPaperCanvas == null) return;

            GridPaperCanvas.Children.Clear();

            // 获取画布尺寸
            double width = GridPaperCanvas.ActualWidth;
            double height = GridPaperCanvas.ActualHeight;

            if (width <= 0 || height <= 0) return;

            // 根据当前背景色决定方格颜色（黑底白格，白底黑格）
            bool isWhiteboard = Settings.Canvas.UsingWhiteboard;
            Color gridColor = isWhiteboard ? Colors.Black : Colors.White;
            byte gridOpacity = isWhiteboard ? (byte)40 : (byte)50; // 半透明效果

            SolidColorBrush gridBrush = new SolidColorBrush(Color.FromArgb(gridOpacity, gridColor.R, gridColor.G, gridColor.B));

            // 绘制垂直线
            for (double x = 0; x <= width; x += _gridSpacingX)
            {
                Line verticalLine = new Line
                {
                    X1 = x,
                    Y1 = 0,
                    X2 = x,
                    Y2 = height,
                    Stroke = gridBrush,
                    StrokeThickness = 0.5
                };
                GridPaperCanvas.Children.Add(verticalLine);
            }

            // 绘制水平线
            for (double y = 0; y <= height; y += _gridSpacingY)
            {
                Line horizontalLine = new Line
                {
                    X1 = 0,
                    Y1 = y,
                    X2 = width,
                    Y2 = y,
                    Stroke = gridBrush,
                    StrokeThickness = 0.5
                };
                GridPaperCanvas.Children.Add(horizontalLine);
            }
        }

        /// <summary>
        /// 刷新方格纸（当背景色改变时调用）
        /// </summary>
        public void RefreshGridPaper()
        {
            if (_isGridPaperEnabled && GridPaperCanvas != null && GridPaperCanvas.Visibility == Visibility.Visible)
            {
                DrawGridPaper();
            }
        }
    }
}
