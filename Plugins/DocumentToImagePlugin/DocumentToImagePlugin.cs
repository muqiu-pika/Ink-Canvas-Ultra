using Ink_Canvas.Plugins;
using Ink_Canvas.Plugins.DocumentToImage.Converters;
using Ink_Canvas.Plugins.DocumentToImage.UI;
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace Ink_Canvas.Plugins.DocumentToImage
{
    /// <summary>
    /// 文档转照片插件：将 Word / Excel / PDF 文件转为图片并插入 ICU 照片列表。
    /// 通过注册 document-open 路由响应主程序的「导入文档」按钮和命令行打开。
    /// 使用开源 NPOI 与 PdfiumViewer，无需安装 Microsoft Office。
    /// </summary>
    public class DocumentToImagePlugin : IPlugin
    {
        private IPluginHost _host;
        private string _pluginDirectory;
        private ResolveEventHandler _assemblyResolveHandler;

        public PluginManifest Manifest { get; } = new PluginManifest
        {
            Id = "ink-canvas.document-to-image",
            Name = "文档转照片",
            Version = "2.0.0",
            Author = "muqiu",
            Description = "将 Word / Excel / PDF 文件转换为图片并添加到照片列表（无需 Office）",
            EntryAssembly = "DocumentToImagePlugin.dll",
            EntryClass = "Ink_Canvas.Plugins.DocumentToImage.DocumentToImagePlugin",
            MinHostVersion = "7.0.0"
        };

        public void Initialize(IPluginHost host)
        {
            _host = host ?? throw new ArgumentNullException(nameof(host));

            // ICU 使用 Assembly.Load(byte[]) 加载插件主 DLL，导致 .NET 不会自动探测插件目录中的依赖。
            // 注册 AssemblyResolve 事件，手动从插件安装目录加载 NPOI、PdfiumViewer 等依赖 DLL。
            _pluginDirectory = FindPluginDirectory();
            _assemblyResolveHandler = CurrentDomain_AssemblyResolve;
            AppDomain.CurrentDomain.AssemblyResolve += _assemblyResolveHandler;

            _host.RegisterRouteHandler("document-open", OnDocumentOpen);
            _host.Log("文档转照片插件已加载", PluginLogLevel.Info);
        }

        public void Shutdown()
        {
            if (_assemblyResolveHandler != null)
                AppDomain.CurrentDomain.AssemblyResolve -= _assemblyResolveHandler;
            _host?.UnregisterRouteHandler("document-open");
            _host?.Log("文档转照片插件已卸载", PluginLogLevel.Info);
        }

        /// <summary>
        /// 在 ICU Plugins 目录下查找本插件的安装目录。
        /// 通过查找包含 DocumentToImagePlugin.dll 与 plugin.icplugin 的子目录确定。
        /// </summary>
        private string FindPluginDirectory()
        {
            try
            {
                string root = _host?.PluginDirectory;
                if (string.IsNullOrEmpty(root) || !Directory.Exists(root))
                    return null;

                foreach (var dir in Directory.GetDirectories(root))
                {
                    if (File.Exists(Path.Combine(dir, "DocumentToImagePlugin.dll")) &&
                        File.Exists(Path.Combine(dir, "plugin.icplugin")))
                    {
                        return dir;
                    }
                }
            }
            catch { }
            return null;
        }

        /// <summary>
        /// 当 .NET 无法解析本插件的依赖程序集时，从插件安装目录加载对应 DLL。
        /// </summary>
        private Assembly CurrentDomain_AssemblyResolve(object sender, ResolveEventArgs args)
        {
            try
            {
                string name = new AssemblyName(args.Name).Name;
                if (string.IsNullOrEmpty(_pluginDirectory))
                    return null;

                string path = Path.Combine(_pluginDirectory, name + ".dll");
                if (File.Exists(path))
                    return Assembly.LoadFrom(path);
            }
            catch { }
            return null;
        }

        /// <summary>
        /// 处理 document-open 路由。参数应为文档的完整路径。
        /// </summary>
        private bool OnDocumentOpen(object parameter)
        {
            string filePath = parameter as string;
            if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
            {
                _host?.ShowNotification("未找到要导入的文档");
                return false;
            }

            int dpi = _host?.PhotoClarityDpi ?? 150;
            ConversionProgressWindow progressWindow = null;

            // 在主线程创建进度窗口，确保 Owner 与 Dispatcher 正确
            _host?.MainWindow?.Dispatcher.Invoke(() =>
            {
                progressWindow = new ConversionProgressWindow(_host.MainWindow);
                progressWindow.Show();
            });

            var progress = new Progress<ConversionProgress>(p => progressWindow?.Report(p));

            // XPS 与 WPF 文档渲染需要 STA 线程，在线程池（MTA）中会报 DocumentReference 等异常。
            // 创建一个后台 STA 线程执行转换，避免阻塞主窗口 UI。
            var conversionThread = new Thread(() => ConvertAndAddAsync(filePath, dpi, progress, progressWindow))
            {
                IsBackground = true,
                Name = "DocumentToImageConversion"
            };
            conversionThread.SetApartmentState(ApartmentState.STA);
            conversionThread.Start();
            return true;
        }

        private void ConvertAndAddAsync(string filePath, int dpi, IProgress<ConversionProgress> progress, ConversionProgressWindow progressWindow)
        {
            bool succeeded = false;
            try
            {
                List<BitmapImage> images = ConvertDocumentToImages(filePath, dpi, progress);
                if (images == null || images.Count == 0)
                {
                    _host?.MainWindow?.Dispatcher.Invoke(() => progressWindow?.SetError("未生成任何照片"));
                    ShowNotification("文档转图片失败，未生成任何照片");
                    return;
                }

                _host?.MainWindow?.Dispatcher.Invoke(() =>
                {
                    progressWindow?.SetDone($"已将 {images.Count} 张文档照片放入照片列表");

                    // 仅添加到侧栏照片列表，不直接插入画板；用户需手动点击照片按钮插入。
                    foreach (var image in images)
                    {
                        _host?.AddCapturedPhoto(image, filePath);
                    }
                });

                succeeded = true;
                ShowNotification($"已将 {images.Count} 张文档照片放入照片列表，可手动点击插入画板");
            }
            catch (Exception ex)
            {
                _host?.Log($"文档转图片失败 [{filePath}]: {ex}", PluginLogLevel.Error);
                _host?.MainWindow?.Dispatcher.Invoke(() => progressWindow?.SetError(ex.Message));
                ShowNotification($"文档导入失败: {ex.Message}");
            }
            finally
            {
                // 成功时短暂停留，失败时停留更久方便查看错误
                _host?.MainWindow?.Dispatcher.Invoke(async () =>
                {
                    if (progressWindow != null)
                    {
                        await Task.Delay(succeeded ? 600 : 2500);
                        progressWindow.Close();
                    }
                });
            }
        }

        private List<BitmapImage> ConvertDocumentToImages(string filePath, int dpi, IProgress<ConversionProgress> progress)
        {
            string ext = Path.GetExtension(filePath).ToLowerInvariant();

            if (ext == ".docx")
            {
                return WordToImageConverter.Convert(filePath, dpi, progress);
            }
            else if (ext == ".xls" || ext == ".xlsx")
            {
                return ExcelToImageConverter.Convert(filePath, dpi, progress);
            }
            else if (ext == ".pdf")
            {
                return PdfToImageConverter.Convert(filePath, dpi, progress);
            }
            else
            {
                throw new NotSupportedException($"不支持的文件格式: {ext}");
            }
        }

        private void ShowNotification(string message)
        {
            try
            {
                if (_host?.MainWindow?.Dispatcher != null)
                {
                    _host.MainWindow.Dispatcher.Invoke(() => _host.ShowNotification(message));
                }
                else
                {
                    _host?.ShowNotification(message);
                }
            }
            catch { }
        }
    }
}
