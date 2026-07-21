using System;

namespace Ink_Canvas.Plugins
{
    /// <summary>
    /// plugin 主入口接口。所有 plugin 必须实现一个无参构造函数，
    /// 并由 PluginHost 通过反射创建实例后调用 Initialize。
    /// </summary>
    public interface IPlugin
    {
        /// <summary>plugin 元数据（与 plugin.icplugin 文件内容一致）</summary>
        PluginManifest Manifest { get; }

        /// <summary>
        /// plugin 启动入口。host 提供 plugin 与主程序交互的全部 API。
        /// 在此方法返回前 plugin 不应执行任何 UI 操作。
        /// </summary>
        void Initialize(IPluginHost host);

        /// <summary>plugin 卸载入口。释放资源、注销 UI、关闭线程。</summary>
        void Shutdown();
    }
}
