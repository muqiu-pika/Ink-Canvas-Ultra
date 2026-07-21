using System;
using System.IO;
using System.Windows.Media.Imaging;

namespace Ink_Canvas.Plugins.VisualPresenter
{
    /// <summary>
    /// 拍摄照片的数据模型。
    /// 包含原始图像、缩略图、笔画快照（可选）、时间戳、文件路径。
    /// </summary>
    public class CapturedImage
    {
        public BitmapSource Image { get; set; }
        public BitmapSource Thumbnail { get; set; }
        public DateTime Timestamp { get; set; }
        public string FilePath { get; set; }

        public CapturedImage(BitmapSource image)
        {
            Image = image;
            Timestamp = DateTime.Now;
            Thumbnail = CreateThumbnail(image, 290, 180);
        }

        public CapturedImage(BitmapSource image, string filePath)
            : this(image)
        {
            FilePath = filePath;
            TryExtractTimestampFromFilePath(filePath);
        }

        private void TryExtractTimestampFromFilePath(string filePath)
        {
            try
            {
                string fileName = Path.GetFileNameWithoutExtension(filePath);
                if (fileName.Length >= 15 && fileName[14] == '-')
                {
                    string prefix = fileName.Substring(0, 14);
                    if (DateTime.TryParseExact(prefix, "yyyyMMddHHmmss",
                            System.Globalization.CultureInfo.InvariantCulture,
                            System.Globalization.DateTimeStyles.None, out var ts))
                    {
                        Timestamp = ts;
                    }
                }
            }
            catch { }
        }

        /// <summary>按指定尺寸生成 Jpeg 缩略图</summary>
        public static BitmapSource CreateThumbnail(BitmapSource source, int width, int height)
        {
            if (source == null) return null;
            try
            {
                var scaled = new TransformedBitmap(source,
                    new System.Windows.Media.ScaleTransform(
                        (double)width / source.PixelWidth,
                        (double)height / source.PixelHeight));

                var encoder = new JpegBitmapEncoder { QualityLevel = 85 };
                encoder.Frames.Add(BitmapFrame.Create(scaled));
                using (var ms = new MemoryStream())
                {
                    encoder.Save(ms);
                    ms.Position = 0;
                    var thumb = new BitmapImage();
                    thumb.BeginInit();
                    thumb.CacheOption = BitmapCacheOption.OnLoad;
                    thumb.StreamSource = ms;
                    thumb.EndInit();
                    thumb.Freeze();
                    return thumb;
                }
            }
            catch
            {
                return source;
            }
        }
    }
}
