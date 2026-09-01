using Ink_Canvas.Helpers;
using System;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Ink;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using System.Windows.Interop;
using System.Windows.Media.Imaging;
using iNKORE.UI.WPF.Modern;
using System.Threading;
using Application = System.Windows.Application;
using Point = System.Windows.Point;
using System.Diagnostics;
using iNKORE.UI.WPF.Modern.Controls;
using System.Collections.Generic;

namespace Ink_Canvas
{
    public partial class MainWindow : Window
    {
        #region TwoFingZoomBtn

        private void TwoFingerGestureBorder_Click(object sender, RoutedEventArgs e)
        {
            if (TwoFingerGestureBorder.Visibility == Visibility.Visible)
            {
                AnimationsHelper.HideWithSlideAndFade(TwoFingerGestureBorder);
                AnimationsHelper.HideWithSlideAndFade(BoardTwoFingerGestureBorder);
            }
            else
            {
                AnimationsHelper.ShowWithSlideFromBottomAndFade(TwoFingerGestureBorder);
                AnimationsHelper.ShowWithSlideFromBottomAndFade(BoardTwoFingerGestureBorder);
            }
        }

        private void CheckEnableTwoFingerGestureBtnColorPrompt()
        {
            if (ToggleSwitchEnableMultiTouchMode.IsOn)
            {
                TwoFingerGestureSimpleStackPanel.Opacity = 0.5;
                EnableTwoFingerGestureBtn.Opacity = 0.5;
            }
            else
            {
                TwoFingerGestureSimpleStackPanel.Opacity = 1;
                if (Settings.Gesture.IsEnableTwoFingerGesture)
                {
                    EnableTwoFingerGestureBtn.Opacity = 1;
                }
                else
                {
                    EnableTwoFingerGestureBtn.Opacity = 0.5;
                }
            }
        }

        private void CheckEnableTwoFingerGestureBtnVisibility(bool isVisible)
        {
            if (StackPanelCanvasControls.Visibility != Visibility.Visible
                || BorderFloatingBarMainControls.Visibility != Visibility.Visible)
            {
                EnableTwoFingerGestureBorder.Visibility = Visibility.Collapsed;
            }
            else if (isVisible == true)
            {
                if (BtnPPTSlideShowEnd.Visibility == Visibility.Visible) EnableTwoFingerGestureBorder.Visibility = Visibility.Collapsed;
                else EnableTwoFingerGestureBorder.Visibility = Visibility.Visible;
            }
            else EnableTwoFingerGestureBorder.Visibility = Visibility.Collapsed;
        }

        #endregion TwoFingZoomBtn

        #region Drag

        bool isDragDropInEffect = false;
        Point pos = new Point();
        Point downPos = new Point();
        Point pointDesktop = new Point(-1, -1); //用于记录上次在桌面时的坐标（用户拖动后会锁定在此）
        Point pointPPT = new Point(-1, -1); //用于记录上次在PPT中的坐标
        bool _floatingBarPositionLoadedFromSettings = false;

        /// <summary>用户手动拖动结束后，锁定并持久化浮动栏的桌面位置。</summary>
        private void PersistFloatingBarDesktopPosition()
        {
            try
            {
                if (BtnPPTSlideShowEnd.Visibility == Visibility.Visible) return; // PPT 中的位置不覆盖桌面锁定
                if (pointDesktop.X < 0 || pointDesktop.Y < 0) return; // 未实际移动过则不锁定
                Settings.Appearance.FloatingBarPositionLockedX = pointDesktop.X;
                Settings.Appearance.FloatingBarPositionLockedY = pointDesktop.Y;
                SaveSettingsToFile();
            }
            catch { }
        }

        void SymbolIconEmoji_MouseMove(object sender, MouseEventArgs e)
        {
            if (isDragDropInEffect)
            {
                double xPos = e.GetPosition(null).X - pos.X + ViewboxFloatingBar.Margin.Left;
                double yPos = e.GetPosition(null).Y - pos.Y + ViewboxFloatingBar.Margin.Top;
                ViewboxFloatingBar.Margin = new Thickness(xPos, yPos, -2000, -200);

                pos = e.GetPosition(null);
                if (BtnPPTSlideShowEnd.Visibility == Visibility.Visible)
                {
                    pointPPT = new Point(xPos, yPos);
                }
                else
                {
                    pointDesktop = new Point(xPos, yPos);
                }
            }
        }

        void GridForFloatingBarDraging_TouchMove(object sender, TouchEventArgs e)
        {
            if (!_emojiBtnTouchMoved) return;

            var currentPos = e.GetTouchPoint(null).Position;
            double deltaX = currentPos.X - _emojiBtnTouchLastPos.X;
            double deltaY = currentPos.Y - _emojiBtnTouchLastPos.Y;
            double xPos = ViewboxFloatingBar.Margin.Left + deltaX;
            double yPos = ViewboxFloatingBar.Margin.Top + deltaY;
            ViewboxFloatingBar.Margin = new Thickness(xPos, yPos, -2000, -200);

            _emojiBtnTouchLastPos = currentPos;

            if (BtnPPTSlideShowEnd.Visibility == Visibility.Visible)
            {
                pointPPT = new Point(xPos, yPos);
            }
            else
            {
                pointDesktop = new Point(xPos, yPos);
            }

            e.Handled = true;
        }

        void GridForFloatingBarDraging_TouchUp(object sender, TouchEventArgs e)
        {
            if (!_emojiBtnTouchMoved) return;

            _emojiBtnTouchMoved = false;
            _isEmojiBtnTouchActive = false;
            _emojiBtnTouchHandled = true;

            // 释放触摸捕获
            e.TouchDevice.Capture(null);

            // 拖动结束：锁定并持久化桌面位置
            PersistFloatingBarDesktopPosition();

            // 隐藏快捷按钮窗口
            if (BorderQuickActions != null && BorderQuickActions.Visibility == Visibility.Visible)
            {
                AnimationsHelper.HideWithSlideAndFade(BorderQuickActions);
            }

            // 恢复拖动效果
            GridForFloatingBarDraging.Visibility = Visibility.Collapsed;
            SymbolIconEmoji1.Width = 28;
            SymbolIconEmoji2.Width = 0;
            Topmost = true;

            e.Handled = true;
        }

        void SymbolIconEmoji_MouseDown(object sender, MouseButtonEventArgs e)
        {
            // 只响应左键，右键由 PreviewMouseRightButtonDown 处理
            if (e.ChangedButton != MouseButton.Left) return;

            // 如果当前正在处理触屏操作，则忽略提升的鼠标事件
            if (_isEmojiBtnTouchActive) return;

            // 隐藏快捷按钮窗口
            if (BorderQuickActions != null && BorderQuickActions.Visibility == Visibility.Visible)
            {
                AnimationsHelper.HideWithSlideAndFade(BorderQuickActions);
            }

            if (isViewboxFloatingBarMarginAnimationRunning)
            {
                ViewboxFloatingBar.BeginAnimation(FrameworkElement.MarginProperty, null);
                isViewboxFloatingBarMarginAnimationRunning = false;
            }
            isDragDropInEffect = true;
            pos = e.GetPosition(null);
            downPos = e.GetPosition(null);
            // 确保拖拽开始时窗口保持在最顶层
            Topmost = true;
            GridForFloatingBarDraging.Visibility = Visibility.Visible;
            SymbolIconEmoji1.Width = 0;
            SymbolIconEmoji2.Width = 28;
        }

        void SymbolIconEmoji_MouseUp(object sender, MouseButtonEventArgs e)
        {
            // 只响应左键，右键由 PreviewMouseRightButtonDown 处理
            if (e.ChangedButton != MouseButton.Left) return;

            // 如果该鼠标事件来自触屏提升，则忽略
            if (_emojiBtnTouchHandled)
            {
                _emojiBtnTouchHandled = false;
                isDragDropInEffect = false;
                return;
            }

            isDragDropInEffect = false;

            // 拖动结束：锁定并持久化桌面位置
            PersistFloatingBarDesktopPosition();

            // 如果长按已触发，则不执行点击操作
            if (_emojiBtnLongPressFired)
            {
                _emojiBtnLongPressFired = false;
                return;
            }

            // 隐藏快捷按钮窗口
            if (BorderQuickActions != null && BorderQuickActions.Visibility == Visibility.Visible)
            {
                AnimationsHelper.HideWithSlideAndFade(BorderQuickActions);
            }

            if (e is null || Math.Abs(downPos.X - e.GetPosition(null).X) <= 10 && Math.Abs(downPos.Y - e.GetPosition(null).Y) <= 10)
            {
                if (BorderFloatingBarMainControls.Visibility == Visibility.Visible)
                {
                    BorderFloatingBarMainControls.Visibility = Visibility.Collapsed;
                    CheckEnableTwoFingerGestureBtnVisibility(false);
                }
                else
                {
                    BorderFloatingBarMainControls.Visibility = Visibility.Visible;
                    CheckEnableTwoFingerGestureBtnVisibility(true);
                }
            }

            GridForFloatingBarDraging.Visibility = Visibility.Collapsed;
            SymbolIconEmoji1.Width = 28;
            SymbolIconEmoji2.Width = 0;
            // 确保拖拽结束后窗口仍然保持在最顶层
            Topmost = true;
        }

        // 笑脸按钮右键/长按快捷菜单相关字段
        private DispatcherTimer _emojiBtnLongPressTimer;
        private bool _isEmojiBtnTouchActive = false;
        private bool _emojiBtnLongPressFired = false;
        private bool _emojiBtnTouchMoved = false;
        private bool _emojiBtnTouchHandled = false;
        private Point _emojiBtnTouchDownPos;
        private Point _emojiBtnTouchLastPos;
        private const int EmojiBtnLongPressDurationMs = 800;
        private const int EmojiBtnTouchMoveThreshold = 10;

        /// <summary>
        /// 笑脸按钮右键按下事件 - 显示快捷菜单
        /// </summary>
        void SymbolIconEmoji_PreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (BorderQuickActions == null) return;

            // 切换弹窗显示状态
            if (BorderQuickActions.Visibility == Visibility.Visible)
            {
                AnimationsHelper.HideWithSlideAndFade(BorderQuickActions);
            }
            else
            {
                AnimationsHelper.ShowWithSlideFromBottomAndFade(BorderQuickActions);
            }

            e.Handled = true;
        }

        /// <summary>
        /// 笑脸按钮触摸按下事件 - 启动长按计时器
        /// </summary>
        void SymbolIconEmoji_PreviewTouchDown(object sender, TouchEventArgs e)
        {
            if (BorderQuickActions == null) return;

            _isEmojiBtnTouchActive = true;
            _emojiBtnLongPressFired = false;
            _emojiBtnTouchMoved = false;
            _emojiBtnTouchHandled = false;
            // 使用 null 获取屏幕坐标，避免相对元素变化导致坐标跳变
            _emojiBtnTouchDownPos = e.GetTouchPoint(null).Position;
            _emojiBtnTouchLastPos = _emojiBtnTouchDownPos;

            // 捕获触摸设备到按钮元素，确保手指移出按钮后仍能接收触摸事件
            if (sender is IInputElement inputElement)
            {
                e.TouchDevice.Capture(inputElement);
            }

            // 隐藏快捷按钮窗口
            if (BorderQuickActions.Visibility == Visibility.Visible)
            {
                AnimationsHelper.HideWithSlideAndFade(BorderQuickActions);
            }

            // 创建并启动长按计时器
            _emojiBtnLongPressTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(EmojiBtnLongPressDurationMs)
            };
            _emojiBtnLongPressTimer.Tick += EmojiBtnLongPressTimer_Tick;
            _emojiBtnLongPressTimer.Start();
        }

        /// <summary>
        /// 笑脸按钮触摸移动事件 - 检测拖动并取消长按
        /// </summary>
        void SymbolIconEmoji_PreviewTouchMove(object sender, TouchEventArgs e)
        {
            if (!_isEmojiBtnTouchActive || _emojiBtnLongPressFired) return;

            // 使用屏幕坐标，与 TouchDown 一致
            var currentPos = e.GetTouchPoint(null).Position;

            // 第一次移动超过阈值时，启动拖动
            if (!_emojiBtnTouchMoved)
            {
                if (Math.Abs(currentPos.X - _emojiBtnTouchDownPos.X) > EmojiBtnTouchMoveThreshold ||
                    Math.Abs(currentPos.Y - _emojiBtnTouchDownPos.Y) > EmojiBtnTouchMoveThreshold)
                {
                    _emojiBtnTouchMoved = true;
                    CancelEmojiBtnLongPress();

                    // 隐藏快捷按钮窗口
                    if (BorderQuickActions != null && BorderQuickActions.Visibility == Visibility.Visible)
                    {
                        AnimationsHelper.HideWithSlideAndFade(BorderQuickActions);
                    }

                    // 停止可能正在进行的边距动画
                    if (isViewboxFloatingBarMarginAnimationRunning)
                    {
                        ViewboxFloatingBar.BeginAnimation(FrameworkElement.MarginProperty, null);
                        isViewboxFloatingBarMarginAnimationRunning = false;
                    }

                    // 开始拖动效果
                    Topmost = true;
                    GridForFloatingBarDraging.Visibility = Visibility.Visible;
                    SymbolIconEmoji1.Width = 0;
                    SymbolIconEmoji2.Width = 28;

                    // 将触摸捕获转移到全屏拖拽网格，确保后续触摸事件由它处理
                    e.TouchDevice.Capture(GridForFloatingBarDraging);
                    e.Handled = true;
                    return;
                }
                else
                {
                    return;
                }
            }
        }

        /// <summary>
        /// 笑脸按钮触摸抬起事件 - 取消长按计时器
        /// </summary>
        void SymbolIconEmoji_PreviewTouchUp(object sender, TouchEventArgs e)
        {
            _emojiBtnTouchHandled = true;

            // 释放触摸捕获
            e.TouchDevice.Capture(null);

            // 如果长按已触发，则只重置标志
            if (_emojiBtnLongPressFired)
            {
                _emojiBtnLongPressFired = false;
                CancelEmojiBtnLongPress();
                return;
            }

            // 如果发生了拖动，则不执行单点操作
            if (_emojiBtnTouchMoved)
            {
                _emojiBtnTouchMoved = false;
                CancelEmojiBtnLongPress();

                // 隐藏快捷按钮窗口
                if (BorderQuickActions != null && BorderQuickActions.Visibility == Visibility.Visible)
                {
                    AnimationsHelper.HideWithSlideAndFade(BorderQuickActions);
                }

                // 恢复拖动效果
                GridForFloatingBarDraging.Visibility = Visibility.Collapsed;
                SymbolIconEmoji1.Width = 28;
                SymbolIconEmoji2.Width = 0;
                Topmost = true;
                return;
            }

            // 短按：执行折叠/展开操作
            CancelEmojiBtnLongPress();

            // 隐藏快捷按钮窗口
            if (BorderQuickActions != null && BorderQuickActions.Visibility == Visibility.Visible)
            {
                AnimationsHelper.HideWithSlideAndFade(BorderQuickActions);
            }
            if (BorderFloatingBarMainControls.Visibility == Visibility.Visible)
            {
                BorderFloatingBarMainControls.Visibility = Visibility.Collapsed;
                CheckEnableTwoFingerGestureBtnVisibility(false);
            }
            else
            {
                BorderFloatingBarMainControls.Visibility = Visibility.Visible;
                CheckEnableTwoFingerGestureBtnVisibility(true);
            }
        }

        /// <summary>
        /// 长按计时器触发 - 显示快捷菜单
        /// </summary>
        private void EmojiBtnLongPressTimer_Tick(object sender, EventArgs e)
        {
            // 先停止计时器
            if (_emojiBtnLongPressTimer != null)
            {
                _emojiBtnLongPressTimer.Stop();
                _emojiBtnLongPressTimer.Tick -= EmojiBtnLongPressTimer_Tick;
                _emojiBtnLongPressTimer = null;
            }

            // 如果手指已经移动（正在拖动），则不触发长按
            if (!_isEmojiBtnTouchActive || _emojiBtnTouchMoved || BorderQuickActions == null) return;

            // 标记长按已触发
            _emojiBtnLongPressFired = true;

            // 切换弹窗显示状态
            if (BorderQuickActions.Visibility == Visibility.Visible)
            {
                AnimationsHelper.HideWithSlideAndFade(BorderQuickActions);
            }
            else
            {
                AnimationsHelper.ShowWithSlideFromBottomAndFade(BorderQuickActions);
            }
        }

        /// <summary>
        /// 取消长按计时器
        /// </summary>
        private void CancelEmojiBtnLongPress()
        {
            _isEmojiBtnTouchActive = false;
            if (_emojiBtnLongPressTimer != null)
            {
                _emojiBtnLongPressTimer.Stop();
                _emojiBtnLongPressTimer.Tick -= EmojiBtnLongPressTimer_Tick;
                _emojiBtnLongPressTimer = null;
            }
        }

        #endregion

        private void HideSubPanelsImmediately()
        {
            BorderTools.Visibility = Visibility.Collapsed;
            BorderQuickActions.Visibility = Visibility.Collapsed;
            BoardBorderTools.Visibility = Visibility.Collapsed;
            PenPalette.Visibility = Visibility.Collapsed;
            BoardPenPalette.Visibility = Visibility.Collapsed;
            BoardDeleteIcon.Visibility = Visibility.Collapsed;
            BoardGridPaperBorder.Visibility = Visibility.Collapsed;
        }

        private async void HideSubPanels(String mode = null, bool autoAlignCenter = false, bool isFromBoard = false)
        {
            AnimationsHelper.HideWithSlideAndFade(BorderTools);
            AnimationsHelper.HideWithSlideAndFade(BorderQuickActions);
            AnimationsHelper.HideWithSlideAndFade(BoardBorderTools);
            AnimationsHelper.HideWithSlideAndFade(PenPalette);
            AnimationsHelper.HideWithSlideAndFade(BoardPenPalette);
            AnimationsHelper.HideWithSlideAndFade(BoardDeleteIcon);
            AnimationsHelper.HideWithSlideAndFade(TwoFingerGestureBorder);
            AnimationsHelper.HideWithSlideAndFade(BoardTwoFingerGestureBorder);
            AnimationsHelper.HideWithSlideAndFade(BoardGridPaperBorder);
            if (ToggleSwitchDrawShapeBorderAutoHide.IsOn)
            {
                AnimationsHelper.HideWithSlideAndFade(BorderDrawShape);
                AnimationsHelper.HideWithSlideAndFade(BoardBorderDrawShape);
            }

            if (mode != null)
            {
                if (mode != "clear")
                {
                    // 如果不是来自白板的调用，才更新浮动栏按钮状态
                    if (!isFromBoard)
                    {
                        Pen_Icon.Background = null;
                        Eraser_Icon.Background = null;
                        SymbolIconSelect.Background = null;
                        EraserByStrokes_Icon.Background = null;
                    }
                    // 如果不是来自浮动栏的调用，才更新白板按钮状态
                    BoardPen.Background = (Brush)Application.Current.FindResource("BoardBarBackground");
                    BoardPen.Opacity = 1;
                    BoardEraser.Background = (Brush)Application.Current.FindResource("BoardBarBackground");
                    BoardEraser.Opacity = 1;
                    BoardSelect.Background = (Brush)Application.Current.FindResource("BoardBarBackground");
                    BoardSelect.Opacity = 1;
                    BoardEraserByStrokes.Background = (Brush)Application.Current.FindResource("BoardBarBackground");
                    BoardEraserByStrokes.Opacity = 1;
                }
                if (mode == "pen" || mode == "color")
                {
                    // 如果不是来自白板的调用，才更新浮动栏按钮状态
                    if (!isFromBoard)
                    {
                        Pen_Icon.Background = new ImageBrush(new BitmapImage(new Uri("pack://application:,,,/Resources/Icons-png/check-box-background.png"))) { Opacity = 0.5 };
                    }
                    BoardPen.Background = new ImageBrush(new BitmapImage(new Uri("pack://application:,,,/Resources/Icons-png/check-box-background.png"))) { Opacity = 0.5 };
                    BoardPen.Opacity = 0.99;
                }
                else
                {
                    if (mode == "eraser")
                    {
                        // 如果不是来自白板的调用，才更新浮动栏按钮状态
                        if (!isFromBoard)
                        {
                            Eraser_Icon.Background = new ImageBrush(new BitmapImage(new Uri("pack://application:,,,/Resources/Icons-png/check-box-background.png"))) { Opacity = 0.5 };
                        }
                        BoardEraser.Background = new ImageBrush(new BitmapImage(new Uri("pack://application:,,,/Resources/Icons-png/check-box-background.png"))) { Opacity = 0.5 };
                        BoardEraser.Opacity = 0.99;
                    }
                    else if (mode == "eraserByStrokes")
                    {
                        // 如果不是来自白板的调用，才更新浮动栏按钮状态
                        if (!isFromBoard)
                        {
                            EraserByStrokes_Icon.Background = new ImageBrush(new BitmapImage(new Uri("pack://application:,,,/Resources/Icons-png/check-box-background.png"))) { Opacity = 0.5 };
                        }
                        BoardEraserByStrokes.Background = new ImageBrush(new BitmapImage(new Uri("pack://application:,,,/Resources/Icons-png/check-box-background.png"))) { Opacity = 0.5 };
                        BoardEraserByStrokes.Opacity = 0.99;
                    }
                    else if (mode == "select")
                    {
                        BoardSelect.Background = new ImageBrush(new BitmapImage(new Uri("pack://application:,,,/Resources/Icons-png/check-box-background.png"))) { Opacity = 0.5 };
                        // 如果不是来自白板的调用，才更新浮动栏按钮状态
                        if (!isFromBoard)
                        {
                            SymbolIconSelect.Background = new ImageBrush(new BitmapImage(new Uri("pack://application:,,,/Resources/Icons-png/check-box-background.png"))) { Opacity = 0.5 };
                            SymbolIconSelect.Opacity = 0.99;
                        }
                    }
                }

                if (autoAlignCenter) // 控制居中
                {
                    if (BtnPPTSlideShowEnd.Visibility == Visibility.Visible)
                    {
                        await Task.Delay(50);
                        ViewboxFloatingBarMarginAnimation();
                    }
                    else if (Topmost == true) //非黑板
                    {
                        await Task.Delay(50);
                        ViewboxFloatingBarMarginAnimation();
                    }
                    else //黑板
                    {
                        await Task.Delay(50);
                        ViewboxFloatingBarMarginAnimation();
                    }
                }
            }
            await Task.Delay(150);
            isHidingSubPanelsWhenInking = false;
        }

        private void SymbolIconUndo_Click(object sender, RoutedEventArgs e)
        {
            if (!Icon_Undo.IsEnabled) return;
            BtnUndo_Click(null, null);
            HideSubPanels();
        }

        private void SymbolIconRedo_Click(object sender, RoutedEventArgs e)
        {
            if (!Icon_Redo.IsEnabled) return;
            BtnRedo_Click(null, null);
            HideSubPanels();
        }

        private async void SymbolIconCursor_Click(object sender, RoutedEventArgs e)
        {
            if (currentMode != 0)
            {
                ImageBlackboard_Click(null, null);
            }
            else
            {
                BtnHideInkCanvas_Click(null, null);

                if (BtnPPTSlideShowEnd.Visibility == Visibility.Visible)
                {
                    await Task.Delay(100);
                    ViewboxFloatingBarMarginAnimation();
                }
            }
        }

        private void SymbolIconDelete_MouseUp(object sender, RoutedEventArgs e)
        {
            var selectedStrokes = inkCanvas.GetSelectedStrokes();
            var selectedElements = new List<UIElement>(inkCanvas.GetSelectedElements());
            if (selectedStrokes.Count > 0 || selectedElements.Count > 0)
            {
                inkCanvas.Strokes.Remove(inkCanvas.GetSelectedStrokes());
                foreach(UIElement element in selectedElements)
                {
                    inkCanvas.Children.Remove(element);
                    timeMachine.CommitElementInsertHistory(element, true);
                }
                GridInkCanvasSelectionCover.Visibility = Visibility.Collapsed;
            }
            else if (inkCanvas.Strokes.Count > 0 || inkCanvas.Children.Count > 0)
            {
                if (Settings.Automation.IsAutoSaveStrokesAtClear && inkCanvas.Strokes.Count > Settings.Automation.MinimumAutomationStrokeNumber)
                {
                    if (BtnPPTSlideShowEnd.Visibility == Visibility.Visible)
                        SavePPTScreenshot($"{pptName}/{previousSlideID}_{DateTime.Now:HH-mm-ss}");
                    else
                        SaveScreenshot(true);
                }
                
                // 在白板模式下，保留照片和摄像头画面元素
                if (currentMode == 1) // 白板模式
                {
                    // 保存需要保留的元素
                    var elementsToKeep = new List<UIElement>();
                    
                    foreach (UIElement element in inkCanvas.Children)
                    {
                        if (element is System.Windows.Controls.Image image)
                        {
                            // 保留照片元素（名称以"photo_"开头）
                            if (image.Name != null && image.Name.StartsWith("photo_"))
                            {
                                elementsToKeep.Add(element);
                            }
                            // 保留摄像头画面元素（名称以"camera_"开头）
                            else if (image.Name != null && image.Name.StartsWith("camera_"))
                            {
                                elementsToKeep.Add(element);
                            }
                        }
                    }
                    
                    // 清除所有笔迹
                    inkCanvas.Strokes.Clear();
                    
                    // 清除所有子元素
                    inkCanvas.Children.Clear();
                    
                    // 重新添加需要保留的元素
                    foreach (var element in elementsToKeep)
                    {
                        inkCanvas.Children.Add(element);
                    }
                    
                    CancelSingleFingerDragMode();
                }
                else
                {
                    // 非白板模式下使用原来的清除逻辑
                    BtnClear_Click(null, null);
                }
            }
        }

        private void SymbolIconSettings_Click(object sender, RoutedEventArgs e)
        {
            HideSubPanels();
            BtnSettings_Click(null, null);
        }

        private void SymbolIconSelect_Click(object sender, RoutedEventArgs e)
        {
            BtnSelect_Click(null, null);
            HideSubPanels("select");
        }

        private async void SymbolIconScreenshot_Click(object sender, RoutedEventArgs e)
        {
            HideSubPanelsImmediately();
            await Task.Delay(50);
            await CaptureScreenshotAndInsert();
        }

        bool isDisplayingOrHidingBlackboard = false;
        private void ImageBlackboard_Click(object sender, RoutedEventArgs e)
        {
            if (isDisplayingOrHidingBlackboard) return;
            isDisplayingOrHidingBlackboard = true;

            try
            {
            // 切换模式前先排空残留输入并开启过渡窗口，再取消进行中的笔画：
            // 排空让“卡顿残留”的延迟笔画先在旧画布落地并计入旧历史，
            // 过渡窗口兜底拦截仍可能延迟到达的笔迹，防止其混入/替换白板笔迹。
            BeginBoardModeSwitch();
            CancelInProgressStroke();

            if (inkCanvas.EditingMode == InkCanvasEditingMode.Select) PenIcon_Click(null, null);

            if (VideoPresenterSidebar != null && VideoPresenterSidebar.Visibility == Visibility.Visible
                && (currentMode != 0 || !shouldLaunchIntoVideoPresenterMode))
            {
                VideoPresenterSidebar.Visibility = Visibility.Collapsed;
            }

            if (currentMode == 0)
            {
                currentMode = 1;
                //进入画板
                PPTNavigationBottomLeft.Visibility = Visibility.Collapsed;
                PPTNavigationBottomRight.Visibility = Visibility.Collapsed;
                PPTNavigationSidesLeft.Visibility = Visibility.Collapsed;
                PPTNavigationSidesRight.Visibility = Visibility.Collapsed;

                // 进入白板模式时直接隐藏浮动栏
                ViewboxFloatingBar.Visibility = Visibility.Collapsed;

                // 进入白板模式时，只更新白板按钮状态，不更新浮动栏按钮
                if (BoardPen.Opacity == 1)
                {
                    BoardPenIcon_Click(null, null);
                }

                if (Settings.Gesture.AutoSwitchTwoFingerGesture) // 画板模式：启用双指平移和缩放，禁用多指书写
                {
                    ToggleSwitchEnableTwoFingerTranslate.IsOn = true;
                    ToggleSwitchEnableTwoFingerZoom.IsOn = true;
                    ToggleSwitchEnableMultiTouchMode.IsOn = false;
                    isInMultiTouchMode = false;
                }

                // 进入白板时，如果当前页面存在摄像头内容，通知摄像头管理器恢复播放
                try
                {
                    var cameraDeviceManagerField = typeof(MainWindow).GetField("cameraDeviceManager", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                    var cameraDeviceManager = cameraDeviceManagerField?.GetValue(this);
                    var handlePageChangedMethod = cameraDeviceManager?.GetType().GetMethod("HandlePageChanged", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
                    handlePageChangedMethod?.Invoke(cameraDeviceManager, new object[] { CurrentWhiteboardIndex });
                }
                catch (Exception ex)
                {
                    Helpers.LogHelper.WriteLogToFile($"进入白板时恢复摄像头失败: {ex.Message}");
                }
            }
            else
            {
                currentMode = 0;
                // 退出白板模式时，自动停止摄像头调用以节省资源
                NotifyCameraManagerExitButtonClicked();
                //退出画板
                HideSubPanelsImmediately();

                if (BtnPPTSlideShowEnd.Visibility == Visibility.Visible)
                {
                    if (Settings.PowerPointSettings.IsShowBottomPPTNavigationPanel)
                    {
                        AnimationsHelper.ShowWithScaleFromBottom(PPTNavigationBottomLeft);
                        AnimationsHelper.ShowWithScaleFromBottom(PPTNavigationBottomRight);
                    }
                    if (Settings.PowerPointSettings.IsShowSidePPTNavigationPanel)
                    {
                        AnimationsHelper.ShowWithScaleFromLeft(PPTNavigationSidesLeft);
                        AnimationsHelper.ShowWithScaleFromRight(PPTNavigationSidesRight);
                    }
                }

                if (Settings.Automation.IsAutoSaveStrokesAtClear && inkCanvas.Strokes.Count > Settings.Automation.MinimumAutomationStrokeNumber)
                {
                    SaveScreenshot(true);
                }

                if (BtnPPTSlideShowEnd.Visibility == Visibility.Collapsed)
                {
                    new Thread(new ThreadStart(() =>
                    {
                        Thread.Sleep(100);
                        Application.Current.Dispatcher.Invoke(() =>
                        {
                            ViewboxFloatingBarMarginAnimation();
                        });
                    })).Start();
                }
                else
                {
                    new Thread(new ThreadStart(() =>
                    {
                        Thread.Sleep(100);
                        Application.Current.Dispatcher.Invoke(() =>
                        {
                            ViewboxFloatingBarMarginAnimation();
                        });
                    })).Start();
                }
                if (Pen_Icon.Background == null)
                {
                    PenIcon_Click(null, null);
                }

                if (Settings.Gesture.AutoSwitchTwoFingerGesture) // 幻灯片模式：禁用所有手势，除了选中元素双指旋转
                {
                    ToggleSwitchEnableTwoFingerTranslate.IsOn = false;
                    ToggleSwitchEnableTwoFingerZoom.IsOn = false;
                    ToggleSwitchEnableTwoFingerRotation.IsOn = false;
                    ToggleSwitchEnableMultiTouchMode.IsOn = false;
                    if (ToggleSwitchEnableTwoFingerRotationOnSelection != null)
                        ToggleSwitchEnableTwoFingerRotationOnSelection.IsOn = true;
                    Settings.Gesture.IsEnableTwoFingerRotationOnSelection = true;
                    isInMultiTouchMode = false;
                }
            }

            // 内联模式切换逻辑，正确处理照片的保存与恢复
            // （原 BtnSwitch_Click 只通过 TimeMachineHistory 保存/恢复笔迹，不处理照片元素）
            if (Main_Grid.Background == Brushes.Transparent)
            {
                // 光标模式：先切换到笔模式。
                // 注意：此处 currentMode 已在方法开头完成切换（0→1 进入白板 / 1→0 退出白板）。
                // 必须与下方笔模式分支保持一致：进入白板要显示白板背景与侧边工具栏并取消置顶，
                // 退出白板要保存白板内容（含照片）并恢复桌面笔迹。
                // 否则会出现白板栏已显示但背景/布局错乱、或退出后白板内容丢失等切换异常。
                if (currentMode == 1)
                {
                    // 进入白板：显示白板背景与侧边工具栏，保存桌面笔迹，恢复白板笔迹
                    GridBackgroundCover.Visibility = Visibility.Visible;
                    AnimationsHelper.ShowWithSlideFromBottomAndFade(BlackboardLeftSide);
                    AnimationsHelper.ShowWithSlideFromBottomAndFade(BlackboardCenterSide);
                    AnimationsHelper.ShowWithSlideFromBottomAndFade(BlackboardRightSide);

                    SaveStrokes(true);
                    ClearStrokes(true);
                    bool docRestored = false;
                    try { docRestored = RestoreDocumentPageIfAvailable(CurrentWhiteboardIndex); } catch { }
                    if (!docRestored)
                    {
                        bool rebuiltFromMemory = false;
                        try { rebuiltFromMemory = ReinsertDocumentPhotosFromMemory(CurrentWhiteboardIndex); } catch { }
                        if (!rebuiltFromMemory) RestoreStrokes();
                    }

                    Topmost = false;
                    this.Activate();
                    TouchLockFix.ReRegisterTouchWindow(this);
                }
                else // currentMode == 0：退出白板 → 桌面模式
                {
                    // 保存白板内容（含照片），恢复桌面笔迹，与笔模式退出分支保持一致
                    SaveStrokes();
                    SaveDocumentPageIfNeeded(CurrentWhiteboardIndex);
                    if (pageDocumentMapping.ContainsKey(CurrentWhiteboardIndex))
                    {
                        TimeMachineHistories[CurrentWhiteboardIndex] = null;
                    }
                    ClearStrokes(true);
                    RestoreStrokes(true);

                    Topmost = true;
                    WindowFocusHelper.EnsureWindowFocus(this);
                    TouchLockFix.ReRegisterTouchWindow(this);
                }
                // 无论进入还是退出白板，最终都切回笔模式（批注模式）
                BtnHideInkCanvas_Click(null, null);
            }
            else
            {
                // 笔模式：根据 currentMode 执行保存/恢复
                if (currentMode == 0)
                {
                    // 退出白板 → 桌面模式：保存白板内容（含照片），恢复桌面笔迹
                    SaveStrokes();
                    SaveDocumentPageIfNeeded(CurrentWhiteboardIndex);
                    // 文档页已完整落盘（.icstk/.xaml 为真相），清空该页时间机器历史以释放其中
                    // 持有的照片 Image 及其大位图引用（否则退出白板后内存无法释放，持续偏高）。
                    // 重新进入该页时会从磁盘恢复并再次置空历史，行为一致。
                    if (pageDocumentMapping.ContainsKey(CurrentWhiteboardIndex))
                    {
                        TimeMachineHistories[CurrentWhiteboardIndex] = null;
                    }
                    ClearStrokes(true);
                    RestoreStrokes(true);

                    // 退出白板后画布上的瓦片照片 Image 已被 ClearStrokes 移除、历史引用也已断开，
                    // 但 OnDemand BitmapImage 的解码缓存仍可能驻留。后台延迟触发一次完整 GC 释放这些
                    // 不再被引用的内存，避免下一次操作前内存占用持续偏高。GC 在后台线程执行，不阻塞 UI；
                    // WaitForPendingFinalizers 等待终结器回收非托管资源，确保大位图及时释放。
                    System.Threading.Tasks.Task.Run(() =>
                    {
                        System.Threading.Thread.Sleep(300);
                        try
                        {
                            GC.Collect(2, GCCollectionMode.Forced, blocking: false);
                            GC.WaitForPendingFinalizers();
                        }
                        catch { }
                    });

                    Topmost = true;
                    WindowFocusHelper.EnsureWindowFocus(this);
                    TouchLockFix.ReRegisterTouchWindow(this);
                }
                else // currentMode == 1
                {
                    // 进入白板模式：保存桌面笔迹，恢复白板内容（含照片）
                    GridBackgroundCover.Visibility = Visibility.Visible;
                    AnimationsHelper.ShowWithSlideFromBottomAndFade(BlackboardLeftSide);
                    AnimationsHelper.ShowWithSlideFromBottomAndFade(BlackboardCenterSide);
                    AnimationsHelper.ShowWithSlideFromBottomAndFade(BlackboardRightSide);

                    SaveStrokes(true);
                    ClearStrokes(true);
                    bool docRestored = false;
                    try { docRestored = RestoreDocumentPageIfAvailable(CurrentWhiteboardIndex); } catch { }
                    if (!docRestored)
                    {
                        // 磁盘恢复失败（如退出白板后立即重进，异步保存尚未落盘）时，
                        // 若该页仍有文档映射，则从内存照片列表重建文档瓦片，避免照片丢失
                        bool rebuiltFromMemory = false;
                        try { rebuiltFromMemory = ReinsertDocumentPhotosFromMemory(CurrentWhiteboardIndex); } catch { }
                        // 已从内存照片重建文档瓦片后，不再回放时间机器历史：历史中同样含照片插入提交，
                        // 回放会导致照片被重复插入（"进入白板时重新插入一张照片"的根因之一）。
                        if (!rebuiltFromMemory) RestoreStrokes();
                        if (rebuiltFromMemory)
                        {
                            LogHelper.WriteLogToFile($"磁盘恢复失败，已从内存照片重建文档页照片: {CurrentWhiteboardIndex}", Helpers.LogHelper.LogType.Trace);
                        }
                    }

                    Topmost = false;
                    this.Activate();
                    TouchLockFix.ReRegisterTouchWindow(this);
                }
            }

            CompletePendingVideoPresenterActivation();

            if (currentMode == 0 && inkCanvas.Strokes.Count == 0 && BtnPPTSlideShowEnd.Visibility != Visibility.Visible)
            {
                CursorIcon_Click(null, null);
            }

            ThemeManager.Current.ApplicationTheme = ApplicationTheme.Dark;

            new Thread(new ThreadStart(() =>
            {
                Thread.Sleep(200);
                Application.Current.Dispatcher.Invoke(() =>
                {
                    isDisplayingOrHidingBlackboard = false;
                });
            })).Start();

            CheckColorTheme(true);
            }
            catch (Exception ex)
            {
                Helpers.LogHelper.WriteLogToFile($"ImageBlackboard_Click(模式切换)异常已拦截: {ex}");
                try { isDisplayingOrHidingBlackboard = false; } catch { }
            }
        }

        /// <summary>
        /// 取消 inkCanvas 上正在进行的任何活动笔画，释放所有捕获。
        /// 在切换模式（白板/桌面/批注）前调用，避免卡顿时延迟提交的笔迹错误地出现在新模式的画布上。
        /// </summary>
        private bool _isCancellingActiveStroke = false;
        private void CancelInProgressStroke()
        {
            try
            {
                if (inkCanvas == null) return;
                var mode = inkCanvas.EditingMode;
                if (mode == InkCanvasEditingMode.None) return;
                _isCancellingActiveStroke = true;
                // 设置为 None 立即终止当前正在绘制的笔画，防止 StylusUp 延迟提交
                inkCanvas.EditingMode = InkCanvasEditingMode.None;
                inkCanvas.ReleaseStylusCapture();
                inkCanvas.ReleaseAllTouchCaptures();
                inkCanvas.ReleaseMouseCapture();
                // 恢复原编辑模式（恢复后仍可继续书写/擦除）
                inkCanvas.EditingMode = mode;
                _isCancellingActiveStroke = false;
            }
            catch { }
        }

        /// <summary>
        /// 模式（白板/桌面/批注）切换期间是否正处于“清空-恢复”过渡窗口。
        /// 卡顿时，切换前残留的桌面笔迹其延迟的 StrokesChanged 会在过渡窗口内被提交到画布，
        /// 造成“浮动栏笔迹混入/替换白板笔迹”。该窗口内到达的残留笔画会被 StrokesOnStrokesChanged 拦截移除。
        /// </summary>
        private bool _isInBoardModeSwitch = false;

        /// <summary>
        /// 过渡窗口内是否已检测到“新画布上的新书写”。为真时说明用户已开始在新模式画布上正常书写，
        /// 后续笔迹应放行（不拦截），避免把用户新写的笔画误当残留笔迹删除。
        /// </summary>
        private bool _inputActivityAfterSwitch = false;

        /// <summary>
        /// 过渡窗口开启的时刻（单调时钟，用于区分“切换前的残留输入”与“切换后的新输入”）。
        /// </summary>
        private long _boardModeSwitchStartedTicks = 0;

        /// <summary>
        /// 模式（白板/桌面/批注）切换前的防护：
        /// 1) 同步排空排队中的输入事件（含 Win32 消息队列中延迟的 StylusUp/TouchUp）。
        ///    此阶段过渡窗口尚未开启，切换前“卡顿残留”的延迟笔画会在旧画布上先落地并计入旧历史，
        ///    随后由 SaveStrokes 保存到正确页面，而不是提交到新模式画布。
        /// 2) 开启过渡窗口，兜底拦截排空后仍可能延迟到达的笔画提交。
        /// 3) 用户在过渡窗口内开始新书写（下笔）后自动放行，避免误删新笔画。
        /// </summary>
        private void BeginBoardModeSwitch()
        {
            try
            {
                // 阶段一：排空排队中的输入（Background 优先级会连带处理 Input/Normal 等更高优先级操作，
                // 并泵送 Win32 消息队列）。此阶段 _isInBoardModeSwitch 尚未开启，
                // 残留笔画会正常提交到旧画布并计入旧历史，不会混入新模式画布。
                Dispatcher.Invoke(System.Windows.Threading.DispatcherPriority.Background, new Action(() => { }));
            }
            catch { }

            // 阶段二：开启过渡窗口。时刻在排空之后记录，确保排空阶段处理的输入被判定为“切换前”。
            // 使用 Environment.TickCount（int，约 25 天回绕一次）作为单调时钟，不受系统时间调整影响。
            _boardModeSwitchStartedTicks = Environment.TickCount;
            _inputActivityAfterSwitch = false;
            _isInBoardModeSwitch = true;
            Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.ContextIdle,
                new Action(() => _isInBoardModeSwitch = false));
        }

        /// <summary>
        /// 记录一次“下笔”输入（由 StylusDown/TouchDown 调用）。
        /// 若发生在过渡窗口开启之后，说明用户已在新模式画布上开始书写，后续笔迹应放行。
        /// </summary>
        private void RecordInputDown()
        {
            try
            {
                if (!_isInBoardModeSwitch) return;
                // 以 int 比较：过渡窗口存活时间远小于 TickCount 约 25 天的回绕周期，可直接比较判定先后。
                if (Environment.TickCount >= (int)_boardModeSwitchStartedTicks)
                    _inputActivityAfterSwitch = true;
            }
            catch { }
        }

        private void ImageCountdownTimer_Click(object sender, RoutedEventArgs e)
        {
            AnimationsHelper.HideWithSlideAndFade(BorderTools);
            AnimationsHelper.HideWithSlideAndFade(BoardBorderTools);
            var w = new CountdownTimerWindow();
            Helpers.WindowMemoryHelper.ReleaseOnClose(w);
            w.Show();
        }

        private void OperatingGuideWindowIcon_Click(object sender, RoutedEventArgs e)
        {
            AnimationsHelper.HideWithSlideAndFade(BorderTools);
            AnimationsHelper.HideWithSlideAndFade(BoardBorderTools);
            var w = new OperatingGuideWindow();
            Helpers.WindowMemoryHelper.ReleaseOnClose(w);
            w.Show();
        }

        private void SymbolIconRand_Click(object sender, RoutedEventArgs e)
        {
            AnimationsHelper.HideWithSlideAndFade(BorderTools);
            AnimationsHelper.HideWithSlideAndFade(BoardBorderTools);
            var w = new RandWindow();
            Helpers.WindowMemoryHelper.ReleaseOnClose(w);
            w.Show();
        }

        private void SymbolIconRandOne_Click(object sender, RoutedEventArgs e)
        {
            AnimationsHelper.HideWithSlideAndFade(BorderTools);
            AnimationsHelper.HideWithSlideAndFade(BoardBorderTools);
            var w = new RandWindow(true);
            Helpers.WindowMemoryHelper.ReleaseOnClose(w);
            w.ShowDialog();
            // ShowDialog 返回后窗口已关闭，直接触发后台 GC 回收
            Helpers.WindowMemoryHelper.ScheduleRelease();
        }

        private void GridInkReplayButton_Click(object sender, RoutedEventArgs e)
        {
            AnimationsHelper.HideWithSlideAndFade(BorderTools);
            AnimationsHelper.HideWithSlideAndFade(BoardBorderTools);

            CollapseBorderDrawShape();

            InkCanvasForInkReplay.Visibility = Visibility.Visible;
            inkCanvas.Visibility = Visibility.Collapsed;
            isStopInkReplay = false;
            InkCanvasForInkReplay.Strokes.Clear();
            StrokeCollection strokes = inkCanvas.Strokes.Clone();
            if (inkCanvas.GetSelectedStrokes().Count != 0)
            {
                strokes = inkCanvas.GetSelectedStrokes().Clone();
            }
            int k = 1, i = 0;
            new Thread(new ThreadStart(() =>
            {
                foreach (Stroke stroke in strokes)
                {
                    StylusPointCollection stylusPoints = new StylusPointCollection();
                    if (stroke.StylusPoints.Count == 629) //圆或椭圆
                    {
                        Stroke s = null;
                        foreach (StylusPoint stylusPoint in stroke.StylusPoints)
                        {
                            if (i++ >= 50)
                            {
                                i = 0;
                                Thread.Sleep(10);
                                if (isStopInkReplay) return;
                            }
                            Application.Current.Dispatcher.Invoke(() =>
                            {
                                try
                                {
                                    InkCanvasForInkReplay.Strokes.Remove(s);
                                }
                                catch { }
                                stylusPoints.Add(stylusPoint);
                                s = new Stroke(stylusPoints.Clone());
                                s.DrawingAttributes = stroke.DrawingAttributes;
                                InkCanvasForInkReplay.Strokes.Add(s);
                            });
                        }
                    }
                    else
                    {
                        Stroke s = null;
                        foreach (StylusPoint stylusPoint in stroke.StylusPoints)
                        {
                            if (i++ >= k)
                            {
                                i = 0;
                                Thread.Sleep(10);
                                if (isStopInkReplay) return;
                            }
                            Application.Current.Dispatcher.Invoke(() =>
                            {
                                try
                                {
                                    InkCanvasForInkReplay.Strokes.Remove(s);
                                }
                                catch { }
                                stylusPoints.Add(stylusPoint);
                                s = new Stroke(stylusPoints.Clone());
                                s.DrawingAttributes = stroke.DrawingAttributes;
                                InkCanvasForInkReplay.Strokes.Add(s);
                            });
                        }
                    }
                }
                Thread.Sleep(100);
                Application.Current.Dispatcher.Invoke(() =>
                {
                    InkCanvasForInkReplay.Visibility = Visibility.Collapsed;
                    inkCanvas.Visibility = Visibility.Visible;
                });
            })).Start();
        }
        bool isStopInkReplay = false;
        private void InkCanvasForInkReplay_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ClickCount == 2)
            {
                InkCanvasForInkReplay.Visibility = Visibility.Collapsed;
                inkCanvas.Visibility = Visibility.Visible;
                isStopInkReplay = true;
            }
        }

        private void SymbolIconTools_Click(object sender, RoutedEventArgs e)
        {
            // 根据当前模式只显示对应窗口
            if (currentMode == 1) // 画板模式
            {
                if (BoardBorderTools.Visibility == Visibility.Visible)
                {
                    AnimationsHelper.HideWithSlideAndFade(BoardBorderTools);
                }
                else
                {
                    AnimationsHelper.ShowWithSlideFromBottomAndFade(BoardBorderTools);
                }
            }
            else // 浮动栏模式
            {
                if (BorderTools.Visibility == Visibility.Visible)
                {
                    AnimationsHelper.HideWithSlideAndFade(BorderTools);
                }
                else
                {
                    AnimationsHelper.ShowWithSlideFromBottomAndFade(BorderTools);
                }
            }
        }

        bool isViewboxFloatingBarMarginAnimationRunning = false;

        private async void ViewboxFloatingBarMarginAnimation()
        {
            // Ensure all UI property accesses in this method run on the UI thread
            if (!Dispatcher.CheckAccess())
            {
                await Dispatcher.InvokeAsync(() => ViewboxFloatingBarMarginAnimation());
                return;
            }

            double MarginFromEdge = Settings.Appearance.FloatingBarBottomMargin;
            if (isFloatingBarFolded)
            {
                MarginFromEdge = -100;
            }
            else if (BtnPPTSlideShowEnd.Visibility == Visibility.Visible)
            {
                // PPT 放映模式：固定为 0px
                MarginFromEdge = 0;
            }
            else if (Topmost == false)
            {
                // 黑板模式：设置为 -60（隐藏到屏幕外）
                MarginFromEdge = -60;
            }
            
            Point newPos = new Point();
            Point target = new Point();
            
            await Dispatcher.InvokeAsync(() =>
            {
                // 黑板模式下需要特殊处理
                if (Topmost == false && BtnPPTSlideShowEnd.Visibility != Visibility.Visible)
                {
                    MarginFromEdge = -60;
                    ViewboxFloatingBar.Visibility = Visibility.Hidden;
                }
                else
                {
                    ViewboxFloatingBar.Visibility = Visibility.Visible;
                }
                isViewboxFloatingBarMarginAnimationRunning = true;

                double dpiScaleX = 1, dpiScaleY = 1;
                PresentationSource source = PresentationSource.FromVisual(this);
                if (source != null)
                {
                    dpiScaleX = source.CompositionTarget.TransformToDevice.M11;
                    dpiScaleY = source.CompositionTarget.TransformToDevice.M22;
                }
                IntPtr windowHandle = new WindowInteropHelper(this).Handle;
                System.Windows.Forms.Screen screen = System.Windows.Forms.Screen.FromHandle(windowHandle);

                // 使用屏幕Bounds而不是WorkingArea，与community-beta版本一致
                double screenWidth = screen.Bounds.Width / dpiScaleX;
                double screenHeight = screen.Bounds.Height / dpiScaleY;

                // 使用更可靠的方法获取浮动栏宽度
                double baseWidth = ViewboxFloatingBar.ActualWidth;

                // 如果ActualWidth为0，尝试强制测量
                if (baseWidth <= 0)
                {
                    // 强制测量以获取准确尺寸
                    double heightConstraint = ViewboxFloatingBar.Height;
                    if (double.IsNaN(heightConstraint) || heightConstraint <= 0) heightConstraint = 50;
                    ViewboxFloatingBar.Measure(new Size(double.PositiveInfinity, heightConstraint));
                    baseWidth = ViewboxFloatingBar.DesiredSize.Width;
                }

                // 如果仍然为0，尝试使用RenderSize
                if (baseWidth <= 0)
                {
                    baseWidth = ViewboxFloatingBar.RenderSize.Width;
                }

                // 如果所有方法都失败，使用一个基于内容的估算值
                if (baseWidth <= 0)
                {
                    // 根据浮动栏内容估算宽度
                    baseWidth = 200; // 最小宽度
                    LogHelper.WriteLogToFile($"浮动栏宽度无法获取，使用估算值: {baseWidth}");
                }

                double floatingBarWidth = baseWidth * ViewboxFloatingBarScaleTransform.ScaleX;
                double floatingBarHeight = ViewboxFloatingBar.Height * ViewboxFloatingBarScaleTransform.ScaleY;
                
                // 水平居中计算，与 community-beta 版本一致
                newPos.X = (screenWidth - floatingBarWidth) / 2;
                
                // Y 坐标计算，使用固定的底部边距，不随缩放比例变化
                // 需要减去浮动栏的实际渲染高度，确保底部边距是从浮动栏底部到屏幕底部的距离
                newPos.Y = screenHeight - MarginFromEdge - floatingBarHeight;

                // 首次定位时，从设置读取用户已锁定的位置（NaN 表示从未手动移动过）
                if (!_floatingBarPositionLoadedFromSettings)
                {
                    _floatingBarPositionLoadedFromSettings = true;
                    if (!double.IsNaN(Settings.Appearance.FloatingBarPositionLockedX)
                        && !double.IsNaN(Settings.Appearance.FloatingBarPositionLockedY))
                    {
                        pointDesktop = new Point(Settings.Appearance.FloatingBarPositionLockedX,
                                                 Settings.Appearance.FloatingBarPositionLockedY);
                    }
                }

                // 最终目标位置：
                // - 黑板模式：使用 newPos（离屏 -60，隐藏）
                // - PPT 放映：返回其「对应位置」（底部居中新算），不受用户桌面位置影响
                // - 其余（桌面/批注）：若用户已拖动锁定过则用 pointDesktop，否则居中
                bool isPPT = BtnPPTSlideShowEnd.Visibility == Visibility.Visible;
                target = newPos;
                if (Topmost != false || isPPT)
                {
                    if (!isPPT && pointDesktop.X >= 0 && pointDesktop.Y >= 0)
                    {
                        target = pointDesktop;
                    }
                }

                // 设置一个合理的起始位置，然后播放平滑动画
                // 如果浮动栏之前是隐藏状态，从屏幕底部开始动画
                Thickness fromMargin;
                if (ViewboxFloatingBar.Visibility != Visibility.Visible)
                {
                    fromMargin = new Thickness(newPos.X, SystemParameters.WorkArea.Height + 100, 0, -20);
                }
                else
                {
                    fromMargin = ViewboxFloatingBar.Margin;
                }

                ThicknessAnimation marginAnimation = new ThicknessAnimation
                {
                    Duration = TimeSpan.FromSeconds(0.5),
                    From = fromMargin,
                    To = new Thickness(target.X, target.Y, -2000, -200),
                    EasingFunction = new CircleEase()
                };
                ViewboxFloatingBar.BeginAnimation(FrameworkElement.MarginProperty, marginAnimation);
            });

            await Task.Delay(200);

            await Dispatcher.InvokeAsync(() =>
            {
                ViewboxFloatingBar.Margin = new Thickness(target.X, target.Y, -2000, -200);
                pos = target;
                // 折叠时隐藏浮动栏
                if (isFloatingBarFolded)
                {
                    ViewboxFloatingBar.Visibility = Visibility.Hidden;
                }
                // 确保浮动栏可见时始终保持在最顶层
                else if (ViewboxFloatingBar.Visibility == Visibility.Visible)
                {
                    Topmost = true;
                    ViewboxFloatingBar.Visibility = Visibility.Visible;
                }
                // 只有在黑板模式下且非PPT放映时才隐藏浮动栏
                else if (currentMode == 1 && BtnPPTSlideShowEnd.Visibility != Visibility.Visible)
                {
                    ViewboxFloatingBar.Visibility = Visibility.Hidden;
                }
            });
        }

        private async void CursorIcon_Click(object sender, RoutedEventArgs e)
        {
            try
            {
            // 切换前自动截图保存墨迹
            if (inkCanvas.Strokes.Count > 0 && inkCanvas.Strokes.Count > Settings.Automation.MinimumAutomationStrokeNumber)
            {
                if (BtnPPTSlideShowEnd.Visibility == Visibility.Visible) SavePPTScreenshot($"{pptName}/{previousSlideID}_{DateTime.Now:HH-mm-ss}");
                else SaveScreenshot(true);
            }

            if (BtnPPTSlideShowEnd.Visibility != Visibility.Visible)
            {
                if (Settings.Canvas.HideStrokeWhenSelecting)
                    inkCanvas.Visibility = Visibility.Collapsed;
                else
                {
                    inkCanvas.IsHitTestVisible = false;
                    inkCanvas.Visibility = Visibility.Visible;
                }
            }
            else
            {
                if (Settings.PowerPointSettings.IsShowStrokeOnSelectInPowerPoint)
                {
                    inkCanvas.Visibility = Visibility.Visible;
                    inkCanvas.IsHitTestVisible = true;
                }
                else
                {
                    if (Settings.Canvas.HideStrokeWhenSelecting)
                        inkCanvas.Visibility = Visibility.Collapsed;
                    else
                    {
                        inkCanvas.IsHitTestVisible = false;
                        inkCanvas.Visibility = Visibility.Visible;
                    }
                }
            }


            Main_Grid.Background = Brushes.Transparent;


            GridBackgroundCoverHolder.Visibility = Visibility.Collapsed;
            inkCanvas.Select(new StrokeCollection());
            GridInkCanvasSelectionCover.Visibility = Visibility.Collapsed;

            if (currentMode != 0)
            {
                SaveStrokes();
                RestoreStrokes(true);
            }

            CheckEnableTwoFingerGestureBtnVisibility(false);


            StackPanelCanvasControls.Visibility = Visibility.Collapsed;

            if (!isFloatingBarFolded)
            {
                HideSubPanels("cursor", true);
                // 避免在从白板模式切换过来时重复调用动画，因为 ImageBlackboard_Click 中已经调用过了
                if (currentMode == 0 && !(isDisplayingOrHidingBlackboard))
                {
                    await Task.Delay(50);

                    if (BtnPPTSlideShowEnd.Visibility == Visibility.Visible)
                    {
                        ViewboxFloatingBarMarginAnimation();
                    }
                    else
                    {
                        ViewboxFloatingBarMarginAnimation();
                    }
                }
            }
            }
            catch (Exception ex)
            {
                Helpers.LogHelper.WriteLogToFile($"CursorIcon_Click(切换到鼠标模式)异常已拦截: {ex}");
                // 即使中途异常，也强制完成切换到鼠标模式的最小状态，避免按钮“点击无反应”。
                try
                {
                    if (!Dispatcher.CheckAccess()) Dispatcher.Invoke(new Action(() => { }));
                    try { inkCanvas.EditingMode = InkCanvasEditingMode.Select; } catch { }
                    try { inkCanvas.IsHitTestVisible = false; } catch { }
                    try { Main_Grid.Background = Brushes.Transparent; } catch { }
                    try { GridInkCanvasSelectionCover.Visibility = Visibility.Collapsed; } catch { }
                    try { StackPanelCanvasControls.Visibility = Visibility.Collapsed; } catch { }
                    try { CheckEnableTwoFingerGestureBtnVisibility(false); } catch { }
                    try { HideSubPanels("cursor", false); } catch { }
                }
                catch { }
            }
        }

        private void PenIcon_Click(object sender, RoutedEventArgs e)
        {
            if (Pen_Icon.Background == null || StackPanelCanvasControls.Visibility == Visibility.Collapsed)
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
                HideSubPanels("pen", true);
            }
            else
            {
                if (PenPalette.Visibility == Visibility.Visible)
                {
                    AnimationsHelper.HideWithSlideAndFade(PenPalette);
                    AnimationsHelper.HideWithSlideAndFade(BoardPenPalette);
                }
                else
                {
                    AnimationsHelper.ShowWithSlideFromBottomAndFade(PenPalette);
                    AnimationsHelper.ShowWithSlideFromBottomAndFade(BoardPenPalette);
                }
            }
        }

        private void ColorThemeSwitch_MouseUp(object sender, RoutedEventArgs e)
        {
            isUselightThemeColor = !isUselightThemeColor;
            if (currentMode == 0)
            {
                isDesktopUselightThemeColor = isUselightThemeColor;
            }
            CheckColorTheme();
        }

        private void EraserIcon_Click(object sender, RoutedEventArgs e)
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

            HideSubPanels("eraser");
        }

        private void EraserIconByStrokes_Click(object sender, RoutedEventArgs e)
        {
            forceEraser = true;
            forcePointEraser = false;

            inkCanvas.EraserShape = new EllipseStylusShape(5, 5);
            inkCanvas.EditingMode = InkCanvasEditingMode.EraseByStroke;
            drawingShapeMode = 0;

            InkCanvas_EditingModeChanged(inkCanvas, null);
            CancelSingleFingerDragMode();

            HideSubPanels("eraserByStrokes");
        }

        private void CursorWithDelIcon_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                SymbolIconDelete_MouseUp(sender, null);
            }
            catch (Exception ex)
            {
                Helpers.LogHelper.WriteLogToFile($"CursorWithDelIcon_Click(清并鼠)删除阶段异常已拦截: {ex}");
            }
            // 无论删除阶段是否成功，都继续切换到鼠标模式，避免“清并鼠”按钮因删除异常而整个失去响应或崩溃
            try
            {
                CursorIcon_Click(null, null);
            }
            catch (Exception ex)
            {
                Helpers.LogHelper.WriteLogToFile($"CursorWithDelIcon_Click(清并鼠)切换鼠标阶段异常已拦截: {ex}");
            }
        }

        private void SelectIcon_MouseUp(object sender, RoutedEvent e)
        {
            forceEraser = true;
            drawingShapeMode = 0;
            inkCanvas.IsManipulationEnabled = false;
            if (inkCanvas.EditingMode == InkCanvasEditingMode.Select)
            {
                StrokeCollection selectedStrokes = new StrokeCollection();
                foreach (Stroke stroke in inkCanvas.Strokes)
                {
                    if (stroke.GetBounds().Width > 0 && stroke.GetBounds().Height > 0)
                    {
                        selectedStrokes.Add(stroke);
                    }
                }
                inkCanvas.Select(selectedStrokes);
            }
            else
            {
                inkCanvas.EditingMode = InkCanvasEditingMode.Select;
            }
        }

        private void CollapseBorderDrawShape(bool isLongPressSelected = false)
        {
            AnimationsHelper.HideWithSlideAndFade(BorderDrawShape);
            AnimationsHelper.HideWithSlideAndFade(BoardBorderDrawShape);
        }

        private void DrawShapePromptToPen()
        {
            if (isLongPressSelected == true)
            {
                HideSubPanels("pen");
            }
            else
            {
                if (StackPanelCanvasControls.Visibility == Visibility.Visible)
                {
                    HideSubPanels("pen");
                }
                else
                {
                    HideSubPanels("cursor");
                }
            }
        }

        private void CloseBordertools_MouseUp(object sender, MouseButtonEventArgs e)
        {
            HideSubPanels();
        }

        #region Left Side Panel

        private void BtnFingerDragMode_Click(object sender, RoutedEventArgs e)
        {
            if (isSingleFingerDragMode)
            {
                isSingleFingerDragMode = false;
                //BtnFingerDragMode.Content = "单指\n拖动";
            }
            else
            {
                isSingleFingerDragMode = true;
                //BtnFingerDragMode.Content = "多指\n拖动";
            }
        }

        private void BtnUndo_Click(object sender, RoutedEventArgs e)
        {
            if (inkCanvas.GetSelectedStrokes().Count != 0)
            {
                GridInkCanvasSelectionCover.Visibility = Visibility.Collapsed;
                inkCanvas.Select(new StrokeCollection());
            }
            var item = timeMachine.Undo();
            ApplyHistoryToCanvas(item);
        }

        private void BtnRedo_Click(object sender, RoutedEventArgs e)
        {
            if (inkCanvas.GetSelectedStrokes().Count != 0)
            {
                GridInkCanvasSelectionCover.Visibility = Visibility.Collapsed;
                inkCanvas.Select(new StrokeCollection());
            }
            var item = timeMachine.Redo();
            ApplyHistoryToCanvas(item);
        }

        private void Element_IsEnabledChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            if (!isLoaded || _isLoadingSettings) return;
            try
            {
                if (sender is Button button)
                {
                    if (((Button)sender).IsEnabled)
                    {
                        ((UIElement)((Button)sender).Content).Opacity = 1;
                    }
                    else
                    {
                        ((UIElement)((Button)sender).Content).Opacity = 0.5;
                    }
                }
                else if (sender is FontIcon fontIcon)
                {
                    if (((FontIcon)sender).IsEnabled)
                    {
                        ((FontIcon)sender).Opacity = 1;
                    }
                    else
                    {
                        ((FontIcon)sender).Opacity = 0.5;
                    }
                }
            }
            catch { }
        }

        #endregion Left Side Panel

        #region Right Side Panel

        public static bool CloseIsFromButton = false;
        private void BtnExit_Click(object sender, RoutedEventArgs e)
        {
            // 通知摄像头管理器处理退出按钮点击事件，暂停摄像头画面
            NotifyCameraManagerExitButtonClicked();
            
            CloseIsFromButton = true;
            Close();
        }

        private void BtnRestart_Click(object sender, RoutedEventArgs e)
        {
            // 在重启前保存当前会话快照与 PPT 页面缓存
            try { SaveLastSessionSnapshot(); } catch { }
            try { SavePptSlidesSnapshotBeforeRestart(); } catch { }

            try
            {
                var basePath = Settings.Automation.AutoSavedStrokesLocation + @"\Auto Saved - Session";
                try { if (!System.IO.Directory.Exists(basePath)) System.IO.Directory.CreateDirectory(basePath); } catch { }
                var reasonPath = basePath + @"\RestartReason.txt";
                try { System.IO.File.WriteAllText(reasonPath, "settings"); } catch { }
            }
            catch { }

            Process.Start(System.Windows.Forms.Application.ExecutablePath, "-m");

            CloseIsFromButton = true;
            Application.Current.Shutdown();
        }

        // 缓存设置窗口实例，避免每次打开都重新解析 4600+ 行（约 170KB）的 XAML，显著减少卡顿
        private static MW_Settings _settingsWindowInstance;
        // 标记是否处于“空闲预构建”阶段：此阶段设置窗口在后台渲染、不淡入、渲染完成后隐藏
        internal static bool IsSettingsPrebuilding { get; set; }

        /// <summary>
        /// 在应用空闲时预先构造并重渲染设置窗口，把“解析 170KB XAML + 首次布局/渲染”的
        /// 开销从“点击设置”挪到空闲时段，从而避免点击时的卡顿与黑屏闪烁。
        /// </summary>
        private void PrebuildSettingsWindow()
        {
            if (_settingsWindowInstance != null) return;
            IsSettingsPrebuilding = true;
            var w = new MW_Settings { Owner = this, ShowActivated = false };
            // 预构建阶段在屏幕外 + 隐藏任务栏 + 透明渲染，避免启动时出现“窗口弹出后又自动关闭”的闪烁。
            // 用户点击设置时再移动到主窗口居中显示。
            // 注意：XAML 里声明了 WindowStartupLocation="CenterScreen"，若不改为 Manual，
            // Show() 时 WPF 会无视上面设置的 Left/Top 而强制将窗口居中到屏幕，导致启动时仍会闪烁。
            w.WindowStartupLocation = WindowStartupLocation.Manual;
            w.ShowInTaskbar = false;
            w.Opacity = 0;
            w.Left = -10000;
            w.Top = -10000;
            w.ContentRendered += OnSettingsPrebuiltRendered;
            _settingsWindowInstance = w;
            // 窗口真正关闭（如 Alt+F4）时清空缓存，下次再新建，避免使用已失效的实例
            w.Closed += (s, ev) =>
            {
                if (ReferenceEquals(_settingsWindowInstance, s))
                {
                    _settingsWindowInstance = null;
                }
                // 关闭即释放内存：延时后台 GC 回收窗口可视化树
                Helpers.WindowMemoryHelper.ScheduleRelease();
            };
            w.Show();
        }

        /// <summary>
        /// 把设置窗口恢复到主窗口居中显示（预构建阶段被挪到了屏幕外，重新打开前需复位）。
        /// </summary>
        private static void CenterSettingsWindow(Window w)
        {
            try
            {
                var owner = (w.Owner as Window) ?? Application.Current?.MainWindow;
                if (owner == null) return;
                w.Left = owner.Left + (owner.ActualWidth - w.Width) / 2;
                w.Top = owner.Top + (owner.ActualHeight - w.Height) / 2;
            }
            catch { }
        }

        /// <summary>
        /// 关闭并释放被缓存（可能处于隐藏状态）的设置窗口实例。
        /// 隐藏的窗口在 WPF 里仍算“已打开”，若不显式关闭，主窗口退出后进程可能无法结束、内存无法回收。
        /// </summary>
        internal void CloseCachedSettingsWindow()
        {
            var w = _settingsWindowInstance;
            _settingsWindowInstance = null;
            IsSettingsPrebuilding = false;
            if (w == null) return;
            try
            {
                w.ContentRendered -= OnSettingsPrebuiltRendered;
                w.Owner = null;
                w.Close();
            }
            catch { }
        }

        private void OnSettingsPrebuiltRendered(object sender, EventArgs e)
        {
            var w = sender as MW_Settings;
            if (w == null) return;
            w.ContentRendered -= OnSettingsPrebuiltRendered;
            // 空闲时段已完成首次渲染（用户无感），隐藏后缓存，点击时即时复用
            if (IsSettingsPrebuilding)
            {
                IsSettingsPrebuilding = false;
                w.Hide();
            }
        }

        private void BtnSettings_Click(object sender, RoutedEventArgs e)
        {
            // 复用已存在的设置窗口实例（可能只是被隐藏/预构建缓存），点击即开，不再重复解析巨型 XAML
            if (_settingsWindowInstance != null)
            {
                var w = _settingsWindowInstance;
                IsSettingsPrebuilding = false; // 防止预构建的 ContentRendered 将其再次隐藏
                w.Owner = this;
                w.ShowInTaskbar = true;
                w.ShowActivated = true;
                // 预构建阶段被挪到了屏幕外，重新显示前恢复到主窗口居中位置
                CenterSettingsWindow(w);
                if (!w.IsVisible) w.Show();
                w.Opacity = 1; // 预构建阶段为 0，确保点击时立即可见
                w.ReloadContents();
                w.Activate();
                return;
            }

            var settingsWindow = new MW_Settings
            {
                Owner = this
            };
            _settingsWindowInstance = settingsWindow;
            settingsWindow.Closed += (s, ev) =>
            {
                if (ReferenceEquals(_settingsWindowInstance, s))
                {
                    _settingsWindowInstance = null;
                }
                // 关闭即释放内存：设置窗口不再长期缓存，延时后台 GC 回收其大 XAML 可视化树
                Helpers.WindowMemoryHelper.ScheduleRelease();
            };
            settingsWindow.Show();
        }

        private void SettingsNav_SelectionChanged(iNKORE.UI.WPF.Modern.Controls.NavigationView sender, iNKORE.UI.WPF.Modern.Controls.NavigationViewSelectionChangedEventArgs args)
        {
            if (args.SelectedItem is iNKORE.UI.WPF.Modern.Controls.NavigationViewItem item && item.Tag is string tag)
            {
                FrameworkElement target = null;
                switch (tag)
                {
                    case "Overview":
                        target = SettingsSection_Overview;
                        break;
                    case "Startup":
                        target = SettingsSection_Startup;
                        break;
                    case "Board":
                        target = SettingsSection_Board;
                        break;
                    case "Gesture":
                        target = SettingsSection_Gesture;
                        break;
                    case "InkRecognition":
                        target = GroupBoxInkRecognition;
                        break;
                    case "Appearance":
                        target = SettingsSection_Appearance;
                        break;
                    case "PPT":
                        target = SettingsSection_PPT;
                        break;
                    case "Advanced":
                        target = SettingsSection_Advanced;
                        break;
                    case "Auto":
                        target = SettingsSection_Auto;
                        break;
                    case "PluginWorkshop":
                        target = SettingsSection_PluginWorkshop;
                        break;
                    case "About":
                        target = SettingsSection_About;
                        break;
                }

                if (target != null)
                {
                    var transform = target.TransformToVisual(SettingsScrollViewer);
                    var offset = transform.Transform(new Point(0, 0));
                    var targetOffset = Math.Max(0, SettingsScrollViewer.VerticalOffset + offset.Y - 80);
                    AnimateScrollTo(targetOffset);
                }
            }
        }

        private async void AnimateScrollTo(double targetOffset)
        {
            double startOffset = SettingsScrollViewer.VerticalOffset;
            double distance = targetOffset - startOffset;
            if (Math.Abs(distance) < 1) return;

            int steps = 20;
            for (int i = 1; i <= steps; i++)
            {
                double progress = (double)i / steps;
                double easedProgress = 1.0 - Math.Pow(1.0 - progress, 3);
                SettingsScrollViewer.ScrollToVerticalOffset(startOffset + distance * easedProgress);
                await Task.Delay(12);
            }
        }

        // ===== 插件工坊 =====

        // plugin 存放目录（位于程序运行目录下的 Plugins 文件夹）
        private static string PluginDirectory => App.RootPath + "Plugins\\";

        private void BtnOpenPluginWorkshop_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // 确保插件目录存在，便于用户立即使用「打开插件目录」
                if (!System.IO.Directory.Exists(PluginDirectory))
                    System.IO.Directory.CreateDirectory(PluginDirectory);

                // 无论插件工坊是否已打开，从设置进入时都要隐藏设置窗口（保留实例以便复用）
                var settings = SettingsWindow;
                if (settings != null)
                {
                    settings.Hide();
                }

                if (PluginWorkshopWindow.HasInstance)
                {
                    // 已打开：仅激活已有实例并置于最前，不重复创建
                    // 传入 null 作为 owner，避免重新绑定到已关闭的设置窗口
                    PluginWorkshopWindow.GetOrCreate(null);
                    return;
                }

                // 未打开：创建插件工坊单例；不绑定 Owner，避免设置窗口关闭时被连带关闭
                var workshop = PluginWorkshopWindow.GetOrCreate(null);
                workshop.Owner = null;
                workshop.Show();
            }
            catch (Exception ex)
            {
                LogHelper.WriteLogToFile($"打开插件工坊失败: {ex.Message}", LogHelper.LogType.Error);
                ShowNotificationAsync("打开插件工坊失败");
            }
        }

        bool forceEraser = false;


        private void BtnClear_Click(object sender, RoutedEventArgs e)
        {
            forceEraser = false;
            //BorderClearInDelete.Visibility = Visibility.Collapsed;

            if (currentMode == 0)
            { // 先回到画笔再清屏，避免 TimeMachine 的相关 bug 影响
                if (Pen_Icon.Background == null && StackPanelCanvasControls.Visibility == Visibility.Visible)
                {
                    PenIcon_Click(null, null);
                }
            }
            else
            {
                if (Pen_Icon.Background == null)
                {
                    PenIcon_Click(null, null);
                }
            }

            // 原此处会判断笔迹非空后执行 strokeCollections[whiteboardIndex] = inkCanvas.Strokes.Clone()，
            // 即整页笔迹的一次全量深拷贝。该数组全仓无读取（见 MW_BoardControls 注释），
            // 纯粹浪费一次 Clone 的 CPU 并把整页笔迹长期钉住，已移除。
            ClearStrokes(false);
            inkCanvas.Children.Clear();

            CancelSingleFingerDragMode();
        }

        bool lastIsInMultiTouchMode = false;

        private void CancelSingleFingerDragMode()
        {
            if (ToggleSwitchDrawShapeBorderAutoHide.IsOn)
            {
                CollapseBorderDrawShape();
            }

            GridInkCanvasSelectionCover.Visibility = Visibility.Collapsed;

            if (isSingleFingerDragMode)
            {
                BtnFingerDragMode_Click(null, null);
            }
            isLongPressSelected = false;
        }

        // 白板浮动栏(MWBoardHost)仅应在白板模式(currentMode == 1)下显示。
        // 原先 XAML 将 MWBoardHost.Visibility 绑定到 GridBackgroundCoverHolder，
        // 而批注(PenIcon_Click)也会把该 Holder 置为 Visible，导致点击"批注"误呼出白板栏。
        // 改为由 currentMode 统一驱动：任何进入/退出白板模式的地方只需设置 currentMode，
        // 工具栏可见性即自动同步，覆盖 ImageBlackboard_Click / BtnSwitch_Click / MW_PenColors / MW_PPT 等所有入口。
        private int _currentMode = 0;
        private int currentMode
        {
            get => _currentMode;
            set
            {
                if (_currentMode == value) return;
                _currentMode = value;
                if (MWBoardHost != null)
                    MWBoardHost.Visibility = _currentMode == 1 ? Visibility.Visible : Visibility.Collapsed;
                // 进入白板模式时同步隐藏浮动栏，避免白板栏与浮动栏同时显示造成界面错乱；
                // 退出白板时浮动栏由 ViewboxFloatingBarMarginAnimation 负责恢复显示与定位。
                if (_currentMode == 1 && ViewboxFloatingBar != null)
                    ViewboxFloatingBar.Visibility = Visibility.Collapsed;
                // 退出白板（切回桌面批注模式）时隐藏白板画布背景。
                // 否则 GridBackgroundCover 会在进入白板时被置为 Visible 后一直残留，
                // 导致再次点击“批注”时出现白色画布而不是直接透明批注。
                if (_currentMode == 0 && GridBackgroundCover != null)
                    GridBackgroundCover.Visibility = Visibility.Collapsed;
                // 退出白板时恢复被折叠的浮动栏显示。注释原依赖 ViewboxFloatingBarMarginAnimation
                // 负责恢复，但并非所有退出入口都会调用该动画，导致“退出白板后浮动栏消失”。
                // 在此统一兜底恢复可见，位置修正由调用方（退出入口）的定位动画负责。
                if (_currentMode == 0 && ViewboxFloatingBar != null)
                    ViewboxFloatingBar.Visibility = Visibility.Visible;
            }
        }

        private void BtnSwitch_Click(object sender, RoutedEventArgs e)
        {
            // 模式切换前先排空残留输入并开启过渡窗口，再取消进行中的笔画，避免残留笔迹混入新模式画布
            BeginBoardModeSwitch();
            CancelInProgressStroke();
            if (Main_Grid.Background == Brushes.Transparent)
            {
                if (currentMode == 1)
                {
                    GridBackgroundCover.Visibility = Visibility.Collapsed;
                    AnimationsHelper.HideWithSlideAndFade(BlackboardLeftSide);
                    AnimationsHelper.HideWithSlideAndFade(BlackboardCenterSide);
                    AnimationsHelper.HideWithSlideAndFade(BlackboardRightSide);

                    SaveStrokes(true);
                    ClearStrokes(true);
                    RestoreStrokes();
                }
                Topmost = true;
                // 确保窗口获得焦点
                WindowFocusHelper.EnsureWindowFocus(this);
                // 重新注册触摸窗口以修复讯飞智慧窗触屏锁导致的触摸问题
                TouchLockFix.ReRegisterTouchWindow(this);
                BtnHideInkCanvas_Click(null, e);
            }
            else
            {
                switch (currentMode)
                {
                    case 0: //屏幕模式
                        currentMode = 0;
                        GridBackgroundCover.Visibility = Visibility.Collapsed;
                        AnimationsHelper.HideWithSlideAndFade(BlackboardLeftSide);
                        AnimationsHelper.HideWithSlideAndFade(BlackboardCenterSide);
                        AnimationsHelper.HideWithSlideAndFade(BlackboardRightSide);

                        SaveStrokes();
                        ClearStrokes(true);
                        RestoreStrokes(true);
                        Topmost = true;
                        // 确保窗口获得焦点
                        WindowFocusHelper.EnsureWindowFocus(this);
                        // 重新注册触摸窗口以修复讯飞智慧窗触屏锁导致的触摸问题
                        TouchLockFix.ReRegisterTouchWindow(this);
                        break;
                    case 1: //黑板或白板模式
                        currentMode = 1;
                        GridBackgroundCover.Visibility = Visibility.Visible;
                        AnimationsHelper.ShowWithSlideFromBottomAndFade(BlackboardLeftSide);
                        AnimationsHelper.ShowWithSlideFromBottomAndFade(BlackboardCenterSide);
                        AnimationsHelper.ShowWithSlideFromBottomAndFade(BlackboardRightSide);

                        SaveStrokes(true);
                        ClearStrokes(true);
                        RestoreStrokes();

                        Topmost = false;
                        // 确保窗口在非Topmost模式下也能正确显示
                        this.Activate();
                        // 重新注册触摸窗口以修复讯飞智慧窗触屏锁导致的触摸问题
                        TouchLockFix.ReRegisterTouchWindow(this);
                        break;
                }
            }
        }

        int BoundsWidth = 5;

        private void BtnHideInkCanvas_Click(object sender, RoutedEventArgs e)
        {
            if (Main_Grid.Background == Brushes.Transparent)
            {
                Main_Grid.Background = new SolidColorBrush(StringToColor("#01FFFFFF"));
                inkCanvas.IsHitTestVisible = true;
                inkCanvas.Visibility = Visibility.Visible;

                GridBackgroundCoverHolder.Visibility = Visibility.Visible;

                GridInkCanvasSelectionCover.Visibility = Visibility.Collapsed;
            }
            else
            {
                try
                {
                // Auto-clear Strokes 要等待截图完成再清理笔记
                if (BtnPPTSlideShowEnd.Visibility != Visibility.Visible)
                {
                    if (!_isRestoringDesktopStrokesAfterPpt && isLoaded && Settings.Automation.IsAutoClearWhenExitingWritingMode)
                    {
                        if (inkCanvas.Strokes.Count > 0)
                        {
                            if (Settings.Automation.IsAutoSaveStrokesAtClear && inkCanvas.Strokes.Count >
                                Settings.Automation.MinimumAutomationStrokeNumber)
                            {
                                SaveScreenshot(true);
                            }

                            BtnClear_Click(null, null);
                        }
                    }
                    inkCanvas.IsHitTestVisible = true;
                    inkCanvas.Visibility = Visibility.Visible;
                }
                else
                {
                    if (!_isRestoringDesktopStrokesAfterPpt && isLoaded && Settings.Automation.IsAutoClearWhenExitingWritingMode && !Settings.PowerPointSettings.IsNoClearStrokeOnSelectWhenInPowerPoint)
                    {
                        if (inkCanvas.Strokes.Count > 0)
                        {
                            if (Settings.Automation.IsAutoSaveStrokesAtClear && inkCanvas.Strokes.Count >
                                Settings.Automation.MinimumAutomationStrokeNumber)
                            {
                                SaveScreenshot(true);
                            }

                            BtnClear_Click(null, null);
                        }
                    }


                    if (Settings.PowerPointSettings.IsShowStrokeOnSelectInPowerPoint)
                    {
                        inkCanvas.Visibility = Visibility.Visible;
                        inkCanvas.IsHitTestVisible = true;
                    }
                    else
                    {
                        inkCanvas.IsHitTestVisible = true;
                        inkCanvas.Visibility = Visibility.Visible;
                    }
                }

                Main_Grid.Background = Brushes.Transparent;

                GridBackgroundCoverHolder.Visibility = Visibility.Collapsed;

                if (currentMode != 0)
                {
                    SaveStrokes();
                    RestoreStrokes(true);
                }
            }
                catch (Exception ex)
                {
                    Helpers.LogHelper.WriteLogToFile($"BtnHideInkCanvas_Click(退出批注/切换到鼠标模式)异常已拦截: {ex}");
                }
            }

            try
            {
                if (Main_Grid.Background == Brushes.Transparent)
                {
                    StackPanelCanvasControls.Visibility = Visibility.Collapsed;
                    CheckEnableTwoFingerGestureBtnVisibility(false);
                    HideSubPanels("cursor");
                }
                else
                {
                    AnimationsHelper.ShowWithSlideFromLeftAndFade(StackPanelCanvasControls);
                    CheckEnableTwoFingerGestureBtnVisibility(true);
                }
            }
            catch (Exception ex)
            {
                Helpers.LogHelper.WriteLogToFile($"BtnHideInkCanvas_Click(退出批注/切换到鼠标模式)尾段异常已拦截: {ex}");
            }
        }
        #endregion

        /// <summary>
        /// 通知摄像头管理器处理退出按钮点击事件
        /// </summary>
        private void NotifyCameraManagerExitButtonClicked()
        {
            try
            {
                // 通过反射获取摄像头设备管理器实例
                var cameraDeviceManagerField = typeof(MainWindow).GetField("cameraDeviceManager", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                if (cameraDeviceManagerField != null)
                {
                    var cameraDeviceManager = cameraDeviceManagerField.GetValue(this);
                    if (cameraDeviceManager != null)
                    {
                        // 调用摄像头管理器的HandleExitButtonClicked方法
                        var handleExitButtonClickedMethod = cameraDeviceManager.GetType().GetMethod("HandleExitButtonClicked", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
                        handleExitButtonClickedMethod?.Invoke(cameraDeviceManager, null);
                    }
                }
            }
            catch (Exception ex)
            {
                // 记录错误日志，但不影响正常退出流程
                LogHelper.WriteLogToFile($"通知摄像头管理器退出按钮点击事件失败: {ex.Message}");
            }
        }
    }
}
