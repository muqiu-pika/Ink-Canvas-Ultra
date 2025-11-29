using Ink_Canvas.Helpers;
using Ink_Canvas.Models;
using iNKORE.UI.WPF.Modern;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Ink;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Drawing;
using System.Windows.Threading;
using File = System.IO.File;
using MessageBox = System.Windows.MessageBox;
using Ink_Canvas.MainWindow_cs;
using AForge.Imaging;
using AForge.Imaging.Filters;
using AForge.Math.Geometry;

namespace Ink_Canvas
{
    public partial class MainWindow : System.Windows.Window
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
        // 侧栏照片选中状态（使用时间戳标识当前选中照片）
        private string selectedPhotoTimestamp = null;
        
        // 照片页面管理相关字段
        private Dictionary<string, int> photoPageMapping = new Dictionary<string, int>(); // 记录照片时间戳与页码的关联
        private System.Windows.Controls.Image currentPhotoImage; // 当前显示的照片元素

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

            // 启动后提示是否恢复上次会话
            PromptRestoreLastSessionOnStartup();
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

        private void PromptRestoreLastSessionOnStartup()
        {
            try
            {
                string basePath = Settings.Automation.AutoSavedStrokesLocation + @"\Auto Saved - Session";
                string reasonPath = basePath + @"\RestartReason.txt";
                string reason = null;
                try
                {
                    if (File.Exists(reasonPath))
                    {
                        reason = File.ReadAllText(reasonPath).Trim().ToLowerInvariant();
                        try { File.Delete(reasonPath); } catch { }
                    }
                }
                catch { }
                if (string.IsNullOrEmpty(reason) || (reason != "settings" && reason != "silent" && reason != "crash")) return;
                string metaPath = basePath + @"\SessionMeta.txt";
                string icartPath = basePath + @"\LastSession.icart";
                if (!File.Exists(metaPath) || !File.Exists(icartPath)) return;

                var notificationWindow = new YesOrNoNotificationWindow("检测到上次会话快照，是否恢复？",
                    yesAction: () =>
                    {
                        try
                        {
                            // 首先确认恢复会话，设置全局标志
                            ConfirmRestoreSession();
                            
                            // 读取元信息
                            int metaMode = currentMode;
                            int metaWhiteboardIndex = CurrentWhiteboardIndex;
                            int metaPpt = 0;
                            try
                            {
                                var lines = File.ReadAllLines(metaPath);
                                foreach (var line in lines)
                                {
                                    var kv = line.Split('=');
                                    if (kv.Length == 2)
                                    {
                                        string key = kv[0].Trim().ToLowerInvariant();
                                        string val = kv[1].Trim();
                                        if (key == "mode") int.TryParse(val, out metaMode);
                                        else if (key == "whiteboard") int.TryParse(val, out metaWhiteboardIndex);
                                        else if (key == "ppt") int.TryParse(val, out metaPpt);
                                        else if (key == "whiteboard_total") int.TryParse(val, out WhiteboardTotalCount);
                                    }
                                }
                            }
                            catch { }

                            // 切换模式以匹配快照
                            if (metaMode != currentMode)
                            {
                                ImageBlackboard_Click(null, null);
                            }

                            // 恢复快照
                            bool ok = OpenLastSessionSnapshotIfExists();
                            if (ok)
                            {
                                CurrentWhiteboardIndex = metaWhiteboardIndex;
                                try
                                {
                                    string basePath2 = Settings.Automation.AutoSavedStrokesLocation + @"\Auto Saved - Session";
                                    string pagesDir = System.IO.Path.Combine(basePath2, "pages");
                                    if (System.IO.Directory.Exists(pagesDir))
                                    {
                                        int count = 0;
                                        foreach (var d in System.IO.Directory.GetDirectories(pagesDir)) count++;
                                        if (count > 0) WhiteboardTotalCount = count;
                                        UpdateIndexInfoDisplay();
                                    }
                                }
                                catch { }
                                ShowNotificationAsync("已恢复上次会话快照", true);
                                try { LoadLastSessionPhotosToSidebarAndBind(); } catch { }
                            }
                            else
                            {
                                ShowNotificationAsync("会话快照恢复失败", true);
                            }
                        }
                        catch { }
                    },
                    noAction: () => { });

                notificationWindow.Show();
            }
            catch (Exception ex)
            {
                LogHelper.WriteLogToFile("Prompt restore last session failed | " + ex.ToString(), LogHelper.LogType.Error);
            }
        }

        private void LoadLastSessionPhotosToSidebarAndBind()
        {
            try
            {
                string basePath = Settings.Automation.AutoSavedStrokesLocation + @"\Auto Saved - Session";
                string pagesDir = System.IO.Path.Combine(basePath, "pages");
                if (!System.IO.Directory.Exists(pagesDir)) return;

                capturedPhotos.Clear();
                photoPageMapping.Clear();

                foreach (var dir in System.IO.Directory.GetDirectories(pagesDir))
                {
                    int pageIndex = 0;
                    int.TryParse(System.IO.Path.GetFileName(dir), out pageIndex);
                    string elementsPath = System.IO.Path.Combine(dir, "elements.xaml");
                    if (!System.IO.File.Exists(elementsPath)) continue;
                    try
                    {
                        using (var fs = new System.IO.FileStream(elementsPath, System.IO.FileMode.Open))
                        {
                            if (System.Windows.Markup.XamlReader.Load(fs) is System.Windows.Controls.InkCanvas loadedCanvas)
                            {
                                foreach (System.Windows.UIElement child in loadedCanvas.Children)
                                {
                                    if (child is System.Windows.Controls.Image image)
                                    {
                                        string candidate = null;
                                        try
                                        {
                                            string tagPath = image.Tag as string;
                                            if (!string.IsNullOrEmpty(tagPath) && tagPath.StartsWith("File Dependency"))
                                            {
                                                candidate = System.IO.Path.Combine(dir, tagPath.Replace('/', '\\'));
                                            }
                                            else if (image.Source is System.Windows.Media.Imaging.BitmapImage bmi && bmi.UriSource != null)
                                            {
                                                candidate = bmi.UriSource.LocalPath;
                                                string tryLocal = System.IO.Path.Combine(dir, "File Dependency", System.IO.Path.GetFileName(candidate));
                                                if (System.IO.File.Exists(tryLocal)) candidate = tryLocal;
                                            }
                                        }
                                        catch { }

                                        if (!string.IsNullOrEmpty(candidate) && System.IO.File.Exists(candidate))
                                        {
                                            try
                                            {
                                                var bi = new System.Windows.Media.Imaging.BitmapImage();
                                                bi.BeginInit();
                                                bi.UriSource = new Uri(candidate, UriKind.Absolute);
                                                bi.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad;
                                                bi.EndInit();
                                                bi.Freeze();
                                                var ci = new Ink_Canvas.Models.CapturedImage(bi, candidate);

                                                bool exists = capturedPhotos.Any(p => (!string.IsNullOrEmpty(p.FilePath) && p.FilePath == candidate) || p.Timestamp == ci.Timestamp);
                                                if (!exists)
                                                {
                                                    capturedPhotos.Insert(0, ci);
                                                }
                                                if (!string.IsNullOrEmpty(ci.Timestamp))
                                                {
                                                    photoPageMapping[ci.Timestamp] = pageIndex;
                                                }
                                            }
                                            catch { }
                                        }
                                    }
                                }
                            }
                        }
                    }
                    catch { }
                }

                UpdateCapturedPhotosDisplay();
                try { UpdatePhotoSelectionIndicators(); } catch { }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"加载上次会话照片并绑定失败: {ex.Message}");
            }
        }

        // 视频展台按钮点击事件
        private void BtnVideoPresenter_Click(object sender, RoutedEventArgs e)
        {
            // 切换视频展台侧栏的可见性
            if (VideoPresenterSidebar.Visibility == Visibility.Visible)
            {
                VideoPresenterSidebar.Visibility = Visibility.Collapsed;
                // 注意：不再自动移除摄像头画面，让用户手动控制画面显示
                // 摄像头画面会继续显示在白板中，即使侧栏被隐藏
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
            // 注意：不再自动移除摄像头画面，让用户手动控制画面显示
            // 摄像头画面会继续显示在白板中，即使侧栏被隐藏
        }

        // 摄像头设备管理器
        private CameraDeviceManager cameraDeviceManager;
        // 当前显示的摄像头画面元素
        private System.Windows.Controls.Image currentCameraImage;
        private Dictionary<int, System.Windows.Controls.Image> cameraFramesByPage = new Dictionary<int, System.Windows.Controls.Image>();
        // 摄像头画面更新定时器
        private DispatcherTimer cameraFrameTimer;
        private const int CorrectedPaperWidth = 500;
        private const int CorrectedPaperHeight = 600;

        #region Photo Capture Functions

        // 防重复点击计时器
        private DateTime lastCaptureTime = DateTime.MinValue;
        private const int CAPTURE_COOLDOWN_MS = 1000; // 1秒冷却时间

        private void BtnCapturePhoto_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // 防重复点击检查
                if ((DateTime.Now - lastCaptureTime).TotalMilliseconds < CAPTURE_COOLDOWN_MS)
                {
                    Console.WriteLine("拍照功能冷却中，请稍后再试");
                    return;
                }

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

                // 记录本次拍照时间
                lastCaptureTime = DateTime.Now;

                // 获取当前摄像头画面的旋转角度
                double rotationAngle = 0;
                if (currentCameraImage != null && currentCameraImage.RenderTransform is TransformGroup tg)
                {
                    foreach (var t in tg.Children)
                    {
                        if (t is RotateTransform rt)
                        {
                            rotationAngle += rt.Angle;
                        }
                    }
                }

                // 在后台线程中处理图像转换
                Task.Run(() =>
                {
                    try
                    {
                        using (frame)
                        {
                            if (rotationAngle != 0)
                            {
                                System.Drawing.RotateFlipType rotateFlipType = System.Drawing.RotateFlipType.RotateNoneFlipNone;
                                if (rotationAngle % 360 == 90 || rotationAngle % 360 == -270)
                                    rotateFlipType = System.Drawing.RotateFlipType.Rotate90FlipNone;
                                else if (rotationAngle % 360 == 180 || rotationAngle % 360 == -180)
                                    rotateFlipType = System.Drawing.RotateFlipType.Rotate180FlipNone;
                                else if (rotationAngle % 360 == 270 || rotationAngle % 360 == -90)
                                    rotateFlipType = System.Drawing.RotateFlipType.Rotate270FlipNone;
                                frame.RotateFlip(rotateFlipType);
                            }
                            Bitmap toSave = frame;
                            List<AForge.IntPoint> corners;
                            if (TryDetectPaperCorners(toSave, out corners))
                            {
                                var corrected = ApplyPerspectiveCorrection(toSave, corners);
                                if (corrected != null)
                                {
                                    toSave = corrected;
                                }
                            }
                            var bitmapImage = ConvertBitmapToBitmapImage(toSave);
                            if (!ReferenceEquals(toSave, frame))
                            {
                                toSave.Dispose();
                            }
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

        private void BtnRotateImage_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // 获取当前页面上所有可旋转的图像元素
                var rotatableElements = GetRotatableElementsOnCurrentPage();
                
                if (rotatableElements.Count == 0)
                {
                    Console.WriteLine("当前页面没有可旋转的图像元素");
                    return;
                }

                // 如果有多个可旋转元素，优先旋转最近添加的或当前选中的
                UIElement elementToRotate = rotatableElements[rotatableElements.Count - 1]; // 默认旋转最后一个
                
                // 检查是否有选中的元素
                var selectedElements = InkCanvasElementsHelper.GetSelectedElements(inkCanvas);
                if (selectedElements.Count > 0)
                {
                    // 如果有选中的元素，优先旋转选中的第一个图像元素
                    var selectedImage = selectedElements.FirstOrDefault(el => el is System.Windows.Controls.Image || el is MediaElement);
                    if (selectedImage != null && rotatableElements.Contains(selectedImage))
                    {
                        elementToRotate = selectedImage;
                    }
                }
                
                // 旋转图像元素
                RotateImageElement(elementToRotate, 90);
                
                Console.WriteLine($"图像已向右旋转90度，元素类型: {elementToRotate.GetType().Name}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"旋转图像失败: {ex.Message}");
            }
        }

        private void RotateImageElement(UIElement imageElement, double angle)
        {
            FrameworkElement frameworkElement = imageElement as FrameworkElement;
            if (frameworkElement == null) return;
            
            // 获取或创建变换组
            var transformGroup = frameworkElement.RenderTransform as TransformGroup;
            if (transformGroup == null)
            {
                transformGroup = new TransformGroup();
                frameworkElement.RenderTransform = transformGroup;
            }

            // 记录初始状态
            if (!ElementsInitialHistory.ContainsKey(frameworkElement.Name))
            {
                ElementsInitialHistory[frameworkElement.Name] = transformGroup.Clone();
            }

            // 计算元素的实际中心位置
            // 获取元素的边界框，考虑现有的变换
            var bounds = frameworkElement.TransformToVisual(inkCanvas).TransformBounds(new System.Windows.Rect(0, 0, frameworkElement.ActualWidth, frameworkElement.ActualHeight));
            double centerX = bounds.Left + bounds.Width / 2;
            double centerY = bounds.Top + bounds.Height / 2;

            // 创建旋转变换，设置中心点为元素的实际中心
            var rotateTransform = new RotateTransform(angle, centerX, centerY);
            
            // 添加到变换组
            transformGroup.Children.Add(rotateTransform);

            // 记录变换历史（使用与MW_SelectionGestures.cs相同的方式）
            if (ElementsManipulationHistory == null)
            {
                ElementsManipulationHistory = new Dictionary<string, Tuple<object, TransformGroup>>();
            }
            
            ElementsManipulationHistory[frameworkElement.Name] =
                new Tuple<object, TransformGroup>(ElementsInitialHistory[frameworkElement.Name], transformGroup.Clone());
            
            // 提交变换历史
            timeMachine.CommitStrokeManipulationHistory(null, ElementsManipulationHistory);
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
                string path = SaveBitmapImageToPhotoFile(image);
                var capturedImage = string.IsNullOrEmpty(path) ? new CapturedImage(image) : new CapturedImage(image, path);
                capturedPhotos.Insert(0, capturedImage);
                UpdateCapturedPhotosDisplay();
                
                // 拍照后不立即插入照片到白板，等待用户点击照片按钮后再插入
                Console.WriteLine($"照片已保存到相册，时间戳: {capturedImage.Timestamp}");
                Console.WriteLine("请点击照片按钮将照片插入白板");
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

        // 仅同步侧栏照片的选中显示（不插入/删除画面），用于翻页或其他外部触发
        private void UpdatePhotoSelectionIndicators()
        {
            try
            {
                int currentPage = GetCurrentPageIndex();
                string timestampOnThisPage = null;

                // 查找当前页关联的照片时间戳
                foreach (var kvp in photoPageMapping)
                {
                    if (kvp.Value == currentPage)
                    {
                        timestampOnThisPage = kvp.Key;
                        break; // 每页最多一张照片
                    }
                }

                if (!string.IsNullOrEmpty(timestampOnThisPage) && capturedPhotos.Any(p => p.Timestamp == timestampOnThisPage))
                {
                    selectedPhotoTimestamp = timestampOnThisPage;
                }
                else
                {
                    selectedPhotoTimestamp = null; // 当前页没有关联照片或相册中不存在该照片
                }

                // 刷新侧栏按钮样式
                UpdateCapturedPhotosDisplay();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"同步照片选中显示失败: {ex.Message}");
            }
        }

        private Button CreatePhotoButton(CapturedImage photo)
        {
            bool isSelected = selectedPhotoTimestamp != null && selectedPhotoTimestamp == photo.Timestamp;

            var defaultBorderBrush = new SolidColorBrush(System.Windows.Media.Color.FromArgb(0x80, 0x80, 0x80, 0x80));
            var image = new System.Windows.Controls.Image
            {
                Source = photo.Thumbnail,
                Stretch = Stretch.Uniform,
                Width = 290,
                Height = 180,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };

            // 提升缩放质量，减少缩略图缩放时的模糊
            System.Windows.Media.RenderOptions.SetBitmapScalingMode(image, System.Windows.Media.BitmapScalingMode.HighQuality);

            // 选中叠加层：居中显示天蓝色☑
            var checkOverlay = new TextBlock
            {
                Text = "☑",
                Foreground = System.Windows.Media.Brushes.SkyBlue,
                FontSize = 40,
                FontWeight = FontWeights.Bold,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Visibility = isSelected ? Visibility.Visible : Visibility.Collapsed
            };

            var contentGrid = new Grid();
            contentGrid.Children.Add(image);
            contentGrid.Children.Add(checkOverlay);

            var button = new Button
            {
                Width = 300,
                Height = 200,
                Margin = new Thickness(4),
                Background = System.Windows.Media.Brushes.Transparent,
                BorderThickness = isSelected ? new Thickness(4) : new Thickness(1),
                BorderBrush = isSelected ? System.Windows.Media.Brushes.SkyBlue : defaultBorderBrush,
                Content = contentGrid,
                Tag = photo.Timestamp
            };

            button.Click += (sender, e) =>
            {
                // 更新选中状态并刷新侧栏照片样式
                selectedPhotoTimestamp = photo.Timestamp;
                UpdateCapturedPhotosDisplay();

                // 检查该照片是否已经插入过白板
                if (photoPageMapping.ContainsKey(photo.Timestamp))
                {
                    // 如果已经插入过，直接跳转到该照片所在的页码
                    int targetPage = photoPageMapping[photo.Timestamp];
                    Console.WriteLine($"照片 {photo.Timestamp} 已存在于页码 {targetPage}，正在跳转...");
                    
                    // 跳转到目标页码
                    SwitchToPage(targetPage);
                    Console.WriteLine($"已跳转到页码 {targetPage}，照片已存在，无需重新插入");
                }
                else
                {
                    // 如果当前页面已有摄像头画面或照片，先切换到下一页再插入
                    if (HasCameraFrameOrPhotoOnCurrentPage())
                    {
                        Console.WriteLine("当前页面已有摄像头画面或照片，切换到下一页插入");
                        SwitchToNextBoardAndInsertPhoto(photo);
                    }
                    else
                    {
                        // 如果没有摄像头画面或照片，直接插入到当前页面
                        Console.WriteLine("当前页面无摄像头画面或照片，直接插入到当前页面");
                        InsertPhotoToCanvas(photo);
                    }
                }
            };

            return button;
        }

        private void InsertPhotoToCanvas(CapturedImage photo)
        {
            try
            {
                // 去重判断：检查当前页面是否已经有该照片
                int currentPage = GetCurrentPageIndex();
                
                // 检查照片与页码的映射关系
                if (photoPageMapping.ContainsKey(photo.Timestamp))
                {
                    int existingPage = photoPageMapping[photo.Timestamp];
                    
                    // 如果照片已经存在于当前页面，则不再插入
                    if (existingPage == currentPage)
                    {
                        Console.WriteLine($"照片 {photo.Timestamp} 已经存在于当前页面 {currentPage}，跳过插入操作");
                        return;
                    }
                    else
                    {
                        // 如果照片存在于其他页面，更新映射关系到当前页面
                        photoPageMapping[photo.Timestamp] = currentPage;
                        Console.WriteLine($"照片 {photo.Timestamp} 从页面 {existingPage} 移动到页面 {currentPage}");
                    }
                }
                
                // 检查当前页面是否已经有照片元素或摄像头画面
                if (HasPhotoOnCurrentPage() || HasCameraFrameOnCurrentPage())
                {
                    Console.WriteLine($"当前页面 {currentPage} 已有照片或摄像头画面，将移除现有元素并插入新照片");
                    
                    // 移除当前页面的照片元素
                    if (currentPhotoImage != null)
                    {
                        inkCanvas.Children.Remove(currentPhotoImage);
                        currentPhotoImage = null;
                    }
                    
                    // 移除当前页面的摄像头画面元素
                    if (currentCameraImage != null)
                    {
                        inkCanvas.Children.Remove(currentCameraImage);
                        currentCameraImage = null;
                    }
                    
                    // 清除可能存在的其他照片元素
                    ClearPhotoElementsFromCanvas();
                    
                    // 清除可能存在的其他摄像头元素
                    ClearCameraElementsFromCanvas();
                }

                // 创建图片元素
                var imageElement = new System.Windows.Controls.Image
                {
                    Source = CreateBitmapImageFromFileOrMemory(photo),
                    Width = photo.Image.PixelWidth,
                    Height = photo.Image.PixelHeight,
                    Name = "photo_" + DateTime.Now.ToString("yyyyMMdd_HH_mm_ss_fff")
                };

                // 居中并缩放
                CenterAndScaleElement(imageElement);

                // 添加到画布
                InkCanvas.SetLeft(imageElement, 0);
                InkCanvas.SetTop(imageElement, 0);
                inkCanvas.Children.Add(imageElement);

                // 记录当前照片元素引用
                currentPhotoImage = imageElement;

                // 记录照片与页码的关联
                photoPageMapping[photo.Timestamp] = currentPage;
                Console.WriteLine($"照片已记录到页码: {currentPage}");

                // 记录历史
                timeMachine.CommitElementInsertHistory(imageElement);

                // 显示成功提示
                Console.WriteLine($"照片已成功插入白板: {photo.Timestamp}");

                // 更新选中状态与侧栏样式
                selectedPhotoTimestamp = photo.Timestamp;
                UpdateCapturedPhotosDisplay();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"插入照片失败: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
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
            
            // 设置拍照按钮的初始状态
            UpdateCapturePhotoButtonState();
            try
            {
                var cb = FindName("CheckBoxEnablePhotoCorrection") as ToggleButton;
                if (cb != null) cb.IsChecked = Settings.Automation.IsEnablePhotoCorrection;
            }
            catch { }
        }

        // 摄像头控制按钮功能已移除，仅保留设备选择功能

        // 插入摄像头画面到白板
        public async void InsertCameraFrameToCanvas()
        {
            if (cameraDeviceManager == null) return;

            int currentPage = GetCurrentPageIndex();
            if (cameraFramesByPage.TryGetValue(currentPage, out var existing) && existing != null)
            {
                if (!inkCanvas.Children.Contains(existing))
                {
                    // 检查当前页面是否已经有照片或其他摄像头画面
                    if (HasPhotoOnCurrentPage() || HasCameraFrameOnCurrentPage())
                    {
                        Console.WriteLine($"当前页面 {currentPage} 已有照片或摄像头画面，将移除现有元素并恢复摄像头画面");
                        
                        // 移除当前页面的照片元素
                        if (currentPhotoImage != null)
                        {
                            inkCanvas.Children.Remove(currentPhotoImage);
                            currentPhotoImage = null;
                        }
                        
                        // 移除当前页面的摄像头画面元素
                        if (currentCameraImage != null)
                        {
                            inkCanvas.Children.Remove(currentCameraImage);
                            currentCameraImage = null;
                        }
                        
                        // 清除可能存在的其他照片元素
                        ClearPhotoElementsFromCanvas();
                        
                        // 清除可能存在的其他摄像头元素
                        ClearCameraElementsFromCanvas();
                    }
                    
                    currentCameraImage = existing;
                    inkCanvas.Children.Add(existing);
                }
                else
                {
                    currentCameraImage = existing;
                }
                cameraFrameTimer?.Start();
                UpdateCapturePhotoButtonState();
                try { cameraDeviceManager?.HandlePageChanged(GetCurrentPageIndex()); } catch (Exception ex) { Console.WriteLine($"插入摄像头画面后刷新设备选中显示失败: {ex.Message}"); }
                return;
            }

            // 检查当前页面是否已经有照片或摄像头画面
            if (HasPhotoOnCurrentPage() || HasCameraFrameOnCurrentPage())
            {
                Console.WriteLine($"当前页面 {currentPage} 已有照片或摄像头画面，将移除现有元素并插入新摄像头画面");
                
                // 移除当前页面的照片元素
                if (currentPhotoImage != null)
                {
                    inkCanvas.Children.Remove(currentPhotoImage);
                    currentPhotoImage = null;
                }
                
                // 移除当前页面的摄像头画面元素
                if (currentCameraImage != null)
                {
                    inkCanvas.Children.Remove(currentCameraImage);
                    currentCameraImage = null;
                }
                
                // 清除可能存在的其他照片元素
                ClearPhotoElementsFromCanvas();
                
                // 清除可能存在的其他摄像头元素
                ClearCameraElementsFromCanvas();
            }

            ClearCameraElementsFromCanvas();

            bool frameInserted = false;
            for (int i = 0; i < 5; i++)
            {
                var frame = cameraDeviceManager.GetFrameCopy();
                if (frame != null)
                {
                    await InsertCameraFrameAsync(frame);
                    frame.Dispose();
                    frameInserted = true;
                    cameraFrameTimer?.Start();
                    break;
                }
                await System.Threading.Tasks.Task.Delay(500);
            }

            if (!frameInserted)
            {
                Console.WriteLine("无法获取摄像头画面，可能是摄像头未初始化完成");
            }

            UpdateCapturePhotoButtonState();

            try
            {
                cameraDeviceManager?.HandlePageChanged(GetCurrentPageIndex());
            }
            catch (Exception ex)
            {
                Console.WriteLine($"插入摄像头画面后刷新设备选中显示失败: {ex.Message}");
            }
        }

        // 检测当前页面是否有摄像头画面
        public bool HasCameraFrameOnCurrentPage()
        {
            // 首先检查currentCameraImage引用
            if (currentCameraImage != null) return true;
            
            // 如果currentCameraImage为null，再检查画布上是否有摄像头画面元素
            if (inkCanvas != null)
            {
                foreach (var child in inkCanvas.Children)
                {
                    if (child is System.Windows.Controls.Image image && 
                        image.Name != null && 
                        image.Name.StartsWith("camera_"))
                    {
                        // 找到摄像头画面元素，更新currentCameraImage引用
                        currentCameraImage = image;
                        return true;
                    }
                }
            }
            
            return false;
        }
        
        // 更新拍照按钮状态
        public void UpdateCapturePhotoButtonState()
        {
            if (BtnCapturePhoto == null) return;
            
            bool hasCameraFrame = HasCameraFrameOnCurrentPage();
            BtnCapturePhoto.IsEnabled = hasCameraFrame;
            var btnCorrect = FindName("BtnCorrectPhoto") as Button;
            if (btnCorrect != null) btnCorrect.IsEnabled = hasCameraFrame;
            
            // 根据按钮状态更新视觉样式
            if (hasCameraFrame)
            {
                BtnCapturePhoto.Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Colors.SkyBlue);
                if (btnCorrect != null) btnCorrect.Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Colors.SkyBlue);
            }
            else
            {
                BtnCapturePhoto.Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(160, 160, 160));
                if (btnCorrect != null) btnCorrect.Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(160, 160, 160));
            }
        }
        
        // 检测当前页面是否有照片
        public bool HasPhotoOnCurrentPage()
        {
            // 首先检查currentPhotoImage引用
            if (currentPhotoImage != null) return true;
            
            // 如果currentPhotoImage为null，再检查画布上是否有照片元素
            if (inkCanvas != null)
            {
                foreach (var child in inkCanvas.Children)
                {
                    if (child is System.Windows.Controls.Image image && 
                        image.Name != null && 
                        image.Name.StartsWith("photo_"))
                    {
                        // 找到照片元素，更新currentPhotoImage引用
                        currentPhotoImage = image;
                        return true;
                    }
                }
            }
            
            return false;
        }

        private BitmapImage CreateBitmapImageFromFileOrMemory(CapturedImage photo)
        {
            try
            {
                if (!string.IsNullOrEmpty(photo.FilePath) && File.Exists(photo.FilePath))
                {
                    var bi = new BitmapImage();
                    bi.BeginInit();
                    bi.UriSource = new Uri(photo.FilePath, UriKind.Absolute);
                    bi.CacheOption = BitmapCacheOption.OnLoad;
                    bi.EndInit();
                    bi.Freeze();
                    return bi;
                }
            }
            catch { }
            return photo.Image;
        }

        private string SaveBitmapImageToPhotoFile(BitmapImage image)
        {
            try
            {
                string baseDir = Settings.Automation.AutoSavedStrokesLocation + @"\Auto Saved - Photos";
                if (Settings.Automation.IsSaveScreenshotsInDateFolders)
                {
                    baseDir += @"\" + DateTime.Now.ToString("yyyy-MM-dd");
                }
                if (!Directory.Exists(baseDir)) Directory.CreateDirectory(baseDir);
                string fileName = DateTime.Now.ToString("yyyy-MM-dd HH-mm-ss-fff") + ".png";
                string path = System.IO.Path.Combine(baseDir, fileName);
                var encoder = new PngBitmapEncoder();
                encoder.Frames.Add(BitmapFrame.Create(image));
                using (var fs = new FileStream(path, FileMode.Create, FileAccess.Write))
                {
                    encoder.Save(fs);
                }
                return path;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"保存照片失败: {ex.Message}");
                return null;
            }
        }

        private void LoadSavedPhotosToSidebar()
        {
            try
            {
                string baseDir = Settings.Automation.AutoSavedStrokesLocation + @"\Auto Saved - Photos";
                var dirs = new List<string>();
                if (Directory.Exists(baseDir))
                {
                    dirs.Add(baseDir);
                    try
                    {
                        foreach (var d in Directory.GetDirectories(baseDir)) dirs.Add(d);
                    }
                    catch { }
                }
                foreach (var dir in dirs)
                {
                    foreach (var file in Directory.GetFiles(dir, "*.png"))
                    {
                        try
                        {
                            var bi = new BitmapImage();
                            bi.BeginInit();
                            bi.UriSource = new Uri(file, UriKind.Absolute);
                            bi.CacheOption = BitmapCacheOption.OnLoad;
                            bi.EndInit();
                            bi.Freeze();
                            var ci = new CapturedImage(bi, file);
                            capturedPhotos.Insert(0, ci);
                        }
                        catch { }
                    }
                }
                UpdateCapturedPhotosDisplay();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"加载已保存照片失败: {ex.Message}");
            }
        }

        

        /// <summary>
        /// 获取当前页面上所有可旋转的图像元素
        /// </summary>
        private List<UIElement> GetRotatableElementsOnCurrentPage()
        {
            var rotatableElements = new List<UIElement>();

            foreach (var element in inkCanvas.Children)
            {
                // 检查图像元素（包括摄像头画面、照片列表照片、导入媒体图片）
                if (element is System.Windows.Controls.Image imageElement)
                {
                    // 检查元素名称前缀，包括所有类型的图像元素
                    if (imageElement.Name.StartsWith("camera_") || 
                        imageElement.Name.StartsWith("photo_") || 
                        imageElement.Name.StartsWith("img_"))
                    {
                        rotatableElements.Add(imageElement);
                    }
                }
                // 检查媒体元素（视频）
                else if (element is MediaElement mediaElement)
                {
                    if (mediaElement.Name.StartsWith("media_"))
                    {
                        rotatableElements.Add(mediaElement);
                    }
                }
            }

            return rotatableElements;
        }
        
        // 检测当前页面是否有摄像头画面或照片（两者只要有一种就返回true）
        public bool HasCameraFrameOrPhotoOnCurrentPage()
        {
            return HasCameraFrameOnCurrentPage() || HasPhotoOnCurrentPage();
        }

        // 清除画布上的所有照片元素
        private void ClearPhotoElementsFromCanvas()
        {
            try
            {
                if (inkCanvas == null) return;
                
                // 收集所有照片元素
                var photoElements = new List<System.Windows.Controls.Image>();
                
                foreach (var child in inkCanvas.Children)
                {
                    if (child is System.Windows.Controls.Image image && 
                        image.Name != null && 
                        image.Name.StartsWith("photo_"))
                    {
                        photoElements.Add(image);
                    }
                }
                
                // 移除所有照片元素
                foreach (var photoElement in photoElements)
                {
                    inkCanvas.Children.Remove(photoElement);
                }
                
                // 重置当前照片引用
                currentPhotoImage = null;
                
                Console.WriteLine($"已清除画布上的 {photoElements.Count} 个照片元素");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"清除照片元素失败: {ex.Message}");
            }
        }

        // 处理页面切换时的照片显示逻辑
        private void HandlePhotoDisplayOnPageChange(int newPageIndex)
        {
            try
            {
                // 清除当前照片显示
                if (currentPhotoImage != null)
                {
                    inkCanvas.Children.Remove(currentPhotoImage);
                    currentPhotoImage = null;
                }
                
                // 检查新页面是否有关联的照片
                bool hasPhotoOnNewPage = false;
                
                // 遍历photoPageMapping字典，查找与新页面关联的照片
                foreach (var kvp in photoPageMapping)
                {
                    if (kvp.Value == newPageIndex)
                    {
                        // 找到与新页面关联的照片
                        hasPhotoOnNewPage = true;
                        
                        // 在照片集合中查找对应的照片
                        var photo = capturedPhotos.FirstOrDefault(p => p.Timestamp.Equals(kvp.Key));
                        if (photo != null)
                        {
                            // 在新页面上显示照片
                            InsertPhotoToCanvas(photo);
                            Console.WriteLine($"页码 {newPageIndex} 上的照片已恢复显示");
                            // 同步侧栏选中状态
                            selectedPhotoTimestamp = photo.Timestamp;
                            UpdateCapturedPhotosDisplay();
                        }
                        break; // 每个页面最多只能有一张照片
                    }
                }
                
                if (!hasPhotoOnNewPage)
                {
                    Console.WriteLine($"页码 {newPageIndex} 上没有关联的照片");
                    // 清除选中状态并刷新侧栏样式
                    selectedPhotoTimestamp = null;
                    UpdateCapturedPhotosDisplay();
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"处理页面切换时的照片显示失败: {ex.Message}");
            }
        }

        // 获取当前页码
        public int GetCurrentPageIndex()
        {
            return CurrentWhiteboardIndex;
        }

        // 切换到下一页白板并插入摄像头画面
        public void SwitchToNextBoardAndInsertCameraFrame()
        {
            try
            {
                // 调用白板切换功能
                BtnWhiteBoardSwitchNext_Click(null, null);
                
                // 延迟一小段时间确保白板切换完成，然后插入摄像头画面
                System.Threading.Tasks.Task.Delay(300).ContinueWith(_ =>
                {
                    Dispatcher.BeginInvoke(new Action(() =>
                    {
                        InsertCameraFrameToCanvas();
                    }));
                });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"切换到下一页并插入摄像头画面失败: {ex.Message}");
                // 如果切换失败，尝试直接插入
                InsertCameraFrameToCanvas();
            }
        }
        
        // 切换到下一页白板并插入照片
        public async void SwitchToNextBoardAndInsertPhoto(CapturedImage photo)
        {
            try
            {
                // 切换到下一页
                BtnWhiteBoardSwitchNext_Click(null, null);
                
                // 等待页面切换完成
                await System.Threading.Tasks.Task.Delay(300);
                
                // 插入照片
                InsertPhotoToCanvas(photo);

                // 插入后同步一次摄像头设备侧栏选中显示（仅视觉同步，不触发逻辑）
                try
                {
                    cameraDeviceManager?.HandlePageChanged(GetCurrentPageIndex());
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"插入照片后刷新摄像头设备选中显示失败: {ex.Message}");
                }

                // 插入后再次同步侧栏选中显示，保证首次插入场景视觉稳定
                try
                {
                    UpdatePhotoSelectionIndicators();
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"插入照片后刷新侧栏选中显示失败: {ex.Message}");
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"切换到下一页并插入照片失败: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        // 跳转到指定页码
        public void SwitchToPage(int pageIndex)
        {
            try
            {
                int currentPage = GetCurrentPageIndex();
                if (pageIndex != currentPage)
                {
                    // 保存当前页面的墨迹
                    SaveStrokes(false);
                    
                    // 清除当前画布
                    ClearStrokes(true);
                    
                    // 设置新的页码
                    CurrentWhiteboardIndex = pageIndex;
                    try { RestorePageFromDiskIfAvailable(pageIndex); } catch { }
                    
                    // 恢复新页面的墨迹
                    RestoreStrokes(false);
                    
                    // 处理页面切换时的照片显示逻辑
                    HandlePhotoDisplayOnPageChange(pageIndex);
                    // 再次仅刷新侧栏选中状态，确保视觉完全同步
                    UpdatePhotoSelectionIndicators();
                    
                    // 通知摄像头管理器页面切换
                    if (cameraDeviceManager != null)
                    {
                        cameraDeviceManager.HandlePageChanged(pageIndex);
                    }
                    
                    // 更新页面显示
                    UpdateIndexInfoDisplay();
                    
                    // 更新拍照按钮状态
                    UpdateCapturePhotoButtonState();
                    
                    Console.WriteLine($"已成功切换到页码: {pageIndex}");
                }
                else
                {
                    Console.WriteLine($"当前已在页码: {pageIndex}");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"切换到页码 {pageIndex} 失败: {ex.Message}");
            }
        }

        // 移除摄像头画面
        public void RemoveCameraFrame()
        {
            // 不停止定时器，以便翻页后可以继续使用
            
            // 移除页面上所有摄像头画面元素，避免残留导致重复
            ClearCameraElementsFromCanvas();
            
            // 更新拍照按钮状态
            UpdateCapturePhotoButtonState();
        }

        // 清除画布上的所有摄像头画面元素
        private void ClearCameraElementsFromCanvas()
        {
            try
            {
                if (inkCanvas == null) return;
                var cameraElements = new List<System.Windows.Controls.Image>();
                foreach (var child in inkCanvas.Children)
                {
                    if (child is System.Windows.Controls.Image image &&
                        image.Name != null &&
                        image.Name.StartsWith("camera_"))
                    {
                        cameraElements.Add(image);
                    }
                }
                foreach (var img in cameraElements)
                {
                    inkCanvas.Children.Remove(img);
                }
                currentCameraImage = null;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"清除摄像头画面元素失败: {ex.Message}");
            }
        }

        // 摄像头画面更新定时器事件
        private async void CameraFrameTimer_Tick(object sender, EventArgs e)
        {
            if (cameraDeviceManager == null) return;

            try
            {
                // 如果currentCameraImage为null，尝试在画布上查找摄像头画面元素
                if (currentCameraImage == null)
                {
                    foreach (var child in inkCanvas.Children)
                    {
                        if (child is System.Windows.Controls.Image image && 
                            image.Name != null && 
                            image.Name.StartsWith("camera_"))
                        {
                            currentCameraImage = image;
                            break;
                        }
                    }
                }

                // 如果仍然没有找到摄像头画面元素，停止定时器
                if (currentCameraImage == null)
                {
                    cameraFrameTimer?.Stop();
                    return;
                }

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
                if (Settings.Automation.IsEnablePhotoCorrection)
                {
                    OverlayPaperEdgesOnFrame(frame);
                }
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
                try
                {
                    var page = GetCurrentPageIndex();
                    cameraFramesByPage[page] = currentCameraImage;
                }
                catch { }
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
                if (Settings.Automation.IsEnablePhotoCorrection)
                {
                    OverlayPaperEdgesOnFrame(frame);
                }

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

        private void OverlayPaperEdgesOnFrame(Bitmap frame)
        {
            try
            {
                if (frame == null) return;
                int targetWidth = 640;
                int ow = frame.Width;
                int oh = frame.Height;
                double scale = 1.0;
                Bitmap work = frame;
                if (ow > targetWidth)
                {
                    int nh = (int)Math.Round(oh * (targetWidth / (double)ow));
                    var resize = new ResizeBilinear(targetWidth, nh);
                    work = resize.Apply(frame);
                    scale = (double)ow / targetWidth;
                }
                var gray = Grayscale.CommonAlgorithms.BT709.Apply(work);
                var blur = new GaussianBlur(3, 3);
                blur.ApplyInPlace(gray);
                var canny = new CannyEdgeDetector();
                canny.ApplyInPlace(gray);
                var dilate = new Dilatation3x3();
                dilate.ApplyInPlace(gray);
                var bc = new BlobCounter
                {
                    FilterBlobs = true,
                    MinHeight = 50,
                    MinWidth = 50,
                    ObjectsOrder = ObjectsOrder.Size
                };
                bc.ProcessImage(gray);
                var blobs = bc.GetObjectsInformation();
                var sc = new SimpleShapeChecker();
                List<AForge.IntPoint> best = null;
                double bestArea = 0;
                foreach (var blob in blobs)
                {
                    var edgePoints = bc.GetBlobsEdgePoints(blob);
                    if (edgePoints == null || edgePoints.Count < 4) continue;
                    List<AForge.IntPoint> corners;
                    if (sc.IsQuadrilateral(edgePoints, out corners))
                    {
                        double area = Math.Abs(PolygonArea(corners));
                        if (area > bestArea)
                        {
                            bestArea = area;
                            best = corners;
                        }
                    }
                }
                if (best != null)
                {
                    var pts = best.Select(p => new System.Drawing.Point((int)Math.Round(p.X * scale), (int)Math.Round(p.Y * scale))).ToList();
                    pts.Sort((a, b) => a.Y.CompareTo(b.Y));
                    if (pts[0].X > pts[1].X) { var t = pts[0]; pts[0] = pts[1]; pts[1] = t; }
                    if (pts[2].X > pts[3].X) { var t = pts[2]; pts[2] = pts[3]; pts[3] = t; }
                    using (var g = Graphics.FromImage(frame))
                    {
                        g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                        using (var pen = new System.Drawing.Pen(System.Drawing.Color.Lime, 3))
                        {
                            g.DrawPolygon(pen, pts.ToArray());
                        }
                        using (var brush = new System.Drawing.SolidBrush(System.Drawing.Color.Red))
                        {
                            foreach (var p in pts)
                            {
                                g.FillEllipse(brush, p.X - 6, p.Y - 6, 12, 12);
                            }
                        }
                        using (var font = new System.Drawing.Font(System.Drawing.FontFamily.GenericSansSerif, 18f, System.Drawing.FontStyle.Bold))
                        using (var labBrush = new System.Drawing.SolidBrush(System.Drawing.Color.Lime))
                        {
                            for (int i = 0; i < 4; i++)
                            {
                                g.DrawString((i + 1).ToString(), font, labBrush, pts[i]);
                            }
                        }
                    }
                }
                if (!ReferenceEquals(work, frame)) work.Dispose();
                gray.Dispose();
            }
            catch { }
        }

        private bool TryDetectPaperCorners(Bitmap frame, out List<AForge.IntPoint> cornersOut)
        {
            cornersOut = null;
            try
            {
                if (frame == null) return false;
                int targetWidth = 640;
                int ow = frame.Width;
                int oh = frame.Height;
                double scale = 1.0;
                Bitmap work = frame;
                if (ow > targetWidth)
                {
                    int nh = (int)Math.Round(oh * (targetWidth / (double)ow));
                    var resize = new ResizeBilinear(targetWidth, nh);
                    work = resize.Apply(frame);
                    scale = (double)ow / targetWidth;
                }
                var gray = Grayscale.CommonAlgorithms.BT709.Apply(work);
                var blur = new GaussianBlur(3, 3);
                blur.ApplyInPlace(gray);
                var canny = new CannyEdgeDetector();
                canny.ApplyInPlace(gray);
                var dilate = new Dilatation3x3();
                dilate.ApplyInPlace(gray);
                var bc = new BlobCounter
                {
                    FilterBlobs = true,
                    MinHeight = 50,
                    MinWidth = 50,
                    ObjectsOrder = ObjectsOrder.Size
                };
                bc.ProcessImage(gray);
                var blobs = bc.GetObjectsInformation();
                var sc = new SimpleShapeChecker();
                List<AForge.IntPoint> best = null;
                double bestArea = 0;
                foreach (var blob in blobs)
                {
                    var edgePoints = bc.GetBlobsEdgePoints(blob);
                    if (edgePoints == null || edgePoints.Count < 4) continue;
                    List<AForge.IntPoint> crn;
                    if (sc.IsQuadrilateral(edgePoints, out crn))
                    {
                        double area = Math.Abs(PolygonArea(crn));
                        if (area > bestArea)
                        {
                            bestArea = area;
                            best = crn;
                        }
                    }
                }
                if (best != null)
                {
                    var pts = best
                        .Select(p => new AForge.IntPoint((int)Math.Round(p.X * scale), (int)Math.Round(p.Y * scale)))
                        .ToList();
                    pts.Sort((a, b) => a.Y.CompareTo(b.Y));
                    if (pts[0].X > pts[1].X) { var t = pts[0]; pts[0] = pts[1]; pts[1] = t; }
                    if (pts[2].X > pts[3].X) { var t = pts[2]; pts[2] = pts[3]; pts[3] = t; }
                    cornersOut = pts;
                    if (!ReferenceEquals(work, frame)) work.Dispose();
                    gray.Dispose();
                    return true;
                }
                if (!ReferenceEquals(work, frame)) work.Dispose();
                gray.Dispose();
                return false;
            }
            catch
            {
                return false;
            }
        }

        private Bitmap ApplyPerspectiveCorrection(Bitmap frame, List<AForge.IntPoint> corners)
        {
            try
            {
                if (frame == null || corners == null || corners.Count != 4) return null;
                var tl = corners[0];
                var tr = corners[1];
                var bl = corners[2];
                var br = corners[3];

                double topW = Math.Sqrt((tr.X - tl.X) * (tr.X - tl.X) + (tr.Y - tl.Y) * (tr.Y - tl.Y));
                double bottomW = Math.Sqrt((br.X - bl.X) * (br.X - bl.X) + (br.Y - bl.Y) * (br.Y - bl.Y));
                double leftH = Math.Sqrt((bl.X - tl.X) * (bl.X - tl.X) + (bl.Y - tl.Y) * (bl.Y - tl.Y));
                double rightH = Math.Sqrt((br.X - tr.X) * (br.X - tr.X) + (br.Y - tr.Y) * (br.Y - tr.Y));

                double avgW = (topW + bottomW) / 2.0;
                double avgH = (leftH + rightH) / 2.0;
                if (avgH <= 0) avgH = 1;
                double ratio = avgW / avgH;

                int targetH = CorrectedPaperHeight;
                int targetW = Math.Max(1, (int)Math.Round(targetH * ratio));

                var orderedCorners = new List<AForge.IntPoint> { tl, tr, br, bl };
                var qtf = new QuadrilateralTransformation(orderedCorners, targetW, targetH);
                return qtf.Apply(frame);
            }
            catch { return null; }
        }

        private static double PolygonArea(List<AForge.IntPoint> pts)
        {
            int n = pts.Count;
            if (n < 3) return 0;
            long sum = 0;
            for (int i = 0; i < n; i++)
            {
                var p = pts[i];
                var q = pts[(i + 1) % n];
                sum += (long)p.X * q.Y - (long)p.Y * q.X;
            }
            return 0.5 * sum;
        }

        private void BtnCorrectPhoto_Click(object sender, RoutedEventArgs e)
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

                double rotationAngle = 0;
                if (currentCameraImage != null && currentCameraImage.RenderTransform is TransformGroup tg)
                {
                    foreach (var t in tg.Children)
                    {
                        if (t is RotateTransform rt)
                        {
                            rotationAngle += rt.Angle;
                        }
                    }
                }

                Task.Run(() =>
                {
                    try
                    {
                        using (frame)
                        {
                            if (rotationAngle != 0)
                            {
                                System.Drawing.RotateFlipType rotateFlipType = System.Drawing.RotateFlipType.RotateNoneFlipNone;
                                if (rotationAngle % 360 == 90 || rotationAngle % 360 == -270)
                                    rotateFlipType = System.Drawing.RotateFlipType.Rotate90FlipNone;
                                else if (rotationAngle % 360 == 180 || rotationAngle % 360 == -180)
                                    rotateFlipType = System.Drawing.RotateFlipType.Rotate180FlipNone;
                                else if (rotationAngle % 360 == 270 || rotationAngle % 360 == -90)
                                    rotateFlipType = System.Drawing.RotateFlipType.Rotate270FlipNone;
                                frame.RotateFlip(rotateFlipType);
                            }
                            Bitmap toSave = frame;
                            List<AForge.IntPoint> corners;
                            if (TryDetectPaperCorners(toSave, out corners))
                            {
                                var corrected = ApplyPerspectiveCorrection(toSave, corners);
                                if (corrected != null)
                                {
                                    toSave = corrected;
                                }
                            }
                            var bitmapImage = ConvertBitmapToBitmapImage(toSave);
                            if (!ReferenceEquals(toSave, frame))
                            {
                                toSave.Dispose();
                            }
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
                        Console.WriteLine($"矫正拍照处理失败: {ex.Message}");
                    }
                });
            }
            catch (Exception ex)
            {
                MessageBox.Show($"矫正拍照失败: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void CheckBoxEnablePhotoCorrection_Checked(object sender, RoutedEventArgs e)
        {
            Settings.Automation.IsEnablePhotoCorrection = true;
            SaveSettingsToFile();
        }

        private void CheckBoxEnablePhotoCorrection_Unchecked(object sender, RoutedEventArgs e)
        {
            Settings.Automation.IsEnablePhotoCorrection = false;
            SaveSettingsToFile();
        }
    }
}
