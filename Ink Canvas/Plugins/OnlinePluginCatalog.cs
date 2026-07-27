using System;
using System.Collections.Generic;
using Newtonsoft.Json;

namespace Ink_Canvas.Plugins
{
    /// <summary>
    /// 在线插件商店目录。对应 Ink-Canvas-Ultra-Plugin 仓库中的 market/v1/market.json。
    /// </summary>
    public class OnlinePluginCatalog
    {
        [JsonProperty("apiVersion")]
        public string ApiVersion { get; set; }

        [JsonProperty("lastUpdated")]
        public DateTime LastUpdated { get; set; }

        [JsonProperty("market")]
        public MarketInfo Market { get; set; }

        [JsonProperty("plugins")]
        public List<OnlinePluginInfo> Plugins { get; set; } = new List<OnlinePluginInfo>();
    }

    public class MarketInfo
    {
        [JsonProperty("name")]
        public string Name { get; set; }

        [JsonProperty("homepage")]
        public string Homepage { get; set; }

        [JsonProperty("cdnBaseUrl")]
        public string CdnBaseUrl { get; set; }
    }

    public class OnlinePluginInfo
    {
        [JsonProperty("id")]
        public string Id { get; set; }

        [JsonProperty("name")]
        public string Name { get; set; }

        [JsonProperty("version")]
        public string Version { get; set; }

        [JsonProperty("gitRef")]
        public string GitRef { get; set; }

        [JsonProperty("author")]
        public string Author { get; set; }

        [JsonProperty("description")]
        public string Description { get; set; }

        [JsonProperty("tags")]
        public List<string> Tags { get; set; }

        [JsonProperty("category")]
        public string Category { get; set; }

        [JsonProperty("minHostVersion")]
        public string MinHostVersion { get; set; }

        [JsonProperty("size")]
        public long Size { get; set; }

        [JsonProperty("icon")]
        public string Icon { get; set; }

        [JsonProperty("iconUrl")]
        public string IconUrl { get; set; }

        [JsonProperty("downloadUrl")]
        public string DownloadUrl { get; set; }

        [JsonProperty("fallbackUrl")]
        public string FallbackUrl { get; set; }

        [JsonProperty("checksum")]
        public ChecksumInfo Checksum { get; set; }

        [JsonProperty("changelogUrl")]
        public string ChangelogUrl { get; set; }

        [JsonProperty("homepage")]
        public string Homepage { get; set; }

        [JsonProperty("releasedAt")]
        public DateTime ReleasedAt { get; set; }
    }

    public class ChecksumInfo
    {
        [JsonProperty("algorithm")]
        public string Algorithm { get; set; }

        [JsonProperty("value")]
        public string Value { get; set; }
    }
}
