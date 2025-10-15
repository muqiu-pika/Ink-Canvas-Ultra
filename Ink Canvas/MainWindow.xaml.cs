using Ink_Canvas.Helpers;
using Ink_Canvas.Models;
using iNKORE.UI.WPF.Modern;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Reflection;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Ink;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Drawing;
using System.Windows.Threading;
using File = System.IO.File;
using MessageBox = System.Windows.MessageBox;

namespace Ink_Canvas
{
    public partial class MainWindow : Window
    {
        #region Window Initialization

        public MainWindow()
        {
            /*
                处于画板模式内：Topmost == false / currentMode != 0
                处于 PPT 放映内：BtnPPTSlideShowEnd.Visibility
            */
            InitializeComponent();
            // 将视频控制条嵌入到 BorderStrokeSelectionControl 内部容器，统一显示与随动
            try
            {
				if (VideoControlContainer != null && BorderVideoSelectionControl != null)
				{
					if (BorderVideoSelectionControl.Parent is Panel parentPanel)
					{
						parentPanel.Children.Remove(BorderVideoSelectionControl);
					}
					VideoControlContainer.Children.Add(BorderVideoSelectionControl);
					BorderVideoSelectionControl.Margin = new Thickness(0, 4, 0, 0);
					BorderVideoSelectionControl.HorizontalAlignment = HorizontalAlignment.Stretch;
					BorderVideoSelectionControl.VerticalAlignment = VerticalAlignment.Top;
				}
            }
            catch { }
            // 绑定额外的选择事件以管理视频控制条（与 BorderStrokeSelectionControl 相同方式）
            try { inkCanvas.SelectionChanged += InkCanvas_VideoSelectionChanged; } catch { }

            BlackboardLeftSide.Visibility = Visibility.Collapsed;
            BlackboardCenterSide.Visibility = Visibility.Collapsed;
            BlackboardRightSide.Visibility = Visibility.Collapsed;

            BorderTools.Visibility = Visibility.Collapsed;
            BorderSettings.Visibility = Visibility.Collapsed;

            BtnPPTSlideShowEnd.Visibility = Visibility.Collapsed;
            PPTNavigationBottomLeft.Visibility = Visibility.Collapsed;
            PPTNavigationBottomRight.Visibility = Visibility.Collapsed;
            PPTNavigationSidesLeft.Visibility = Visibility.Collapsed;
            PPTNavigationSidesRight.Visibility = Visibility.Collapsed;

            BorderSettings.Margin = new Thickness(0, 150, 0, 150);

            TwoFingerGestureBorder.Visibility = Visibility.Collapsed;
            BoardTwoFingerGestureBorder.Visibility = Visibility.Collapsed;
            BorderDrawShape.Visibility = Visibility.Collapsed;
            BoardBorderDrawShape.Visibility = Visibility.Collapsed;

            GridInkCanvasSelectionCover.Visibility = Visibility.Collapsed;

            ViewboxFloatingBar.Margin = new Thickness((SystemParameters.WorkArea.Width - 284) / 2, SystemParameters.WorkArea.Height - 60, -2000, -200);
            ViewboxFloatingBarMarginAnimation();

            try
            {
                if (File.Exists("Log.txt"))
                {
                    FileInfo fileInfo = new FileInfo("Log.txt");
                    long fileSizeInKB = fileInfo.Length / 1024;
                    if (fileSizeInKB > 512)
                    {
                        try
                        {
                            File.Delete("Log.txt");
                            LogHelper.WriteLogToFile("The Log.txt file has been successfully deleted. Original file size: " + fileSizeInKB + " KB", LogHelper.LogType.Info);
                        }
                        catch (Exception ex)
                        {
                            LogHelper.WriteLogToFile(ex + " | Can not delete the Log.txt file. File size: " + fileSizeInKB + " KB", LogHelper.LogType.Error);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                LogHelper.WriteLogToFile(ex.ToString(), LogHelper.LogType.Error);
            }

            InitTimers();
            timeMachine.OnRedoStateChanged += TimeMachine_OnRedoStateChanged;
            timeMachine.OnUndoStateChanged += TimeMachine_OnUndoStateChanged;
            inkCanvas.Strokes.StrokesChanged += StrokesOnStrokesChanged;

            Microsoft.Win32.SystemEvents.UserPreferenceChanged += SystemEvents_UserPreferenceChanged;
            try
            {
                if (File.Exists("SpecialVersion.ini")) SpecialVersionResetToSuggestion_Click();
            }
            catch (Exception ex)
            {
                LogHelper.WriteLogToFile(ex.ToString(), LogHelper.LogType.Error);
            }

            CheckColorTheme(true);
            
            // 注册窗口大小变化事件
            this.SizeChanged += MainWindow_SizeChanged;
        }

        // 窗口大小变化事件处理
        private void MainWindow_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            // 如果视频展台侧栏可见，重新计算照片区域高度
            if (VideoPresenterSidebar.Visibility == Visibility.Visible)
            {
                AutoCalculatePhotoAreaHeight();
            }
        }

        #endregion

        #region Ink Canvas Functions

        DrawingAttributes drawingAttributes;
		private void LoadPenCanvas()
        {
            try
            {
                drawingAttributes = inkCanvas.DefaultDrawingAttributes;
                drawingAttributes.Color = Colors.Red;

                drawingAttributes.Height = 2.5;
                drawingAttributes.Width = 2.5;

                inkCanvas.EditingMode = InkCanvasEditingMode.Ink;
                inkCanvas.Gesture += InkCanvas_Gesture;
            }
            catch { }
        }

        private void InkCanvas_Gesture(object sender, InkCanvasGestureEventArgs e)
        {
            ReadOnlyCollection<GestureRecognitionResult> gestures = e.GetGestureRecognitionResults();
            try
            {
                foreach (GestureRecognitionResult gest in gestures)
                {
                    if (BtnPPTSlideShowEnd.Visibility == Visibility.Visible)
                    {
                        if (gest.ApplicationGesture == ApplicationGesture.Left)
                        {
                            BtnPPTSlidesDown_Click(null, null);
                        }
                        if (gest.ApplicationGesture == ApplicationGesture.Right)
                        {
                            BtnPPTSlidesUp_Click(null, null);
                        }
                    }
                }
            }
            catch { }
        }

		private void InkCanvas_EditingModeChanged(object sender, RoutedEventArgs e)
        {
			if (!(sender is InkCanvas inkCanvas1)) return;
            if (Settings.Canvas.IsShowCursor)
            {
                if (inkCanvas1.EditingMode == InkCanvasEditingMode.Ink || drawingShapeMode != 0)
                {
                    inkCanvas1.ForceCursor = true;
                }
                else
                {
                    inkCanvas1.ForceCursor = false;
                }
            }
            else
            {
                inkCanvas1.ForceCursor = false;
            }
            if (inkCanvas1.EditingMode == InkCanvasEditingMode.Ink) forcePointEraser = !forcePointEraser;
        }

        #endregion Ink Canvas Functions

        #region Definations and Loading

        public static Settings Settings = new Settings();
        public static string settingsFileName = "Settings.json";
        bool isLoaded = false;

        // 拍照功能相关字段
        private ObservableCollection<CapturedImage> capturedPhotos = new ObservableCollection<CapturedImage>();

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
			LoadPenCanvas();
            //加载设置
            LoadSettings(true);
            if (Environment.Is64BitProcess)
            {
                GroupBoxInkRecognition.Visibility = Visibility.Collapsed;
            }

            ThemeManager.Current.ApplicationTheme = ApplicationTheme.Light;
            SystemEvents_UserPreferenceChanged(null, null);

            AppVersionTextBlock.Text = Assembly.GetExecutingAssembly().GetName().Version.ToString();
            LogHelper.WriteLogToFile("Ink Canvas Loaded", LogHelper.LogType.Event);
            
            // 初始化摄像头设备管理器
            InitializeCameraDeviceManager();
            
            isLoaded = true;
            RegisterGlobalHotkeys();
        }

        private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            LogHelper.WriteLogToFile("Ink Canvas closing", LogHelper.LogType.Event);
            if (!CloseIsFromButton && Settings.Advanced.IsSecondConfimeWhenShutdownApp)
            {
                e.Cancel = true;
                if (MessageBox.Show("是否继续关闭 Ink Canvas 画板，这将丢失当前未保存的工作。", "Ink Canvas 画板", MessageBoxButton.OKCancel, MessageBoxImage.Warning) == MessageBoxResult.OK)
                {
                    if (MessageBox.Show("真的狠心关闭 Ink Canvas 画板吗？", "Ink Canvas 画板", MessageBoxButton.OKCancel, MessageBoxImage.Error) == MessageBoxResult.OK)
                    {
                        if (MessageBox.Show("是否取消关闭 Ink Canvas 画板？", "Ink Canvas 画板", MessageBoxButton.OKCancel, MessageBoxImage.Error) != MessageBoxResult.OK)
                        {
                            e.Cancel = false;
                        }
                    }
                }
            }
            if (e.Cancel)
            {
                LogHelper.WriteLogToFile("Ink Canvas closing cancelled", LogHelper.LogType.Event);
            }
        }

        private void Window_Closed(object sender, EventArgs e)
        {
            LogHelper.WriteLogToFile("Ink Canvas closed", LogHelper.LogType.Event);
            // 移除摄像头画面
            RemoveCameraFrame();
            // 清理摄像头资源
            cameraDeviceManager?.Dispose();
        }

        #endregion Definations and Loading

        // 视频展台按钮点击事件
        private void BtnVideoPresenter_Click(object sender, RoutedEventArgs e)
        {
            // 切换视频展台侧栏的可见性
            if (VideoPresenterSidebar.Visibility == Visibility.Visible)
            {
                VideoPresenterSidebar.Visibility = Visibility.Collapsed;
                // 移除摄像头画面
                RemoveCameraFrame();
            }
            else
            {
                VideoPresenterSidebar.Visibility = Visibility.Visible;
                // 当打开侧栏时自动刷新设备列表（设备选择功能保持不变）
                cameraDeviceManager?.RefreshCameraDevices();
                // 自动计算照片显示区域高度
                AutoCalculatePhotoAreaHeight();
            }
        }

        // 关闭视频展台侧栏按钮点击事件
        private void BtnCloseVideoPresenter_Click(object sender, RoutedEventArgs e)
        {
            VideoPresenterSidebar.Visibility = Visibility.Collapsed;
            // 移除摄像头画面
            RemoveCameraFrame();
        }

        // 摄像头设备管理器
        private CameraDeviceManager cameraDeviceManager;
        // 当前显示的摄像头画面元素
        private System.Windows.Controls.Image currentCameraImage;
        // 摄像头画面更新定时器
        private DispatcherTimer cameraFrameTimer;

        #region Photo Capture Functions

        private void BtnCapturePhoto_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (cameraDeviceManager == null)
                {
                    MessageBox.Show("摄像头设备管理器未初始化", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                var frame = cameraDeviceManager.GetFrameCopy();
                if (frame == null)
                {
                    MessageBox.Show("未获取到摄像头画面", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }

                // 在后台线程中处理图像转换
                Task.Run(() =>
                {
                    try
                    {
                        using (frame)
                        {
                            var bitmapImage = ConvertBitmapToBitmapImage(frame);
                            if (bitmapImage != null)
                            {
                                Dispatcher.BeginInvoke(new Action(() =>
                                {
                                    AddCapturedPhoto(bitmapImage);
                                }));
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"拍照处理失败: {ex.Message}");
                    }
                });
            }
            catch (Exception ex)
            {
                MessageBox.Show($"拍照失败: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private BitmapImage ConvertBitmapToBitmapImage(Bitmap bitmap)
        {
            try
            {
                using (var memory = new MemoryStream())
                {
                    bitmap.Save(memory, System.Drawing.Imaging.ImageFormat.Bmp);
                    memory.Position = 0;

                    var bitmapImage = new BitmapImage();
                    bitmapImage.BeginInit();
                    bitmapImage.StreamSource = memory;
                    bitmapImage.CacheOption = BitmapCacheOption.OnLoad;
                    bitmapImage.EndInit();
                    bitmapImage.Freeze();

                    return bitmapImage;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"图像转换失败: {ex.Message}");
                return null;
            }
        }

        private void AddCapturedPhoto(BitmapImage image)
        {
            try
            {
                var capturedImage = new CapturedImage(image);
                capturedPhotos.Insert(0, capturedImage);
                UpdateCapturedPhotosDisplay();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"添加照片失败: {ex.Message}");
            }
        }

        private void UpdateCapturedPhotosDisplay()
        {
            try
            {
                var capturedPhotosStackPanel = FindName("CapturedPhotosStackPanel") as StackPanel;
                if (capturedPhotosStackPanel == null) return;

                capturedPhotosStackPanel.Children.Clear();

                foreach (var photo in capturedPhotos)
                {
                    var photoButton = CreatePhotoButton(photo);
                    capturedPhotosStackPanel.Children.Add(photoButton);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"更新照片显示失败: {ex.Message}");
            }
        }

        private Button CreatePhotoButton(CapturedImage photo)
        {
            var button = new Button
            {
                Width = 300,
                Height = 200,
                Margin = new Thickness(4),
                Background = System.Windows.Media.Brushes.Transparent,
                BorderThickness = new Thickness(1),
                BorderBrush = new SolidColorBrush(System.Windows.Media.Color.FromArgb(0x80, 0x80, 0x80, 0x80)),
                Content = new System.Windows.Controls.Image
                {
                    Source = photo.Thumbnail,
                    Stretch = Stretch.Uniform,
                    Width = 290,
                    Height = 180
                }
            };

            button.Click += (sender, e) =>
            {
                // 点击照片时显示大图（这里可以扩展功能）
                MessageBox.Show($"拍摄时间: {photo.Timestamp}", "照片信息", MessageBoxButton.OK, MessageBoxImage.Information);
            };

            return button;
        }

        #endregion

        // 自动计算照片显示区域高度算法
        private void AutoCalculatePhotoAreaHeight()
        {
            try
            {
                // 等待UI布局完成后再计算
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    try
                    {
                        // 获取侧栏的实际高度
                        double sidebarHeight = VideoPresenterSidebar.ActualHeight;
                        if (sidebarHeight <= 0) return;

                        // 计算各固定区域的高度
                        double titleBarHeight = 50; // 顶部标题栏高度
                        double bottomButtonHeight = 60; // 底部按钮区域高度
                        
                        // 获取设备选择区域的实际高度
                        double deviceAreaHeight = 0;
                        var cameraDevicesScrollViewer = FindName("CameraDevicesScrollViewer") as ScrollViewer;
                        if (cameraDevicesScrollViewer != null)
                        {
                            // 测量设备选择区域的实际高度
                            cameraDevicesScrollViewer.Measure(new System.Windows.Size(double.PositiveInfinity, double.PositiveInfinity));
                            deviceAreaHeight = cameraDevicesScrollViewer.DesiredSize.Height;
                            
                            // 如果设备区域高度为0，使用默认值
                            if (deviceAreaHeight <= 0)
                            {
                                deviceAreaHeight = 120; // 默认设备区域高度
                            }
                            else
                            {
                                // 加上设备区域的边距和内边距
                                deviceAreaHeight += 10 + 2 + 5; // 上边距2 + 下边距5 + 内边距10
                            }
                        }

                        // 计算照片区域的边距和内边距
                        double photoAreaMargin = 10 + 10 + 2; // 上边距10 + 下边距2 + 内边距10
                        
                        // 计算照片显示区域可以使用的最大高度
                        // 目标：照片区域从当前位置一直延伸到设备列表上方几个像素
                        double maxPhotoHeight = sidebarHeight - titleBarHeight - bottomButtonHeight - photoAreaMargin - deviceAreaHeight - 10; // 留出10像素间距

                        // 确保照片显示区域至少有最小高度
                        double minPhotoHeight = 200; // 最小照片区域高度
                        
                        // 计算照片显示区域的理想高度
                        double idealPhotoHeight = Math.Max(minPhotoHeight, maxPhotoHeight);
                        
                        // 设置照片滚动区域的最大高度
                        var capturedPhotosScrollViewer = FindName("CapturedPhotosScrollViewer") as ScrollViewer;
                        if (capturedPhotosScrollViewer != null)
                        {
                            // 为了防止底部照片超出滚动条范围，减去额外的边距
                            capturedPhotosScrollViewer.MaxHeight = idealPhotoHeight - 20; // 减去20像素作为额外缓冲
                        }

                        Console.WriteLine($"照片区域高度计算完成: 侧栏高度={sidebarHeight}, 设备区域高度={deviceAreaHeight}, 照片区域高度={idealPhotoHeight}");
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"自动计算照片区域高度失败: {ex.Message}");
                    }
                }), System.Windows.Threading.DispatcherPriority.Background);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"自动计算照片区域高度失败: {ex.Message}");
            }
        }

        // 初始化摄像头设备管理器
        private void InitializeCameraDeviceManager()
        {
            cameraDeviceManager = new CameraDeviceManager(this);
            cameraDeviceManager.RefreshCameraDevices();
            
            // 初始化摄像头画面更新定时器（30fps）
            cameraFrameTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(33) // ~30fps
            };
            cameraFrameTimer.Tick += CameraFrameTimer_Tick;
        }

        // 摄像头控制按钮功能已移除，仅保留设备选择功能

        // 插入摄像头画面到白板
        public async void InsertCameraFrameToCanvas()
        {
            if (cameraDeviceManager == null) return;

            // 如果已有摄像头画面，先移除
            RemoveCameraFrame();

            // 尝试多次获取第一帧画面并插入
            bool frameInserted = false;
            for (int i = 0; i < 5; i++) // 最多尝试5次
            {
                var frame = cameraDeviceManager.GetFrameCopy();
                if (frame != null)
                {
                    await InsertCameraFrameAsync(frame);
                    frame.Dispose();
                    frameInserted = true;
                    
                    // 启动定时器持续更新画面
                    cameraFrameTimer?.Start();
                    break;
                }
                
                // 如果没有获取到帧，等待一段时间再重试
                await System.Threading.Tasks.Task.Delay(500);
            }
            
            if (!frameInserted)
            {
                Console.WriteLine("无法获取摄像头画面，可能是摄像头未初始化完成");
            }
        }

        // 移除摄像头画面
        public void RemoveCameraFrame()
        {
            // 停止定时器
            cameraFrameTimer?.Stop();
            
            // 移除摄像头画面元素
            if (currentCameraImage != null)
            {
                inkCanvas.Children.Remove(currentCameraImage);
                currentCameraImage = null;
            }
        }

        // 摄像头画面更新定时器事件
        private async void CameraFrameTimer_Tick(object sender, EventArgs e)
        {
            if (cameraDeviceManager == null || currentCameraImage == null) return;

            try
            {
                var frame = cameraDeviceManager.GetFrameCopy();
                if (frame != null)
                {
                    await UpdateCameraFrameAsync(frame);
                    frame.Dispose();
                }
            }
            catch (Exception ex)
            {
                // 静默处理定时器更新错误
                Console.WriteLine($"摄像头画面定时器更新失败: {ex.Message}");
            }
        }

        private async Task InsertCameraFrameAsync(Bitmap frame)
        {
            try
            {
                // 转换Bitmap到BitmapImage
                var bitmapImage = await Task.Run(() =>
                {
                    using (var memoryStream = new MemoryStream())
                    {
                        frame.Save(memoryStream, System.Drawing.Imaging.ImageFormat.Png);
                        memoryStream.Position = 0;

                        var bitmap = new BitmapImage();
                        bitmap.BeginInit();
                        bitmap.StreamSource = memoryStream;
                        bitmap.CacheOption = BitmapCacheOption.OnLoad;
                        bitmap.EndInit();
                        bitmap.Freeze();

                        return bitmap;
                    }
                });

                // 创建图片元素
                currentCameraImage = new System.Windows.Controls.Image
                {
                    Source = bitmapImage,
                    Width = bitmapImage.PixelWidth,
                    Height = bitmapImage.PixelHeight,
                    Name = "camera_" + DateTime.Now.ToString("yyyyMMdd_HH_mm_ss_fff")
                };

                // 居中并缩放
                CenterAndScaleElement(currentCameraImage);

                // 添加到画布
                InkCanvas.SetLeft(currentCameraImage, 0);
                InkCanvas.SetTop(currentCameraImage, 0);
                inkCanvas.Children.Add(currentCameraImage);

                // 记录历史
                timeMachine.CommitElementInsertHistory(currentCameraImage);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"插入摄像头画面失败: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        // 更新摄像头画面
        private async Task UpdateCameraFrameAsync(Bitmap frame)
        {
            try
            {
                if (currentCameraImage == null) return;

                // 转换Bitmap到BitmapImage
                var bitmapImage = await Task.Run(() =>
                {
                    if (frame == null) return null;
                    
                    using (var memoryStream = new MemoryStream())
                    {
                        frame.Save(memoryStream, System.Drawing.Imaging.ImageFormat.Png);
                        memoryStream.Position = 0;

                        var bitmap = new BitmapImage();
                        bitmap.BeginInit();
                        bitmap.StreamSource = memoryStream;
                        bitmap.CacheOption = BitmapCacheOption.OnLoad;
                        bitmap.EndInit();
                        bitmap.Freeze();

                        return bitmap;
                    }
                });

                // 在UI线程更新图片源
                if (bitmapImage != null && currentCameraImage != null)
                {
                    await Dispatcher.BeginInvoke(new Action(() =>
                    {
                        if (currentCameraImage != null)
                        {
                            currentCameraImage.Source = bitmapImage;
                        }
                    }));
                }
            }
            catch (Exception ex)
            {
                // 静默处理更新错误，避免频繁弹窗
                Console.WriteLine($"更新摄像头画面失败: {ex.Message}");
            }
        }
    }
}