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
            // 图库副本目录优先使用自动保存位置；该目录不可写（Access denied）时回退到系统临时目录，
            // 避免在安装到受限环境（如非当前用户可写的路径）时插入媒体即报"访问被拒绝"。
            string savePath = null;
            try
            {
                savePath = Path.Combine(Settings.Automation.AutoSavedStrokesLocation, "File Dependency");
                Directory.CreateDirectory(savePath);
            }
            catch
            {
                savePath = null;
            }

            string copyPath = null;
            string fileExtension = Path.GetExtension(filePath);

            if (!string.IsNullOrEmpty(savePath))
            {
                try
                {
                    string timestamp = "img_" + DateTime.Now.ToString("yyyyMMdd_HH_mm_ss_fff");
                    copyPath = Path.Combine(savePath, timestamp + fileExtension);
                }
                catch { copyPath = null; }
            }

            if (string.IsNullOrEmpty(copyPath))
            {
                try
                {
                    string tmpDir = Path.Combine(Path.GetTempPath(), "Ink Canvas Media");
                    Directory.CreateDirectory(tmpDir);
                    string timestamp = "img_" + DateTime.Now.ToString("yyyyMMdd_HH_mm_ss_fff");
                    copyPath = Path.Combine(tmpDir, timestamp + fileExtension);
                }
                catch { copyPath = null; }
            }

            // 优先解码副本（保持独立快照）；副本无法建立或复制失败时，直接解码原文件（只读即可）。
            string sourcePath = filePath;
            if (!string.IsNullOrEmpty(copyPath))
            {
                try
                {
                    await Task.Run(() => File.Copy(filePath, copyPath, true));
                    sourcePath = copyPath;
                }
                catch
                {
                    sourcePath = filePath;
                }
            }

            return await Dispatcher.InvokeAsync(() =>
            {
                BitmapImage bitmapImage = new BitmapImage();
                bitmapImage.BeginInit();
                bitmapImage.UriSource = new Uri(sourcePath, UriKind.Absolute);
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

            // 以画布为基准缩放并留约 20% 边距，让插入的媒体/图片不要铺满整个画板，
            // 四周留白更美观，也避免上下左右贴边。
            const double marginRatio = 0.8;
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

        /// <summary>
        /// 将插入的媒体图片在画板上居中放置，并按画板比例缩小留一圈边距（不铺满屏幕）。
        /// 采用直接设置 Width/Height + InkCanvas 绝对定位的方式，避免依赖 RenderTransform
        /// 的叠加顺序，保证最终横向/纵向都精确居中于画布（即屏幕）中央。
        /// </summary>
        private System.Windows.Controls.Image CenterAndFitMedia(System.Windows.Controls.Image image)
        {
            try
            {
                if (image == null) return image;
                double oldWidth = image.Width;
                double oldHeight = image.Height;
                if (oldWidth <= 0 || oldHeight <= 0 || double.IsNaN(oldWidth) || double.IsNaN(oldHeight)) return image;

                double canvasW = inkCanvas.ActualWidth;
                double canvasH = inkCanvas.ActualHeight;
                if (canvasW <= 0) canvasW = SystemParameters.PrimaryScreenWidth;
                if (canvasH <= 0) canvasH = SystemParameters.PrimaryScreenHeight;

                // 比屏幕小一圈：占画布的 80%，四周各留约 10%
                const double marginRatio = 0.8;
                double scale = Math.Min((canvasW * marginRatio) / oldWidth, (canvasH * marginRatio) / oldHeight);
                if (double.IsNaN(scale) || double.IsInfinity(scale) || scale <= 0) return image;

                double newWidth = oldWidth * scale;
                double newHeight = oldHeight * scale;

                image.Width = newWidth;
                image.Height = newHeight;
                image.RenderTransform = null;
                InkCanvas.SetLeft(image, (canvasW - newWidth) / 2);
                InkCanvas.SetTop(image, (canvasH - newHeight) / 2);
            }
            catch { }
            return image;
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
