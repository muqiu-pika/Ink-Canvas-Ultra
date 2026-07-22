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
        /// 统一媒体插入按钮点击事件。
        /// 图片：主程序直接处理；视频：委托给已安装的视频控件 plugin（路由 "video-insert"）。
        /// OpenFileDialog 的 Filter 根据视频控件 plugin 是否已安装/启用动态调整：
        ///   - 已安装且启用：Filter 包含视频选项
        ///   - 未安装或已禁用：Filter 仅包含图片选项（避免用户选了视频后才提示失败）
        /// 每次点击按钮时实时检查路由可用性，因此插件热加载/卸载后立即反映到 Filter。
        /// </summary>
        private async void BtnMediaInsertUnified_Click(object sender, RoutedEventArgs e)
        {
            // 实时检查视频控件 plugin 是否可用（支持热加载/卸载）
            var host = Plugins.PluginHost.Instance;
            bool videoAvailable = host != null && host.IsRouteAvailable("video-insert");

            string filter;
            if (videoAvailable)
            {
                filter = "图片/视频 (*.jpg;*.jpeg;*.png;*.bmp;*.mp4;*.avi;*.wmv)|*.jpg;*.jpeg;*.png;*.bmp;*.mp4;*.avi;*.wmv|图片 (*.jpg;*.jpeg;*.png;*.bmp)|*.jpg;*.jpeg;*.png;*.bmp|视频 (*.mp4;*.avi;*.wmv)|*.mp4;*.avi;*.wmv";
            }
            else
            {
                // 视频控件 plugin 未安装或已禁用，Filter 仅显示图片
                filter = "图片 (*.jpg;*.jpeg;*.png;*.bmp)|*.jpg;*.jpeg;*.png;*.bmp";
            }

            OpenFileDialog openFileDialog = new OpenFileDialog { Filter = filter };

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
                    // 视频插入委托给已安装的视频控件 plugin
                    if (host == null || !host.IsRouteAvailable("video-insert"))
                    {
                        ShowNotificationAsync("未安装视频控件 plugin，无法插入视频。请到插件工坊安装 videocontrols。");
                        return;
                    }
                    host.TriggerRoute("video-insert", filePath);
                }
                else
                {
                    MessageBox.Show("不支持的媒体格式", "插入媒体", MessageBoxButton.OK, MessageBoxImage.Information);
                }
            }
        }
    }
}
