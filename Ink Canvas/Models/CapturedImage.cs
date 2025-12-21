using System;
using System.Windows.Ink;
using System.Windows.Media.Imaging;

namespace Ink_Canvas.Models
{
    public class CapturedImage
    {
        public BitmapImage Image { get; }
        public BitmapImage Thumbnail { get; }
        public StrokeCollection Strokes { get; }
        public string Timestamp { get; }
        public string FilePath { get; }

        public CapturedImage(BitmapImage image)
        {
            Image = image;
            Thumbnail = CreateThumbnail(image);
            Strokes = new StrokeCollection();
            Timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
            FilePath = null;
        }

        public CapturedImage(BitmapImage image, string filePath)
        {
            Image = image;
            Thumbnail = CreateThumbnail(image);
            Strokes = new StrokeCollection();
            FilePath = filePath;
            Timestamp = TryExtractTimestampFromFilePath(filePath) ?? DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
        }

        private static string TryExtractTimestampFromFilePath(string filePath)
        {
            try
            {
                if (string.IsNullOrEmpty(filePath)) return null;
                var name = System.IO.Path.GetFileNameWithoutExtension(filePath);
                if (DateTime.TryParseExact(name, "yyyy-MM-dd HH-mm-ss-fff", System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out var dt))
                {
                    return dt.ToString("yyyy-MM-dd HH:mm:ss");
                }
                if (name.Length >= 23)
                {
                    var tail = name.Substring(name.Length - 23);
                    if (DateTime.TryParseExact(tail, "yyyy-MM-dd HH-mm-ss-fff", System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out var dt2))
                    {
                        return dt2.ToString("yyyy-MM-dd HH:mm:ss");
                    }
                }
                return null;
            }
            catch { return null; }
        }

        private BitmapImage CreateThumbnail(BitmapImage original)
        {
            // 生成接近展示尺寸（侧栏使用 290x180）的缩略图，避免放大导致模糊
            double targetWidth = 290.0;
            double targetHeight = 180.0;
            double scale = Math.Min(targetWidth / original.PixelWidth, targetHeight / original.PixelHeight);
            var thumbnail = new TransformedBitmap(original,
                new System.Windows.Media.ScaleTransform(scale, scale));

            // 使用 JpegBitmapEncoder 进行略微压缩，平衡画质和文件大小
            var bmp = new JpegBitmapEncoder();
            bmp.QualityLevel = 85; // 设置质量为85%，在画质和压缩之间取得平衡
            bmp.Frames.Add(BitmapFrame.Create(thumbnail));

            using (var stream = new System.IO.MemoryStream())
            {
                bmp.Save(stream);
                stream.Seek(0, System.IO.SeekOrigin.Begin);

                var result = new BitmapImage();
                result.BeginInit();
                result.CacheOption = BitmapCacheOption.OnLoad;
                result.StreamSource = stream;
                result.EndInit();
                result.Freeze();

                return result;
            }
        }
    }
}
