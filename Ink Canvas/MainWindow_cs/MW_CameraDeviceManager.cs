using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using AForge.Video;
using AForge.Video.DirectShow;
using System.Windows.Threading;
using System.Drawing;

namespace Ink_Canvas.MainWindow_cs
{
    public class CameraDeviceManager
    {
        private FilterInfoCollection videoDevices;
        private VideoCaptureDevice currentVideoDevice;
        private readonly MainWindow mainWindow;
        private string selectedDeviceName;
        // 自动同步设备选中状态时，用于抑制选中事件处理逻辑
        private bool suppressSelectionHandlers = false;
        private DateTime lastFrameTime = DateTime.MinValue;
        private readonly object frameLock = new object();
        private Bitmap currentFrame;
        public const double MinFrameIntervalMs = 33; // ~30fps
        public event Action<Bitmap> OnNewFrameProcessed;
        
        // 摄像头设备与页码的关联字典
        private readonly Dictionary<string, int> cameraPageMapping = new Dictionary<string, int>();

        // 获取板幕布侧栏的前景色画刷（优先从窗口资源，其次应用资源）
        private System.Windows.Media.Brush GetBoardBarForegroundBrush()
        {
            try
            {
                var fromWindow = mainWindow?.TryFindResource("BoardBarForeground");
                if (fromWindow is System.Windows.Media.Brush b1) return b1;

                var fromApp = Application.Current?.TryFindResource("BoardBarForeground");
                if (fromApp is System.Windows.Media.Brush b2) return b2;
            }
            catch { /* 忽略资源查找异常，使用回退颜色 */ }
            return new System.Windows.Media.SolidColorBrush(System.Windows.Media.Colors.Black);
        }

        public CameraDeviceManager(MainWindow window)
        {
            mainWindow = window;
            selectedDeviceName = "";
        }

        public List<string> GetAvailableCameras()
        {
            List<string> cameras = new List<string>();
            try
            {
                videoDevices = new FilterInfoCollection(FilterCategory.VideoInputDevice);
                if (videoDevices.Count == 0)
                {
                    cameras.Add("未检测到摄像头设备");
                }
                else
                {
                    foreach (FilterInfo device in videoDevices)
                    {
                        cameras.Add(device.Name);
                    }
                }
            }
            catch (Exception ex)
            {
                cameras.Add("检测设备失败: " + ex.Message);
            }
            return cameras;
        }

        public void RefreshCameraDevices()
        {
            var cameras = GetAvailableCameras();
            UpdateCameraDeviceList(cameras);
        }

        public void UpdateCameraDeviceList(List<string> cameras)
        {
            mainWindow.Dispatcher.BeginInvoke(new Action(() =>
            {
                var stackPanel = mainWindow.CameraDevicesStackPanel;
                stackPanel.Children.Clear();

                if (cameras.Count == 0 || cameras[0].Contains("未检测到") || cameras[0].Contains("检测设备失败"))
                {
                    var noDeviceText = new TextBlock
                    {
                        Text = cameras.Count > 0 ? cameras[0] : "未检测到摄像头设备",
                        FontSize = 12,
                        Margin = new Thickness(5),
                        HorizontalAlignment = HorizontalAlignment.Center
                    };
                    // 动态资源绑定，确保主题切换时自动更新颜色
                    noDeviceText.SetResourceReference(TextBlock.ForegroundProperty, "BoardBarForeground");
                    stackPanel.Children.Add(noDeviceText);
                    return;
                }

                foreach (var cameraName in cameras)
                {
                    var radioButton = new RadioButton
                    {
                        Content = cameraName,
                        Margin = new Thickness(0, 2, 0, 2),
                        FontSize = 12,
                        IsChecked = cameraName == selectedDeviceName
                    };
                    // 动态资源绑定，确保主题切换时自动更新颜色
                    radioButton.SetResourceReference(Control.ForegroundProperty, "BoardBarForeground");

                    radioButton.Checked += (sender, e) =>
                    {
                        if (suppressSelectionHandlers) return; // 自动同步时仅更新显示，不触发逻辑
                        selectedDeviceName = cameraName;
                        OnCameraSelected(cameraName);
                    };

                    radioButton.Unchecked += (sender, e) =>
                    {
                        if (suppressSelectionHandlers) return; // 自动同步时仅更新显示，不触发逻辑
                        if (selectedDeviceName == cameraName)
                        {
                            OnCameraDeselected();
                        }
                    };

                    stackPanel.Children.Add(radioButton);
                }
            }));
        }

        public void StartCamera(string deviceName)
        {
            try
            {
                if (string.IsNullOrEmpty(deviceName))
                {
                    Console.WriteLine("设备名称为空");
                    return;
                }

                if (videoDevices == null)
                {
                    videoDevices = new FilterInfoCollection(FilterCategory.VideoInputDevice);
                }

                if (videoDevices.Count == 0)
                {
                    Console.WriteLine("未找到摄像头设备");
                    return;
                }

                // 如果当前设备已经在运行且设备名称相同，无需重新启动
                if (currentVideoDevice != null && currentVideoDevice.IsRunning && selectedDeviceName == deviceName)
                {
                    Console.WriteLine($"摄像头设备 {deviceName} 已在运行，无需重新启动");
                    return;
                }

                if (currentVideoDevice != null && currentVideoDevice.IsRunning)
                {
                    currentVideoDevice.SignalToStop();
                    currentVideoDevice.WaitForStop();
                }

                bool deviceFound = false;
                foreach (FilterInfo device in videoDevices)
                {
                    if (device.Name == deviceName)
                    {
                        currentVideoDevice = new VideoCaptureDevice(device.MonikerString);
                        currentVideoDevice.NewFrame += Device_NewFrame;
                        currentVideoDevice.Start();
                        selectedDeviceName = deviceName;
                        deviceFound = true;
                        break;
                    }
                }

                if (!deviceFound)
                {
                    Console.WriteLine($"未找到指定设备: {deviceName}");
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"启动摄像头失败: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void Device_NewFrame(object sender, NewFrameEventArgs e)
        {
            try
            {
                double elapsed = (DateTime.Now - lastFrameTime).TotalMilliseconds;
                if (elapsed < MinFrameIntervalMs) return;
                lastFrameTime = DateTime.Now;

                lock (frameLock)
                {
                    currentFrame?.Dispose();
                    currentFrame = (Bitmap)e.Frame.Clone();
                }
                
                var frameCopy = GetFrameCopy();
                if (frameCopy != null)
                {
                    OnNewFrameProcessed?.Invoke(frameCopy);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"摄像头帧处理错误: {ex.Message}");
            }
        }

        public Bitmap GetFrameCopy()
        {
            lock (frameLock)
            {
                if (currentFrame == null) return null;
                
                try
                {
                    return new Bitmap(currentFrame);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"复制帧失败: {ex.Message}");
                    return null;
                }
            }
        }

        public void StopCamera()
        {
            try
            {
                if (currentVideoDevice != null && currentVideoDevice.IsRunning)
                {
                    currentVideoDevice.NewFrame -= Device_NewFrame;
                    currentVideoDevice.SignalToStop();
                    currentVideoDevice.WaitForStop();
                    currentVideoDevice = null;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"停止摄像头失败: {ex.Message}");
                currentVideoDevice = null;
            }
        }

        public string GetCurrentCameraName()
        {
            return selectedDeviceName;
        }

        // 获取当前页码
        private int GetCurrentPageIndex()
        {
            int currentPage = 1;
            mainWindow.Dispatcher.Invoke(new Action(() =>
            {
                // 通过反射或其他方式获取当前页码
                // 这里假设MainWindow有CurrentWhiteboardIndex属性
                var field = mainWindow.GetType().GetField("CurrentWhiteboardIndex", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                if (field != null)
                {
                    currentPage = (int)field.GetValue(mainWindow);
                }
            }));
            return currentPage;
        }

        // 跳转到指定页码（统一走主窗口逻辑，确保侧栏状态与照片显示同步）
        private void SwitchToPage(int pageIndex)
        {
            mainWindow.Dispatcher.BeginInvoke(new Action(() =>
            {
                try
                {
                    mainWindow.SwitchToPage(pageIndex);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"通过主窗口切换到页码 {pageIndex} 失败: {ex.Message}");
                }
            }));
        }

        private void OnCameraSelected(string deviceName)
        {
            // 检查该摄像头设备是否已经插入过白板
            if (cameraPageMapping.ContainsKey(deviceName))
            {
                // 如果已经插入过，直接跳转到该设备所在的页码
                int targetPage = cameraPageMapping[deviceName];
                Console.WriteLine($"摄像头设备 {deviceName} 已存在于页码 {targetPage}，正在跳转...");
                
                // 启动摄像头
                selectedDeviceName = deviceName;
                StartCamera(deviceName);
                
                // 减少等待时间，从1000ms减到300ms，快速响应
                System.Threading.Tasks.Task.Delay(300).ContinueWith(_ =>
                {
                    // 合并多个Dispatcher调用，减少UI线程负担
                    mainWindow.Dispatcher.BeginInvoke(new Action(() =>
                    {
                        // 跳转到目标页码
                        SwitchToPage(targetPage);
                        Console.WriteLine($"已跳转到页码 {targetPage}，摄像头画面已存在，无需重新插入");
                        
                        // 合并检查和启动定时器的操作
                        bool hasCurrentCameraFrame = mainWindow.HasCameraFrameOnCurrentPage();
                        
                        // 一次性获取timer字段，避免重复反射
                        var cameraFrameTimerField = mainWindow.GetType().GetField("cameraFrameTimer", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                        if (cameraFrameTimerField != null)
                        {
                            var cameraFrameTimer = cameraFrameTimerField.GetValue(mainWindow) as System.Windows.Threading.DispatcherTimer;
                            cameraFrameTimer?.Start();
                            Console.WriteLine(hasCurrentCameraFrame ? "已启动摄像头画面定时器更新" : $"已恢复摄像头画面显示到页码 {targetPage}");
                        }
                    }));
                });
                return;
            }
            
            // 先检测当前页面是否有摄像头画面或照片（在启动摄像头之前检测）
            bool hasCameraFrameOrPhoto = false;
            int currentPage = GetCurrentPageIndex();
            
            // 增强检测逻辑，检查photoPageMapping中是否有与当前页面关联的照片
            mainWindow.Dispatcher.Invoke(new Action(() =>
            {
                // 首先检查画布上的元素
                hasCameraFrameOrPhoto = mainWindow.HasCameraFrameOrPhotoOnCurrentPage();
                
                // 然后检查是否有照片与当前页面关联（通过反射访问photoPageMapping）
                if (!hasCameraFrameOrPhoto)
                {
                    try
                    {
                        var photoPageMappingField = mainWindow.GetType().GetField("photoPageMapping", 
                            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                        if (photoPageMappingField != null)
                        {
                            var photoPageMapping = photoPageMappingField.GetValue(mainWindow) as System.Collections.Generic.Dictionary<string, int>;
                            if (photoPageMapping != null && photoPageMapping.ContainsValue(currentPage))
                            {
                                hasCameraFrameOrPhoto = true;
                                Console.WriteLine($"检测到当前页面 {currentPage} 在photoPageMapping中有关联照片");
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"检查photoPageMapping失败: {ex.Message}");
                    }
                }
            }));
            
            // 启动摄像头
            selectedDeviceName = deviceName;
            StartCamera(deviceName);
            
            // 减少等待时间，从1000ms减到300ms，快速响应
            System.Threading.Tasks.Task.Delay(300).ContinueWith(_ =>
            {
                mainWindow.Dispatcher.BeginInvoke(new Action(() =>
                {
                    if (hasCameraFrameOrPhoto)
                    {
                        // 如果当前页面已有摄像头画面或照片，先切换到下一页再插入
                        mainWindow.SwitchToNextBoardAndInsertCameraFrame();
                        
                        // 记录设备与页码的关联（新插入的页码）
                        int newPage = GetCurrentPageIndex();
                        if (newPage != currentPage)
                        {
                            cameraPageMapping[deviceName] = newPage;
                            Console.WriteLine($"摄像头设备 {deviceName} 已插入到页码 {newPage}");
                        }
                    }
                    else
                    {
                        // 如果没有摄像头画面或照片，直接插入
                        mainWindow.InsertCameraFrameToCanvas();
                        
                        // 记录设备与页码的关联（当前页码）
                        cameraPageMapping[deviceName] = currentPage;
                        Console.WriteLine($"摄像头设备 {deviceName} 已插入到页码 {currentPage}");
                    }
                }));
            });
        }

        private void OnCameraDeselected()
        {
            // 停止摄像头并移除画面
            selectedDeviceName = "";
            StopCamera();
            
            // 通知主窗口移除摄像头画面
            mainWindow.Dispatcher.BeginInvoke(new Action(() =>
            {
                mainWindow.RemoveCameraFrame();
            }));
        }

        // 页面切换事件处理
        public void HandlePageChanged(int newPageIndex)
        {
            // 检查新页面是否有摄像头画面或照片
            bool hasCameraFrameOrPhotoOnNewPage = false;
            string cameraDeviceOnNewPage = null;
            
            // 查找新页面是否有关联的摄像头设备
            foreach (var mapping in cameraPageMapping)
            {
                if (mapping.Value == newPageIndex)
                {
                    hasCameraFrameOrPhotoOnNewPage = true;
                    cameraDeviceOnNewPage = mapping.Key;
                    break;
                }
            }
            
            // 如果没有摄像头设备关联，检查是否有照片关联
            if (!hasCameraFrameOrPhotoOnNewPage)
            {
                try
                {
                    // 通过反射检查photoPageMapping中是否有与新页面关联的照片
                    var photoPageMappingField = mainWindow.GetType().GetField("photoPageMapping",
                        System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                    if (photoPageMappingField != null)
                    {
                        var photoPageMapping = photoPageMappingField.GetValue(mainWindow) as System.Collections.Generic.Dictionary<string, int>;
                        if (photoPageMapping != null && photoPageMapping.ContainsValue(newPageIndex))
                        {
                            hasCameraFrameOrPhotoOnNewPage = true;
                            Console.WriteLine($"页面切换时检测到新页面 {newPageIndex} 在photoPageMapping中有关联照片");
                        }
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"页面切换时检查photoPageMapping失败: {ex.Message}");
                }
            }

            // 如果新页面有摄像头画面或照片，恢复摄像头显示
            if (hasCameraFrameOrPhotoOnNewPage && cameraDeviceOnNewPage != null)
            {
                Console.WriteLine($"切换到页码 {newPageIndex}，检测到摄像头画面，恢复摄像头显示");
                
                // 检查当前运行的摄像头设备是否与新页面的设备一致
                bool needToSwitchCamera = currentVideoDevice == null || 
                                         !currentVideoDevice.IsRunning || 
                                         selectedDeviceName != cameraDeviceOnNewPage;
                
                if (needToSwitchCamera)
                {
                    // 如果摄像头未运行或设备不一致，启动新摄像头
                    StartCamera(cameraDeviceOnNewPage);
                    
                    // 在主线程上恢复画面显示，如页面已有画面则仅启动更新，否则插入
                    mainWindow.Dispatcher.BeginInvoke(new Action(() =>
                    {
                        if (mainWindow.HasCameraFrameOnCurrentPage())
                        {
                            var cameraFrameTimerField = mainWindow.GetType().GetField("cameraFrameTimer", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                            if (cameraFrameTimerField != null)
                            {
                                var cameraFrameTimer = cameraFrameTimerField.GetValue(mainWindow) as System.Windows.Threading.DispatcherTimer;
                                cameraFrameTimer?.Start();
                                Console.WriteLine($"摄像头设备 {cameraDeviceOnNewPage} 已启动并恢复旧画面更新");
                            }
                        }
                        else
                        {
                            mainWindow.InsertCameraFrameToCanvas();
                        }
                    }));
                }
                else
                {
                    // 如果已经是正确的摄像头设备在运行，只需恢复画面显示和启动定时器
                    Console.WriteLine($"摄像头设备 {cameraDeviceOnNewPage} 已在运行，直接恢复画面显示");
                    
                    // 统一在主线程处理，避免多次调用Dispatcher
                    mainWindow.Dispatcher.BeginInvoke(new Action(() =>
                    {
                        // 检查当前页面是否已经有摄像头画面或照片
                        bool hasCurrentCameraFrameOrPhoto = mainWindow.HasCameraFrameOrPhotoOnCurrentPage();
                        Console.WriteLine($"HasCameraFrameOrPhotoOnCurrentPage返回: {hasCurrentCameraFrameOrPhoto}");
                        
                        if (hasCurrentCameraFrameOrPhoto)
                        {
                            // 如果当前页面有摄像头画面或照片，只需启动定时器更新画面
                            Console.WriteLine("当前页面已有摄像头画面或照片，启动定时器更新");
                            
                            // 启动定时器持续更新画面
                            var cameraFrameTimerField = mainWindow.GetType().GetField("cameraFrameTimer", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                            if (cameraFrameTimerField != null)
                            {
                                var cameraFrameTimer = cameraFrameTimerField.GetValue(mainWindow) as System.Windows.Threading.DispatcherTimer;
                                cameraFrameTimer?.Start();
                                Console.WriteLine("已启动摄像头画面定时器更新");
                            }
                        }
                        else
                        {
                            // 如果当前页面没有摄像头画面或照片，需要插入新的摄像头画面
                            Console.WriteLine("当前页面无摄像头画面或照片，插入新的摄像头画面");
                            
                            // 插入摄像头画面
                            mainWindow.InsertCameraFrameToCanvas();
                        }
                    }));
                }

                // 自动同步设备列表的选中状态（仅更新显示，不触发选中逻辑）
                mainWindow.Dispatcher.BeginInvoke(new Action(() =>
                {
                    suppressSelectionHandlers = true;
                    selectedDeviceName = cameraDeviceOnNewPage;
                    var stackPanel = mainWindow.CameraDevicesStackPanel;
                    foreach (var child in stackPanel.Children)
                    {
                        if (child is RadioButton rb)
                        {
                            var name = rb.Content?.ToString();
                            rb.IsChecked = !string.IsNullOrEmpty(selectedDeviceName) && name == selectedDeviceName;
                        }
                    }
                    suppressSelectionHandlers = false;
                }));
            }
            else
            {
                // 如果新页面没有摄像头画面或照片，停止摄像头设备以节省资源
                Console.WriteLine($"切换到页码 {newPageIndex}，无摄像头画面或照片，停止摄像头设备以节省资源");
                
                // 停止摄像头设备以释放资源
                if (currentVideoDevice != null && currentVideoDevice.IsRunning)
                {
                    StopCamera();
                    Console.WriteLine("摄像头设备已停止，资源已释放");
                }
                
                // 停止摄像头画面定时器，进一步减少资源占用
                var cameraFrameTimerFieldOnEmptyPage = mainWindow.GetType().GetField("cameraFrameTimer", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                if (cameraFrameTimerFieldOnEmptyPage != null)
                {
                    var cameraFrameTimer = cameraFrameTimerFieldOnEmptyPage.GetValue(mainWindow) as System.Windows.Threading.DispatcherTimer;
                    cameraFrameTimer?.Stop();
                    Console.WriteLine("已停止摄像头画面定时器（当前页无摄像头画面/照片）");
                }
                
                // 检查当前页面是否已经有摄像头画面或照片，避免重复移除
                bool hasCurrentCameraFrameOrPhoto = false;
                mainWindow.Dispatcher.Invoke(new Action(() =>
                {
                    hasCurrentCameraFrameOrPhoto = mainWindow.HasCameraFrameOrPhotoOnCurrentPage();
                }));
                
                if (hasCurrentCameraFrameOrPhoto)
                {
                    // 通知主窗口移除摄像头画面
                    mainWindow.Dispatcher.BeginInvoke(new Action(() =>
                    {
                        mainWindow.RemoveCameraFrame();
                    }));
                }

                // 自动取消设备列表选中状态（仅更新显示，不触发选中逻辑）
                mainWindow.Dispatcher.BeginInvoke(new Action(() =>
                {
                    suppressSelectionHandlers = true;
                    selectedDeviceName = ""; // 清空选中设备名称
                    var stackPanel = mainWindow.CameraDevicesStackPanel;
                    foreach (var child in stackPanel.Children)
                    {
                        if (child is RadioButton rb)
                        {
                            rb.IsChecked = false;
                        }
                    }
                    suppressSelectionHandlers = false;
                }));
            }
            
            // 更新拍照按钮状态
            mainWindow.Dispatcher.BeginInvoke(new Action(() =>
            {
                mainWindow.UpdateCapturePhotoButtonState();
            }));
        }
        
        // 退出按钮点击事件处理
        public void HandleExitButtonClicked()
        {
            Console.WriteLine("退出按钮点击，暂停摄像头显示以节省资源");
            
            // 停止摄像头设备以节省资源
            StopCamera();
            
            // 停止摄像头画面定时器，防止空转占用资源
            var cameraFrameTimerField = mainWindow.GetType().GetField("cameraFrameTimer", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            if (cameraFrameTimerField != null)
            {
                var cameraFrameTimer = cameraFrameTimerField.GetValue(mainWindow) as System.Windows.Threading.DispatcherTimer;
                cameraFrameTimer?.Stop();
                Console.WriteLine("已停止摄像头画面定时器（退出白板/应用）");
            }
            
            // 通知主窗口移除摄像头画面
            mainWindow.Dispatcher.BeginInvoke(new Action(() =>
            {
                mainWindow.RemoveCameraFrame();
            }));
        }

        public void Dispose()
        {
            StopCamera();
            lock (frameLock)
            {
                currentFrame?.Dispose();
                currentFrame = null;
            }
            videoDevices = null;
        }
    }
}
