using Ink_Canvas.Plugins.DocumentToImage.UI;
using PdfiumViewer;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Windows.Media.Imaging;

namespace Ink_Canvas.Plugins.DocumentToImage.Converters
{
    /// <summary>
    /// 使用 PdfiumViewer 将 PDF 每一页渲染为 BitmapImage。
    /// 需要随插件携带 pdfium.dll（PdfiumViewer NuGet 包会自动复制）。
    /// </summary>
    internal static class PdfToImageConverter
    {
        public static List<BitmapImage> Convert(string pdfPath, int dpi, IProgress<ConversionProgress> progress)
        {
            if (string.IsNullOrWhiteSpace(pdfPath) || !File.Exists(pdfPath))
                throw new FileNotFoundException("PDF 文件不存在", pdfPath);

            string fileName = Path.GetFileName(pdfPath);
            progress?.Report(new ConversionProgress
            {
                FileName = fileName,
                Message = "正在读取 PDF 页面..."
            });

            var result = new List<BitmapImage>();

            using (var document = PdfDocument.Load(pdfPath))
            {
                int pageCount = document.PageCount;
                if (pageCount <= 0)
                    throw new InvalidOperationException("PDF 没有可渲染的页面");

                for (int i = 0; i < pageCount; i++)
                {
                    progress?.Report(new ConversionProgress
                    {
                        FileName = fileName,
                        Current = i + 1,
                        Total = pageCount,
                        Message = $"正在转换 PDF 第 {i + 1}/{pageCount} 页..."
                    });

                    var size = document.PageSizes[i];
                    int width = Math.Max(1, (int)(size.Width * dpi / 72.0));
                    int height = Math.Max(1, (int)(size.Height * dpi / 72.0));

                    using (Bitmap bitmap = (Bitmap)document.Render(i, width, height, dpi, dpi, PdfRenderFlags.Annotations))
                    {
                        var bitmapImage = ConvertBitmapToBitmapImage(bitmap);
                        if (bitmapImage != null)
                            result.Add(bitmapImage);
                    }
                }
            }

            return result;
        }

        private static BitmapImage ConvertBitmapToBitmapImage(Bitmap bitmap)
        {
            if (bitmap == null) return null;

            using (var memory = new MemoryStream())
            {
                bitmap.Save(memory, ImageFormat.Png);
                memory.Position = 0;

                var bitmapImage = new BitmapImage();
                bitmapImage.BeginInit();
                bitmapImage.StreamSource = memory;
                bitmapImage.CacheOption = BitmapCacheOption.OnLoad;
                bitmapImage.EndInit();
                bitmapImage.Freeze();

                return bitmapImage;
            }
        }
    }
}
