using System;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Documents;
using System.Windows.Ink;
using Ink_Canvas.Helpers;
using System.Windows.Media.Imaging;
using System.Windows.Input;

namespace Ink_Canvas
{
    public partial class MainWindow : Window
    {
        #region Image
        private async Task<Image> CreateAndCompressImageAsync(string filePath)
        {
            string savePath = Path.Combine(Settings.Automation.AutoSavedStrokesLocation, "File Dependency");
            if (!Directory.Exists(savePath))
            {
                Directory.CreateDirectory(savePath);
            }

            string fileExtension = Path.GetExtension(filePath);
            string timestamp = "img_" + DateTime.Now.ToString("yyyyMMdd_HH_mm_ss_fff");
            string newFilePath = Path.Combine(savePath, timestamp + fileExtension);

            await Task.Run(() => File.Copy(filePath, newFilePath, true));

            return await Dispatcher.InvokeAsync(() =>
            {
                BitmapImage bitmapImage = new BitmapImage();
                bitmapImage.BeginInit();
                bitmapImage.UriSource = new Uri(newFilePath);
                bitmapImage.CacheOption = BitmapCacheOption.OnLoad;
                bitmapImage.EndInit();

                int width = bitmapImage.PixelWidth;
                int height = bitmapImage.PixelHeight;

                Image image = new Image();
                if (isLoaded && Settings.Canvas.IsCompressPicturesUploaded && (width > 1920 || height > 1080))
                {
                    double scaleX = 1920.0 / width;
                    double scaleY = 1080.0 / height;
                    double scale = Math.Min(scaleX, scaleY);

                    TransformedBitmap transformedBitmap = new TransformedBitmap(bitmapImage, new ScaleTransform(scale, scale));

                    image.Source = transformedBitmap;
                    image.Width = transformedBitmap.PixelWidth;
                    image.Height = transformedBitmap.PixelHeight;
                }
                else
                {
                    image.Source = bitmapImage;
                    image.Width = width;
                    image.Height = height;
                }

                return image;
            });
        }
        #endregion

        private void CenterAndScaleElement(FrameworkElement element)
        {
            double elementWidth = element.Width;
            double elementHeight = element.Height;

            // 尺寸无效时放弃变换：否则后续除法会产生 Infinity/NaN，
            // 元素尺寸被污染为 NaN 后将彻底无法渲染（表现为"插入了但看不见"）。
            if (elementWidth <= 0 || elementHeight <= 0 ||
                double.IsNaN(elementWidth) || double.IsNaN(elementHeight))
            {
                return;
            }

            // 画布尺寸兜底：新建页面或尚未完成布局时 ActualWidth/ActualHeight 可能为 0，
            // 直接参与计算会使居中偏移变成负数，元素被平移到画布左上方之外而完全不可见。
            double canvasWidth = inkCanvas.ActualWidth;
            double canvasHeight = inkCanvas.ActualHeight;
            if (canvasWidth <= 0) canvasWidth = SystemParameters.PrimaryScreenWidth;
            if (canvasHeight <= 0) canvasHeight = SystemParameters.PrimaryScreenHeight;

            // 以画布为基准缩放并留约 5% 边距，保证图片始终能放入画板内。
            // 此前以"主屏幕一半"为基准缩放，在画板比它小或比例不同时图片会超出画板，
            // 配合下方钳制会被顶到左上角，表现为"插入后没有居中"。
            const double marginRatio = 0.95;
            double maxWidth = canvasWidth * marginRatio;
            double maxHeight = canvasHeight * marginRatio;

            double scaleX = maxWidth / elementWidth;
            double scaleY = maxHeight / elementHeight;
            double scale = Math.Min(scaleX, scaleY);
            if (double.IsNaN(scale) || double.IsInfinity(scale) || scale <= 0)
            {
                return;
            }

            // 缩放后尺寸必然不大于画布（见上式含边距），居中偏移非负，天然居中，
            // 不再需要 Math.Max 钳制（钳制会把超出的图片顶到左上角）。
            double centerX = (canvasWidth - elementWidth * scale) / 2;
            double centerY = (canvasHeight - elementHeight * scale) / 2;

            // 变换顺序：TransformGroup 的矩阵按 Children 顺序右乘（M = 前 × 后）。
            // 若 Scale 在前，平移量会被再乘一次 scale（实际平移 centerX*scale），落点偏左上；
            // 因此必须先 Translate 后 Scale，平移量才是预期的 centerX/centerY。
            TransformGroup transformGroup = new TransformGroup();
            transformGroup.Children.Add(new TranslateTransform(centerX, centerY));
            transformGroup.Children.Add(new ScaleTransform(scale, scale));

            element.RenderTransform = transformGroup;
        }

        // 初始化InkCanvas选择设置
        private void InitializeInkCanvasSelectionSettings()
        {
            if (inkCanvas != null)
            {
                // 清除当前选择，避免显示控制点
                inkCanvas.Select(new StrokeCollection());
                // 设置编辑模式为非选择模式
                inkCanvas.EditingMode = InkCanvasEditingMode.None;
            }
        }
    }
}
