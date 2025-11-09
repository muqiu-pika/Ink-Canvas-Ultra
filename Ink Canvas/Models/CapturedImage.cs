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

        public CapturedImage(BitmapImage image)
        {
            Image = image;
            Thumbnail = CreateThumbnail(image);
            Strokes = new StrokeCollection();
            Timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
        }

        private BitmapImage CreateThumbnail(BitmapImage original)
        {
            // 生成接近展示尺寸（侧栏使用 290x180）的缩略图，避免放大导致模糊
            double targetWidth = 290.0;
            double targetHeight = 180.0;
            double scale = Math.Min(targetWidth / original.PixelWidth, targetHeight / original.PixelHeight);
            var thumbnail = new TransformedBitmap(original,
                new System.Windows.Media.ScaleTransform(scale, scale));

            var bmp = new PngBitmapEncoder();
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