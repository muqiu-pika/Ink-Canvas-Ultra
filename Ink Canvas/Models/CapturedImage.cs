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
            double scale = Math.Min(70.0 / original.PixelWidth, 52.0 / original.PixelHeight);
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