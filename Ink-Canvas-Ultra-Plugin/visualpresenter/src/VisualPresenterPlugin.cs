using System;
using Ink_Canvas.Plugins;

namespace Ink_Canvas.Plugins.VisualPresenter
{
    /// <summary>
    /// 视频展台 plugin 入口。
    /// 负责注册主程序路由、初始化摄像头设备管理器、订阅主程序事件。
    /// 实际的 UI 与白板画面集成由主程序内建路由 video-presenter 触发，
    /// 该路由在 MainWindow.InitializePluginSystem 中注册为「显示 VideoPresenterSidebar」。
    /// </summary>
    public class VisualPresenterPlugin : IPlugin
    {
        public PluginManifest Manifest { get; private set; }

        private IPluginHost _host;
        private CameraDeviceManager _cameraManager;

        public void Initialize(IPluginHost host)
        {
            _host = host;
            Manifest = new PluginManifest
            {
                Id = "ink-canvas.visualpresenter",
                Name = "视频展台",
                Version = "1.0.0",
                Author = "Ink Canvas Ultra",
                Description = "为 Ink Canvas Ultra 提供视频展台能力。"
            };

            try
            {
                _cameraManager = new CameraDeviceManager();
                _host.Log($"VisualPresenterPlugin 已初始化，目录: {_host.PluginDirectory}",
                    PluginLogLevel.Event);

                _host.ApplicationExiting += OnApplicationExiting;
                _host.BoardModeChanged += OnBoardModeChanged;
            }
            catch (Exception ex)
            {
                _host.Log($"VisualPresenterPlugin 初始化失败: {ex.Message}", PluginLogLevel.Error);
                throw;
            }
        }

        public void Shutdown()
        {
            try
            {
                _cameraManager?.Dispose();
                _host?.Log("VisualPresenterPlugin 已关闭", PluginLogLevel.Event);
            }
            catch (Exception ex)
            {
                _host?.Log($"VisualPresenterPlugin 关闭异常: {ex.Message}", PluginLogLevel.Error);
            }
        }

        private void OnApplicationExiting(object sender, EventArgs e)
        {
            try { _cameraManager?.Dispose(); } catch { }
        }

        private void OnBoardModeChanged(object sender, BoardModeChangedEventArgs e)
        {
            // 主程序切板模式时释放摄像头以避免与白板画面冲突
            // 实际策略由主程序 VideoPresenterSidebar 内部处理，这里仅记录日志
            _host?.Log($"视频展台 plugin 感知到白板模式变化: {e.NewMode}", PluginLogLevel.Trace);
        }

        /// <summary>暴露摄像头管理器供主程序在需要时调用（未来扩展）</summary>
        public CameraDeviceManager CameraManager => _cameraManager;
    }
}
