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

        #region Media
        private async Task<MediaElement> CreateMediaElementAsync(string filePath)
        {
            string savePath = Path.Combine(Settings.Automation.AutoSavedStrokesLocation, "File Dependency");
            if (!Directory.Exists(savePath))
            {
                Directory.CreateDirectory(savePath);
            }
            return await Dispatcher.InvokeAsync(() =>
            {
                string timestamp = "media_" + DateTime.Now.ToString("yyyyMMdd_HH_mm_ss_fff");
                MediaElement mediaElement = new MediaElement
                {
                    Source = new Uri(filePath),
                    Name = timestamp,
                    LoadedBehavior = MediaState.Manual,
                    UnloadedBehavior = MediaState.Manual,
                    ScrubbingEnabled = true,
                    IsHitTestVisible = true,
                    Focusable = true,
                    Width = 256,
                    Height = 256
                };

                string fileExtension = Path.GetExtension(filePath);
                string newFilePath = Path.Combine(savePath, mediaElement.Name + fileExtension);

                File.Copy(filePath, newFilePath, true);

                mediaElement.Source = new Uri(newFilePath);

                // Allow play/pause toggle in screen pen mode by tapping/clicking the media
                mediaElement.Tag = false; // playing state
                void TogglePlayback()
                {
                    bool isPlaying = mediaElement.Tag is bool b && b;
                    if (isPlaying)
                    {
                        mediaElement.Pause();
                        mediaElement.Tag = false;
                    }
                    else
                    {
                        mediaElement.Play();
                        mediaElement.Tag = true;
                    }
                }
                mediaElement.PreviewMouseLeftButtonDown += (s, e) =>
                {
                    e.Handled = true;
                    TogglePlayback();
                };
                mediaElement.PreviewTouchDown += (s, e) =>
                {
                    e.Handled = true;
                    TogglePlayback();
                };
                mediaElement.PreviewStylusDown += (s, e) =>
                {
                    e.Handled = true;
                    TogglePlayback();
                };

                // 取消通过 Adorner 附加底部控制栏，改为使用类似 BorderStrokeSelectionControl 的 Border 控件方式

                return mediaElement;
            });
        }
        #endregion

        private void CenterAndScaleElement(FrameworkElement element)
        {
            double maxWidth = SystemParameters.PrimaryScreenWidth / 2;
            double maxHeight = SystemParameters.PrimaryScreenHeight / 2;

            double scaleX = maxWidth / element.Width;
            double scaleY = maxHeight / element.Height;
            double scale = Math.Min(scaleX, scaleY);

            TransformGroup transformGroup = new TransformGroup();
            transformGroup.Children.Add(new ScaleTransform(scale, scale));

            double canvasWidth = inkCanvas.ActualWidth;
            double canvasHeight = inkCanvas.ActualHeight;
            double centerX = (canvasWidth - element.Width * scale) / 2;
            double centerY = (canvasHeight - element.Height * scale) / 2;

            transformGroup.Children.Add(new TranslateTransform(centerX, centerY));

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
