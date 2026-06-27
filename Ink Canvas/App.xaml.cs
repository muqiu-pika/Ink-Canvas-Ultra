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
            this.Startup += new StartupEventHandler(App_Startup);
            this.DispatcherUnhandledException += App_DispatcherUnhandledException;
        }

        private void App_DispatcherUnhandledException(object sender, System.Windows.Threading.DispatcherUnhandledExceptionEventArgs e)
        {
            try
            {
                if (Ink_Canvas.MainWindow.Settings != null && Ink_Canvas.MainWindow.Settings.Advanced != null && Ink_Canvas.MainWindow.Settings.Advanced.IsEnableSilentRestartOnCrash)
                {
                    try { Ink_Canvas.MainWindow.ShowNewMessage("抱歉，出现未预期的异常，可能导致 Ink Canvas 画板运行不稳定。\n建议保存墨迹后重启应用。", true); } catch { }
                    try
                    {
                        Application.Current.Dispatcher.Invoke(new Action(() =>
                        {
                            try
                            {
                                var mw = Application.Current.MainWindow as Ink_Canvas.MainWindow;
                                mw?.SaveLastSessionSnapshot();
                            }
                            catch { }
                        }));
                    }
                    catch { }
                    try
                    {
                        var basePath = Ink_Canvas.MainWindow.Settings.Automation.AutoSavedStrokesLocation + @"\Auto Saved - Session";
                        try { if (!Directory.Exists(basePath)) Directory.CreateDirectory(basePath); } catch { }
                        var reasonPath = basePath + @"\RestartReason.txt";
                        try { File.WriteAllText(reasonPath, "silent"); } catch { }
                    }
                    catch { }
                    LogHelper.NewLog(e.Exception.ToString());
                    RestartApplication();
                    e.Handled = true;
                    return;
                }
            }
            catch { }

            Ink_Canvas.MainWindow.ShowNewMessage("抱歉，出现未预期的异常，可能导致 Ink Canvas 画板运行不稳定。\n建议保存墨迹后重启应用。", true);
            LogHelper.NewLog(e.Exception.ToString());
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
            /*if (!StoreHelper.IsStoreApp) */RootPath = AppDomain.CurrentDomain.SetupInformation.ApplicationBase;

            // RootPath 已就绪，初始化用户数据目录（含旧设置迁移）
            InitializeUserDataPath();

            LogHelper.NewLog(string.Format("Ink Canvas Starting (Version: {0})", Assembly.GetExecutingAssembly().GetName().Version.ToString()));

            mutex = new System.Threading.Mutex(true, "Ink_Canvas_Ultra", out bool ret);

            if (!ret && !e.Args.Contains("-m")) //-m multiple
            {
                LogHelper.NewLog("Detected existing instance");
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
            }
        }

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

        private bool TryNotifyExistingInstance()
        {
            try
            {
                using (var client = new NamedPipeClientStream(".", SingleInstancePipeName, PipeDirection.Out))
                {
                    client.Connect(1500);
                    using (var writer = new StreamWriter(client, Encoding.UTF8, 1024, true))
                    {
                        writer.Write(ActivateVideoPresenterCommand);
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
