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

        private void OnCameraSelected(string deviceName)
        {
            // 启动摄像头并插入画面到白板
            selectedDeviceName = deviceName;
            StartCamera(deviceName);
            
            // 通知主窗口插入摄像头画面
            mainWindow.Dispatcher.BeginInvoke(new Action(() =>
            {
                mainWindow.InsertCameraFrameToCanvas();
            }));
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