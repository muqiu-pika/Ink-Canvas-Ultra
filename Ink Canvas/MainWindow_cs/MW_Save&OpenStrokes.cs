using Ink_Canvas.Helpers;
using Microsoft.Win32;
using System;
using System.IO;
using System.IO.Compression;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Ink;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Threading.Tasks;

namespace Ink_Canvas
{
    public partial class MainWindow : Window
    {
        // 将内存图像持久化为文件，返回保存后的绝对路径
        private string SaveBitmapSourceToFile(BitmapSource source)
        {
            try
            {
                string dir = Settings.Automation.AutoSavedStrokesLocation + @"\File Dependency";
                if (!Directory.Exists(dir))
                {
                    Directory.CreateDirectory(dir);
                }

                string fileName = $"InlineImage_{DateTime.Now:yyyyMMdd_HHmmssfff}_{Guid.NewGuid().ToString("N").Substring(0,8)}.png";
                string path = Path.Combine(dir, fileName);

                var encoder = new PngBitmapEncoder();
                encoder.Frames.Add(BitmapFrame.Create(source));
                using (var fs = new FileStream(path, FileMode.Create, FileAccess.Write))
                {
                    encoder.Save(fs);
                }
                return path;
            }
            catch (Exception ex)
            {
                LogHelper.WriteLogToFile($"保存内存图像到文件失败: {ex.Message}", LogHelper.LogType.Error);
                return null;
            }
        }

        // 克隆单个元素用于快照保存：对 Image 做手动构造以避免 XamlReader 在无 UriSource/StreamSource 时抛异常
        private UIElement CloneElementForSnapshot(UIElement element)
        {
            try
            {
                if (element is Image origImg)
                {
                    string filePath = null;
                    if (origImg.Source is BitmapImage bi && bi.UriSource != null)
                    {
                        filePath = bi.UriSource.LocalPath;
                    }
                    else if (origImg.Source is BitmapSource bs)
                    {
                        filePath = SaveBitmapSourceToFile(bs);
                    }

                    if (!string.IsNullOrEmpty(filePath))
                    {
                        var newBi = new BitmapImage();
                        newBi.BeginInit();
                        newBi.UriSource = new Uri(filePath, UriKind.Absolute);
                        newBi.CacheOption = BitmapCacheOption.OnLoad;
                        newBi.EndInit();
                        newBi.Freeze();

                        var clonedImage = new Image
                        {
                            Source = newBi,
                            Width = origImg.Width,
                            Height = origImg.Height,
                            Stretch = origImg.Stretch,
                            Name = origImg.Name
                        };
                        try { clonedImage.RenderTransform = origImg.RenderTransform?.CloneCurrentValue(); } catch { }
                        try
                        {
                            double left = InkCanvas.GetLeft(origImg);
                            double top = InkCanvas.GetTop(origImg);
                            InkCanvas.SetLeft(clonedImage, left);
                            InkCanvas.SetTop(clonedImage, top);
                        }
                        catch { }
                        try { clonedImage.Tag = "File Dependency/" + System.IO.Path.GetFileName(filePath); } catch { }
                        return clonedImage;
                    }

                    // 无法获取文件路径则跳过该图像，避免不可解析的内存源导致异常
                    return null;
                }

                // 非 Image 走常规 XAML 克隆路径
                var xaml = XamlWriter.Save(element);
                return (UIElement)XamlReader.Parse(xaml);
            }
            catch (Exception ex)
            {
                LogHelper.WriteLogToFile($"克隆元素用于快照失败: {ex.Message}", LogHelper.LogType.Error);
                return null;
            }
        }

        // 创建用于保存的可序列化画布（将所有内存图像替换为文件引用）
        private InkCanvas CreateSerializableCanvasForSnapshot()
        {
            var canvas = new InkCanvas();
            try
            {
                foreach (UIElement child in inkCanvas.Children)
                {
                    var cloned = CloneElementForSnapshot(child);
                    if (cloned != null)
                    {
                        canvas.Children.Add(cloned);
                    }
                }
            }
            catch (Exception ex)
            {
                LogHelper.WriteLogToFile($"创建可序列化画布失败: {ex.Message}", LogHelper.LogType.Error);
            }
            return canvas;
        }

        private InkCanvas CreateSerializableCanvasForPage(int pageIndex)
        {
            var canvas = new InkCanvas();
            try
            {
                var histories = TimeMachineHistories[pageIndex];
                if (histories != null)
                {
                    foreach (var h in histories)
                    {
                        ApplyHistoryToCanvasOn(canvas, h);
                    }
                }

                foreach (UIElement child in canvas.Children)
                {
                    if (child is Image img)
                    {
                        try
                        {
                            string filePath = null;
                            if (img.Source is BitmapImage bi && bi.UriSource != null)
                            {
                                filePath = bi.UriSource.LocalPath;
                            }
                            else if (img.Source is BitmapSource bs)
                            {
                                filePath = SaveBitmapSourceToFile(bs);
                            }

                            if (!string.IsNullOrEmpty(filePath))
                            {
                                var bi2 = new BitmapImage();
                                bi2.BeginInit();
                                bi2.UriSource = new Uri(filePath, UriKind.Absolute);
                                bi2.CacheOption = BitmapCacheOption.OnLoad;
                                bi2.EndInit();
                                bi2.Freeze();
                                img.Source = bi2;
                                try { img.Tag = "File Dependency/" + System.IO.Path.GetFileName(filePath); } catch { }
                            }
                        }
                        catch { }
                    }
                }
            }
            catch (Exception ex)
            {
                LogHelper.WriteLogToFile($"创建页面可序列化画布失败: {ex.Message}", LogHelper.LogType.Error);
            }
            return canvas;
        }

        // 针对指定画布保存相关依赖文件（图像/媒体源）到压缩包
        private void SaveRelatedUrlFilesForCanvas(ZipArchive archive, InkCanvas canvas)
        {
            string dependencyFolder = "File Dependency";
            foreach (UIElement element in canvas.Children)
            {
                if (element is Image image && image.Source is BitmapImage bitmapImage && bitmapImage.UriSource != null)
                {
                    AddFileToArchive(archive, bitmapImage.UriSource.LocalPath, dependencyFolder);
                }
                else if (element is MediaElement mediaElement && mediaElement.Source != null)
                {
                    AddFileToArchive(archive, mediaElement.Source.LocalPath, dependencyFolder);
                }
                else
                {
                    // 其他类型暂不保存依赖文件
                }
            }
        }

        private void SaveRelatedUrlFilesForCanvas(ZipArchive archive, InkCanvas canvas, string baseFolder)
        {
            foreach (UIElement element in canvas.Children)
            {
                if (element is Image image && image.Source is BitmapImage bitmapImage && bitmapImage.UriSource != null)
                {
                    AddFileToArchive(archive, bitmapImage.UriSource.LocalPath, baseFolder);
                }
                else if (element is MediaElement mediaElement && mediaElement.Source != null)
                {
                    AddFileToArchive(archive, mediaElement.Source.LocalPath, baseFolder);
                }
            }
        }
        private void SymbolIconSaveStrokes_Click(object sender, RoutedEventArgs e)
        {
            if (inkCanvas.Visibility != Visibility.Visible) return;
            AnimationsHelper.HideWithSlideAndFade(BorderTools);
            AnimationsHelper.HideWithSlideAndFade(BoardBorderTools);
            GridNotifications.Visibility = Visibility.Collapsed;
            SaveInkCanvasFile(true, true);
        }

        private void SaveInkCanvasFile(bool newNotice = true, bool saveByUser = false)
        {
            try
            {
                string savePath = Settings.Automation.AutoSavedStrokesLocation
                    + (saveByUser ? @"\User Saved - " : @"\Auto Saved - ")
                    + (currentMode == 0 ? "Annotation Strokes" : "BlackBoard Strokes");

                if (!Directory.Exists(savePath))
                {
                    Directory.CreateDirectory(savePath);
                }

                string savePathWithName = savePath + @"\" + DateTime.Now.ToString("yyyy-MM-dd HH-mm-ss-fff")
                    + (currentMode != 0 ? " Page-" + CurrentWhiteboardIndex + " StrokesCount-" + inkCanvas.Strokes.Count + ".icart" : ".icart");

                using (FileStream fs = new FileStream(savePathWithName, FileMode.Create))
                using (var archive = new ZipArchive(fs, ZipArchiveMode.Create))
                {
                    // Save strokes
                    var strokesEntry = archive.CreateEntry("strokes.icstk");
                    using (var strokesStream = strokesEntry.Open())
                    {
                        inkCanvas.Strokes.Save(strokesStream);
                    }

                    // Save UI elements
                    var elementsEntry = archive.CreateEntry("elements.xaml");
                    using (var elementsStream = elementsEntry.Open())
                    {
                        var serializableCanvas = CreateSerializableCanvasForSnapshot();
                        XamlWriter.Save(serializableCanvas, elementsStream);
                    }

                    // Save related URL files based on serializable canvas
                    var serializableCanvasForFiles = CreateSerializableCanvasForSnapshot();
                    SaveRelatedUrlFilesForCanvas(archive, serializableCanvasForFiles);

                    if (newNotice)
                    {
                        ShowNotificationAsync("墨迹及元素成功保存至 " + savePathWithName);
                    }
                }
            }
            catch (Exception Ex)
            {
                ShowNotificationAsync("墨迹及元素保存失败！");
                LogHelper.WriteLogToFile("墨迹及元素保存失败 | " + Ex.ToString(), LogHelper.LogType.Error);
            }
        }

        private void SaveRelatedUrlFiles(ZipArchive archive)
        {
            string dependencyFolder = "File Dependency";
            foreach (UIElement element in inkCanvas.Children)
            {
                if (element is Image image && image.Source is BitmapImage bitmapImage && bitmapImage.UriSource != null)
                {
                    AddFileToArchive(archive, bitmapImage.UriSource.LocalPath, dependencyFolder);
                }
                else if (element is MediaElement mediaElement && mediaElement.Source != null)
                {
                    AddFileToArchive(archive, mediaElement.Source.LocalPath, dependencyFolder);
                }
                else
                {
                    LogHelper.WriteLogToFile("该元素类型暂不支持保存", LogHelper.LogType.Error);
                }
            }
        }

        private void AddFileToArchive(ZipArchive archive, string filePath, string folderName)
        {
            if (File.Exists(filePath))
            {
                string fileName = Path.GetFileName(filePath);
                var fileEntry = archive.CreateEntry(folderName + "/" + fileName);
                using (var entryStream = fileEntry.Open())
                using (var fileStream = File.OpenRead(filePath))
                {
                    fileStream.CopyTo(entryStream);
                }
            }
        }



        private void SymbolIconOpenInkCanvasFile_Click(object sender, RoutedEventArgs e)
        {
            AnimationsHelper.HideWithSlideAndFade(BorderTools);
            AnimationsHelper.HideWithSlideAndFade(BoardBorderTools);

            OpenFileDialog openFileDialog = new OpenFileDialog
            {
                InitialDirectory = Settings.Automation.AutoSavedStrokesLocation,
                Title = "打开墨迹文件",
                Filter = "Ink Canvas Files (*.icart;*.icstk)|*.icart;*.icstk|Ink Canvas Ultra Files (*.icart)|*.icart|Ink Canvas Stroke Files (*.icstk)|*.icstk"
            };

            if (openFileDialog.ShowDialog() == true)
            {
                LogHelper.WriteLogToFile($"Strokes Insert: Name: {openFileDialog.FileName}", LogHelper.LogType.Event);

                try
                {
                    string extension = Path.GetExtension(openFileDialog.FileName).ToLower();
                    using (var fs = new FileStream(openFileDialog.FileName, FileMode.Open))
                    {
                        if (extension == ".icart")
                        {
                            using (var archive = new ZipArchive(fs, ZipArchiveMode.Read))
                            {
                                // load strokes
                                var strokesEntry = archive.GetEntry("strokes.icstk");
                                if (strokesEntry != null)
                                {
                                    using (var strokesStream = strokesEntry.Open())
                                    {
                                        var strokes = new StrokeCollection(strokesStream);
                                        ClearStrokes(true);
                                        timeMachine.ClearStrokeHistory();
                                        inkCanvas.Strokes.Add(strokes);
                                        LogHelper.NewLog($"Strokes Insert: Strokes Count: {inkCanvas.Strokes.Count}");
                                    }
                                }

                                // load URL files
                                string saveDirectory = Settings.Automation.AutoSavedStrokesLocation;
                                ExtractUrlFiles(archive, saveDirectory);

                                // load UI Elements
                                var elementsEntry = archive.GetEntry("elements.xaml");
                                using (var elementsStream = elementsEntry.Open())
                                {
                                    try
                                    {
                                        if (XamlReader.Load(elementsStream) is InkCanvas loadedCanvas)
                                        {
                                            inkCanvas.Children.Clear();
                                            foreach (UIElement child in loadedCanvas.Children)
                                            {
                                                var xaml = XamlWriter.Save(child);
                                                UIElement clonedChild = (UIElement)XamlReader.Parse(xaml);
                                                if (clonedChild is Image image)
                                                {
                                                    try
                                                    {
                                                        if (image.Source is BitmapImage bmi)
                                                        {
                                                            var uri = bmi.UriSource;
                                                            bool needFix = uri == null || (uri != null && !File.Exists(uri.LocalPath));
                                                            if (needFix)
                                                            {
                                                                string candidate = null;
                                                                string tagPath = image.Tag as string;
                                                                if (!string.IsNullOrEmpty(tagPath))
                                                                {
                                                                    candidate = System.IO.Path.Combine(Settings.Automation.AutoSavedStrokesLocation, tagPath.Replace('/', '\\'));
                                                                    if (!File.Exists(candidate))
                                                                    {
                                                                        candidate = System.IO.Path.Combine(Settings.Automation.AutoSavedStrokesLocation, "File Dependency", System.IO.Path.GetFileName(tagPath));
                                                                    }
                                                                }
                                                                if ((candidate == null || !File.Exists(candidate)) && uri != null)
                                                                {
                                                                    string fname = System.IO.Path.GetFileName(uri.LocalPath);
                                                                    if (!string.IsNullOrEmpty(fname))
                                                                    {
                                                                        var fd = System.IO.Path.Combine(Settings.Automation.AutoSavedStrokesLocation, "File Dependency");
                                                                        var tryPath = System.IO.Path.Combine(fd, fname);
                                                                        if (File.Exists(tryPath)) candidate = tryPath;
                                                                    }
                                                                }
                                                                if (candidate != null && File.Exists(candidate))
                                                                {
                                                                    var bi2 = new BitmapImage();
                                                                    bi2.BeginInit();
                                                                    bi2.UriSource = new Uri(candidate, UriKind.Absolute);
                                                                    bi2.CacheOption = BitmapCacheOption.OnLoad;
                                                                    bi2.EndInit();
                                                                    bi2.Freeze();
                                                                    image.Source = bi2;
                                                                }
                                                            }
                                                        }
                                                    }
                                                    catch { }
                                                }
                                                if (clonedChild is MediaElement mediaElement)
                                                {
                                                    mediaElement.LoadedBehavior = MediaState.Manual;
                                                    mediaElement.UnloadedBehavior = MediaState.Manual;
                                                    mediaElement.Loaded += (_, args) =>
                                                    {
                                                        // 所有模式导入后自动播放
                                                        mediaElement.Play();
                                                    };
                                                }
                                                inkCanvas.Children.Add(clonedChild);
                                            }
                                            LogHelper.NewLog($"Elements Insert: Elements Count: {inkCanvas.Children.Count}");
                                        }
                                    }
                                    catch (XamlParseException xamlEx)
                                    {
                                        LogHelper.WriteLogToFile($"XAML 解析错误: {xamlEx.Message}", LogHelper.LogType.Error);
                                        ShowNotificationAsync("加载 UI 元素时出现 XAML 解析错误");
                                    }
                                    catch (Exception ex)
                                    {
                                        LogHelper.WriteLogToFile($"加载 UI 元素失败: {ex.Message}", LogHelper.LogType.Error);
                                        ShowNotificationAsync("加载 UI 元素失败");
                                    }
                                }
                            }
                        }
                        else if (extension == ".icstk")
                        {
                            // 直接加载 .icstk 文件中的墨迹
                            using (var strokesStream = new MemoryStream())
                            {
                                fs.CopyTo(strokesStream);
                                strokesStream.Seek(0, SeekOrigin.Begin);
                                var strokes = new StrokeCollection(strokesStream);
                                ClearStrokes(true);
                                timeMachine.ClearStrokeHistory();
                                inkCanvas.Strokes.Add(strokes);
                                LogHelper.NewLog($"Strokes Insert: Strokes Count: {inkCanvas.Strokes.Count}");
                            }
                        }
                        else
                        {
                            ShowNotificationAsync("不支持的文件格式。");
                        }

                        if (inkCanvas.Visibility != Visibility.Visible)
                        {
                            SymbolIconCursor_Click(sender, null);
                        }
                    }
                }
                catch (Exception ex)
                {
                    ShowNotificationAsync("墨迹或元素打开失败");
                    LogHelper.WriteLogToFile($"打开墨迹或元素失败: {ex.Message}\n{ex.StackTrace}", LogHelper.LogType.Error);
                }
            }
        }

        private void ExtractUrlFiles(ZipArchive archive, string outputDirectory)
        {
            foreach (var entry in archive.Entries)
            {
                if (entry.FullName.StartsWith("File Dependency/", StringComparison.OrdinalIgnoreCase))
                {
                    if (!string.IsNullOrEmpty(entry.Name))
                    {
                        string fileName = Path.Combine(outputDirectory, entry.FullName);

                        string directoryPath = Path.GetDirectoryName(fileName);
                        if (!Directory.Exists(directoryPath))
                        {
                            Directory.CreateDirectory(directoryPath);
                        }
                        entry.ExtractToFile(fileName, overwrite: true);
                    }
                }
            }
        }

        /// <summary>
        /// 保存一个固定路径的会话快照，用于重启后恢复。
        /// 文件会保存到 AutoSavedStrokesLocation\Auto Saved - Session\LastSession.icart。
        /// 同时保存会话元信息到 SessionMeta.txt。
        /// </summary>
        public void SaveLastSessionSnapshot()
        {
            try
            {
                string basePath = Settings.Automation.AutoSavedStrokesLocation + @"\Auto Saved - Session";
                if (!Directory.Exists(basePath))
                {
                    Directory.CreateDirectory(basePath);
                }

                string savePathWithName = basePath + @"\LastSession.icart";
                using (FileStream fs = new FileStream(savePathWithName, FileMode.Create))
                using (var archive = new ZipArchive(fs, ZipArchiveMode.Create))
                {
                    var strokesEntry = archive.CreateEntry("strokes.icstk");
                    using (var strokesStream = strokesEntry.Open())
                    {
                        inkCanvas.Strokes.Save(strokesStream);
                    }

                    var elementsEntry = archive.CreateEntry("elements.xaml");
                    using (var elementsStream = elementsEntry.Open())
                    {
                        var serializableCanvas = CreateSerializableCanvasForSnapshot();
                        XamlWriter.Save(serializableCanvas, elementsStream);
                    }

                    var serializableCanvasForFiles = CreateSerializableCanvasForSnapshot();
                    SaveRelatedUrlFilesForCanvas(archive, serializableCanvasForFiles);

                    for (int i = 1; i <= WhiteboardTotalCount; i++)
                    {
                        var pageCanvas = CreateSerializableCanvasForPage(i);
                        var pageFolder = $"pages/{i}";
                        var pageStrokesEntry = archive.CreateEntry(pageFolder + "/strokes.icstk");
                        using (var ps = pageStrokesEntry.Open())
                        {
                            pageCanvas.Strokes.Save(ps);
                        }
                        var pageElementsEntry = archive.CreateEntry(pageFolder + "/elements.xaml");
                        using (var pe = pageElementsEntry.Open())
                        {
                            XamlWriter.Save(pageCanvas, pe);
                        }
                        SaveRelatedUrlFilesForCanvas(archive, pageCanvas, pageFolder + "/File Dependency");
                    }
                }

                // 保存元信息（模式与白板页索引、是否处于PPT放映）
                string metaPath = basePath + @"\SessionMeta.txt";
                try
                {
                    File.WriteAllText(metaPath, $"mode={currentMode}\nwhiteboard={CurrentWhiteboardIndex}\nppt={(BtnPPTSlideShowEnd.Visibility == Visibility.Visible ? 1 : 0)}\nwhiteboard_total={WhiteboardTotalCount}");
                }
                catch { }

                LogHelper.WriteLogToFile($"Saved Last Session Snapshot: {savePathWithName}", LogHelper.LogType.Event);
            }
            catch (Exception ex)
            {
                LogHelper.WriteLogToFile("保存会话快照失败 | " + ex.ToString(), LogHelper.LogType.Error);
            }
        }

        /// <summary>
        /// 打开固定路径的会话快照。如果存在，则加载墨迹与元素并尝试自动播放媒体元素。
        /// </summary>
        private bool OpenLastSessionSnapshotIfExists()
        {
            try
            {
                string basePath = Settings.Automation.AutoSavedStrokesLocation + @"\Auto Saved - Session";
                string icartPath = basePath + @"\LastSession.icart";
                if (!File.Exists(icartPath)) return false;

                using (var fs = new FileStream(icartPath, FileMode.Open))
                using (var archive = new ZipArchive(fs, ZipArchiveMode.Read))
                {
                    var strokesEntry = archive.GetEntry("strokes.icstk");
                    if (strokesEntry != null)
                    {
                        using (var strokesStream = strokesEntry.Open())
                        {
                            var strokes = new StrokeCollection(strokesStream);
                            ClearStrokes(true);
                            timeMachine.ClearStrokeHistory();
                            inkCanvas.Strokes.Add(strokes);
                        }
                    }

                    // 提取依赖文件到 AutoSavedStrokesLocation 下
                    ExtractUrlFiles(archive, Settings.Automation.AutoSavedStrokesLocation);

                    var elementsEntry = archive.GetEntry("elements.xaml");
                    if (elementsEntry != null)
                    {
                        using (var elementsStream = elementsEntry.Open())
                        {
                            if (XamlReader.Load(elementsStream) is InkCanvas loadedCanvas)
                            {
                                inkCanvas.Children.Clear();
                                foreach (UIElement child in loadedCanvas.Children)
                                {
                                    var xaml = XamlWriter.Save(child);
                                    UIElement clonedChild = (UIElement)XamlReader.Parse(xaml);
                                    if (clonedChild is Image image)
                                    {
                                        try
                                        {
                                            if (image.Source is BitmapImage bmi)
                                            {
                                                var uri = bmi.UriSource;
                                                bool needFix = uri == null || (uri != null && !File.Exists(uri.LocalPath));
                                                if (needFix)
                                                {
                                                    string candidate = null;
                                                    string tagPath = image.Tag as string;
                                                    if (!string.IsNullOrEmpty(tagPath))
                                                    {
                                                        candidate = System.IO.Path.Combine(Settings.Automation.AutoSavedStrokesLocation, tagPath.Replace('/', '\\'));
                                                        if (!File.Exists(candidate))
                                                        {
                                                            candidate = System.IO.Path.Combine(Settings.Automation.AutoSavedStrokesLocation, "File Dependency", System.IO.Path.GetFileName(tagPath));
                                                        }
                                                    }
                                                    if ((candidate == null || !File.Exists(candidate)) && uri != null)
                                                    {
                                                        string fname = System.IO.Path.GetFileName(uri.LocalPath);
                                                        if (!string.IsNullOrEmpty(fname))
                                                        {
                                                            var fd = System.IO.Path.Combine(Settings.Automation.AutoSavedStrokesLocation, "File Dependency");
                                                            var tryPath = System.IO.Path.Combine(fd, fname);
                                                            if (File.Exists(tryPath)) candidate = tryPath;
                                                        }
                                                    }
                                                    if (candidate != null && File.Exists(candidate))
                                                    {
                                                        var bi2 = new BitmapImage();
                                                        bi2.BeginInit();
                                                        bi2.UriSource = new Uri(candidate, UriKind.Absolute);
                                                        bi2.CacheOption = BitmapCacheOption.OnLoad;
                                                        bi2.EndInit();
                                                        bi2.Freeze();
                                                        image.Source = bi2;
                                                    }
                                                }
                                            }
                                        }
                                        catch { }
                                    }
                                    if (clonedChild is MediaElement mediaElement)
                                    {
                                        mediaElement.LoadedBehavior = MediaState.Manual;
                                        mediaElement.UnloadedBehavior = MediaState.Manual;
                                        mediaElement.Loaded += (_, __) => { mediaElement.Play(); };
                                    }
                                    inkCanvas.Children.Add(clonedChild);
                                }
                            }
                        }
                    }

                    foreach (var entry in archive.Entries)
                    {
                        if (entry.FullName.StartsWith("pages/", StringComparison.OrdinalIgnoreCase))
                        {
                            string outPath = System.IO.Path.Combine(basePath, entry.FullName.Replace('/', '\\'));
                            string dir = System.IO.Path.GetDirectoryName(outPath);
                            if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
                            entry.ExtractToFile(outPath, overwrite: true);
                        }
                    }
                }

                if (inkCanvas.Visibility != Visibility.Visible)
                {
                    SymbolIconCursor_Click(null, null);
                }

                LogHelper.NewLog("Restored Last Session Snapshot");
                return true;
            }
            catch (Exception ex)
            {
                LogHelper.WriteLogToFile("恢复会话快照失败 | " + ex.ToString(), LogHelper.LogType.Error);
                return false;
            }
        }

        // 添加一个标志来跟踪用户是否已确认恢复会话
        private bool _isRestoreSessionConfirmed = false;

        // 当用户通过对话框确认恢复会话时，设置此标志
        public void ConfirmRestoreSession()
        {
            _isRestoreSessionConfirmed = true;
        }

        private void RestorePageFromDiskIfAvailable(int pageIndex)
        {
            // 只有在用户确认恢复会话后才执行恢复操作
            if (!_isRestoreSessionConfirmed)
            {
                // 检查是否存在会话文件，但不自动恢复
                string basePath = Settings.Automation.AutoSavedStrokesLocation + @"\Auto Saved - Session";
                string pageDir = System.IO.Path.Combine(basePath, "pages", pageIndex.ToString());
                string strokesPath = System.IO.Path.Combine(pageDir, "strokes.icstk");
                string elementsPath = System.IO.Path.Combine(pageDir, "elements.xaml");
                if (!File.Exists(strokesPath) && !File.Exists(elementsPath)) return;
                
                // 不执行清除和恢复操作，保持空白状态
                return;
            }
            
            try
            {
                string basePath = Settings.Automation.AutoSavedStrokesLocation + @"\Auto Saved - Session";
                string pageDir = System.IO.Path.Combine(basePath, "pages", pageIndex.ToString());
                string strokesPath = System.IO.Path.Combine(pageDir, "strokes.icstk");
                string elementsPath = System.IO.Path.Combine(pageDir, "elements.xaml");
                if (!File.Exists(strokesPath) && !File.Exists(elementsPath)) return;
                ClearStrokes(true);
                timeMachine.ClearStrokeHistory();
                if (File.Exists(strokesPath))
                {
                    using (var ss = new FileStream(strokesPath, FileMode.Open))
                    {
                        var strokes = new StrokeCollection(ss);
                        inkCanvas.Strokes.Add(strokes);
                    }
                }
                if (File.Exists(elementsPath))
                {
                    try
                    {
                        using (var es = new FileStream(elementsPath, FileMode.Open))
                        {
                            var obj = XamlReader.Load(es);
                            if (obj is InkCanvas loadedCanvas)
                            {
                                foreach (UIElement child in loadedCanvas.Children)
                                {
                                    try
                                    {
                                        var xaml = XamlWriter.Save(child);
                                        UIElement clonedChild = (UIElement)XamlReader.Parse(xaml);
                                        if (clonedChild is Image image)
                                        {
                                            try
                                            {
                                                if (image.Source is BitmapImage bmi)
                                                {
                                                    var uri = bmi.UriSource;
                                                    bool needFix = uri == null || (uri != null && !File.Exists(uri.LocalPath));
                                                    if (needFix)
                                                    {
                                                        string candidate = null;
                                                        string tagPath = image.Tag as string;
                                                        if (!string.IsNullOrEmpty(tagPath))
                                                        {
                                                            candidate = System.IO.Path.Combine(Settings.Automation.AutoSavedStrokesLocation, tagPath.Replace('/', '\\'));
                                                            if (!File.Exists(candidate))
                                                            {
                                                                candidate = System.IO.Path.Combine(Settings.Automation.AutoSavedStrokesLocation, "File Dependency", System.IO.Path.GetFileName(tagPath));
                                                            }
                                                        }
                                                        if ((candidate == null || !File.Exists(candidate)) && uri != null)
                                                        {
                                                            string fname = System.IO.Path.GetFileName(uri.LocalPath);
                                                            if (!string.IsNullOrEmpty(fname))
                                                            {
                                                                var fd = System.IO.Path.Combine(Settings.Automation.AutoSavedStrokesLocation, "File Dependency");
                                                                var tryPath = System.IO.Path.Combine(fd, fname);
                                                                if (File.Exists(tryPath)) candidate = tryPath;
                                                            }
                                                        }
                                                        if (candidate != null && !string.IsNullOrEmpty(candidate) && File.Exists(candidate))
                                                        {
                                                            try
                                                            {
                                                                var bi2 = new BitmapImage();
                                                                bi2.BeginInit();
                                                                bi2.UriSource = new Uri(candidate, UriKind.Absolute);
                                                                bi2.CacheOption = BitmapCacheOption.OnLoad;
                                                                bi2.EndInit();
                                                                bi2.Freeze();
                                                                image.Source = bi2;
                                                            }
                                                            catch (Exception ex)
                                                            {
                                                                // 记录错误但不中断程序
                                                                Ink_Canvas.Helpers.LogHelper.WriteLogToFile("Failed to load image: " + ex.Message, Ink_Canvas.Helpers.LogHelper.LogType.Error);
                                                            }
                                                        }
                                                    }
                                                }
                                            }
                                            catch { }
                                        }
                                        if (clonedChild is MediaElement mediaElement)
                                        {
                                            mediaElement.LoadedBehavior = MediaState.Manual;
                                            mediaElement.UnloadedBehavior = MediaState.Manual;
                                            mediaElement.Loaded += (_, __) => { mediaElement.Play(); };
                                        }
                                        inkCanvas.Children.Add(clonedChild);
                                    }
                                    catch { }
                                }
                            }
                        }
                    }
                    catch (Exception exLoad)
                    {
                        LogHelper.WriteLogToFile($"加载页面元素失败: {exLoad.Message}", LogHelper.LogType.Error);
                    }
                }
            }
            catch { }
        }

        private void ApplyHistoryToCanvasOn(InkCanvas target, TimeMachineHistory item)
        {
            try
            {
                if (item.CommitType == TimeMachineHistoryType.UserInput)
                {
                    if (!item.StrokeHasBeenCleared)
                    {
                        foreach (var strokes in item.CurrentStroke)
                        {
                            if (!target.Strokes.Contains(strokes)) target.Strokes.Add(strokes);
                        }
                    }
                    else
                    {
                        foreach (var strokes in item.CurrentStroke)
                        {
                            if (target.Strokes.Contains(strokes)) target.Strokes.Remove(strokes);
                        }
                    }
                }
                else if (item.CommitType == TimeMachineHistoryType.ShapeRecognition)
                {
                    if (item.StrokeHasBeenCleared)
                    {
                        foreach (var strokes in item.CurrentStroke)
                        {
                            if (target.Strokes.Contains(strokes)) target.Strokes.Remove(strokes);
                        }
                        foreach (var strokes in item.ReplacedStroke)
                        {
                            if (!target.Strokes.Contains(strokes)) target.Strokes.Add(strokes);
                        }
                    }
                    else
                    {
                        foreach (var strokes in item.CurrentStroke)
                        {
                            if (!target.Strokes.Contains(strokes)) target.Strokes.Add(strokes);
                        }
                        foreach (var strokes in item.ReplacedStroke)
                        {
                            if (target.Strokes.Contains(strokes)) target.Strokes.Remove(strokes);
                        }
                    }
                }
                else if (item.CommitType == TimeMachineHistoryType.Manipulation)
                {
                    if (!item.StrokeHasBeenCleared)
                    {
                        if (item.StylusPointDictionary != null)
                        {
                            foreach (var currentStroke in item.StylusPointDictionary)
                            {
                                if (target.Strokes.Contains(currentStroke.Key)) currentStroke.Key.StylusPoints = currentStroke.Value.Item2;
                            }
                        }
                        if (item.ElementsManipulationHistory != null)
                        {
                            foreach (var currentElement in item.ElementsManipulationHistory)
                            {
                                FrameworkElement fe = null;
                                foreach (UIElement child in target.Children)
                                {
                                    if (child is FrameworkElement fe2 && fe2.Name == currentElement.Key) { fe = fe2; break; }
                                }
                                if (fe != null && target.Children.Contains(fe)) fe.RenderTransform = currentElement.Value.Item2;
                                else
                                {
                                    if (currentElement.Value.Item1 is InkCanvasElementsHelper.ElementData)
                                    {
                                        var ed = currentElement.Value.Item1 as InkCanvasElementsHelper.ElementData;
                                        InkCanvas.SetLeft(ed.FrameworkElement, ed.SetLeftData);
                                        InkCanvas.SetTop(ed.FrameworkElement, ed.SetTopData);
                                        target.Children.Add(ed.FrameworkElement);
                                        ed.FrameworkElement.RenderTransform = currentElement.Value.Item2;
                                    }
                                }
                            }
                        }
                    }
                    else
                    {
                        if (item.StylusPointDictionary != null)
                        {
                            foreach (var currentStroke in item.StylusPointDictionary)
                            {
                                if (target.Strokes.Contains(currentStroke.Key)) currentStroke.Key.StylusPoints = currentStroke.Value.Item1;
                            }
                        }
                        if (item.ElementsManipulationHistory != null)
                        {
                            foreach (var currentElement in item.ElementsManipulationHistory)
                            {
                                FrameworkElement fe = null;
                                foreach (UIElement child in target.Children)
                                {
                                    if (child is FrameworkElement fe2 && fe2.Name == currentElement.Key) { fe = fe2; break; }
                                }
                                if (fe != null && target.Children.Contains(fe))
                                {
                                    if (currentElement.Value.Item1 is TransformGroup tg) fe.RenderTransform = tg;
                                    else if (currentElement.Value.Item1 is InkCanvasElementsHelper.ElementData) target.Children.Remove(fe);
                                }
                            }
                        }
                    }
                }
                else if (item.CommitType == TimeMachineHistoryType.DrawingAttributes)
                {
                    if (!item.StrokeHasBeenCleared)
                    {
                        foreach (var currentStroke in item.DrawingAttributes)
                        {
                            if (target.Strokes.Contains(currentStroke.Key)) currentStroke.Key.DrawingAttributes = currentStroke.Value.Item2;
                        }
                    }
                    else
                    {
                        foreach (var currentStroke in item.DrawingAttributes)
                        {
                            if (target.Strokes.Contains(currentStroke.Key)) currentStroke.Key.DrawingAttributes = currentStroke.Value.Item1;
                        }
                    }
                }
                else if (item.CommitType == TimeMachineHistoryType.Clear)
                {
                    if (!item.StrokeHasBeenCleared)
                    {
                        if (item.CurrentStroke != null)
                        {
                            foreach (var currentStroke in item.CurrentStroke)
                            {
                                if (!target.Strokes.Contains(currentStroke)) target.Strokes.Add(currentStroke);
                            }
                        }
                        if (item.ReplacedStroke != null)
                        {
                            foreach (var replacedStroke in item.ReplacedStroke)
                            {
                                if (target.Strokes.Contains(replacedStroke)) target.Strokes.Remove(replacedStroke);
                            }
                        }
                    }
                    else
                    {
                        if (item.ReplacedStroke != null)
                        {
                            foreach (var replacedStroke in item.ReplacedStroke)
                            {
                                if (!target.Strokes.Contains(replacedStroke)) target.Strokes.Add(replacedStroke);
                            }
                        }
                        if (item.CurrentStroke != null)
                        {
                            foreach (var currentStroke in item.CurrentStroke)
                            {
                                if (target.Strokes.Contains(currentStroke)) target.Strokes.Remove(currentStroke);
                            }
                        }
                    }
                }
                else if (item.CommitType == TimeMachineHistoryType.ElementInsert)
                {
                    if (!item.StrokeHasBeenCleared) target.Children.Add(item.Element);
                    else target.Children.Remove(item.Element);
                }
            }
            catch { }
        }
    }
}
