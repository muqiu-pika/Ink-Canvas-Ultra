using System.Collections.Generic;
using Newtonsoft.Json;

namespace Ink_Canvas.Plugins
{
    /// <summary>
    /// 在线插件商店目录。对应 Ink-Canvas-Ultra-Plugin 仓库中的 plugins.json。
    /// </summary>
    public class OnlinePluginCatalog
    {
        [JsonProperty("version")]
        public int Version { get; set; }

        [JsonProperty("plugins")]
        public List<OnlinePluginInfo> Plugins { get; set; } = new List<OnlinePluginInfo>();
    }

    public class OnlinePluginInfo
    {
        [JsonProperty("id")]
        public string Id { get; set; }

        [JsonProperty("name")]
        public string Name { get; set; }

        [JsonProperty("version")]
        public string Version { get; set; }

        [JsonProperty("author")]
        public string Author { get; set; }

        [JsonProperty("description")]
        public string Description { get; set; }

        [JsonProperty("downloadUrl")]
        public string DownloadUrl { get; set; }

        [JsonProperty("icon")]
        public string Icon { get; set; }

        [JsonProperty("minHostVersion")]
        public string MinHostVersion { get; set; }
    }
}
