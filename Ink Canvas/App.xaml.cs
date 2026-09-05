using Ink_Canvas.Helpers;
using iNKORE.UI.WPF.Modern.Controls;
using System;
using System.Linq;
using System.IO;
using System.IO.Pipes;
using System.Diagnostics;
using System.Reflection;
using System.Text;
using System.Windows;
using MessageBox = System.Windows.MessageBox;
using Microsoft.Win32;

namespace Ink_Canvas
{
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App : Application
    {
        public const string VideoPresenterLaunchArgument = "--video-presenter";
        public const string SingleInstancePipeName = "Ink_Canvas_Ultra_CommandPipe";
        public const string ActivateVideoPresenterCommand = "activate-video-presenter";

        System.Threading.Mutex mutex;

        public static string[] StartArgs = null;
        public static string RootPath = Environment.GetEnvironmentVariable("APPDATA") + "\\Ink Canvas\\";

        /// <summary>
        /// 用户数据目录（始终可写），用于存放 Settings.json 等需要持久化的用户数据。
        /// 即使程序被安装到 Program Files 等需要管理员权限才能写入的目录，
        /// 该路径仍然可由当前用户写入，避免设置静默保存失败的问题。
        /// </summary>
        public static string UserDataPath = Environment.GetEnvironmentVariable("APPDATA") + "\\Ink Canvas\\";
        private static bool _userDataPathInitialized = false;

        /// <summary>
        /// 初始化 UserDataPath 并在首次启动时把旧版本遗留在 exe 目录下的 Settings.json 迁移过来。
        /// 必须在 App_Startup 中 RootPath 被重新赋值之后调用。
        /// </summary>
        public static void InitializeUserDataPath()
        {
            if (_userDataPathInitialized) return;
            _userDataPathInitialized = true;
            try
            {
                if (!Directory.Exists(UserDataPath))
                {
                    Directory.CreateDirectory(UserDataPath);
                }

                string legacySettings = RootPath + "Settings.json";
                string newUserSettings = UserDataPath + "Settings.json";
                // 仅当新位置不存在、旧位置存在且二者路径不同时迁移，避免覆盖新位置已有的设置
                if (!File.Exists(newUserSettings)
                    && File.Exists(legacySettings)
                    && !string.Equals(
                        System.IO.Path.GetFullPath(legacySettings).TrimEnd('\\'),
                        System.IO.Path.GetFullPath(newUserSettings).TrimEnd('\\'),
                        StringComparison.OrdinalIgnoreCase))
                {
                    try
                    {
                        File.Copy(legacySettings, newUserSettings, false);
                        LogHelper.NewLog("Migrated Settings.json from exe directory to user data directory: "
                                         + newUserSettings);
                    }
                    catch (Exception ex)
                    {
                        LogHelper.WriteLogToFile(
                            "Failed to migrate Settings.json: " + ex.Message,
                            LogHelper.LogType.Error);
                    }
                }
            }
            catch (Exception ex)
            {
                LogHelper.WriteLogToFile(
                    "InitializeUserDataPath failed: " + ex.Message,
                    LogHelper.LogType.Error);
            }
        }

        public enum StartupMode
        {
            Normal,
            Whiteboard,
            Camera,
            WhiteboardAndCamera
        }

        public static StartupMode CurrentStartupMode = StartupMode.Normal;

        public App()
        {
            InitializeWpfStylusSafetySettings();
            this.Startup += new StartupEventHandler(App_Startup);
            this.DispatcherUnhandledException += App_DispatcherUnhandledException;
            AppDomain.CurrentDomain.UnhandledException += CurrentDomain_UnhandledException;
        }

        private static void InitializeWpfStylusSafetySettings()
        {
            try
            {
                AppContext.SetSwitch("Switch.System.Windows.Input.Stylus.DisableStylusAndTouchSupport", false);
                AppContext.SetSwitch("Switch.System.Windows.Input.Stylus.EnablePointerSupport", false);
            }
            catch { }
        }

        private void CurrentDomain_UnhandledException(object sender, UnhandledExceptionEventArgs e)
        {
            try
            {
                if (e.ExceptionObject is Exception ex)
                {
                    LogHelper.NewLog("Non-UI thread unhandled exception: " + ex.ToString());
                }
                else
                {
                    LogHelper.NewLog("Non-UI thread unhandled exception (unknown type)");
                }
            }
            catch { }

            // 后台线程 / 损坏状态异常（ACCESS_VIOLATION 等）不经过 UI 线程的
            // DispatcherUnhandledException，若不做处理则进程直接终止：既不会触发
            // “崩溃静默重启”，也不会写入 RestartReason.txt，导致重启后无法弹出恢复询问。
            // 因此这里同样执行：保存会话快照 → 写静默重启标记 → 重启新进程。
            try
            {
                var mw = Application.Current?.MainWindow as Ink_Canvas.MainWindow;
                TrySnapshotRestartAndExit(mw, "silent");
            }
            catch { }
        }

        /// <summary>
        /// 保存会话快照并静默重启。先写 RestartReason 标记（尽量在任何可能的中断前完成），
        /// 再写快照（内部先写 meta），最后启动新进程。供 UI 线程与后台线程崩溃时共用。
        /// </summary>
        private bool TrySnapshotRestartAndExit(Ink_Canvas.MainWindow mw, string reason)
        {
            bool silentRestart = true;
            if (Ink_Canvas.MainWindow.Settings?.Advanced != null)
            {
                silentRestart = Ink_Canvas.MainWindow.Settings.Advanced.IsEnableSilentRestartOnCrash;
            }
            if (!silentRestart) return false;

            // 1) 先写 reason（最轻量，确保重启后能识别“需要恢复询问”）
            WriteRestartReason(reason);

            // 2) 尽量在进程终止前把快照落盘（后台线程需要切到 UI 线程访问画布）
            if (mw != null)
            {
                try
                {
                    if (Dispatcher.CheckAccess())
                    {
                        try { mw.SaveLastSessionSnapshot(); } catch { }
                    }
                    else
                    {
                        try { Dispatcher.BeginInvoke(new Action(() => { try { mw.SaveLastSessionSnapshot(); } catch { } })); } catch { }
                        // BeginInvoke 不等待；进程即将终止，交给编辑器尽可能排队的动作执行。
                    }
                }
                catch { }
            }

            // 3) 重启新进程
            try { RestartApplication(); } catch { }
            return true;
        }

        private void WriteRestartReason(string reason)
        {
            try
            {
                string autoPath = (Ink_Canvas.MainWindow.Settings?.Automation != null
                                   && !string.IsNullOrEmpty(Ink_Canvas.MainWindow.Settings.Automation.AutoSavedStrokesLocation))
                    ? Ink_Canvas.MainWindow.Settings.Automation.AutoSavedStrokesLocation
                    : (Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments) + @"\Ink Canvas");
                var basePath = autoPath + @"\Auto Saved - Session";
                try { if (!Directory.Exists(basePath)) Directory.CreateDirectory(basePath); } catch { }
                File.WriteAllText(basePath + @"\RestartReason.txt", reason);
            }
            catch { }
        }

        private void App_DispatcherUnhandledException(object sender, System.Windows.Threading.DispatcherUnhandledExceptionEventArgs e)
        {
            try
            {
                LogHelper.NewLog("UI thread unhandled exception: " + e.Exception?.ToString() ?? "Unknown exception");
            }
            catch { }

            try
            {
                var mw = Application.Current?.MainWindow as Ink_Canvas.MainWindow;
                if (mw != null)
                {
                    // 复用共享逻辑：写理由 → 保存快照 → 重启。若已静默重启走 e.Handled=true 直接返回。
                    if (TrySnapshotRestartAndExit(mw, "silent"))
                    {
                        e.Handled = true;
                        return;
                    }
                }
            }
            catch { }

            try
            {
                var mw2 = Application.Current?.MainWindow as Ink_Canvas.MainWindow;
                if (mw2 != null)
                {
                    Ink_Canvas.MainWindow.ShowNewMessage("抱歉，出现未预期的异常，可能导致 Ink Canvas 画板运行不稳定。\n建议保存墨迹后重启应用。", true);
                }
                else
                {
                    MessageBox.Show("抱歉，出现未预期的异常，可能导致 Ink Canvas 画板运行不稳定。\n建议保存墨迹后重启应用。", "Ink Canvas", MessageBoxButton.OK, MessageBoxImage.Warning);
                }
            }
            catch
            {
                try { MessageBox.Show("抱歉，出现未预期的异常，应用可能运行不稳定。\n建议保存墨迹后重启应用。", "Ink Canvas", MessageBoxButton.OK, MessageBoxImage.Warning); } catch { }
            }
            e.Handled = true;
        }

        private void RestartApplication()
        {
            try
            {
                string exePath = Assembly.GetExecutingAssembly().Location;
                if (string.IsNullOrEmpty(exePath))
                {
                    try { exePath = Process.GetCurrentProcess()?.MainModule?.FileName; } catch { }
                }
                if (string.IsNullOrEmpty(exePath))
                {
                    try
                    {
                        var args0 = Environment.GetCommandLineArgs();
                        if (args0 != null && args0.Length > 0) exePath = args0[0];
                    }
                    catch { }
                }
                if (string.IsNullOrEmpty(exePath)) return;

                string args = (StartArgs != null && StartArgs.Length > 0) ? string.Join(" ", StartArgs) : string.Empty;
                // 使用 -m 允许新进程在旧进程尚未释放互斥量时启动，避免重启失败
                if (!string.IsNullOrEmpty(args)) args += " ";
                args += "-m";
                Process.Start(exePath, args);
            }
            catch { }
            finally
            {
                LogHelper.NewLog("Ink Canvas automatically restarting due to unhandled exception");
                Application.Current.Shutdown();
            }
        }

        void App_Startup(object sender, StartupEventArgs e)
        {
            // 尽早关闭 WPF "Resource not found" 调试噪音（只影响调试器输出，不影响任何运行行为）。
            // 必须在资源初始化 / MainWindow 创建之前执行，否则 TraceResourceDictionary 已开始输出。
            Helpers.SkipResourceNotFound.Install();

            /*if (!StoreHelper.IsStoreApp) */RootPath = AppDomain.CurrentDomain.SetupInformation.ApplicationBase;

            // RootPath 已就绪，初始化用户数据目录（含旧设置迁移）
            InitializeUserDataPath();

            LogHelper.NewLog(string.Format("Ink Canvas Starting (Version: {0})", Assembly.GetExecutingAssembly().GetName().Version.ToString()));

            mutex = new System.Threading.Mutex(true, "Ink_Canvas_Ultra", out bool ret);

            if (!ret && !e.Args.Contains("-m")) //-m multiple
            {
                LogHelper.NewLog("Detected existing instance");

                // 如果启动参数包含文档路径，尝试转发给已运行的实例处理
                string documentPath = e.Args.FirstOrDefault(IsDocumentFilePath);
                if (!string.IsNullOrEmpty(documentPath) && TryNotifyExistingInstance($"document-open|{documentPath}"))
                {
                    LogHelper.NewLog("Document open request sent to existing instance");
                    Environment.Exit(0);
                }

                if (IsVideoPresenterLaunchRequested(e.Args) && TryNotifyExistingInstance())
                {
                    LogHelper.NewLog("Ink Canvas activation request sent to existing instance");
                    Environment.Exit(0);
                }
                MessageBox.Show("已有一个程序实例正在运行");
                LogHelper.NewLog("Ink Canvas automatically closed");
                Environment.Exit(0);
            }

            StartArgs = e.Args;

            // 解析命令行参数
            ParseCommandLineArgs(e.Args);

            // 在 MainWindow 创建前，确保应用级资源里存在自定义主题键。
            // Release 下 iNKORE 的 XamlControlsResources 会重建应用级资源字典，可能把 App.xaml
            // 静态合并的 Light.xaml 冲刷掉；若不在此及时补回，MainWindow 渲染浮动栏/白板栏时，
            // 其中的 DynamicResource（FloatBar*/BoardBar*）会瞬间找不到键而刷一批
            // "Resource not found" Warning，随后主题切换才恢复。
            // 注意 FloatBar* 与 BoardBar* 分属 Light.xaml 与 Light-Board.xaml 两个文件，必须成对补齐。
            // 随后 MainWindow 构造函数里的 SetTheme 会按设置自动纠正为正确主题，故此处默认 Light 即可。
            try
            {
                Helpers.ResourceDictionaryHelper.EnsureStartupThemeKeys();
            }
            catch { }
        }

        private void ParseCommandLineArgs(string[] args)
        {
            if (args == null || args.Length == 0) return;

            foreach (string arg in args)
            {
                string lowerArg = arg.ToLower();
                
                // 处理 URI 格式：inkcanvasultra://xxx
                if (lowerArg.StartsWith("inkcanvasultra://"))
                {
                    string path = lowerArg.Substring("inkcanvasultra://".Length);
                    if (path.StartsWith("whiteboard"))
                    {
                        if (path.Contains("camera"))
                        {
                            CurrentStartupMode = StartupMode.WhiteboardAndCamera;
                        }
                        else
                        {
                            CurrentStartupMode = StartupMode.Whiteboard;
                        }
                    }
                    else if (path.StartsWith("camera") || path.StartsWith("video-presenter"))
                    {
                        CurrentStartupMode = StartupMode.Camera;
                    }
                    continue;
                }
                
                // 处理命令行参数格式
                if (lowerArg == "--whiteboard" || lowerArg == "-w" || lowerArg == "--whiteboard-mode")
                {
                    CurrentStartupMode = StartupMode.Whiteboard;
                }
                else if (lowerArg == "--camera" || lowerArg == "-c" || lowerArg == "--video-presenter")
                {
                    CurrentStartupMode = StartupMode.Camera;
                }
                else if (lowerArg == "--whiteboard-camera" || lowerArg == "--whiteboard-and-camera")
                {
                    CurrentStartupMode = StartupMode.WhiteboardAndCamera;
                }
                else if (IsDocumentFilePath(arg))
                {
                    PendingDocumentPath = arg;
                }
            }
        }

        /// <summary>是否为插件支持的文档文件路径（Word / Excel / PDF）</summary>
        private static bool IsDocumentFilePath(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return false;
            string ext = System.IO.Path.GetExtension(path).ToLowerInvariant();
            return ext == ".doc" || ext == ".docx" || ext == ".xls" || ext == ".xlsx" || ext == ".pdf";
        }

        /// <summary>启动时待处理的文档文件路径（由 document-viewer 插件处理）</summary>
        public static string PendingDocumentPath { get; set; }

        public static void RegisterUriScheme()
        {
            try
            {
                string exePath = Process.GetCurrentProcess().MainModule.FileName;
                string protocolName = "inkcanvasultra";

                using (RegistryKey key = Registry.ClassesRoot.CreateSubKey(protocolName))
                {
                    key.SetValue("", "URL:Ink Canvas Ultra Protocol");
                    key.SetValue("URL Protocol", "");

                    using (RegistryKey shellKey = key.CreateSubKey("shell"))
                    using (RegistryKey openKey = shellKey.CreateSubKey("open"))
                    using (RegistryKey commandKey = openKey.CreateSubKey("command"))
                    {
                        commandKey.SetValue("", $"\"{exePath}\" \"%1\"");
                    }
                }

                LogHelper.NewLog("URI scheme registered successfully: inkcanvasultra://");
            }
            catch (Exception ex)
            {
                LogHelper.WriteLogToFile($"Failed to register URI scheme: {ex.Message}", LogHelper.LogType.Error);
            }
        }

        private bool TryNotifyExistingInstance(string command = null)
        {
            try
            {
                using (var client = new NamedPipeClientStream(".", SingleInstancePipeName, PipeDirection.Out))
                {
                    client.Connect(1500);
                    using (var writer = new StreamWriter(client, Encoding.UTF8, 1024, true))
                    {
                        writer.Write(command ?? ActivateVideoPresenterCommand);
                        writer.Flush();
                    }
                }
                return true;
            }
            catch (Exception ex)
            {
                LogHelper.NewLog("Failed to notify existing instance | " + ex);
                return false;
            }
        }

        private bool IsVideoPresenterLaunchRequested(string[] args)
        {
            return args != null && args.Any(arg => string.Equals(arg, VideoPresenterLaunchArgument, StringComparison.OrdinalIgnoreCase));
        }

        private void ScrollViewer_PreviewMouseWheel(object sender, System.Windows.Input.MouseWheelEventArgs e)
        {
            try
            {
                if (System.Windows.Forms.SystemInformation.MouseWheelScrollLines == -1)
                    e.Handled = false;
                else
                    try
                    {
                        ScrollViewerEx SenderScrollViewer = (ScrollViewerEx)sender;
                        SenderScrollViewer.ScrollToVerticalOffset(SenderScrollViewer.VerticalOffset - e.Delta * 10 * System.Windows.Forms.SystemInformation.MouseWheelScrollLines / (double)120);
                        e.Handled = true;
                    }
                    catch {  }
            }
            catch {  }
        }
    }
}
