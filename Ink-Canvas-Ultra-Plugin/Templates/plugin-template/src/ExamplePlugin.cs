using System;
using Ink_Canvas.Helpers;
using Ink_Canvas.Plugins;

namespace Ink_Canvas.Plugins.Example
{
    /// <summary>
    /// Ink Canvas Ultra plugin 示例入口。
    /// 把此目录复制为新名称后：
    ///   1. 修改 plugin.icplugin 中的 id / name / entryAssembly / entryClass
    ///   2. 修改本文件的命名空间与类名（与 entryClass 一致）
    ///   3. 编译生成 DLL，与 plugin.icplugin 一起放到主程序 Plugins\<新 id>\ 目录
    /// </summary>
    public class ExamplePlugin : IPlugin
    {
        public PluginManifest Manifest { get; private set; }

        private IPluginHost _host;

        public void Initialize(IPluginHost host)
        {
            _host = host;
            Manifest = new PluginManifest
            {
                Id = "ink-canvas.example",
                Name = "示例 plugin",
                Version = "1.0.0"
            };

            _host.Log($"ExamplePlugin 已初始化，目录: {_host.PluginDirectory}", LogHelper.LogType.Event);

            // 订阅主程序事件
            _host.ApplicationExiting += OnExiting;
            _host.BoardModeChanged += OnBoardModeChanged;

            // 演示：调用主程序路由
            // _host.TriggerRoute("settings:plugin-workshop");
        }

        public void Shutdown()
        {
            _host?.Log("ExamplePlugin 已关闭", LogHelper.LogType.Event);
        }

        private void OnExiting(object sender, EventArgs e)
        {
            // 在主程序退出时释放资源
        }

        private void OnBoardModeChanged(object sender, BoardModeChangedEventArgs e)
        {
            _host?.Log($"白板模式变化: {e.NewMode}", LogHelper.LogType.Trace);
        }
    }
}
