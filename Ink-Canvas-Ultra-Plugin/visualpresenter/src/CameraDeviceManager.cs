using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.Threading;
using AForge.Video;
using AForge.Video.DirectShow;

namespace Ink_Canvas.Plugins.VisualPresenter
{
    /// <summary>
    /// 视频展台 plugin 的摄像头设备管理器。
    /// 负责：枚举设备、启停预览、获取当前帧、节流。
    /// 与主程序 MW_CameraDeviceManager 的实现思路类似，但作为 plugin 私有组件，
    /// 不直接依赖主程序的 UI 控件；UI 集成通过主程序内建路由完成。
    /// </summary>
    public class CameraDeviceManager : IDisposable
    {
        private FilterInfoCollection _videoDevices;
        private VideoCaptureDevice _currentDevice;
        private Bitmap _currentFrame;
        private readonly object _frameLock = new object();

        // 节流：相邻两帧之间最小间隔（毫秒）
        private const int MinFrameIntervalMs = 33;

        // 摄像头-白板页码绑定（用于切换页时恢复 / 释放摄像头）
        private readonly Dictionary<int, string> _cameraPageMapping = new Dictionary<int, string>();

        private DateTime _lastFrameTime = DateTime.MinValue;
        private volatile bool _isRunning;

        public event EventHandler<Bitmap> OnNewFrameProcessed;

        public string SelectedDeviceName { get; private set; }

        public CameraDeviceManager()
        {
            RefreshCameraDevices();
        }

        /// <summary>枚举系统可用摄像头</summary>
        public List<string> GetAvailableCameras()
        {
            var names = new List<string>();
            if (_videoDevices == null) return names;
            foreach (FilterInfo fi in _videoDevices)
            {
                names.Add(fi.Name);
            }
            return names;
        }

        /// <summary>刷新设备列表（用户插入 / 拔出摄像头时调用）</summary>
        public void RefreshCameraDevices()
        {
            try
            {
                _videoDevices = new FilterInfoCollection(FilterCategory.VideoInputDevice);
            }
            catch
            {
                _videoDevices = null;
            }
        }

        /// <summary>启动指定名称的摄像头</summary>
        public bool StartCamera(string deviceName)
        {
            if (_videoDevices == null) RefreshCameraDevices();
            if (_videoDevices == null || _videoDevices.Count == 0) return false;

            FilterInfo target = null;
            foreach (FilterInfo fi in _videoDevices)
            {
                if (fi.Name == deviceName) { target = fi; break; }
            }
            if (target == null) return false;

            StopCamera();

            try
            {
                _currentDevice = new VideoCaptureDevice(target.MonikerString);
                _currentDevice.NewFrame += Device_NewFrame;
                _currentDevice.Start();
                SelectedDeviceName = deviceName;
                _isRunning = true;
                return true;
            }
            catch
            {
                _currentDevice = null;
                return false;
            }
        }

        private void Device_NewFrame(object sender, NewFrameEventArgs eventArgs)
        {
            // 节流：相邻帧间隔不足 MinFrameIntervalMs 时丢弃
            var now = DateTime.Now;
            if ((now - _lastFrameTime).TotalMilliseconds < MinFrameIntervalMs) return;
            _lastFrameTime = now;

            try
            {
                var frame = (Bitmap)eventArgs.Frame.Clone();
                lock (_frameLock)
                {
                    _currentFrame?.Dispose();
                    _currentFrame = frame;
                }
                OnNewFrameProcessed?.Invoke(this, frame);
            }
            catch
            {
                // 忽略单帧异常，避免崩溃
            }
        }

        /// <summary>获取当前帧的线程安全副本</summary>
        public Bitmap GetFrameCopy()
        {
            lock (_frameLock)
            {
                return _currentFrame?.Clone() as Bitmap;
            }
        }

        /// <summary>停止当前摄像头</summary>
        public void StopCamera()
        {
            if (_currentDevice == null) return;
            try
            {
                _isRunning = false;
                if (_currentDevice.IsRunning) _currentDevice.SignalToStop();
                _currentDevice.WaitForStop();
                _currentDevice.NewFrame -= Device_NewFrame;
            }
            catch { }
            finally
            {
                _currentDevice = null;
                SelectedDeviceName = null;
                lock (_frameLock)
                {
                    _currentFrame?.Dispose();
                    _currentFrame = null;
                }
            }
        }

        public string GetCurrentCameraName() => SelectedDeviceName;

        /// <summary>把当前摄像头绑定到指定白板页（切换页时用）</summary>
        public void BindCurrentCameraToPage(int pageIndex)
        {
            if (!string.IsNullOrEmpty(SelectedDeviceName))
            {
                _cameraPageMapping[pageIndex] = SelectedDeviceName;
            }
        }

        public void ClearDeviceSelection()
        {
            _cameraPageMapping.Clear();
        }

        public void Dispose()
        {
            StopCamera();
        }
    }
}
