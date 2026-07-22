using System;
using System.Windows.Ink;
using System.Windows.Media;
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

        /// <summary>是否为视频条目（true=视频，false=图片）</summary>
        public bool IsVideo { get; }

        /// <summary>视频文件原始路径（仅当 IsVideo=true 时有效）</summary>
        public string VideoFilePath { get; }

        public CapturedImage(BitmapImage image)
        {
            Image = image;
            Thumbnail = CreateThumbnail(image);
            Strokes = new StrokeCollection();
            Timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
            FilePath = null;
            IsVideo = false;
            VideoFilePath = null;
        }

        public CapturedImage(BitmapImage image, string filePath)
        {
            Image = image;
            Thumbnail = CreateThumbnail(image);
            Strokes = new StrokeCollection();
            FilePath = filePath;
            Timestamp = TryExtractTimestampFromFilePath(filePath) ?? DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
            IsVideo = false;
            VideoFilePath = null;
        }

        /// <summary>构造视频条目：使用传入的缩略图和视频文件路径</summary>
        public CapturedImage(BitmapImage thumbnail, string videoFilePath, bool isVideo)
        {
            Image = thumbnail;
            Thumbnail = thumbnail;
            Strokes = new StrokeCollection();
            FilePath = videoFilePath;
            VideoFilePath = videoFilePath;
            IsVideo = isVideo;
            Timestamp = TryExtractTimestampFromFilePath(videoFilePath) ?? DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
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

        /// <summary>为视频条目生成占位缩略图（深色背景 + 播放图标）</summary>
        public static BitmapImage CreateVideoPlaceholderThumbnail()
        {
            // 生成 290x180 的深色背景 + 白色播放三角占位图
            double w = 290, h = 180;
            var dv = new DrawingVisual();
            using (var dc = dv.RenderOpen())
            {
                // 深色渐变背景
                dc.DrawRectangle(
                    new LinearGradientBrush(
                        Color.FromRgb(0x2D, 0x2D, 0x30),
                        Color.FromRgb(0x1A, 0x1A, 0x1E),
                        90),
                    null,
                    new System.Windows.Rect(0, 0, w, h));

                // 白色半透明播放三角（圆形背景 + 三角）
                double cx = w / 2, cy = h / 2;
                double r = 32;
                dc.DrawEllipse(
                    new SolidColorBrush(Color.FromArgb(0xB0, 0xFF, 0xFF, 0xFF)),
                    null,
                    new System.Windows.Point(cx, cy), r, r);

                // 三角形（向右）
                var triStart = cx - 8;
                var triTop = cy - 12;
                var triBottom = cy + 12;
                var triRight = cx + 12;
                var triangle = new StreamGeometry();
                using (var ctx = triangle.Open())
                {
                    ctx.BeginFigure(new System.Windows.Point(triStart, triTop), true, true);
                    ctx.LineTo(new System.Windows.Point(triRight, cy), true, false);
                    ctx.LineTo(new System.Windows.Point(triStart, triBottom), true, false);
                }
                dc.DrawGeometry(Brushes.White, null, triangle);
            }

            var rtb = new RenderTargetBitmap((int)w, (int)h, 96, 96, PixelFormats.Pbgra32);
            rtb.Render(dv);

            var bmp = new JpegBitmapEncoder();
            bmp.QualityLevel = 90;
            bmp.Frames.Add(BitmapFrame.Create(rtb));

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
