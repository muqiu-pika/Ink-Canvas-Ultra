using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;

namespace Ink_Canvas
{
    public partial class MainWindow : Window
    {
        /// <summary>
        /// 统一媒体插入按钮点击事件
        /// </summary>
        private async void BtnMediaInsertUnified_Click(object sender, RoutedEventArgs e)
        {
            OpenFileDialog openFileDialog = new OpenFileDialog();
            openFileDialog.Filter = "图片/视频 (*.jpg;*.jpeg;*.png;*.bmp;*.mp4;*.avi;*.wmv)|*.jpg;*.jpeg;*.png;*.bmp;*.mp4;*.avi;*.wmv|图片 (*.jpg;*.jpeg;*.png;*.bmp)|*.jpg;*.jpeg;*.png;*.bmp|视频 (*.mp4;*.avi;*.wmv)|*.mp4;*.avi;*.wmv";

            if (openFileDialog.ShowDialog() == true)
            {
                string filePath = openFileDialog.FileName;

                string ext = (Path.GetExtension(filePath) ?? string.Empty).ToLowerInvariant();
                var imageExts = new HashSet<string> { ".jpg", ".jpeg", ".png", ".bmp" };
                var videoExts = new HashSet<string> { ".mp4", ".avi", ".wmv" };

                if (imageExts.Contains(ext))
                {
                    Image image = await CreateAndCompressImageAsync(filePath);

                    if (image != null)
                    {
                        string timestamp = "img_" + DateTime.Now.ToString("yyyyMMdd_HH_mm_ss_fff");
                        image.Name = timestamp;

                        CenterAndScaleElement(image);

                        InkCanvas.SetLeft(image, 0);
                        InkCanvas.SetTop(image, 0);
                        inkCanvas.Children.Add(image);

                        timeMachine.CommitElementInsertHistory(image);
                    }
                }
                else if (videoExts.Contains(ext))
                {
                    MediaElement mediaElement = await CreateMediaElementAsync(filePath);

                    if (mediaElement != null)
                    {
                        CenterAndScaleElement(mediaElement);

                        InkCanvas.SetLeft(mediaElement, 0);
                        InkCanvas.SetTop(mediaElement, 0);
                        inkCanvas.Children.Add(mediaElement);

                        mediaElement.LoadedBehavior = MediaState.Manual;
                        mediaElement.UnloadedBehavior = MediaState.Manual;

                        mediaElement.Loaded += (_, args) =>
                        {
                            try
                            {
                                if (mediaElement != null && inkCanvas.Children.Contains(mediaElement))
                                {
                                    mediaElement.Play();
                                }
                            }
                            catch (Exception ex)
                            {
                                Console.WriteLine($"视频自动播放失败: {ex.Message}");
                            }
                        };

                        mediaElement.MediaFailed += (_, args) =>
                        {
                            Console.WriteLine($"媒体加载失败: {args.ErrorException?.Message}");
                            MessageBox.Show("视频文件加载失败，请检查文件格式是否支持。", "插入媒体", MessageBoxButton.OK, MessageBoxImage.Warning);
                        };

                        timeMachine.CommitElementInsertHistory(mediaElement);
                    }
                }
                else
                {
                    MessageBox.Show("不支持的媒体格式", "插入媒体", MessageBoxButton.OK, MessageBoxImage.Information);
                }
            }
        }
    }
}