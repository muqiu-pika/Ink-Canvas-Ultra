using System;
using System.Windows.Ink;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Ink_Canvas.Models
{
    public class CapturedImage
    {
        public BitmapImage Image { get; private set; }
        public BitmapImage Thumbnail { get; private set; }
        public StrokeCollection Strokes { get; }
        public string Timestamp { get; }
        public string FilePath { get; private set; }

        /// <summary>原始像素尺寸缓存。图片落盘后 Image 会被释放以节省内存，尺寸仍由此提供。</summary>
        public int PixelWidth { get; private set; }
        public int PixelHeight { get; private set; }

        /// <summary>图片是否已被释放（落盘后为 true，仅保留缩略图与文件路径）。</summary>
        public bool IsImageReleased { get; private set; }

        /// <summary>原始来源文件路径（例如导入的文档路径），与照片列表保存路径 FilePath 区分。</summary>
        public string SourceFilePath { get; }

        /// <summary>更新图片并重绘缩略图，保留时间戳等元数据。</summary>
        public void UpdateImage(BitmapImage newImage)
        {
            Image = newImage;
            Thumbnail = CreateThumbnail(newImage);
            PixelWidth = newImage?.PixelWidth ?? 0;
            PixelHeight = newImage?.PixelHeight ?? 0;
            IsImageReleased = false;
        }

        /// <summary>更新图片、缩略图及保存路径，保留时间戳等元数据。</summary>
        public void UpdateImage(BitmapImage newImage, string newFilePath)
        {
            Image = newImage;
            Thumbnail = CreateThumbnail(newImage);
            PixelWidth = newImage?.PixelWidth ?? 0;
            PixelHeight = newImage?.PixelHeight ?? 0;
            IsImageReleased = false;
            if (!string.IsNullOrEmpty(newFilePath))
            {
                FilePath = newFilePath;
            }
        }

        /// <summary>是否为视频条目（true=视频，false=图片）</summary>
        public bool IsVideo { get; }

        /// <summary>视频文件原始路径（仅当 IsVideo=true 时有效）</summary>
        public string VideoFilePath { get; }

        public CapturedImage(BitmapImage image)
        {
            Image = image;
            Thumbnail = CreateThumbnail(image);
            PixelWidth = image?.PixelWidth ?? 0;
            PixelHeight = image?.PixelHeight ?? 0;
            Strokes = new StrokeCollection();
            Timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff");
            FilePath = null;
            SourceFilePath = null;
            IsVideo = false;
            VideoFilePath = null;
        }

        public CapturedImage(BitmapImage image, string filePath)
            : this(image, filePath, sourceFilePath: null)
        {
        }

        public CapturedImage(BitmapImage image, string filePath, string sourceFilePath)
        {
            Image = image;
            Thumbnail = CreateThumbnail(image);
            PixelWidth = image?.PixelWidth ?? 0;
            PixelHeight = image?.PixelHeight ?? 0;
            Strokes = new StrokeCollection();
            FilePath = filePath;
            SourceFilePath = sourceFilePath;
            Timestamp = TryExtractTimestampFromFilePath(filePath) ?? DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff");
            IsVideo = false;
            VideoFilePath = null;
        }

        /// <summary>构造视频条目：使用传入的缩略图和视频文件路径</summary>
        public CapturedImage(BitmapImage thumbnail, string videoFilePath, bool isVideo)
        {
            Image = thumbnail;
            Thumbnail = thumbnail;
            PixelWidth = thumbnail?.PixelWidth ?? 0;
            PixelHeight = thumbnail?.PixelHeight ?? 0;
            Strokes = new StrokeCollection();
            FilePath = videoFilePath;
            SourceFilePath = videoFilePath;
            VideoFilePath = videoFilePath;
            IsVideo = isVideo;
            Timestamp = TryExtractTimestampFromFilePath(videoFilePath) ?? DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff");
        }

        /// <summary>
        /// 释放全尺寸大图 Image 以节省内存。调用前需确保图片已落盘（FilePath 有效），
        /// 之后如需原始图片可由 CreateBitmapImageFromFileOrMemory 从文件重新加载。
        /// 缩略图与像素尺寸缓存保留，照片列表显示不受影响。
        /// </summary>
        public void ReleaseImageMemory()
        {
            if (IsImageReleased) return;
            IsImageReleased = true;
            Image = null;
        }

        private static string TryExtractTimestampFromFilePath(string filePath)
        {
            try
            {
                if (string.IsNullOrEmpty(filePath)) return null;
                var name = System.IO.Path.GetFileNameWithoutExtension(filePath);
                if (DateTime.TryParseExact(name, "yyyy-MM-dd HH-mm-ss-fff", System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out var dt))
                {
                    return dt.ToString("yyyy-MM-dd HH:mm:ss.fff");
                }
                if (name.Length >= 23)
                {
                    var tail = name.Substring(name.Length - 23);
                    if (DateTime.TryParseExact(tail, "yyyy-MM-dd HH-mm-ss-fff", System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out var dt2))
                    {
                        return dt2.ToString("yyyy-MM-dd HH:mm:ss.fff");
                    }
                }
                return null;
            }
            catch { return null; }
        }

        private BitmapImage CreateThumbnail(BitmapImage original)
        {
            // 生成接近展示尺寸（侧栏使用 220x140）的缩略图，避免放大导致模糊
            double targetWidth = 220.0;
            double targetHeight = 140.0;
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
            // 生成 220x140 的深色背景 + 白色播放三角占位图
            double w = 220, h = 140;
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
                double r = 26;
                dc.DrawEllipse(
                    new SolidColorBrush(Color.FromArgb(0xB0, 0xFF, 0xFF, 0xFF)),
                    null,
                    new System.Windows.Point(cx, cy), r, r);

                // 三角形（向右）
                var triStart = cx - 7;
                var triTop = cy - 10;
                var triBottom = cy + 10;
                var triRight = cx + 10;
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
