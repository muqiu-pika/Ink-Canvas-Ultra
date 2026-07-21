using Newtonsoft.Json;

namespace Ink_Canvas.Plugins
{
    /// <summary>
    /// 声明式 UI 入口点描述符。plugin 在 manifest 中声明后，
    /// 主程序会在对应区域自动渲染按钮，无需 plugin 提供代码。
    /// </summary>
    public class PluginEntryPoint
    {
        /// <summary>入口点路由键。主程序预定义的可选值：
        /// - "video-presenter" 激活视频展台侧栏
        /// - "settings:appearance" 跳转到外观设置
        /// 后续可由主程序扩展。</summary>
        [JsonProperty("route", Required = Required.Always)]
        public string Route { get; set; }

        /// <summary>放置位置。可选值：
        /// - "board-toolbar" 白板工具栏
        /// - "float-bar" 浮动工具栏
        /// - "sidebar" 侧栏头部</summary>
        [JsonProperty("placement", Required = Required.Always)]
        public string Placement { get; set; }

        /// <summary>按钮显示文本（任意语言）</summary>
        [JsonProperty("label", Required = Required.Always)]
        public string Label { get; set; }

        /// <summary>Segoe Fluent Icons 字符 Glyph（如 &#xe714;）</summary>
        [JsonProperty("glyph")]
        public string Glyph { get; set; }

        /// <summary>图标资源相对路径（与 glyph 二选一，优先 glyph）</summary>
        [JsonProperty("icon")]
        public string Icon { get; set; }

        /// <summary>排序权重，数字越小越靠前。默认 100。</summary>
        [JsonProperty("order")]
        public int Order { get; set; } = 100;

        /// <summary>鼠标悬停提示文本</summary>
        [JsonProperty("tooltip")]
        public string Tooltip { get; set; }
    }
}
