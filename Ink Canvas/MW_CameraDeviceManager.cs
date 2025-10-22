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

namespace Ink_Canvas
{
    public class CameraDeviceManager
    {
        private FilterInfoCollection videoDevices;
        private VideoCaptureDevice currentVideoDevice;
        private MainWindow mainWindow;
        private string selectedDeviceName;
        private DateTime lastFrameTime = DateTime.MinValue;
        private readonly object frameLock = new object();
        private Bitmap currentFrame;
        public const double MinFrameIntervalMs = 33; // ~30fps
        public event Action<Bitmap> OnNewFrameProcessed;
        
        // 摄像头设备与页码的关联字典
        private Dictionary<string, int> cameraPageMapping = new Dictionary<string, int>();

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
                        Foreground = new SolidColorBrush(Colors.Gray),
                        FontSize = 12,
                        Margin = new Thickness(5),
                        HorizontalAlignment = HorizontalAlignment.Center
                    };
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

                    radioButton.Checked += (sender, e) =>
                    {
                        selectedDeviceName = cameraName;
                        OnCameraSelected(cameraName);
                    };

                    radioButton.Unchecked += (sender, e) =>
                    {
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

        // 跳转到指定页码
        private void SwitchToPage(int pageIndex)
        {
            mainWindow.Dispatcher.BeginInvoke(new Action(() =>
            {
                // 通过反射获取CurrentWhiteboardIndex字段并设置页码
                var field = mainWindow.GetType().GetField("CurrentWhiteboardIndex", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                if (field != null)
                {
                    int currentPage = (int)field.GetValue(mainWindow);
                    if (pageIndex != currentPage)
                    {
                        // 保存当前页面的墨迹
                        var saveMethod = mainWindow.GetType().GetMethod("SaveStrokes", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                        if (saveMethod != null)
                        {
                            saveMethod.Invoke(mainWindow, new object[] { false });
                        }
                        
                        // 清除当前画布
                        var clearMethod = mainWindow.GetType().GetMethod("ClearStrokes", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                        if (clearMethod != null)
                        {
                            clearMethod.Invoke(mainWindow, new object[] { true });
                        }
                        
                        // 设置新的页码
                        field.SetValue(mainWindow, pageIndex);
                        
                        // 恢复新页面的墨迹
                        var restoreMethod = mainWindow.GetType().GetMethod("RestoreStrokes", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                        if (restoreMethod != null)
                        {
                            restoreMethod.Invoke(mainWindow, new object[] { false });
                        }
                        
                        // 更新页面显示
                        var updateMethod = mainWindow.GetType().GetMethod("UpdateIndexInfoDisplay", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                        if (updateMethod != null)
                        {
                            updateMethod.Invoke(mainWindow, null);
                        }
                        
                        Console.WriteLine($"已成功切换到页码: {pageIndex}");
                        
                        // 调用页面切换事件处理，确保摄像头画面正确显示
                        HandlePageChanged(pageIndex);
                    }
                    else
                    {
                        Console.WriteLine($"当前已在页码: {pageIndex}");
                    }
                }
                else
                {
                    Console.WriteLine($"无法切换到页码: {pageIndex}，未找到CurrentWhiteboardIndex字段");
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
                
                // 等待一段时间确保摄像头初始化完成，然后跳转到目标页码
                System.Threading.Tasks.Task.Delay(1000).ContinueWith(_ =>
                {
                    mainWindow.Dispatcher.BeginInvoke(new Action(() =>
                    {
                        // 跳转到目标页码
                        SwitchToPage(targetPage);
                        Console.WriteLine($"已跳转到页码 {targetPage}，摄像头画面已存在，无需重新插入");
                        
                        // 检查当前页面是否已经有摄像头画面
                        bool hasCurrentCameraFrame = false;
                        mainWindow.Dispatcher.Invoke(new Action(() =>
                        {
                            hasCurrentCameraFrame = mainWindow.HasCameraFrameOnCurrentPage();
                        }));
                        
                        if (!hasCurrentCameraFrame)
                        {
                            // 如果当前页面没有摄像头画面，只需启动定时器恢复显示，不插入新画面
                            mainWindow.Dispatcher.BeginInvoke(new Action(() =>
                            {
                                // 启动定时器持续更新画面
                                var cameraFrameTimerField = mainWindow.GetType().GetField("cameraFrameTimer", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                                if (cameraFrameTimerField != null)
                                {
                                    var cameraFrameTimer = cameraFrameTimerField.GetValue(mainWindow) as System.Windows.Threading.DispatcherTimer;
                                    cameraFrameTimer?.Start();
                                    Console.WriteLine($"已恢复摄像头画面显示到页码 {targetPage}");
                                }
                            }));
                        }
                        else
                        {
                            // 如果已经有摄像头画面，只需启动定时器更新
                            mainWindow.Dispatcher.BeginInvoke(new Action(() =>
                            {
                                // 启动定时器持续更新画面
                                var cameraFrameTimerField = mainWindow.GetType().GetField("cameraFrameTimer", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                                if (cameraFrameTimerField != null)
                                {
                                    var cameraFrameTimer = cameraFrameTimerField.GetValue(mainWindow) as System.Windows.Threading.DispatcherTimer;
                                    cameraFrameTimer?.Start();
                                    Console.WriteLine($"已启动摄像头画面定时器更新");
                                }
                            }));
                        }
                    }));
                });
                return;
            }
            
            // 先检测当前页面是否有摄像头画面或照片（在启动摄像头之前检测）
            bool hasCameraFrameOrPhoto = false;
            int currentPage = GetCurrentPageIndex();
            mainWindow.Dispatcher.Invoke(new Action(() =>
            {
                hasCameraFrameOrPhoto = mainWindow.HasCameraFrameOrPhotoOnCurrentPage();
            }));
            
            // 启动摄像头
            selectedDeviceName = deviceName;
            StartCamera(deviceName);
            
            // 等待一段时间确保摄像头初始化完成并有帧数据，然后通知主窗口插入摄像头画面
            System.Threading.Tasks.Task.Delay(1000).ContinueWith(_ =>
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
            // 检查新页面是否有摄像头画面
            bool hasCameraFrameOnNewPage = false;
            string cameraDeviceOnNewPage = null;
            
            // 查找新页面是否有关联的摄像头设备
            foreach (var mapping in cameraPageMapping)
            {
                if (mapping.Value == newPageIndex)
                {
                    hasCameraFrameOnNewPage = true;
                    cameraDeviceOnNewPage = mapping.Key;
                    break;
                }
            }
            
            // 如果新页面有摄像头画面，恢复摄像头显示
            if (hasCameraFrameOnNewPage && cameraDeviceOnNewPage != null)
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
                    
                    // 直接在主线程上恢复画面显示，避免延迟任务导致的多次插入问题
                    mainWindow.Dispatcher.BeginInvoke(new Action(() =>
                    {
                        // 确保先移除旧的画面，然后只启动定时器恢复显示，不插入新画面
                        mainWindow.RemoveCameraFrame();
                        
                        // 启动定时器持续更新画面
                        var cameraFrameTimerField = mainWindow.GetType().GetField("cameraFrameTimer", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                        if (cameraFrameTimerField != null)
                        {
                            var cameraFrameTimer = cameraFrameTimerField.GetValue(mainWindow) as System.Windows.Threading.DispatcherTimer;
                            cameraFrameTimer?.Start();
                            Console.WriteLine("摄像头设备已切换，已恢复画面显示");
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
                        // 检查当前页面是否已经有摄像头画面
                        bool hasCurrentCameraFrame = mainWindow.HasCameraFrameOnCurrentPage();
                        Console.WriteLine($"HasCameraFrameOnCurrentPage返回: {hasCurrentCameraFrame}");
                        
                        if (hasCurrentCameraFrame)
                        {
                            // 如果当前页面有摄像头画面，只需启动定时器更新画面
                            Console.WriteLine("当前页面已有摄像头画面，启动定时器更新");
                            
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
                            // 如果当前页面没有摄像头画面，需要插入新的摄像头画面
                            Console.WriteLine("当前页面无摄像头画面，插入新的摄像头画面");
                            
                            // 插入摄像头画面
                            mainWindow.InsertCameraFrameToCanvas();
                        }
                    }));
                }
            }
            else
            {
                // 如果新页面没有摄像头画面，暂停摄像头显示（但不停止摄像头设备）
                Console.WriteLine($"切换到页码 {newPageIndex}，无摄像头画面，暂停摄像头显示");
                
                // 检查当前页面是否已经有摄像头画面，避免重复移除
                bool hasCurrentCameraFrame = false;
                mainWindow.Dispatcher.Invoke(new Action(() =>
                {
                    hasCurrentCameraFrame = mainWindow.HasCameraFrameOnCurrentPage();
                }));
                
                if (hasCurrentCameraFrame)
                {
                    // 通知主窗口移除摄像头画面
                    mainWindow.Dispatcher.BeginInvoke(new Action(() =>
                    {
                        mainWindow.RemoveCameraFrame();
                    }));
                }
            }
        }
        
        // 退出按钮点击事件处理
        public void HandleExitButtonClicked()
        {
            Console.WriteLine("退出按钮点击，暂停摄像头显示以节省资源");
            
            // 停止摄像头设备以节省资源
            StopCamera();
            
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