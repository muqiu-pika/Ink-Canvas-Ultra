using System;
using Newtonsoft.Json;

namespace Ink_Canvas.Plugins
{
    /// <summary>
    /// plugin 元数据。对应 plugin 目录下 plugin.icplugin 文件反序列化的内容。
    /// 该文件为带 BOM 的 UTF-8 文本，JSON 格式，根对象即此类型。
    /// </summary>
    public class PluginManifest
    {
        /// <summary>plugin 唯一标识符。建议使用反向域名格式（如 ink-canvas.visualpresenter）。
        /// 仅允许小写字母 / 数字 / 连字符 / 点号，长度 2-64。</summary>
        [JsonProperty("id", Required = Required.Always)]
        public string Id { get; set; }

        /// <summary>展示给用户的名称（任意语言文本，建议 ≤ 32 字符）</summary>
        [JsonProperty("name", Required = Required.Always)]
        public string Name { get; set; }

        /// <summary>语义化版本号（如 1.0.0），用于依赖检查与升级判断</summary>
        [JsonProperty("version", Required = Required.Always)]
        public string Version { get; set; }

        /// <summary>作者或组织名</summary>
        [JsonProperty("author")]
        public string Author { get; set; }

        /// <summary>简短描述（建议 ≤ 128 字符）</summary>
        [JsonProperty("description")]
        public string Description { get; set; }

        /// <summary>主程序入口程序集文件名（相对 plugin 目录，如 VisualPresenter.dll）。
        /// 留空时 PluginHost 仅按 manifest 注册声明式入口点，不加载程序集。</summary>
        [JsonProperty("entryAssembly")]
        public string EntryAssembly { get; set; }

        /// <summary>实现 IPlugin 的类的全限定名（如 Ink_Canvas.Plugins.VisualPresenter.VisualPresenterPlugin）。
        /// 仅在 entryAssembly 非空时有效。</summary>
        [JsonProperty("entryClass")]
        public string EntryClass { get; set; }

        /// <summary>兼容主程序的最低版本号（如 7.0.0）。低于此版本时拒绝加载。</summary>
        [JsonProperty("minHostVersion")]
        public string MinHostVersion { get; set; }

        /// <summary>plugin 主页 URL（可选，用于在工坊中展示「访问主页」按钮）</summary>
        [JsonProperty("homepage")]
        public string Homepage { get; set; }

        /// <summary>plugin 图标文件相对路径（建议 64×64 PNG）。可选。</summary>
        [JsonProperty("icon")]
        public string Icon { get; set; }

        /// <summary>
        /// 声明式 UI 入口点。plugin 可不写任何代码，仅通过 manifest 声明
        /// 在主程序某个区域显示一个按钮，点击时由主程序内建路由处理。
        /// 例如 entryPoint = "video-presenter" 时主程序自动激活视频展台侧栏。
        /// </summary>
        [JsonProperty("entryPoints")]
        public PluginEntryPoint[] EntryPoints { get; set; }
    }
}
