using Ink_Canvas.Helpers;
using System;
using System.Collections.Specialized;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Forms;
using System.Windows.Media.Imaging;

namespace Ink_Canvas
{
    public partial class MainWindow : Window
    {
        private void SaveScreenshot(bool isHideNotification, string fileName = null)
        {
            var savePath = Settings.Automation.IsSaveScreenshotsInDateFolders
                ? GetDateFolderPath(fileName)
                : GetDefaultFolderPath();

            CaptureAndSaveScreenshot(savePath, isHideNotification);

            if (Settings.Automation.IsAutoSaveStrokesAtScreenshot)
                SaveInkCanvasFile(false, false);
        }

        // 提取公共的截图和保存逻辑
        private void CaptureAndSaveScreenshot(string savePath, bool isHideNotification)
        {
            var rc = SystemInformation.VirtualScreen;

            using (var bitmap = new Bitmap(rc.Width, rc.Height, PixelFormat.Format32bppArgb))
            using (var memoryGraphics = Graphics.FromImage(bitmap))
            {
                // 设置高质量渲染
                memoryGraphics.CompositingQuality = CompositingQuality.HighQuality;
                memoryGraphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
                memoryGraphics.SmoothingMode = SmoothingMode.HighQuality;
                memoryGraphics.CompositingMode = CompositingMode.SourceOver;

                memoryGraphics.CopyFromScreen(rc.X, rc.Y, 0, 0, rc.Size, CopyPixelOperation.SourceCopy);

                // 确保目录存在
                var directory = Path.GetDirectoryName(savePath);
                if (!Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                // 使用PNG格式保存，确保透明度信息不丢失
                bitmap.Save(savePath, ImageFormat.Png);

                // 截图落盘后按需复制到剪贴板：图片数据 + 文件路径，
                // 既可直接粘贴到文档/聊天窗口，也可在资源管理器里粘贴为图片文件。
                if (Settings.Automation.IsCopyScreenshotToClipboard)
                {
                    var source = ConvertBitmapToBitmapSource(bitmap);
                    if (source != null)
                    {
                        CopyScreenshotToClipboard(source, savePath, isHideNotification);
                    }
                }
            }

            if (!isHideNotification)
            {
                ShowNotificationAsync($"截图成功保存至 {savePath}");
            }
        }

        // 获取日期文件夹路径
        private string GetDateFolderPath(string fileName)
        {
            if (string.IsNullOrWhiteSpace(fileName))
            {
                fileName = DateTime.Now.ToString("HH-mm-ss");
            }

            var basePath = Settings.Automation.AutoSavedStrokesLocation;
            var dateFolder = DateTime.Now.ToString("yyyyMMdd");

            return Path.Combine(
                basePath,
                "Auto Saved - Screenshots",
                dateFolder,
                $"{fileName}.png");
        }

        // 获取默认文件夹路径
        private string GetDefaultFolderPath()
        {
            var basePath = Settings.Automation.AutoSavedStrokesLocation;
            var screenshotsFolder = Path.Combine(basePath, "Auto Saved - Screenshots");

            if (!Directory.Exists(screenshotsFolder))
            {
                Directory.CreateDirectory(screenshotsFolder);
            }

            return Path.Combine(
                screenshotsFolder,
                $"{DateTime.Now:yyyy-MM-dd_HH-mm-ss}.png");
        }

        // 保存PPT截图
        private void SavePPTScreenshot(string fileName)
        {
            var basePath = Settings?.Automation?.AutoSavedStrokesLocation;
            if (string.IsNullOrWhiteSpace(basePath)) basePath = @"D:\Ink Canvas";

            string folderPath = Path.Combine(basePath, "Auto Saved - PPT Screenshots");
            if (Settings.Automation.IsSaveScreenshotsInDateFolders)
            {
                folderPath = Path.Combine(folderPath, DateTime.Now.ToString("yyyy-MM-dd"));
            }

            if (fileName == null) fileName = DateTime.Now.ToString("u").Replace(":", "-");
            fileName = SanitizeFileName(fileName);

            string savePath = Path.Combine(folderPath, fileName + ".png");

            var saveDir = Path.GetDirectoryName(savePath);
            if (!string.IsNullOrEmpty(saveDir) && !Directory.Exists(saveDir))
            {
                Directory.CreateDirectory(saveDir);
            }

            using (var bitmap = GetScreenshotBitmap())
            {
                bitmap.Save(savePath, ImageFormat.Png);
            }
            if (Settings.Automation.IsAutoSaveStrokesAtScreenshot)
            {
                SaveInkCanvasFile(false, false);
            }
        }

        // 获取全屏截图位图
        private Bitmap GetScreenshotBitmap()
        {
            Rectangle rc = SystemInformation.VirtualScreen;
            var bitmap = new Bitmap(rc.Width, rc.Height, PixelFormat.Format32bppArgb);
            using (Graphics memoryGraphics = Graphics.FromImage(bitmap))
            {
                memoryGraphics.CopyFromScreen(rc.X, rc.Y, 0, 0, rc.Size, CopyPixelOperation.SourceCopy);
            }
            return bitmap;
        }

        /// <summary>
        /// 将截图保存为 PNG 文件（用于“复制到剪贴板”后仍需要一个真实文件可粘贴到其他位置的场景）。
        /// </summary>
        /// <param name="source">截图位图</param>
        /// <returns>保存成功返回文件完整路径，失败返回 null</returns>
        private string SaveScreenshotPngToDisk(BitmapSource source)
        {
            try
            {
                if (source == null) return null;

                var basePath = Settings?.Automation?.AutoSavedStrokesLocation;
                if (string.IsNullOrWhiteSpace(basePath)) basePath = @"D:\Ink Canvas";

                string folderPath = Path.Combine(basePath, "Auto Saved - Screenshots");
                if (Settings.Automation.IsSaveScreenshotsInDateFolders)
                {
                    folderPath = Path.Combine(folderPath, DateTime.Now.ToString("yyyy-MM-dd"));
                }
                if (!Directory.Exists(folderPath))
                {
                    Directory.CreateDirectory(folderPath);
                }

                string savePath = Path.Combine(folderPath, $"{DateTime.Now:yyyy-MM-dd_HH-mm-ss}.png");

                using (var fileStream = new FileStream(savePath, FileMode.Create, FileAccess.Write))
                {
                    var encoder = new PngBitmapEncoder();
                    encoder.Frames.Add(BitmapFrame.Create(source));
                    encoder.Save(fileStream);
                }

                return savePath;
            }
            catch (Exception ex)
            {
                LogHelper.WriteLogToFile($"保存截图文件失败: {ex.Message}", LogHelper.LogType.Error);
                return null;
            }
        }

        /// <summary>
        /// 将截图复制到剪贴板。
        /// 同时写入两类数据：
        ///   1) 图片数据（BitmapSource）：可直接粘贴到 Word、聊天窗口、画图等；
        ///   2) 文件拖放列表（PNG 文件）：可在资源管理器/桌面或其它位置粘贴为图片文件，
        ///      从而实现“把截图文件复制到其它地方”。
        /// </summary>
        /// <param name="source">截图位图</param>
        /// <param name="existingFilePath">已存在的截图文件路径；为空时会自动另存一份 PNG</param>
        /// <param name="isHideNotification">是否隐藏通知</param>
        private void CopyScreenshotToClipboard(BitmapSource source, string existingFilePath = null, bool isHideNotification = false)
        {
            if (source == null)
            {
                if (!isHideNotification) ShowNotificationAsync("截图为空，无法复制到剪贴板");
                return;
            }

            try
            {
                string filePath = existingFilePath;
                if (string.IsNullOrEmpty(filePath) || !File.Exists(filePath))
                {
                    filePath = SaveScreenshotPngToDisk(source);
                }

                // DataObject / Clipboard 需要显式限定为 WPF 版本：
                // 本文件同时 using 了 System.Windows.Forms，两者都有同名类型。
                var dataObject = new System.Windows.DataObject();
                // 图片数据：用于直接粘贴为图像
                dataObject.SetImage(source);

                // 文件拖放列表：用于在资源管理器中粘贴为文件，方便复制到其它位置
                if (!string.IsNullOrEmpty(filePath) && File.Exists(filePath))
                {
                    var fileDropList = new StringCollection();
                    fileDropList.Add(filePath);
                    dataObject.SetFileDropList(fileDropList);
                }

                // copy=true：即使本程序随后退出，剪贴板内容依然保留
                System.Windows.Clipboard.SetDataObject(dataObject, true);

                if (!isHideNotification)
                {
                    ShowNotificationAsync(string.IsNullOrEmpty(filePath)
                        ? "截图已复制到剪贴板"
                        : $"截图已复制到剪贴板，文件已保存至 {filePath}");
                }
            }
            catch (Exception ex)
            {
                LogHelper.WriteLogToFile($"复制截图到剪贴板失败: {ex.Message}", LogHelper.LogType.Error);
                if (!isHideNotification) ShowNotificationAsync($"复制截图到剪贴板失败：{ex.Message}");
            }
        }

        private static string SanitizeFileName(string name)
        {
            if (string.IsNullOrEmpty(name)) return string.Empty;
            var invalidChars = Path.GetInvalidFileNameChars();
            var result = name.Replace('\\', '-').Replace('/', '-');
            foreach (var ch in invalidChars)
            {
                result = result.Replace(ch, '-');
            }
            return result.Trim();
        }
    }
}
