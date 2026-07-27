namespace Ink_Canvas.Plugins.DocumentToImage.UI
{
    /// <summary>
    /// 文档转换进度信息。
    /// </summary>
    public class ConversionProgress
    {
        /// <summary>当前文档文件名。</summary>
        public string FileName { get; set; }

        /// <summary>当前步骤（第几张 / 第几个工作表）。</summary>
        public int Current { get; set; }

        /// <summary>总步骤数。</summary>
        public int Total { get; set; }

        /// <summary>当前状态描述。</summary>
        public string Message { get; set; }
    }
}
