using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using Ink_Canvas.Helpers;

namespace Ink_Canvas
{
    public partial class MainWindow : Window
    {
        private bool _isGridPaperEnabled = false;
        private double _gridSpacingX = 30.0; // 水平方格间距
        private double _gridSpacingY = 30.0; // 垂直方格间距

        // 触屏长按相关字段
        private DispatcherTimer _canvasBtnLongPressTimer;
        private bool _isCanvasBtnTouchActive = false;
        private bool _canvasBtnLongPressFired = false;
        private Point _canvasBtnTouchPoint;
        private const int LongPressDurationMs = 500; // 长按阈值（毫秒）

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
        /// 画布按钮触摸按下事件 - 启动长按计时器
        /// </summary>
        private void BoardCanvasBtn_PreviewTouchDown(object sender, TouchEventArgs e)
        {
            if (BoardGridPaperBorder == null) return;

            _isCanvasBtnTouchActive = true;
            _canvasBtnLongPressFired = false;
            _canvasBtnTouchPoint = e.GetTouchPoint(null).Position;

            // 创建并启动长按计时器
            _canvasBtnLongPressTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(LongPressDurationMs)
            };
            _canvasBtnLongPressTimer.Tick += CanvasBtnLongPressTimer_Tick;
            _canvasBtnLongPressTimer.Start();

            // 不设置 e.Handled，让短按可以正常触发 Click 事件
        }

        /// <summary>
        /// 画布按钮触摸抬起事件 - 取消长按计时器
        /// </summary>
        private void BoardCanvasBtn_PreviewTouchUp(object sender, TouchEventArgs e)
        {
            CancelCanvasBtnLongPress();
            // 不设置 e.Handled，让短按可以正常触发 Click 事件
        }

        /// <summary>
        /// 长按计时器触发 - 显示方格纸窗口
        /// </summary>
        private void CanvasBtnLongPressTimer_Tick(object sender, EventArgs e)
        {
            // 先停止计时器
            if (_canvasBtnLongPressTimer != null)
            {
                _canvasBtnLongPressTimer.Stop();
                _canvasBtnLongPressTimer.Tick -= CanvasBtnLongPressTimer_Tick;
                _canvasBtnLongPressTimer = null;
            }

            if (!_isCanvasBtnTouchActive || BoardGridPaperBorder == null) return;

            // 标记长按已触发，防止后续 Click 事件执行
            _canvasBtnLongPressFired = true;

            // 切换弹窗显示状态（与右键效果一致）
            if (BoardGridPaperBorder.Visibility == Visibility.Visible)
            {
                AnimationsHelper.HideWithSlideAndFade(BoardGridPaperBorder);
            }
            else
            {
                AnimationsHelper.ShowWithSlideFromBottomAndFade(BoardGridPaperBorder);
            }
        }

        /// <summary>
        /// 取消长按计时器
        /// </summary>
        private void CancelCanvasBtnLongPress()
        {
            _isCanvasBtnTouchActive = false;
            if (_canvasBtnLongPressTimer != null)
            {
                _canvasBtnLongPressTimer.Stop();
                _canvasBtnLongPressTimer.Tick -= CanvasBtnLongPressTimer_Tick;
                _canvasBtnLongPressTimer = null;
            }
        }

        /// <summary>
        /// 检查长按是否已触发（供 Click 事件使用）
        /// </summary>
        private bool IsCanvasBtnLongPressFired()
        {
            return _canvasBtnLongPressFired;
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

            if (_isGridPaperEnabled)
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

            // 获取画布尺寸
            double width = GridPaperCanvas.ActualWidth;
            double height = GridPaperCanvas.ActualHeight;

            // 如果布局尚未完成，等待尺寸可用后再绘制
            if (width <= 0 || height <= 0)
            {
                // 订阅 SizeChanged 事件，在布局完成后绘制
                GridPaperCanvas.SizeChanged -= GridPaperCanvas_SizeChanged;
                GridPaperCanvas.SizeChanged += GridPaperCanvas_SizeChanged;
                return;
            }

            // 取消订阅（如果之前订阅过）
            GridPaperCanvas.SizeChanged -= GridPaperCanvas_SizeChanged;

            GridPaperCanvas.Children.Clear();

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
        /// 方格纸 Canvas 尺寸改变事件 - 在布局完成后绘制
        /// </summary>
        private void GridPaperCanvas_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            if (_isGridPaperEnabled && GridPaperCanvas != null)
            {
                DrawGridPaper();
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
