using Ink_Canvas.Plugins.DocumentToImage.UI;
using NPOI.HSSF.UserModel;
using NPOI.SS.UserModel;
using NPOI.XSSF.UserModel;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Ink_Canvas.Plugins.DocumentToImage.Converters
{
    /// <summary>
    /// 使用 NPOI 读取 Excel 工作簿并通过 WPF 渲染为图片。
    /// 无需安装 Microsoft Office。
    /// </summary>
    public static class ExcelToImageConverter
    {
        public static List<BitmapImage> Convert(string filePath, int dpi, IProgress<ConversionProgress> progress)
        {
            if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
                throw new FileNotFoundException("Excel 文件不存在", filePath);

            string ext = Path.GetExtension(filePath).ToLowerInvariant();
            if (ext != ".xls" && ext != ".xlsx")
                throw new NotSupportedException($"不支持的 Excel 格式: {ext}");

            string fileName = Path.GetFileName(filePath);
            progress?.Report(new ConversionProgress
            {
                FileName = fileName,
                Message = "正在读取 Excel 工作簿..."
            });

            IWorkbook workbook;
            using (var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                workbook = ext == ".xlsx" ? (IWorkbook)new XSSFWorkbook(fs) : new HSSFWorkbook(fs);
            }

            int sheetCount = workbook.NumberOfSheets;
            var images = new List<BitmapImage>();
            for (int i = 0; i < sheetCount; i++)
            {
                ISheet sheet = workbook.GetSheetAt(i);
                progress?.Report(new ConversionProgress
                {
                    FileName = fileName,
                    Current = i + 1,
                    Total = sheetCount,
                    Message = $"正在转换工作表 {sheet.SheetName} ({i + 1}/{sheetCount})..."
                });

                BitmapImage image = RenderSheetToImage(sheet, dpi);
                if (image != null)
                    images.Add(image);
            }

            return images;
        }

        private static BitmapImage RenderSheetToImage(ISheet sheet, int dpi)
        {
            int firstRow = sheet.FirstRowNum;
            int lastRow = sheet.LastRowNum;
            if (firstRow < 0 || lastRow < firstRow)
                return null;

            int maxCol = 0;
            for (int r = firstRow; r <= lastRow; r++)
            {
                IRow row = sheet.GetRow(r);
                if (row != null && row.LastCellNum > maxCol)
                    maxCol = row.LastCellNum;
            }
            if (maxCol <= 0)
                return null;

            var grid = new Grid
            {
                Background = Brushes.White,
                UseLayoutRounding = true,
                SnapsToDevicePixels = true
            };

            for (int c = 0; c < maxCol; c++)
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            for (int r = firstRow; r <= lastRow; r++)
                grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            for (int r = firstRow; r <= lastRow; r++)
            {
                IRow row = sheet.GetRow(r);
                if (row == null) continue;

                for (int c = row.FirstCellNum; c < row.LastCellNum; c++)
                {
                    ICell cell = row.GetCell(c);
                    if (cell == null) continue;

                    string text = cell.ToString();
                    if (string.IsNullOrEmpty(text)) continue;

                    var border = new Border
                    {
                        BorderBrush = Brushes.LightGray,
                        BorderThickness = new Thickness(0.5),
                        Padding = new Thickness(5, 3, 5, 3),
                        Child = new TextBlock
                        {
                            Text = text,
                            FontSize = 11,
                            TextWrapping = TextWrapping.Wrap,
                            VerticalAlignment = System.Windows.VerticalAlignment.Center
                        }
                    };

                    Grid.SetRow(border, r - firstRow);
                    Grid.SetColumn(border, c);
                    grid.Children.Add(border);
                }
            }

            double scale = dpi / 96.0;
            grid.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            grid.Arrange(new Rect(grid.DesiredSize));
            grid.UpdateLayout();

            double renderWidth = grid.DesiredSize.Width;
            double renderHeight = grid.DesiredSize.Height;
            if (renderWidth <= 0 || renderHeight <= 0)
                return null;

            // 限制单张图片最大尺寸，避免极端大表格导致内存爆炸
            const int MaxPixel = 12000;
            int width = (int)Math.Ceiling(renderWidth * scale);
            int height = (int)Math.Ceiling(renderHeight * scale);
            if (width > MaxPixel || height > MaxPixel)
            {
                double fit = Math.Min((double)MaxPixel / width, (double)MaxPixel / height);
                width = (int)Math.Ceiling(width * fit);
                height = (int)Math.Ceiling(height * fit);
            }

            var rtb = new RenderTargetBitmap(width, height, dpi, dpi, PixelFormats.Pbgra32);
            rtb.Render(grid);
            return ConvertBitmapSourceToBitmapImage(rtb);
        }

        private static BitmapImage ConvertBitmapSourceToBitmapImage(BitmapSource source)
        {
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(source));
            using (var ms = new MemoryStream())
            {
                encoder.Save(ms);
                ms.Position = 0;
                var bitmapImage = new BitmapImage();
                bitmapImage.BeginInit();
                bitmapImage.StreamSource = ms;
                bitmapImage.CacheOption = BitmapCacheOption.OnLoad;
                bitmapImage.EndInit();
                bitmapImage.Freeze();
                return bitmapImage;
            }
        }
    }
}
