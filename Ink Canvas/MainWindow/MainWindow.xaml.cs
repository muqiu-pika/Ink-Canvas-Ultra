using Ink_Canvas.Helpers;
using Ink_Canvas.Models;
using iNKORE.UI.WPF.Modern;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.IO.Pipes;
using System.Linq;
using System.Windows.Markup;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Ink;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Drawing;
using System.Windows.Threading;
using System.Windows.Interop;
using File = System.IO.File;
using MessageBox = System.Windows.MessageBox;

namespace Ink_Canvas
{
    public partial class MainWindow : System.Windows.Window
    {
        #region Window Initialization

        public MainWindow()
        {
            InitializeComponent();
            InitializeStartupModes();

            // VideoControlContainer 保留为插件插槽，由视频控件 plugin 通过 host.RegisterSelectionControlBar 注册

            try { inkCanvas.SelectionChanged += InkCanvas_SelectionChanged_ForPlugins; } catch { }

            BlackboardLeftSide.Visibility = Visibility.Collapsed;
            BlackboardCenterSide.Visibility = Visibility.Collapsed;
            BlackboardRightSide.Visibility = Visibility.Collapsed;

            BorderTools.Visibility = Visibility.Collapsed;

            BtnPPTSlideShowEnd.Visibility = Visibility.Collapsed;
            PPTNavigationBottomLeft.Visibility = Visibility.Collapsed;
            PPTNavigationBottomRight.Visibility = Visibility.Collapsed;
            PPTNavigationSidesLeft.Visibility = Visibility.Collapsed;
            PPTNavigationSidesRight.Visibility = Visibility.Collapsed;

            TwoFingerGestureBorder.Visibility = Visibility.Collapsed;
            BoardTwoFingerGestureBorder.Visibility = Visibility.Collapsed;
            BorderDrawShape.Visibility = Visibility.Collapsed;
            BoardBorderDrawShape.Visibility = Visibility.Collapsed;

            GridInkCanvasSelectionCover.Visibility = Visibility.Collapsed;

            ViewboxFloatingBar.Margin = new Thickness((SystemParameters.WorkArea.Width - 284) / 2, SystemParameters.WorkArea.Height - 60, -2000, -200);
            ViewboxFloatingBarMarginAnimation();

            try
            {
                if (File.Exists("Log.txt"))
                {
                    FileInfo fileInfo = new FileInfo("Log.txt");
                    long fileSizeInKB = fileInfo.Length / 1024;
                    if (fileSizeInKB > 512)
                    {
                        try
                        {
                            File.Delete("Log.txt");
                            LogHelper.WriteLogToFile("The Log.txt file has been successfully deleted. Original file size: " + fileSizeInKB + " KB", LogHelper.LogType.Info);
                        }
                        catch (Exception ex)
                        {
                            LogHelper.WriteLogToFile(ex + " | Can not delete the Log.txt file. File size: " + fileSizeInKB + " KB", LogHelper.LogType.Error);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                LogHelper.WriteLogToFile(ex.ToString(), LogHelper.LogType.Error);
            }

            InitTimers();
            timeMachine.OnRedoStateChanged += TimeMachine_OnRedoStateChanged;
            timeMachine.OnUndoStateChanged += TimeMachine_OnUndoStateChanged;
            inkCanvas.Strokes.StrokesChanged += StrokesOnStrokesChanged;

            Microsoft.Win32.SystemEvents.UserPreferenceChanged += SystemEvents_UserPreferenceChanged;
            try
            {
                if (File.Exists("SpecialVersion.ini")) SpecialVersionResetToSuggestion_Click();
            }
            catch (Exception ex)
            {
                LogHelper.WriteLogToFile(ex.ToString(), LogHelper.LogType.Error);
            }

            CheckColorTheme(true);

            // 注册窗口大小变化事件
            this.SizeChanged += MainWindow_SizeChanged;

            // ===== 初始化 plugin 系统 =====
            InitializePluginSystem();
        }

        /// <summary>
        /// 初始化 plugin 系统：创建 PluginHost、注入主程序能力、加载 Plugins 目录下所有 plugin、
        /// 注册主程序内建路由处理器、根据已安装的 plugin 显示对应入口按钮。
        /// </summary>
        private void InitializePluginSystem()
        {
            try
            {
                // 注入主程序能力委托
                var opts = new Plugins.PluginHostOptions
                {
                    GetInkCanvas = () => inkCanvas,
                    GetSelectedElements = () =>
                    {
                        var list = new List<UIElement>();
                        foreach (var el in inkCanvas.GetSelectedElements())
                            list.Add(el);
                        return list;
                    },
                    CommitElementInsertHistory = el => timeMachine.CommitElementInsertHistory(el),
                    GetAutoSavedStrokesLocation = () => Settings.Automation.AutoSavedStrokesLocation,
                    GetPhotoClarityDpi = () => Settings.Automation.PhotoClarityDpi,
                    AddCapturedPhoto = (image, filePath) => Dispatcher.Invoke(() => AddCapturedPhotoInternal(image, filePath)),
                    UpdateCapturedPhoto = (filePath, newImage) => Dispatcher.Invoke(() => UpdateCapturedPhotoInternal(filePath, newImage)),
                    ReplaceDocumentImageOnCanvas = (filePath, newImage) => Dispatcher.Invoke(() => ReplaceDocumentImageOnCanvasInternal(filePath, newImage)),
                    GetCurrentPageIndex = () => GetCurrentPageIndex(),
                    RestoreDocumentPageIfSaved = (filePath) => Dispatcher.Invoke(() => RestoreDocumentPageIfSavedInternal(filePath)),
                    HasCapturedPhotoForFile = (filePath) =>
                    {
                        return Dispatcher.Invoke(() =>
                        {
                            if (string.IsNullOrEmpty(filePath)) return false;
                            // 仅检查照片列表（内存）中是否已有该文档的照片。
                            // 磁盘缓存有效性与同名修改检测由 OpenDocumentWithPhotoCache 在触发转换前完成，
                            // 此处若再按磁盘文件判断会导致需要重新转换的文档被错误跳过。
                            if (capturedPhotos.Any(p =>
                                !string.IsNullOrEmpty(p.SourceFilePath) &&
                                string.Equals(StripChunkSuffix(p.SourceFilePath), filePath, StringComparison.OrdinalIgnoreCase)))
                            {
                                return true;
                            }
                            return false;
                        });
                    },
                    RegisterSelectionControlBar = bar =>
                    {
                        Dispatcher.Invoke(() =>
                        {
                            try
                            {
                                if (VideoControlContainer != null)
                                {
                                    var panel = VideoControlContainer as Panel;
                                    if (panel != null && !panel.Children.Contains(bar))
                                    {
                                        panel.Children.Add(bar);
                                    }
                                }
                            }
                            catch { }
                        });
                    },
                    UnregisterSelectionControlBar = bar =>
                    {
                        Dispatcher.Invoke(() =>
                        {
                            try
                            {
                                if (VideoControlContainer != null)
                                {
                                    var panel = VideoControlContainer as Panel;
                                    if (panel != null && panel.Children.Contains(bar))
                                    {
                                        panel.Children.Remove(bar);
                                    }
                                }
                            }
                            catch { }
                        });
                    }
                };

                var host = Plugins.PluginHost.Initialize(this, opts);

                // 订阅 PluginListChanged：插件加载/卸载后动态刷新入口按钮可见性
                host.PluginListChanged += Host_PluginListChanged;

                // 注册主程序内建路由处理器：video-presenter → 显示视频展台侧栏
                host.RegisterRouteHandler("video-presenter", (entryPoint, parameter) =>
                {
                    try
                    {
                        ShowVideoPresenterSidebar();
                        return true;
                    }
                    catch (Exception ex)
                    {
                        LogHelper.WriteLogToFile($"video-presenter 路由处理失败: {ex.Message}", LogHelper.LogType.Error);
                        return false;
                    }
                });

                // 扫描并加载所有 plugin
                host.LoadAll();

                // 根据已安装的 plugin 显示对应入口按钮
                UpdatePluginBasedButtonVisibility();

                // 启动参数携带的文档延迟到 Window_Loaded 中处理（需等待设置加载完成以确定缓存路径）

                LogHelper.WriteLogToFile($"plugin 系统初始化完成，已加载 {host.GetLoadedManifests().Count} 个 plugin", LogHelper.LogType.Event);
            }
            catch (Exception ex)
            {
                LogHelper.WriteLogToFile($"plugin 系统初始化失败: {ex.Message}", LogHelper.LogType.Error);
            }
        }

        /// <summary>InkCanvas 选择集变化时通知 plugin（替代原 InkCanvas_VideoSelectionChanged）</summary>
        private void InkCanvas_SelectionChanged_ForPlugins(object sender, EventArgs e)
        {
            try
            {
                var host = Plugins.PluginHost.Instance;
                if (host == null) return;
                var selected = inkCanvas.GetSelectedElements().ToList();
                host.RaiseElementSelectionChanged(selected);
            }
            catch { }
        }

        /// <summary>PluginListChanged 事件处理：在 UI 线程上刷新入口按钮可见性与侧栏模式</summary>
        private void Host_PluginListChanged(object sender, EventArgs e)
        {
            try
            {
                Dispatcher.Invoke(() =>
                {
                    UpdatePluginBasedButtonVisibility();
                    // 若侧栏已打开，同步刷新其模式
                    if (VideoPresenterSidebar != null && VideoPresenterSidebar.Visibility == Visibility.Visible)
                    {
                        var host = Plugins.PluginHost.Instance;
                        bool vpAvailable = host != null && host.IsRouteAvailable("video-presenter");
                        UpdateSidebarMode(vpAvailable);
                        if (vpAvailable) cameraDeviceManager?.RefreshCameraDevices();
                    }
                });
            }
            catch { }
        }

        /// <summary>
        /// 根据已安装/启用的 plugin 动态刷新主程序入口按钮的可见性与外观。
        /// 在以下时机调用：
        ///   - InitializePluginSystem 初始化完成时
        ///   - PluginListChanged 事件触发时（插件加载/卸载/启用/禁用/安装）
        /// </summary>
        private void UpdatePluginBasedButtonVisibility()
        {
            try
            {
                var host = Plugins.PluginHost.Instance;
                if (host == null) return;

                // 视频展台按钮：始终显示，但根据插件可用性切换外观
                //   - 插件可用：图标=摄像头，文本="视频展台"
                //   - 插件不可用：图标=照片，文本="照片列表"
                if (BtnVideoPresenter != null)
                {
                    BtnVideoPresenter.Visibility = Visibility.Visible;
                    bool vpAvailable = host.IsRouteAvailable("video-presenter");
                    if (IconVideoPresenter != null)
                    {
                        // &#xe714; = 摄像头/视频图标；&#xe8b9; = 照片图标
                        IconVideoPresenter.Glyph = vpAvailable ? "\ue714" : "\ue8b9";
                    }
                    if (TextVideoPresenter != null)
                    {
                        TextVideoPresenter.Text = vpAvailable ? "视频展台" : "照片列表";
                    }
                }
            }
            catch { }
        }

        /// <summary>
        /// 窗口大小变化事件处理
        /// </summary>
        private void MainWindow_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            if (VideoPresenterSidebar.Visibility == Visibility.Visible)
            {
                AutoCalculatePhotoAreaHeight();
            }
        }

        #endregion

        #region Ink Canvas Functions

        DrawingAttributes drawingAttributes;
        private void loadPenCanvas()
        {
            try
            {
                drawingAttributes = inkCanvas.DefaultDrawingAttributes;
                drawingAttributes.Color = Colors.Red;

                drawingAttributes.Height = 2.5;
                drawingAttributes.Width = 2.5;

                inkCanvas.EditingMode = InkCanvasEditingMode.Ink;
                inkCanvas.Gesture += InkCanvas_Gesture;
            }
            catch { }
        }

        private void InkCanvas_Gesture(object sender, InkCanvasGestureEventArgs e)
        {
            ReadOnlyCollection<GestureRecognitionResult> gestures = e.GetGestureRecognitionResults();
            try
            {
                foreach (GestureRecognitionResult gest in gestures)
                {
                    // 在白板模式下(currentMode == 1)不响应PPT翻页手势
                    if (BtnPPTSlideShowEnd.Visibility == Visibility.Visible && currentMode != 1)
                    {
                        if (gest.ApplicationGesture == ApplicationGesture.Left)
                        {
                            BtnPPTSlidesDown_Click(null, null);
                        }
                        if (gest.ApplicationGesture == ApplicationGesture.Right)
                        {
                            BtnPPTSlidesUp_Click(null, null);
                        }
                    }
                }
            }
            catch { }
        }

		private void InkCanvas_EditingModeChanged(object sender, RoutedEventArgs e)
        {
			if (!(sender is InkCanvas inkCanvas1)) return;
            if (Settings.Canvas.IsShowCursor)
            {
                if (inkCanvas1.EditingMode == InkCanvasEditingMode.Ink || drawingShapeMode != 0)
                {
                    inkCanvas1.ForceCursor = true;
                }
                else
                {
                    inkCanvas1.ForceCursor = false;
                }
            }
            else
            {
                inkCanvas1.ForceCursor = false;
            }
            if (inkCanvas1.EditingMode == InkCanvasEditingMode.Ink && !_isCancellingActiveStroke) forcePointEraser = !forcePointEraser;
        }

        #endregion Ink Canvas Functions

        #region Definations and Loading

        public static Settings Settings = new Settings();
        public static string settingsFileName = "Settings.json";
        bool isLoaded = false;
        bool _isLoadingSettings = false;

        // 拍照功能相关字段
        private ObservableCollection<CapturedImage> capturedPhotos = new ObservableCollection<CapturedImage>();
        // 侧栏照片选中状态（使用时间戳标识当前选中照片）
        private string selectedPhotoTimestamp = null;
        
        // 照片页面管理相关字段
        private Dictionary<string, int> photoPageMapping = new Dictionary<string, int>(); // 记录照片时间戳与页码的关联
        private Dictionary<int, string> pageDocumentMapping = new Dictionary<int, string>(); // 记录页码与文档来源路径的关联
        private System.Windows.Controls.Image currentPhotoImage; // 当前显示的照片元素

        // 元素命名计数器：确保同一毫秒内创建的多个元素（如文档瓦片）名称唯一，避免 XAML 序列化/恢复时命名冲突
        private static int _photoNameSequence = 0;
        private static string GeneratePhotoName()
        {
            int seq = System.Threading.Interlocked.Increment(ref _photoNameSequence);
            return "photo_" + DateTime.Now.ToString("yyyyMMdd_HH_mm_ss_fff") + "_" + seq.ToString();
        }

        // 文档页笔迹自动保存防抖 Timer
        private Timer _documentPageSaveTimer;
        private int _pendingDocumentSavePageIndex = -1;
        // 照片列表交互：右键/长按弹出的操作覆盖层
        private FrameworkElement _activePhotoActionOverlay;
        // 照片列表手动排序
        private bool _isReorderingPhotos = false;
        private int _photoReorderSourceIndex = -1;
        private int _photoReorderTargetIndex = -1;
        private Border _reorderIndicatorLine;
        private DispatcherTimer _reorderAutoScrollTimer;
        private int _reorderAutoScrollDirection = 0; // -1 上, 0 停止, 1 下
        // 画板拖拽插入指示器（Adorner 实现虚线框）
        private DragInsertionAdorner _photoDragInsertionAdorner;
        private bool shouldLaunchIntoVideoPresenterMode;
        private bool hasAppliedVideoPresenterStartupMode;
        private bool shouldOpenVideoPresenterAfterBoardSwitch;
        private CancellationTokenSource singleInstanceCommandServerCancellationTokenSource;
        private Task singleInstanceCommandServerTask;
        private HwndSource inputDeviceMessageSource;
        private bool isInputDeviceRecoveryScheduled;
        private const int WM_DEVICECHANGE = 0x0219;
        private const int WM_ACTIVATEAPP = 0x001C;
        private const int WM_DISPLAYCHANGE = 0x007E;
        private const int WM_POWERBROADCAST = 0x0218;
        private IntPtr _devNotifyHandle;
        private DispatcherTimer _inputWatchdogTimer;
        private int _lastInputActivityTickCount;
        private const int InputWatchdogTimeoutMs = 5000;
        private static readonly Guid GUID_DEVINTERFACE_HID = new Guid("4D1E55B2-F16F-11CF-88CB-001111000030");

        [StructLayout(LayoutKind.Sequential)]
        private struct DEV_BROADCAST_DEVICEINTERFACE
        {
            public int dbcc_size;
            public int dbcc_devicetype;
            public int dbcc_reserved;
            public Guid dbcc_classguid;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 255)]
            public string dbcc_name;
        }

        [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Auto)]
        private static extern IntPtr RegisterDeviceNotification(IntPtr hRecipient, IntPtr notificationFilter, int flags);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool UnregisterDeviceNotification(IntPtr handle);

        private const int DEVICE_NOTIFY_WINDOW_HANDLE = 0x00000000;
        private const int DBT_DEVTYP_DEVICEINTERFACE = 0x00000005;
        private const int DBT_DEVICEARRIVAL = 0x8000;
        private const int DBT_DEVICEREMOVECOMPLETE = 0x8004;
        private const int DBT_DEVNODES_CHANGED = 0x0007;

        private void InitializeStartupModes()
        {
            shouldLaunchIntoVideoPresenterMode = HasStartArgument(
                App.VideoPresenterLaunchArgument,
                "/video-presenter",
                "-video-presenter",
                "-vp",
                "/vp");
        }

        private bool HasStartArgument(params string[] argumentNames)
        {
            if (App.StartArgs == null || argumentNames == null || argumentNames.Length == 0) return false;
            return App.StartArgs.Any(arg =>
                !string.IsNullOrWhiteSpace(arg) &&
                argumentNames.Any(argumentName => string.Equals(arg, argumentName, StringComparison.OrdinalIgnoreCase)));
        }

        private void ShowVideoPresenterSidebar()
        {
            if (VideoPresenterSidebar == null) return;
            VideoPresenterSidebar.Visibility = Visibility.Visible;

            // 根据视频展台插件是否可用，切换侧栏内容
            var host = Plugins.PluginHost.Instance;
            bool vpAvailable = host != null && host.IsRouteAvailable("video-presenter");
            UpdateSidebarMode(vpAvailable);

            if (vpAvailable)
            {
                cameraDeviceManager?.RefreshCameraDevices();
            }

            Dispatcher.BeginInvoke(new Action(() =>
            {
                try
                {
                    AutoCalculatePhotoAreaHeight();
                }
                catch { }
            }), DispatcherPriority.Background);
        }

        /// <summary>
        /// 根据视频展台插件可用性切换侧栏模式。
        ///   - 插件可用（完整模式）：标题="视频展台"，显示摄像头设备选择区、拍照按钮
        ///   - 插件不可用（照片列表模式）：标题="照片列表"，隐藏摄像头相关区域，仅保留照片列表+清除+旋转+插入媒体
        /// 插入媒体按钮始终显示。
        /// </summary>
        private void UpdateSidebarMode(bool videoPresenterAvailable)
        {
            try
            {
                // 标题
                if (SidebarTitleTextBlock != null)
                {
                    SidebarTitleTextBlock.Text = videoPresenterAvailable ? "视频展台" : "照片列表";
                }

                // 摄像头设备选择区（Row 2）
                if (CameraDeviceBorder != null)
                {
                    CameraDeviceBorder.Visibility = videoPresenterAvailable ? Visibility.Visible : Visibility.Collapsed;
                }

                // 拍照按钮
                if (BtnCapturePhoto != null)
                {
                    BtnCapturePhoto.Visibility = videoPresenterAvailable ? Visibility.Visible : Visibility.Collapsed;
                }

                // 插入媒体按钮始终显示（取代原实时矫正开关位置）
                // 旋转按钮始终保留（对照片列表中的图片也有用）
            }
            catch { }
        }

        private void ActivateCurrentWindow()
        {
            try
            {
                if (WindowState == WindowState.Minimized)
                {
                    WindowState = WindowState.Normal;
                }
                Show();
                Activate();
                WindowFocusHelper.EnsureWindowFocus(this);
            }
            catch { }
        }

        private void ActivateVideoPresenterMode()
        {
            shouldLaunchIntoVideoPresenterMode = true;
            shouldOpenVideoPresenterAfterBoardSwitch = true;
            ActivateCurrentWindow();

            if (currentMode != 1)
            {
                ImageBlackboard_Click(null, null);
                return;
            }

            CompletePendingVideoPresenterActivation();
        }

        private void CompletePendingVideoPresenterActivation()
        {
            if (!shouldOpenVideoPresenterAfterBoardSwitch || currentMode != 1) return;

            shouldOpenVideoPresenterAfterBoardSwitch = false;
            Dispatcher.BeginInvoke(new Action(async () =>
            {
                await Task.Delay(150);
                ShowVideoPresenterSidebar();
                ActivateCurrentWindow();
            }), DispatcherPriority.Background);
        }

        private void StartSingleInstanceCommandServer()
        {
            if (singleInstanceCommandServerTask != null) return;

            singleInstanceCommandServerCancellationTokenSource = new CancellationTokenSource();
            singleInstanceCommandServerTask = Task.Run(() => ListenForSingleInstanceCommandsAsync(singleInstanceCommandServerCancellationTokenSource.Token));
        }

        private void StopSingleInstanceCommandServer()
        {
            try
            {
                singleInstanceCommandServerCancellationTokenSource?.Cancel();
            }
            catch { }
        }

        private async Task ListenForSingleInstanceCommandsAsync(CancellationToken cancellationToken)
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                NamedPipeServerStream pipeServer = null;
                try
                {
                    pipeServer = new NamedPipeServerStream(App.SingleInstancePipeName, PipeDirection.In, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
                    await pipeServer.WaitForConnectionAsync(cancellationToken);

                    using (pipeServer)
                    using (var reader = new StreamReader(pipeServer, Encoding.UTF8))
                    {
                        string command = await reader.ReadToEndAsync();
                        if (string.IsNullOrWhiteSpace(command)) continue;

                        await Dispatcher.BeginInvoke(new Action(() => HandleSingleInstanceCommand(command.Trim())));
                    }
                }
                catch (OperationCanceledException)
                {
                    try { pipeServer?.Dispose(); } catch { }
                    break;
                }
                catch (Exception ex)
                {
                    try { pipeServer?.Dispose(); } catch { }
                    LogHelper.WriteLogToFile("Single instance command server error | " + ex, LogHelper.LogType.Error);
                }
            }
        }

        private void HandleSingleInstanceCommand(string command)
        {
            ActivateCurrentWindow();

            if (string.Equals(command, App.ActivateVideoPresenterCommand, StringComparison.OrdinalIgnoreCase))
            {
                ActivateVideoPresenterMode();
                return;
            }

            // 处理来自第二个实例的文档打开请求
            const string documentOpenPrefix = "document-open|";
            if (command.StartsWith(documentOpenPrefix, StringComparison.OrdinalIgnoreCase))
            {
                string filePath = command.Substring(documentOpenPrefix.Length);
                if (!string.IsNullOrWhiteSpace(filePath) && System.IO.File.Exists(filePath))
                {
                    try
                    {
                        // 先查本地转换缓存，已缓存则直接加载照片，未转换/已修改则触发插件转换
                        OpenDocumentWithPhotoCache(filePath);
                    }
                    catch (Exception ex)
                    {
                        LogHelper.WriteLogToFile($"处理单实例文档打开命令失败: {ex.Message}", LogHelper.LogType.Error);
                    }
                }
                return;
            }
        }

        private void ApplyStartupModes()
        {
            if (!shouldLaunchIntoVideoPresenterMode || hasAppliedVideoPresenterStartupMode) return;
            hasAppliedVideoPresenterStartupMode = true;
            ActivateVideoPresenterMode();
        }

        private void HookInputDeviceNotifications()
        {
            if (inputDeviceMessageSource != null) return;

            try
            {
                var interopHelper = new WindowInteropHelper(this);
                if (interopHelper.Handle == IntPtr.Zero) return;

                inputDeviceMessageSource = HwndSource.FromHwnd(interopHelper.Handle);
                inputDeviceMessageSource?.AddHook(MainWindowWndProc);

                RegisterHidDeviceNotification(interopHelper.Handle);
                StartInputWatchdog();
            }
            catch (Exception ex)
            {
                LogHelper.WriteLogToFile("Hook input device notifications failed | " + ex, LogHelper.LogType.Error);
            }
        }

        private void RegisterHidDeviceNotification(IntPtr hwnd)
        {
            try
            {
                if (_devNotifyHandle != IntPtr.Zero)
                {
                    UnregisterDeviceNotification(_devNotifyHandle);
                    _devNotifyHandle = IntPtr.Zero;
                }

                var dbi = new DEV_BROADCAST_DEVICEINTERFACE
                {
                    dbcc_size = Marshal.SizeOf(typeof(DEV_BROADCAST_DEVICEINTERFACE)),
                    dbcc_devicetype = DBT_DEVTYP_DEVICEINTERFACE,
                    dbcc_reserved = 0,
                    dbcc_classguid = GUID_DEVINTERFACE_HID,
                    dbcc_name = null
                };

                IntPtr buffer = Marshal.AllocHGlobal(dbi.dbcc_size);
                try
                {
                    Marshal.StructureToPtr(dbi, buffer, true);
                    _devNotifyHandle = RegisterDeviceNotification(hwnd, buffer, DEVICE_NOTIFY_WINDOW_HANDLE);
                    if (_devNotifyHandle == IntPtr.Zero)
                    {
                        LogHelper.WriteLogToFile("RegisterDeviceNotification failed, error: " + Marshal.GetLastWin32Error(), LogHelper.LogType.Warning);
                    }
                }
                finally
                {
                    Marshal.FreeHGlobal(buffer);
                }
            }
            catch (Exception ex)
            {
                LogHelper.WriteLogToFile("Register HID device notification failed | " + ex, LogHelper.LogType.Error);
            }
        }

        private void StartInputWatchdog()
        {
            try
            {
                if (_inputWatchdogTimer != null) return;

                _lastInputActivityTickCount = Environment.TickCount;
                _inputWatchdogTimer = new DispatcherTimer(DispatcherPriority.Background)
                {
                    Interval = TimeSpan.FromMilliseconds(2000)
                };
                _inputWatchdogTimer.Tick += InputWatchdogTimer_Tick;
                _inputWatchdogTimer.Start();
            }
            catch (Exception ex)
            {
                LogHelper.WriteLogToFile("Start input watchdog failed | " + ex, LogHelper.LogType.Error);
            }
        }

        private void InputWatchdogTimer_Tick(object sender, EventArgs e)
        {
            try
            {
                int now = Environment.TickCount;
                int elapsed = now - _lastInputActivityTickCount;

                if (isMouseDown && elapsed > InputWatchdogTimeoutMs)
                {
                    // 仅在窗口处于活动状态时执行恢复（含 inkCanvas.Focus()），
                    // 避免软件在后台时反复抢占其它软件的键盘焦点导致无法打字
                    if (IsActive)
                    {
                        LogHelper.WriteLogToFile("Input watchdog detected stuck input state (isMouseDown), forcing recovery", LogHelper.LogType.Warning);
                        ScheduleInputDeviceRecovery();
                    }
                    // 无论是否活动，都重置状态和时间戳，防止看门狗持续触发
                    isMouseDown = false;
                    UpdateInputActivityTimestamp();
                }
            }
            catch { }
        }

        private void UpdateInputActivityTimestamp()
        {
            try
            {
                _lastInputActivityTickCount = Environment.TickCount;
            }
            catch { }
        }

        private void UnhookInputDeviceNotifications()
        {
            try
            {
                if (_inputWatchdogTimer != null)
                {
                    _inputWatchdogTimer.Stop();
                    _inputWatchdogTimer.Tick -= InputWatchdogTimer_Tick;
                    _inputWatchdogTimer = null;
                }
            }
            catch (Exception ex)
            {
                LogHelper.WriteLogToFile("Stop input watchdog failed | " + ex, LogHelper.LogType.Error);
            }

            try
            {
                if (_devNotifyHandle != IntPtr.Zero)
                {
                    UnregisterDeviceNotification(_devNotifyHandle);
                    _devNotifyHandle = IntPtr.Zero;
                }
            }
            catch (Exception ex)
            {
                LogHelper.WriteLogToFile("Unregister device notification failed | " + ex, LogHelper.LogType.Error);
            }

            if (inputDeviceMessageSource == null) return;

            try
            {
                inputDeviceMessageSource.RemoveHook(MainWindowWndProc);
            }
            catch (Exception ex)
            {
                LogHelper.WriteLogToFile("Unhook input device notifications failed | " + ex, LogHelper.LogType.Error);
            }
            finally
            {
                inputDeviceMessageSource = null;
            }
        }

        private IntPtr MainWindowWndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            switch (msg)
            {
                case WM_DEVICECHANGE:
                    int eventType = wParam.ToInt32();
                    if (eventType == DBT_DEVICEARRIVAL || eventType == DBT_DEVICEREMOVECOMPLETE || eventType == DBT_DEVNODES_CHANGED)
                    {
                        ScheduleInputDeviceRecovery();
                    }
                    break;
                case WM_ACTIVATEAPP:
                    if (wParam == IntPtr.Zero)
                    {
                        try { Mouse.Capture(null); Stylus.Capture(null); TouchStack_ClearAllCaptures(); } catch { }
                        // 应用失去焦点时重置 isMouseDown，避免输入看门狗在后台反复触发
                        // RecoverFromInputDeviceChange → inkCanvas.Focus()，导致抢占其它软件的键盘焦点
                        isMouseDown = false;
                        UpdateInputActivityTimestamp();
                    }
                    else
                    {
                        ScheduleInputDeviceRecovery();
                    }
                    break;
                case WM_POWERBROADCAST:
                    ScheduleInputDeviceRecovery();
                    break;
            }

            return IntPtr.Zero;
        }

        private void ScheduleInputDeviceRecovery()
        {
            if (!isLoaded || isInputDeviceRecoveryScheduled) return;

            isInputDeviceRecoveryScheduled = true;
            Dispatcher.BeginInvoke(new Action(async () =>
            {
                try
                {
                    // 稍作延迟，等待系统先完成设备增删事件广播。
                    await Task.Delay(150);
                    RecoverFromInputDeviceChange();
                }
                catch (Exception ex)
                {
                    LogHelper.WriteLogToFile("Input device recovery scheduling failed | " + ex, LogHelper.LogType.Error);
                }
                finally
                {
                    isInputDeviceRecoveryScheduled = false;
                }
            }), DispatcherPriority.Background);
        }

        private void RecoverFromInputDeviceChange()
        {
            LogHelper.WriteLogToFile("Input device change detected, starting recovery...", LogHelper.LogType.Info);

            try
            {
                Mouse.Capture(null);
                TouchStack_ClearAllCaptures();
                Stylus.Capture(null);
            }
            catch (Exception ex)
            {
                LogHelper.WriteLogToFile("Release input capture failed | " + ex, LogHelper.LogType.Error);
            }

            try
            {
                isMouseDown = false;
                isInMultiTouchMode = false;
                isSingleFingerDragMode = false;
                twoFingerGestureType = TwoFingerGestureType.None;
                translateAccum = new Vector(0, 0);
                dec.Clear();
            }
            catch (Exception ex)
            {
                LogHelper.WriteLogToFile("Reset input state flags failed | " + ex, LogHelper.LogType.Error);
            }

            try
            {
                ResetTouchState();
            }
            catch (Exception ex)
            {
                LogHelper.WriteLogToFile("Reset touch state after device change failed | " + ex, LogHelper.LogType.Error);
            }

            try
            {
                if (inkCanvas != null && !forceEraser)
                {
                    inkCanvas.EditingMode = InkCanvasEditingMode.Ink;
                }
            }
            catch (Exception ex)
            {
                LogHelper.WriteLogToFile("Reset InkCanvas editing mode failed | " + ex, LogHelper.LogType.Error);
            }

            try
            {
                TouchLockFix.ReRegisterTouchWindow(this);
            }
            catch (Exception ex)
            {
                LogHelper.WriteLogToFile("Re-register touch window after device change failed | " + ex, LogHelper.LogType.Error);
            }

            try
            {
                inkCanvas?.Focus();
            }
            catch { }

            LogHelper.WriteLogToFile("Input device recovery completed", LogHelper.LogType.Info);
        }

        private void TouchStack_ClearAllCaptures()
        {
            try
            {
                if (inkCanvas != null)
                {
                    inkCanvas.ReleaseAllTouchCaptures();
                    inkCanvas.ReleaseMouseCapture();
                    inkCanvas.ReleaseStylusCapture();
                }
            }
            catch { }
        }

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            loadPenCanvas();
            //加载设置
            LoadSettings(true);
            if (Environment.Is64BitProcess && GroupBoxInkRecognition != null)
            {
                GroupBoxInkRecognition.Visibility = Visibility.Collapsed;
            }

            ThemeManager.Current.ApplicationTheme = ApplicationTheme.Light;
            SystemEvents_UserPreferenceChanged(null, null);

            if (AppVersionTextBlock != null)
                AppVersionTextBlock.Text = Assembly.GetExecutingAssembly().GetName().Version.ToString();
            LogHelper.WriteLogToFile("Ink Canvas Loaded", LogHelper.LogType.Event);
            
            // 初始化摄像头设备管理器
            InitializeCameraDeviceManager();

            // 为照片列表 StackPanel 绑定排序与隐藏覆盖层的事件
            try
            {
                object spObj = CapturedPhotosStackPanel;
                if (spObj is StackPanel sp)
                {
                    sp.PreviewMouseMove += CapturedPhotosStackPanel_PreviewMouseMove;
                    sp.PreviewMouseUp += CapturedPhotosStackPanel_PreviewMouseUp;
                    sp.PreviewTouchMove += CapturedPhotosStackPanel_PreviewTouchMove;
                    sp.PreviewTouchUp += CapturedPhotosStackPanel_PreviewTouchUp;
                }
            }
            catch { }

            isLoaded = true;
            StartSingleInstanceCommandServer();
            RegisterGlobalHotkeys();
            timerFixFloatingBarZOrder?.Start();
            HookInputDeviceNotifications();

            // 注册触摸窗口以确保触摸事件正常工作
            TouchLockFix.ReRegisterTouchWindow(this);

            // 启动后提示是否恢复上次会话
            PromptRestoreLastSessionOnStartup();

            // 注意：启动时不再扫描磁盘照片填充照片列表，照片列表保持初始空状态。
            // 文档照片仅在「打开文档」时从该文档的缓存文件夹加载（或重新转换后放入）。

            // 首次安装引导
            TryShowInitialSetupWizard();
            ApplyStartupModes();

            // 启动参数携带的文档：先查本地转换缓存，已缓存则直接加载照片，未转换/已修改则触发插件转换
            if (!string.IsNullOrEmpty(App.PendingDocumentPath))
            {
                try
                {
                    string pendingDoc = App.PendingDocumentPath;
                    App.PendingDocumentPath = null;
                    OpenDocumentWithPhotoCache(pendingDoc);
                }
                catch (Exception ex)
                {
                    LogHelper.WriteLogToFile($"启动时处理文档参数失败: {ex.Message}", LogHelper.LogType.Error);
                }
            }

            // 应用空闲时预先构造并重渲染设置窗口，把“解析 170KB XAML + 首次布局/渲染”
            // 的开销从“点击设置”挪到空闲时段，避免点击时的卡顿与黑屏闪烁。
            Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.ApplicationIdle,
                (System.Action)PrebuildSettingsWindow);
        }

        private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            LogHelper.WriteLogToFile("Ink Canvas closing", LogHelper.LogType.Event);
            // 关闭前立即保存当前文档页的笔迹/元素，避免防抖计时器窗口期内的书写内容丢失
            try { SaveDocumentPageIfNeeded(CurrentWhiteboardIndex); } catch { }
            if (!CloseIsFromButton && Settings.Advanced.IsSecondConfimeWhenShutdownApp)
            {
                e.Cancel = true;
                if (MessageBox.Show("是否继续关闭 Ink Canvas 画板，这将丢失当前未保存的工作。", "Ink Canvas 画板", MessageBoxButton.OKCancel, MessageBoxImage.Warning) == MessageBoxResult.OK)
                {
                    if (MessageBox.Show("真的狠心关闭 Ink Canvas 画板吗？", "Ink Canvas 画板", MessageBoxButton.OKCancel, MessageBoxImage.Error) == MessageBoxResult.OK)
                    {
                        if (MessageBox.Show("是否取消关闭 Ink Canvas 画板？", "Ink Canvas 画板", MessageBoxButton.OKCancel, MessageBoxImage.Error) != MessageBoxResult.OK)
                        {
                            e.Cancel = false;
                        }
                    }
                }
            }
            if (e.Cancel)
            {
                LogHelper.WriteLogToFile("Ink Canvas closing cancelled", LogHelper.LogType.Event);
            }
        }

        private void Window_Closed(object sender, EventArgs e)
        {
            LogHelper.WriteLogToFile("Ink Canvas closed", LogHelper.LogType.Event);
            // 关闭被缓存/隐藏的设置窗口：隐藏窗口在 WPF 中仍属“已打开”，
            // 不显式关闭会阻止应用退出（默认 ShutdownMode 为 OnLastWindowClose），内存也无法释放
            try { CloseCachedSettingsWindow(); } catch { }
            // 关闭其余仍然存活的附属窗口（插件工坊、倒计时、抽奖等）
            try { CloseRemainingOwnedWindows(); } catch { }
            // 停止并释放所有后台定时器：System.Timers.Timer 的 Elapsed 处理函数持有本窗口引用，
            // 若窗口关闭后仍在运行，会阻止主窗口与其可视化树、以及各附属窗口被回收。
            StopAllBackgroundTimers();
            // 反订阅全局系统事件（应用级事件链会持有本窗口引用）
            try { Microsoft.Win32.SystemEvents.UserPreferenceChanged -= SystemEvents_UserPreferenceChanged; } catch { }
            UnhookInputDeviceNotifications();
            StopSingleInstanceCommandServer();
            // 通知所有 plugin 主程序即将退出
            try { Plugins.PluginHost.Instance?.ShutdownAll(); } catch { }
            // 移除摄像头画面
            RemoveCameraFrame();
            // 清理摄像头资源
            cameraDeviceManager?.Dispose();
            // 释放照片列表大图内存并主动回收，减少退出/关闭时的内存占用
            try { ReleaseAllCapturedPhotoMemory(); } catch { }
            try { GC.Collect(); GC.WaitForPendingFinalizers(); } catch { }
        }

        /// <summary>
        /// 停止并释放本窗口的所有后台定时器（System.Timers.Timer 与 DispatcherTimer）。
        /// 定时器一旦停止并反注册 Elapsed/Tick 处理函数，窗口引用即断开，可被正常回收。
        /// </summary>
        private void StopAllBackgroundTimers()
        {
            try { timerCheckPPT?.Stop(); timerCheckPPT?.Dispose(); } catch { }
            try { timerKillProcess?.Stop(); timerKillProcess?.Dispose(); } catch { }
            try { timerCheckAutoFold?.Stop(); timerCheckAutoFold?.Dispose(); } catch { }
            try { timerFixFloatingBarZOrder?.Stop(); timerFixFloatingBarZOrder?.Dispose(); } catch { }
            try { timerCheckAutoUpdateWithSilence?.Stop(); timerCheckAutoUpdateWithSilence?.Dispose(); } catch { }
            try { _inputWatchdogTimer?.Stop(); } catch { }
            try { _documentPageSaveTimer?.Dispose(); } catch { }
            try { cameraFrameTimer?.Stop(); } catch { }
            try { _reorderAutoScrollTimer?.Stop(); } catch { }
        }

        /// <summary>
        /// 关闭除主窗口之外仍然打开（含隐藏）的所有本程序窗口，确保退出时资源被逐个释放。
        /// 每个窗口的 OnClosed/Closing 里会停止各自的计时器、动画与外设资源。
        /// </summary>
        private void CloseRemainingOwnedWindows()
        {
            var windows = Application.Current?.Windows;
            if (windows == null) return;

            foreach (var window in windows.OfType<System.Windows.Window>().Where(w => !ReferenceEquals(w, this)).ToList())
            {
                try
                {
                    window.Owner = null;
                    window.Close();
                }
                catch (Exception ex)
                {
                    LogHelper.WriteLogToFile($"关闭附属窗口失败({window.GetType().Name}): {ex.Message}", LogHelper.LogType.Error);
                }
            }
        }

        /// <summary>释放照片列表中所有已落盘照片的全尺寸大图内存（缩略图与文件路径保留）。</summary>
        private void ReleaseAllCapturedPhotoMemory()
        {
            try
            {
                foreach (var photo in capturedPhotos)
                {
                    if (photo != null && !photo.IsVideo && !photo.IsImageReleased)
                    {
                        photo.ReleaseImageMemory();
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"释放照片内存失败: {ex.Message}");
            }
        }

        #endregion Definations and Loading

        private void PromptRestoreLastSessionOnStartup()
        {
            try
            {
                string basePath = Settings.Automation.AutoSavedStrokesLocation + @"\Auto Saved - Session";
                string reasonPath = basePath + @"\RestartReason.txt";
                string reason = null;
                try
                {
                    if (File.Exists(reasonPath))
                    {
                        reason = File.ReadAllText(reasonPath).Trim().ToLowerInvariant();
                        try { File.Delete(reasonPath); } catch { }
                    }
                }
                catch { }
                if (string.IsNullOrEmpty(reason) || (reason != "settings" && reason != "silent" && reason != "crash")) return;
                string metaPath = basePath + @"\SessionMeta.txt";
                string icartPath = basePath + @"\LastSession.icart";
                if (!File.Exists(metaPath) || !File.Exists(icartPath)) return;

                var notificationWindow = new YesOrNoNotificationWindow("检测到上次会话快照，是否恢复？",
                    yesAction: () =>
                    {
                        try
                        {
                            // 首先确认恢复会话，设置全局标志
                            ConfirmRestoreSession();
                            
                            // 读取元信息
                            int metaMode = currentMode;
                            int metaWhiteboardIndex = CurrentWhiteboardIndex;
                            int metaPpt = 0;
                            try
                            {
                                var lines = File.ReadAllLines(metaPath);
                                foreach (var line in lines)
                                {
                                    var kv = line.Split('=');
                                    if (kv.Length == 2)
                                    {
                                        string key = kv[0].Trim().ToLowerInvariant();
                                        string val = kv[1].Trim();
                                        if (key == "mode") int.TryParse(val, out metaMode);
                                        else if (key == "whiteboard") int.TryParse(val, out metaWhiteboardIndex);
                                        else if (key == "ppt") int.TryParse(val, out metaPpt);
                                        else if (key == "whiteboard_total") int.TryParse(val, out WhiteboardTotalCount);
                                    }
                                }
                            }
                            catch { }

                            // 切换模式以匹配快照
                            if (metaMode != currentMode)
                            {
                                ImageBlackboard_Click(null, null);
                            }

                            // 恢复快照
                            bool ok = OpenLastSessionSnapshotIfExists();
                            if (ok)
                            {
                                CurrentWhiteboardIndex = metaWhiteboardIndex;
                                try
                                {
                                    string basePath2 = Settings.Automation.AutoSavedStrokesLocation + @"\Auto Saved - Session";
                                    string pagesDir = System.IO.Path.Combine(basePath2, "pages");
                                    if (System.IO.Directory.Exists(pagesDir))
                                    {
                                        int count = 0;
                                        foreach (var d in System.IO.Directory.GetDirectories(pagesDir)) count++;
                                        if (count > 0) WhiteboardTotalCount = count;
                                        UpdateIndexInfoDisplay();
                                    }
                                }
                                catch { }
                                ShowNotificationAsync("已恢复上次会话快照", true);
                                try { LoadLastSessionPhotosToSidebarAndBind(); } catch { }
                                // 不再从磁盘照片目录合并已保存照片；文档照片按需通过打开文档加载
                            }
                            else
                            {
                                ShowNotificationAsync("会话快照恢复失败", true);
                            }
                        }
                        catch { }
                    },
                    noAction: () => { });

                Helpers.WindowMemoryHelper.ReleaseOnClose(notificationWindow);
                notificationWindow.Show();
            }
            catch (Exception ex)
            {
                LogHelper.WriteLogToFile("Prompt restore last session failed | " + ex.ToString(), LogHelper.LogType.Error);
            }
        }

        private void LoadLastSessionPhotosToSidebarAndBind()
        {
            try
            {
                string basePath = Settings.Automation.AutoSavedStrokesLocation + @"\Auto Saved - Session";
                string pagesDir = System.IO.Path.Combine(basePath, "pages");
                if (!System.IO.Directory.Exists(pagesDir)) return;

                capturedPhotos.Clear();
                photoPageMapping.Clear();

                foreach (var dir in System.IO.Directory.GetDirectories(pagesDir))
                {
                    int.TryParse(System.IO.Path.GetFileName(dir), out int pageIndex);
                    string elementsPath = System.IO.Path.Combine(dir, "elements.xaml");
                    if (!System.IO.File.Exists(elementsPath)) continue;
                    try
                    {
                        using (var fs = new System.IO.FileStream(elementsPath, System.IO.FileMode.Open))
                        {
                            if (System.Windows.Markup.XamlReader.Load(fs) is System.Windows.Controls.InkCanvas loadedCanvas)
                            {
                                foreach (System.Windows.UIElement child in loadedCanvas.Children)
                                {
                                    if (child is System.Windows.Controls.Image image)
                                    {
                                        string candidate = null;
                                        string sourceFilePath = null;
                                        try
                                        {
                                            string tagPath = image.Tag as string;
                                            if (!string.IsNullOrEmpty(tagPath) && tagPath.StartsWith("File Dependency"))
                                            {
                                                candidate = System.IO.Path.Combine(dir, tagPath.Replace('/', '\\'));
                                            }
                                            else if (image.Source is System.Windows.Media.Imaging.BitmapImage bmi && bmi.UriSource != null)
                                            {
                                                candidate = bmi.UriSource.LocalPath;
                                                string tryLocal = System.IO.Path.Combine(dir, "File Dependency", System.IO.Path.GetFileName(candidate));
                                                if (System.IO.File.Exists(tryLocal)) candidate = tryLocal;
                                            }

                                            // 文档照片的 Tag 存储的是原始文档路径（如 C:\Docs\report.pdf）
                                            // 用于恢复 SourceFilePath，使文档去重与笔迹加载在重启后仍可生效
                                            // （分块照片的 Tag 形如「文档路径#块序号」，需剥离块序号后按文档识别）
                                            if (!string.IsNullOrEmpty(tagPath) && IsDocumentFilePath(StripChunkSuffix(tagPath)))
                                            {
                                                sourceFilePath = tagPath;
                                            }
                                        }
                                        catch { }

                                        if (!string.IsNullOrEmpty(candidate) && System.IO.File.Exists(candidate))
                                        {
                                            try
                                            {
                                                var bi = new System.Windows.Media.Imaging.BitmapImage();
                                                bi.BeginInit();
                                                bi.UriSource = new Uri(candidate, UriKind.Absolute);
                                                bi.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad;
                                                bi.EndInit();
                                                bi.Freeze();
                                                var ci = sourceFilePath != null
                                                    ? new Ink_Canvas.Models.CapturedImage(bi, candidate, sourceFilePath)
                                                    : new Ink_Canvas.Models.CapturedImage(bi, candidate);

                                                bool exists = capturedPhotos.Any(p => (!string.IsNullOrEmpty(p.FilePath) && p.FilePath == candidate) || p.Timestamp == ci.Timestamp);
                                                if (!exists)
                                                {
                                                    capturedPhotos.Insert(0, ci);
                                                }
                                                if (!string.IsNullOrEmpty(ci.Timestamp))
                                                {
                                                    photoPageMapping[ci.Timestamp] = pageIndex;
                                                }
                                                // 文档照片：同时建立页码与文档来源的映射
                                                if (!string.IsNullOrEmpty(sourceFilePath))
                                                {
                                                    pageDocumentMapping[pageIndex] = sourceFilePath;
                                                }
                                            }
                                            catch { }
                                        }
                                    }
                                }
                            }
                        }
                    }
                    catch { }
                }

                UpdateCapturedPhotosDisplay();
                try { UpdatePhotoSelectionIndicators(); } catch { }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"加载上次会话照片并绑定失败: {ex.Message}");
            }
        }

        // 视频展台按钮点击事件
        private void BtnVideoPresenter_Click(object sender, RoutedEventArgs e)
        {
            // 切换视频展台侧栏的可见性
            if (VideoPresenterSidebar.Visibility == Visibility.Visible)
            {
                VideoPresenterSidebar.Visibility = Visibility.Collapsed;
                // 注意：不再自动移除摄像头画面，让用户手动控制画面显示
                // 摄像头画面会继续显示在白板中，即使侧栏被隐藏
            }
            else
            {
                ShowVideoPresenterSidebar();
            }
        }

        // 关闭视频展台侧栏按钮点击事件
        private void BtnCloseVideoPresenter_Click(object sender, RoutedEventArgs e)
        {
            VideoPresenterSidebar.Visibility = Visibility.Collapsed;
            // 注意：不再自动移除摄像头画面，让用户手动控制画面显示
            // 摄像头画面会继续显示在白板中，即使侧栏被隐藏
        }

        // 摄像头设备管理器
        private CameraDeviceManager cameraDeviceManager;
        // 当前显示的摄像头画面元素
        private System.Windows.Controls.Image currentCameraImage;
        private Dictionary<int, System.Windows.Controls.Image> cameraFramesByPage = new Dictionary<int, System.Windows.Controls.Image>();
        // 摄像头画面更新定时器
        private DispatcherTimer cameraFrameTimer;

        #region Photo Capture Functions

        // 防重复点击计时器
        private DateTime lastCaptureTime = DateTime.MinValue;
        private const int CAPTURE_COOLDOWN_MS = 1000; // 1秒冷却时间

        private void BtnCapturePhoto_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // 防重复点击检查
                if ((DateTime.Now - lastCaptureTime).TotalMilliseconds < CAPTURE_COOLDOWN_MS)
                {
                    Console.WriteLine("拍照功能冷却中，请稍后再试");
                    return;
                }

                if (cameraDeviceManager == null)
                {
                    MessageBox.Show("摄像头设备管理器未初始化", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                var frame = cameraDeviceManager.GetFrameCopy();
                if (frame == null)
                {
                    MessageBox.Show("未获取到摄像头画面", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }

                // 记录本次拍照时间
                lastCaptureTime = DateTime.Now;

                // 获取当前摄像头画面的旋转角度
                double rotationAngle = 0;
                if (currentCameraImage != null && currentCameraImage.RenderTransform is TransformGroup tg)
                {
                    foreach (var t in tg.Children)
                    {
                        if (t is RotateTransform rt)
                        {
                            rotationAngle += rt.Angle;
                        }
                    }
                }

                // 在后台线程中处理图像转换
                Task.Run(() =>
                {
                    try
                    {
                        using (frame)
                        {
                            if (rotationAngle != 0)
                            {
                                System.Drawing.RotateFlipType rotateFlipType = System.Drawing.RotateFlipType.RotateNoneFlipNone;
                                if (rotationAngle % 360 == 90 || rotationAngle % 360 == -270)
                                    rotateFlipType = System.Drawing.RotateFlipType.Rotate90FlipNone;
                                else if (rotationAngle % 360 == 180 || rotationAngle % 360 == -180)
                                    rotateFlipType = System.Drawing.RotateFlipType.Rotate180FlipNone;
                                else if (rotationAngle % 360 == 270 || rotationAngle % 360 == -90)
                                    rotateFlipType = System.Drawing.RotateFlipType.Rotate270FlipNone;
                                frame.RotateFlip(rotateFlipType);
                            }
                            Bitmap toSave = frame;
                            var bitmapImage = ConvertBitmapToBitmapImage(toSave);
                            if (!ReferenceEquals(toSave, frame))
                            {
                                toSave.Dispose();
                            }
                            if (bitmapImage != null)
                            {
                                Dispatcher.BeginInvoke(new Action(() =>
                                {
                                    AddCapturedPhoto(bitmapImage);
                                }));
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"拍照处理失败: {ex.Message}");
                    }
                });
            }
            catch (Exception ex)
            {
                MessageBox.Show($"拍照失败: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void BtnRotateImage_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // 获取当前页面上所有可旋转的图像元素
                var rotatableElements = GetRotatableElementsOnCurrentPage();
                
                if (rotatableElements.Count == 0)
                {
                    Console.WriteLine("当前页面没有可旋转的图像元素");
                    return;
                }

                // 如果有多个可旋转元素，优先旋转最近添加的或当前选中的
                UIElement elementToRotate = rotatableElements[rotatableElements.Count - 1]; // 默认旋转最后一个
                
                // 检查是否有选中的元素
                var selectedElements = InkCanvasElementsHelper.GetSelectedElements(inkCanvas);
                if (selectedElements.Count > 0)
                {
                    // 如果有选中的元素，优先旋转选中的第一个图像元素
                    var selectedImage = selectedElements.FirstOrDefault(el => el is System.Windows.Controls.Image || el is MediaElement);
                    if (selectedImage != null && rotatableElements.Contains(selectedImage))
                    {
                        elementToRotate = selectedImage;
                    }
                }
                
                // 旋转图像元素
                RotateImageElement(elementToRotate, 90);
                
                Console.WriteLine($"图像已向右旋转90度，元素类型: {elementToRotate.GetType().Name}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"旋转图像失败: {ex.Message}");
            }
        }

        private void RotateImageElement(UIElement imageElement, double angle)
        {
            if (!(imageElement is FrameworkElement frameworkElement)) return;
            
            // 获取或创建变换组
            if (!(frameworkElement.RenderTransform is TransformGroup transformGroup))
            {
                transformGroup = new TransformGroup();
                frameworkElement.RenderTransform = transformGroup;
            }

            // 记录初始状态
            if (!ElementsInitialHistory.ContainsKey(frameworkElement.Name))
            {
                ElementsInitialHistory[frameworkElement.Name] = transformGroup.Clone();
            }

            // 计算元素的实际中心位置
            // 获取元素的边界框，考虑现有的变换
            var bounds = frameworkElement.TransformToVisual(inkCanvas).TransformBounds(new System.Windows.Rect(0, 0, frameworkElement.ActualWidth, frameworkElement.ActualHeight));
            double centerX = bounds.Left + bounds.Width / 2;
            double centerY = bounds.Top + bounds.Height / 2;

            // 创建旋转变换，设置中心点为元素的实际中心
            var rotateTransform = new RotateTransform(angle, centerX, centerY);
            
            // 添加到变换组
            transformGroup.Children.Add(rotateTransform);

            // 记录变换历史（使用与MW_SelectionGestures.cs相同的方式）
            if (ElementsManipulationHistory == null)
            {
                ElementsManipulationHistory = new Dictionary<string, Tuple<object, TransformGroup>>();
            }
            
            ElementsManipulationHistory[frameworkElement.Name] =
                new Tuple<object, TransformGroup>(ElementsInitialHistory[frameworkElement.Name], transformGroup.Clone());
            
            // 提交变换历史
            timeMachine.CommitStrokeManipulationHistory(null, ElementsManipulationHistory);
        }

        private BitmapImage ConvertBitmapToBitmapImage(Bitmap bitmap)
        {
            try
            {
                using (var memory = new MemoryStream())
                {
                    bitmap.Save(memory, System.Drawing.Imaging.ImageFormat.Bmp);
                    memory.Position = 0;

                    var bitmapImage = new BitmapImage();
                    bitmapImage.BeginInit();
                    bitmapImage.StreamSource = memory;
                    bitmapImage.CacheOption = BitmapCacheOption.OnLoad;
                    bitmapImage.EndInit();
                    bitmapImage.Freeze();

                    return bitmapImage;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"图像转换失败: {ex.Message}");
                return null;
            }
        }


        private void AddCapturedPhoto(BitmapImage image)
        {
            AddCapturedPhoto(image, null);
        }

        private void AddCapturedPhoto(BitmapImage image, string sourceFilePath)
        {
            try
            {
                string path = SaveBitmapImageToPhotoFile(image, sourceFilePath);
                var capturedImage = string.IsNullOrEmpty(path)
                    ? new CapturedImage(image)
                    : new CapturedImage(image, path, sourceFilePath);
                capturedPhotos.Insert(0, capturedImage);
                UpdateCapturedPhotosDisplay();

                // 照片已落盘，释放全尺寸大图以节省内存（缩略图与尺寸缓存保留，
                // 之后插入画板时由 CreateBitmapImageFromFileOrMemory 从磁盘重新加载）
                if (!capturedImage.IsVideo && !string.IsNullOrEmpty(path))
                {
                    capturedImage.ReleaseImageMemory();
                }

                // 拍照后不立即插入照片到白板，等待用户点击照片按钮后再插入
                Console.WriteLine($"照片已保存到相册，时间戳: {capturedImage.Timestamp}");
                Console.WriteLine("请点击照片按钮将照片插入白板");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"添加照片失败: {ex.Message}");
            }
        }

        private void AddCapturedPhotoInternal(BitmapImage image, string sourceFilePath)
        {
            // 文档照片：先写入/替换本地缓存（photo.png + photo.meta，与文档笔迹同文件夹），再进入照片列表
            // （分块照片的 sourceFilePath 形如「文档路径#块序号」，需剥离块序号后按文档识别）
            if (!string.IsNullOrEmpty(sourceFilePath) && IsDocumentFilePath(StripChunkSuffix(sourceFilePath)))
            {
                // 分块瓦片（路径带 #块序号）：不写入同名的 photo.png 缓存（避免互相覆盖），
                // 改为走普通文件保存路径，每张瓦片存为独立文件，由 CapturedImage.FilePath 分别引用。
                // 缓存仅用于无块序号的文档主照片（源文件路径本身即为文档路径）。
                if (sourceFilePath.Contains("#"))
                {
                    // 分块瓦片：除保存到照片列表外，同时写入文档级缓存（chunk_{N}.png + photo.meta），
                    // 记录文档修改信息，使下次打开可从硬盘直接加载，仅当文档被修改时才重新转换。
                    SaveDocumentChunkToCache(sourceFilePath, image);
                    AddCapturedPhoto(image, sourceFilePath);
                    return;
                }

                string cachedPath = SaveDocumentPhotoToCache(sourceFilePath, image);

                // 去重：同一 sourceFilePath 只保留一张照片（同名文档重新转换时更新图片与缓存路径）
                var existing = capturedPhotos.FirstOrDefault(p =>
                    !string.IsNullOrEmpty(p.SourceFilePath) &&
                    string.Equals(p.SourceFilePath, sourceFilePath, StringComparison.OrdinalIgnoreCase));
                if (existing != null)
                {
                    existing.UpdateImage(image, cachedPath ?? existing.FilePath);
                    UpdateCapturedPhotosDisplay();
                    Console.WriteLine($"文档照片已存在，更新图片: {sourceFilePath}");
                    return;
                }

                if (!string.IsNullOrEmpty(cachedPath))
                {
                    var ci = new CapturedImage(image, cachedPath, sourceFilePath);
                    capturedPhotos.Insert(0, ci);
                    UpdateCapturedPhotosDisplay();
                    // 文档主照片已写入缓存，释放全尺寸大图以节省内存
                    if (!ci.IsVideo) ci.ReleaseImageMemory();
                    Console.WriteLine($"文档照片已缓存并加入照片列表: {sourceFilePath}");
                    return;
                }
            }
            AddCapturedPhoto(image, sourceFilePath);
        }

        private bool IsDocumentFilePath(string filePath)
        {
            if (string.IsNullOrEmpty(filePath)) return false;
            string ext = System.IO.Path.GetExtension(filePath).ToLowerInvariant();
            return ext == ".docx" || ext == ".xls" || ext == ".xlsx" || ext == ".pdf";
        }

        /// <summary>
        /// 剥离文档分块照片的块序号后缀（"文档路径#N" → "文档路径"）。
        /// 文档过大时会被拆分为多张照片，每张来源标识为「文档路径#块序号」，
        /// 需要按原始文档路径识别同一文档。
        /// </summary>
        private static string StripChunkSuffix(string filePath)
        {
            if (string.IsNullOrEmpty(filePath)) return filePath;
            int idx = filePath.LastIndexOf('#');
            return idx >= 0 ? filePath.Substring(0, idx) : filePath;
        }

        /// <summary>解析文档分块照片的块序号（"文档路径#N" → N；无块序号返回 0）。</summary>
        private static int GetChunkIndex(string sourceFilePath)
        {
            if (string.IsNullOrEmpty(sourceFilePath)) return 0;
            int idx = sourceFilePath.LastIndexOf('#');
            if (idx < 0 || idx == sourceFilePath.Length - 1) return 0;
            if (int.TryParse(sourceFilePath.Substring(idx + 1), out int n)) return n;
            return 0;
        }

        /// <summary>
        /// 清洗 XAML 文本中重复的 photo_ 元素名称，为后续同名元素附加自增序号，保证名称唯一。
        /// 旧版保存文件可能因同毫秒多瓦片而含重复名称，整体 XamlReader.Load 会因此抛
        /// 「不能在此范围中注册重复的名称」异常。此方法仅作用于 photo_ 前缀的名称，
        /// 不影响其它元素（如视频 MediaElement 的命名）。
        /// </summary>
        private static string SanitizeDuplicatePhotoNames(string xaml)
        {
            if (string.IsNullOrEmpty(xaml)) return xaml;

            var seen = new Dictionary<string, int>(StringComparer.Ordinal);
            var pattern = new System.Text.RegularExpressions.Regex(
                "(Name=\\\"photo_[0-9]{8}_[0-9]{2}_[0-9]{2}_[0-9]{2}_[0-9]{3}\\\")");

            return pattern.Replace(xaml, m =>
            {
                string name = m.Groups[1].Value;
                if (seen.TryGetValue(name, out int count))
                {
                    seen[name] = count + 1;
                    // 追加自增序号，形式如 photo_..._222_1、photo_..._222_2 等
                    string baseName = name.Substring(0, name.Length - 1); // 去掉尾引号
                    return baseName + "_" + (count + 1) + "\"";
                }
                seen[name] = 0;
                return m.Value;
            });
        }

        /// <summary>
        /// 获取同一文档的所有分块照片，按块序号升序排列。
        /// 分块照片来源标识为「文档路径#块序号」，据此归并同一文档。
        /// </summary>
        private List<CapturedImage> GetDocumentTiles(string docPath)
        {
            if (string.IsNullOrEmpty(docPath)) return new List<CapturedImage>();
            return capturedPhotos
                .Where(p => !string.IsNullOrEmpty(p.SourceFilePath) &&
                            string.Equals(StripChunkSuffix(p.SourceFilePath), docPath, StringComparison.OrdinalIgnoreCase))
                .OrderBy(p => GetChunkIndex(p.SourceFilePath))
                .ToList();
        }

        /// <summary>
        /// 从内存照片列表重建指定文档页的照片瓦片到画布。
        /// 用于磁盘保存文件尚未就绪（如退出白板后立即重进）时，保证文档照片不丢失。
        /// 返回是否成功重建了照片。
        /// </summary>
        private bool ReinsertDocumentPhotosFromMemory(int pageIndex)
        {
            try
            {
                if (!pageDocumentMapping.TryGetValue(pageIndex, out string sourceFilePath) ||
                    string.IsNullOrEmpty(sourceFilePath))
                {
                    LogHelper.WriteLogToFile($"[诊断] 内存重建失败：页 {pageIndex} 无文档映射", LogHelper.LogType.Trace);
                    return false;
                }
                string docPath = StripChunkSuffix(sourceFilePath);
                if (string.IsNullOrEmpty(docPath) || !IsDocumentFilePath(docPath))
                {
                    LogHelper.WriteLogToFile($"[诊断] 内存重建失败：页 {pageIndex} 文档无效 {docPath}", LogHelper.LogType.Trace);
                    return false;
                }

                var tiles = GetDocumentTiles(docPath);
                if (tiles.Count == 0)
                {
                    LogHelper.WriteLogToFile($"[诊断] 内存重建失败：页 {pageIndex} 文档 {docPath} 无内存瓦片 (capturedPhotos={capturedPhotos.Count})", LogHelper.LogType.Trace);
                    return false;
                }

                // 按瓦片总高度铺开展示：以最大瓦片宽度为基准水平居中，顶部对齐
                int maxW = tiles.Max(t => t.PixelWidth);
                double canvasWidth = inkCanvas.ActualWidth;
                if (canvasWidth <= 0) canvasWidth = SystemParameters.PrimaryScreenWidth;
                double left = Math.Round(Math.Max(0, (canvasWidth - maxW) / 2.0));
                double top = 0;

                System.Windows.Controls.Image firstTileImg = null;
                for (int ti = 0; ti < tiles.Count; ti++)
                {
                    var tile = tiles[ti];
                    var tileImg = new System.Windows.Controls.Image
                    {
                        Source = CreateBitmapImageFromFileOrMemory(tile),
                        Width = tile.PixelWidth,
                        Height = tile.PixelHeight,
                        Name = GeneratePhotoName(),
                        Tag = tile.SourceFilePath,
                        SnapsToDevicePixels = true,
                        UseLayoutRounding = true
                    };
                    System.Windows.Media.RenderOptions.SetBitmapScalingMode(tileImg, System.Windows.Media.BitmapScalingMode.HighQuality);
                    InkCanvas.SetLeft(tileImg, left);
                    InkCanvas.SetTop(tileImg, Math.Round(top));
                    inkCanvas.Children.Add(tileImg);
                    if (firstTileImg == null) firstTileImg = tileImg;
                    photoPageMapping[tile.Timestamp] = pageIndex;
                    top += tile.PixelHeight;
                }

                currentPhotoImage = firstTileImg;
                // 与 XAML 恢复路径保持一致：按当前画布宽度统一重算瓦片位置
                RecenterDocumentTilesOnCanvas(sourceFilePath);
                Console.WriteLine($"从内存照片重建文档瓦片完成: {tiles.Count} 张, 文档: {docPath}, 总高度: {Math.Round(top)}px");
                return true;
            }
            catch (Exception ex)
            {
                LogHelper.WriteLogToFile($"从内存照片重建文档瓦片失败: {ex.Message}", Helpers.LogHelper.LogType.Error);
                return false;
            }
        }

        /// <summary>
        /// 换页/重进白板时，从内存照片列表重建文档页瓦片的带异常保护包装。
        /// 返回是否成功重建了照片。
        /// </summary>
        private bool ReinsertDocumentPhotosFromMemorySafe(int pageIndex)
        {
            try
            {
                return ReinsertDocumentPhotosFromMemory(pageIndex);
            }
            catch (Exception ex)
            {
                LogHelper.WriteLogToFile($"从内存重建文档瓦片（安全包装）失败: {ex.Message}", Helpers.LogHelper.LogType.Error);
                return false;
            }
        }

        /// <summary>判断当前画布上是否已存在指定文档的任意瓦片照片元素。</summary>
        private bool HasDocumentTilesOnCurrentPage(string docPath)
        {
            if (inkCanvas == null || string.IsNullOrEmpty(docPath)) return false;
            foreach (var child in inkCanvas.Children)
            {
                if (child is System.Windows.Controls.Image img && img.Tag is string tag && !string.IsNullOrEmpty(tag))
                {
                    if (string.Equals(StripChunkSuffix(tag), docPath, StringComparison.OrdinalIgnoreCase))
                        return true;
                }
            }
            return false;
        }

        /// <summary>
        /// 更新照片列表中对应 sourceFilePath 的文档照片。不存在则新增。
        /// </summary>
        private void UpdateCapturedPhotoInternal(string sourceFilePath, BitmapImage newImage)
        {
            try
            {
                if (string.IsNullOrEmpty(sourceFilePath) || newImage == null) return;

                var existing = capturedPhotos.FirstOrDefault(p =>
                    !string.IsNullOrEmpty(p.SourceFilePath) &&
                    string.Equals(p.SourceFilePath, sourceFilePath, StringComparison.OrdinalIgnoreCase));

                if (existing != null)
                {
                    // 刷新时同步保存新图片并更新文件路径，避免后续加载到过期的旧照片文件
                    // 文档照片写入专属缓存（替换原 photo.png 并刷新 meta 修改记录）
                    string newPath = IsDocumentFilePath(StripChunkSuffix(sourceFilePath))
                        ? SaveDocumentPhotoToCache(sourceFilePath, newImage)
                        : SaveBitmapImageToPhotoFile(newImage, sourceFilePath);
                    existing.UpdateImage(newImage, newPath);
                    UpdateCapturedPhotosDisplay();
                    Console.WriteLine($"已更新文档照片: {sourceFilePath}");
                }
                else
                {
                    AddCapturedPhoto(newImage, sourceFilePath);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"UpdateCapturedPhoto 失败: {ex.Message}");
            }
        }

        /// <summary>
        /// 替换画板上对应 sourceFilePath 的文档图片源，保留 Left/Top 位置、宽度及其他内容不变。
        /// </summary>
        private bool ReplaceDocumentImageOnCanvasInternal(string sourceFilePath, BitmapImage newImage)
        {
            try
            {
                if (string.IsNullOrEmpty(sourceFilePath) || newImage == null) return false;

                bool replaced = false;
                foreach (UIElement child in inkCanvas.Children)
                {
                    if (child is System.Windows.Controls.Image img &&
                        img.Tag is string tag &&
                        string.Equals(tag, sourceFilePath, StringComparison.OrdinalIgnoreCase))
                    {
                        double oldWidth = img.Width;
                        double oldLeft = InkCanvas.GetLeft(img);
                        double oldTop = InkCanvas.GetTop(img);

                        img.Source = newImage;

                        // 保持原宽度不变，按新图片宽高比调整高度；保留 Left/Top。
                        // 尺寸取整到像素，避免子像素模糊。
                        if (oldWidth > 0 && newImage.PixelWidth > 0)
                        {
                            img.Width = Math.Round(oldWidth);
                            img.Height = Math.Round(oldWidth * newImage.PixelHeight / newImage.PixelWidth);
                        }
                        else
                        {
                            img.Width = newImage.PixelWidth;
                            img.Height = newImage.PixelHeight;
                        }

                        InkCanvas.SetLeft(img, Math.Round(oldLeft));
                        InkCanvas.SetTop(img, Math.Round(oldTop));

                        replaced = true;
                    }
                }

                if (replaced)
                {
                    Console.WriteLine($"已替换画板文档图片: {sourceFilePath}");
                    // 若当前页面正是该文档页，立即更新保存文件中的图片
                    int currentPage = GetCurrentPageIndex();
                    if (pageDocumentMapping.TryGetValue(currentPage, out string mappedPath) &&
                        string.Equals(mappedPath, sourceFilePath, StringComparison.OrdinalIgnoreCase))
                    {
                        SaveDocumentPageIfNeeded(currentPage);
                    }
                }
                return replaced;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"ReplaceDocumentImageOnCanvas 失败: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// 对文档页笔迹/元素的变更进行防抖自动保存。
        /// </summary>
        private void ScheduleDocumentPageSave(int pageIndex)
        {
            _pendingDocumentSavePageIndex = pageIndex;
            if (_documentPageSaveTimer == null)
            {
                _documentPageSaveTimer = new Timer(_ =>
                {
                    Dispatcher.Invoke(() =>
                    {
                        if (_pendingDocumentSavePageIndex >= 0)
                        {
                            SaveDocumentPageIfNeeded(_pendingDocumentSavePageIndex);
                            _pendingDocumentSavePageIndex = -1;
                        }
                    });
                }, null, Timeout.Infinite, Timeout.Infinite);
            }
            _documentPageSaveTimer.Change(1500, Timeout.Infinite);
        }

        /// <summary>
        /// 如果指定页面对应文档照片，则保存该页全部内容（墨迹+元素）到文件名对应文件夹。
        /// 保存方式与 PPT 笔迹自动保存保持一致：
        /// - 墨迹流保存为 {pageIndex:0000}.icstk（与 PPT 的 .icstk 文件格式相同）
        /// - UI 元素保存为 {pageIndex:0000}.xaml
        /// - 依赖文件保存到 File Dependency 子文件夹
        /// </summary>
        private void SaveDocumentPageIfNeeded(int pageIndex)
        {
            try
            {
                // 该页已被显式保存，取消防抖队列中针对该页的待保存项，
                // 避免翻页后防抖触发时把新页内容误写入旧文档的保存文件
                if (_pendingDocumentSavePageIndex == pageIndex) _pendingDocumentSavePageIndex = -1;

                if (!pageDocumentMapping.TryGetValue(pageIndex, out string sourceFilePath) ||
                    string.IsNullOrEmpty(sourceFilePath) ||
                    !IsDocumentFilePath(StripChunkSuffix(sourceFilePath)))
                {
                    return;
                }

                string folderPath = GetDocumentPageFolderPath(sourceFilePath);
                if (string.IsNullOrEmpty(folderPath)) return;

                if (!Directory.Exists(folderPath))
                    Directory.CreateDirectory(folderPath);

                string baseFilePath = System.IO.Path.Combine(folderPath, pageIndex.ToString("0000"));
                string strokesFilePath = baseFilePath + ".icstk";
                string elementsFilePath = baseFilePath + ".xaml";

                // 清理同文档下其他页码的旧保存文件，避免页码变化后恢复到过期的旧笔迹
                try
                {
                    foreach (var f in Directory.GetFiles(folderPath, "*.icstk"))
                    {
                        if (!string.Equals(f, strokesFilePath, StringComparison.OrdinalIgnoreCase))
                            File.Delete(f);
                    }
                    foreach (var f in Directory.GetFiles(folderPath, "*.xaml"))
                    {
                        if (!string.Equals(f, elementsFilePath, StringComparison.OrdinalIgnoreCase))
                            File.Delete(f);
                    }
                }
                catch { }

                // 保存墨迹与 UI 元素到磁盘。
                // 重要：StrokeCollection.Save / XamlWriter.Save 必须在本（UI）线程执行，
                // 因为 InkCanvas 及其子元素均为 DispatcherObject，不能在后台线程访问其 Children。
                // 之前把这两步放到 Task.Run 中导致「调用线程无法访问此对象」与「流已关闭」异常，
                // 使得 .icstk/.xaml 从未真正落盘，进而任何文档页恢复都失败、回退逻辑重复插入照片。
                // 这里先在 UI 线程完成全部序列化（得到字节数组 / XAML 文本），
                // 再在后台线程仅做纯磁盘写入，既避免跨线程异常，也避免长时间阻塞 UI。

                // 序列化墨迹（与 PPT 的 .icstk 格式一致，均为 ISF 字节流）
                byte[] strokesBytes;
                using (var ms = new System.IO.MemoryStream())
                {
                    inkCanvas.Strokes.Save(ms);
                    strokesBytes = ms.ToArray();
                }

                // 序列化 UI 元素：构造可序列化画布快照并在本线程完成 XAML 序列化
                var serializableCanvas = CreateSerializableCanvasForSnapshot();
                string elementsXaml;
                using (var msXaml = new System.IO.MemoryStream())
                {
                    XamlWriter.Save(serializableCanvas, msXaml);
                    elementsXaml = System.Text.Encoding.UTF8.GetString(msXaml.ToArray());
                }

                // 收集需要复制到 File Dependency 子文件夹的依赖文件（图像/媒体源）。
                // 关键优化：跳过「文档分块照片」——其源已经是持久化的缓存文件（photo.png / chunk_*.png），
                // 保存的 .xaml 直接引用该缓存路径，无需再复制整张长图，既避免每次保存时同步拷贝造成卡顿，
                // 也避免磁盘上重复存储同一张大图。拷贝本身放到后台线程执行，不阻塞 UI（尤其退出白板时）。
                var dependencyFiles = CollectDocumentPageDependencyFiles(serializableCanvas);

                // 后台线程仅做磁盘写入（含依赖文件拷贝），不再访问任何 UI 对象
                SaveDocumentPageDataAsync(strokesBytes, elementsXaml, folderPath, strokesFilePath, elementsFilePath, dependencyFiles);

                LogHelper.WriteLogToFile($"文档页已自动保存: {strokesFilePath}（笔迹 {inkCanvas.Strokes.Count}，元素 {inkCanvas.Children.Count}）", LogHelper.LogType.Trace);
            }
            catch (Exception ex)
            {
                LogHelper.WriteLogToFile($"保存文档页内容失败: {ex.Message}", LogHelper.LogType.Error);
            }
        }

        /// <summary>
        /// 在后台线程将已序列化的墨迹字节与 XAML 文本原子写入磁盘。
        /// 序列化的工作（StrokeCollection.Save / XamlWriter.Save）必须在 UI 线程完成
        /// （InkCanvas 及其子元素为 DispatcherObject，不能跨线程访问），
        /// 本方法仅负责纯磁盘 IO，不再触碰任何 UI 对象，避免跨线程异常与「流已关闭」异常。
        /// </summary>
        private void SaveDocumentPageDataAsync(
            byte[] strokesBytes, string elementsXaml, string folderPath,
            string strokesFilePath, string elementsFilePath, System.Collections.Generic.List<string> dependencyFiles)
        {
            System.Threading.Tasks.Task.Run(() =>
            {
                try
                {
                    // 原子写入墨迹：先写 .tmp，Flush 落盘后替换正式文件，
                    // 避免进程中途退出导致 .icstk 截断
                    if (strokesBytes != null && strokesBytes.Length > 0)
                    {
                        string tmpStrokesPath = strokesFilePath + ".tmp";
                        using (var fs = new FileStream(tmpStrokesPath, FileMode.Create, FileAccess.Write))
                        {
                            fs.Write(strokesBytes, 0, strokesBytes.Length);
                            fs.Flush(true);
                        }
                        if (File.Exists(strokesFilePath)) File.Delete(strokesFilePath);
                        File.Move(tmpStrokesPath, strokesFilePath);
                    }

                    // 原子写入元素 XAML：同样先写 .tmp 再替换
                    if (!string.IsNullOrEmpty(elementsXaml))
                    {
                        string tmpFilePath = elementsFilePath + ".tmp";
                        using (var fs = new FileStream(tmpFilePath, FileMode.Create, FileAccess.Write))
                        using (var sw = new StreamWriter(fs, System.Text.Encoding.UTF8))
                        {
                            sw.Write(elementsXaml);
                            sw.Flush();
                            fs.Flush(true);
                        }
                        if (File.Exists(elementsFilePath)) File.Delete(elementsFilePath);
                        File.Move(tmpFilePath, elementsFilePath);
                    }

                    // 依赖文件（图像/媒体源）复制到 File Dependency 子文件夹。
                    // 此拷贝在后台线程执行（纯磁盘 IO），不阻塞 UI 线程，避免退出白板 / 换页时卡顿。
                    // 文档分块照片已在 CollectDocumentPageDependencyFiles 中剔除（其源即持久化缓存文件）。
                    if (dependencyFiles != null && dependencyFiles.Count > 0)
                    {
                        string dependencyFolder = System.IO.Path.Combine(folderPath, "File Dependency");
                        if (!Directory.Exists(dependencyFolder))
                            Directory.CreateDirectory(dependencyFolder);
                        foreach (var src in dependencyFiles)
                        {
                            try
                            {
                                if (File.Exists(src))
                                {
                                    string destPath = System.IO.Path.Combine(dependencyFolder, System.IO.Path.GetFileName(src));
                                    File.Copy(src, destPath, true);
                                }
                            }
                            catch (Exception copyEx)
                            {
                                LogHelper.WriteLogToFile($"复制依赖文件失败 [{src}]: {copyEx.Message}", LogHelper.LogType.Error);
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    LogHelper.WriteLogToFile($"后台保存文档页失败: {ex.Message}", LogHelper.LogType.Error);
                }
            });
        }

        /// <summary>
        /// 在 UI 线程收集需要复制到 File Dependency 子文件夹的依赖文件（图像/媒体源路径）。
        /// 仅收集路径字符串，不执行磁盘 IO，因此可在 UI 线程安全调用。
        /// 关键：剔除「文档分块照片」——其源已经是持久化的缓存文件（photo.png / chunk_*.png），
        /// 保存的 .xaml 直接引用该缓存路径，无需复制整张长图（避免卡顿与重复存储）。
        /// </summary>
        private System.Collections.Generic.List<string> CollectDocumentPageDependencyFiles(InkCanvas serializableCanvas)
        {
            var list = new System.Collections.Generic.List<string>();
            try
            {
                foreach (UIElement element in serializableCanvas.Children)
                {
                    if (element is System.Windows.Controls.Image image && image.Source is BitmapImage bitmapImage && bitmapImage.UriSource != null)
                    {
                        string tag = image.Tag as string;
                        // 文档分块照片（Tag 形如「文档路径#块序号」）：源已持久化，跳过拷贝
                        if (!string.IsNullOrEmpty(tag) && IsDocumentFilePath(StripChunkSuffix(tag)))
                            continue;
                        try { list.Add(bitmapImage.UriSource.LocalPath); } catch { }
                    }
                    else if (element is MediaElement mediaElement && mediaElement.Source != null)
                    {
                        try { list.Add(mediaElement.Source.LocalPath); } catch { }
                    }
                }
            }
            catch (Exception ex)
            {
                LogHelper.WriteLogToFile($"收集文档页依赖文件失败: {ex.Message}", LogHelper.LogType.Error);
            }
            return list;
        }

        /// <summary>
        /// 在文档页文件夹中查找存在的保存文件基路径。
        /// 优先匹配当前页码 {pageIndex:0000}，未命中则回退到文件夹内任意页文件（页码可能因重启变化）。
        /// 返回基路径（不含扩展名），不存在则返回 null。
        /// </summary>
        private string FindDocumentPageBasePath(string folderPath, int pageIndex)
        {
            if (string.IsNullOrEmpty(folderPath) || !Directory.Exists(folderPath))
                return null;

            // 1. 优先精确匹配当前页码
            string basePath = System.IO.Path.Combine(folderPath, pageIndex.ToString("0000"));
            if (File.Exists(basePath + ".icstk") || File.Exists(basePath + ".xaml"))
                return basePath;

            // 2. 回退：搜索文件夹内任意页文件
            try
            {
                foreach (var f in Directory.GetFiles(folderPath, "*.icstk"))
                {
                    return System.IO.Path.Combine(folderPath, System.IO.Path.GetFileNameWithoutExtension(f));
                }
                foreach (var f in Directory.GetFiles(folderPath, "*.xaml"))
                {
                    return System.IO.Path.Combine(folderPath, System.IO.Path.GetFileNameWithoutExtension(f));
                }
            }
            catch { }
            return null;
        }

        /// <summary>
        /// 导入文档前检测是否已有对应保存的笔迹/元素，有则自动恢复到当前页。
        /// 行为与 PPT 幻灯片开始时检测并加载已有墨迹一致。
        /// 返回是否成功找到并恢复了保存的内容。
        /// </summary>
        private bool RestoreDocumentPageIfSavedInternal(string sourceFilePath)
        {
            try
            {
                if (string.IsNullOrEmpty(sourceFilePath))
                    return false;

                string folderPath = GetDocumentPageFolderPath(sourceFilePath);
                if (string.IsNullOrEmpty(folderPath)) return false;

                int pageIndex = GetCurrentPageIndex();
                // 页码可能因重启变化，回退搜索文件夹内任意页文件
                string basePathName = FindDocumentPageBasePath(folderPath, pageIndex);
                if (basePathName == null)
                    return false;

                // 建立/更新页码与文档的映射关系
                pageDocumentMapping[pageIndex] = sourceFilePath;
                // 清空当前页已有内容，然后从文件恢复（与 PPT 检测到已有墨迹后加载的行为一致）
                RestoreDocumentPageIfAvailable(pageIndex);

                // 若照片列表中没有该文档的照片，从画布上恢复的图片中提取并添加到侧栏
                if (!capturedPhotos.Any(p =>
                    !string.IsNullOrEmpty(p.SourceFilePath) &&
                    string.Equals(p.SourceFilePath, sourceFilePath, StringComparison.OrdinalIgnoreCase)))
                {
                    TryRestorePhotoToSidebarFromCanvas(sourceFilePath);
                }
                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"RestoreDocumentPageIfSaved 失败: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// 从当前画布上查找 Tag 匹配 sourceFilePath 的文档图片，将其添加到照片侧栏。
        /// 用于照片列表被清空但画布已从文件恢复的场景。
        /// </summary>
        private void TryRestorePhotoToSidebarFromCanvas(string sourceFilePath)
        {
            try
            {
                if (string.IsNullOrEmpty(sourceFilePath)) return;
                foreach (UIElement child in inkCanvas.Children)
                {
                    if (child is System.Windows.Controls.Image img &&
                        img.Tag is string tag &&
                        string.Equals(tag, sourceFilePath, StringComparison.OrdinalIgnoreCase))
                    {
                        if (img.Source is BitmapSource bs)
                        {
                            // 转为 BitmapImage 并添加到侧栏
                            var encoder = new PngBitmapEncoder();
                            encoder.Frames.Add(BitmapFrame.Create(bs));
                            using (var ms = new MemoryStream())
                            {
                                encoder.Save(ms);
                                ms.Position = 0;
                                var bi = new BitmapImage();
                                bi.BeginInit();
                                bi.CacheOption = BitmapCacheOption.OnLoad;
                                bi.StreamSource = ms;
                                bi.EndInit();
                                bi.Freeze();
                                AddCapturedPhoto(bi, sourceFilePath);
                            }
                        }
                        break;
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"从画布恢复照片到侧栏失败: {ex.Message}");
            }
        }

        /// <summary>
        /// 尝试从文件名对应文件夹加载指定页的文档内容。
        /// 加载方式与 PPT 笔迹保存保持一致：
        /// - 墨迹从 {pageIndex:0000}.icstk 加载（页码不匹配时回退到文件夹内任意页文件）
        /// - UI 元素从 {pageIndex:0000}.xaml 加载（同上）
        /// - 依赖文件从本文件夹下的 File Dependency 子文件夹加载
        /// 如果加载成功，会清空当前画布并覆盖为保存的内容，同时清空该页内存历史。
        /// 返回是否成功找到并加载了文档页。
        /// </summary>
        private bool RestoreDocumentPageIfAvailable(int pageIndex)
        {
            try
            {
                if (!pageDocumentMapping.TryGetValue(pageIndex, out string sourceFilePath) ||
                    string.IsNullOrEmpty(sourceFilePath))
                {
                    LogHelper.WriteLogToFile($"[诊断] 文档页恢复：页 {pageIndex} 无文档映射，跳过", LogHelper.LogType.Trace);
                    return false;
                }

                string folderPath = GetDocumentPageFolderPath(sourceFilePath);
                if (string.IsNullOrEmpty(folderPath)) return false;

                // 页码可能因重启变化，回退搜索文件夹内任意页文件
                string baseFilePath = FindDocumentPageBasePath(folderPath, pageIndex);
                if (baseFilePath == null)
                {
                    LogHelper.WriteLogToFile($"[诊断] 文档页恢复：页 {pageIndex} 文件夹 {folderPath} 找不到页文件", LogHelper.LogType.Trace);
                    return false;
                }

                string strokesFilePath = baseFilePath + ".icstk";
                string elementsFilePath = baseFilePath + ".xaml";

                // 换页/重进时，SaveDocumentPageIfNeeded 是异步后台写盘，可能还未完成。
                // 等待 .xaml/.icstk 正式文件出现且对应 .tmp 消失（写盘完成），
                // 避免读到半写的旧文件或误判“磁盘恢复失败”而走内存重建。
                // 退出白板后立即重进时，等待能给后台写盘留出完成时间，保证走完整的 XAML 恢复路径。
                bool hasStrokes = false;
                bool hasElements = false;
                DateTime saveDeadline = DateTime.UtcNow.AddMilliseconds(1500);
                while (DateTime.UtcNow < saveDeadline)
                {
                    hasStrokes = File.Exists(strokesFilePath);
                    hasElements = File.Exists(elementsFilePath);
                    bool strokesReady = hasStrokes && !File.Exists(strokesFilePath + ".tmp");
                    bool elementsReady = hasElements && !File.Exists(elementsFilePath + ".tmp");
                    // 两个文件都就绪（或题图文件已就绪）即视为保存完成
                    if (elementsReady && strokesReady) break;
                    if (hasStrokes || hasElements) break; // 已有文件，不再等待
                    System.Threading.Thread.Sleep(60);
                }
                if (!hasStrokes && !hasElements) return false;

                // 先加载到临时对象，全部成功后再替换画布。
                // 避免加载中途失败时已插入的照片被清空、随后空内容又覆盖保存文件。
                StrokeCollection loadedStrokes = null;
                var loadedElements = new List<UIElement>();

                // 加载墨迹（与 PPT 的 .icstk 格式一致）
                if (hasStrokes)
                {
                    using (var fs = new FileStream(strokesFilePath, FileMode.Open, FileAccess.Read))
                    {
                        loadedStrokes = new StrokeCollection(fs);
                    }
                }

                // 加载 UI 元素（单个子元素失败不影响其他元素）
                if (hasElements)
                {
                    string xamlText;
                    using (var fs = new FileStream(elementsFilePath, FileMode.Open, FileAccess.Read))
                    using (var sr = new StreamReader(fs))
                    {
                        xamlText = sr.ReadToEnd();
                    }

                    // 旧版保存文件可能因同毫秒多瓦片而含重复的 photo_ 名称，
                    // 整体加载会因「在此范围中注册重复的名称」抛异常，
                    // 先在文本层面为每个 photo_ 名称附加自增序号，保证唯一后再解析。
                    xamlText = SanitizeDuplicatePhotoNames(xamlText);

                    InkCanvas loadedCanvas = null;
                    try
                    {
                        using (var ms = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(xamlText)))
                        {
                            loadedCanvas = XamlReader.Load(ms) as InkCanvas;
                        }
                    }
                    catch (Exception topEx)
                    {
                        LogHelper.WriteLogToFile($"恢复文档页整体加载失败: {topEx.Message}", LogHelper.LogType.Warning);
                        // 整体解析失败说明 XAML 文件损坏（如后台写入时进程退出导致被截断）。
                        // 不直接删除原文件，改为将损坏文件移动或另存为 .corrupt 以便后续人工分析与恢复，防止数据丢失。
                        try
                        {
                            string corruptPath = elementsFilePath + ".corrupt";
                            if (File.Exists(elementsFilePath))
                            {
                                try
                                {
                                    File.Move(elementsFilePath, corruptPath);
                                    LogHelper.WriteLogToFile($"已移动损坏的文档页元素文件到: {corruptPath}", LogHelper.LogType.Warning);
                                }
                                catch
                                {
                                    // 如果无法移动，尝试以读取到的文本保存为 .corrupt，然后删除原文件以避免重复报错
                                    File.WriteAllText(corruptPath, xamlText ?? string.Empty, System.Text.Encoding.UTF8);
                                    LogHelper.WriteLogToFile($"无法移动文件，已将 XAML 内容另存为: {corruptPath}", LogHelper.LogType.Warning);
                                    try { File.Delete(elementsFilePath); } catch { }
                                }
                            }
                            else
                            {
                                File.WriteAllText(corruptPath, xamlText ?? string.Empty, System.Text.Encoding.UTF8);
                                LogHelper.WriteLogToFile($"已将损坏的 XAML 内容保存为: {corruptPath}", LogHelper.LogType.Warning);
                            }
                        }
                        catch (Exception ex2)
                        {
                            LogHelper.WriteLogToFile($"处理损坏文档页文件时出错: {ex2.Message}", LogHelper.LogType.Warning);
                        }
                    }

                    if (loadedCanvas != null)
                    {
                        foreach (UIElement child in loadedCanvas.Children)
                        {
                            try
                            {
                                var xaml = XamlWriter.Save(child);
                                UIElement clonedChild = (UIElement)XamlReader.Parse(xaml);
                                if (clonedChild is System.Windows.Controls.Image image)
                                {
                                    FixLoadedImageSource(image, folderPath);
                                    // 兜底：文档照片图片源失效时，强制使用缓存的 photo.png
                                    if (image.Name != null && image.Name.StartsWith("photo_") &&
                                        !HasValidImageSource(image) && IsDocumentFilePath(StripChunkSuffix(sourceFilePath)))
                                    {
                                        TrySetImageSourceFromDocumentCache(image, sourceFilePath);
                                    }
                                }
                                if (clonedChild is MediaElement mediaElement)
                                {
                                    mediaElement.LoadedBehavior = MediaState.Manual;
                                    mediaElement.UnloadedBehavior = MediaState.Manual;
                                    mediaElement.Loaded += (_, __) => { mediaElement.Play(); };
                                }
                                loadedElements.Add(clonedChild);
                            }
                            catch (Exception childEx)
                            {
                                LogHelper.WriteLogToFile($"恢复文档页子元素失败: {childEx.Message}", LogHelper.LogType.Warning);
                            }
                        }
                    }
                }

                // 文档页必须包含照片：若恢复内容中没有照片元素（保存文件为空/损坏），
                // 用缓存的 photo.png 重建居中的照片元素，保证文档页一定能显示照片
                bool hasPhotoElement = false;
                foreach (var el in loadedElements)
                {
                    if (el is System.Windows.Controls.Image img && img.Name != null && img.Name.StartsWith("photo_"))
                    {
                        hasPhotoElement = true;
                        break;
                    }
                }
                if (!hasPhotoElement && IsDocumentFilePath(StripChunkSuffix(sourceFilePath)))
                {
                    string docPathForRebuild = StripChunkSuffix(sourceFilePath);
                    // 多块瓦片长图：必须展开所有瓦片，单张 CreateDocumentPhotoImageElement 只能构造一张，
                    // 会导致其他瓦片丢失（表现为“照片消失”）。这里复用瓦片插入逻辑展开所有瓦片。
                    var tilesForRebuild = GetDocumentTiles(docPathForRebuild);
                    if (tilesForRebuild.Count > 1)
                    {
                        int maxW = tilesForRebuild.Max(t => t.PixelWidth);
                        double canvasWidth = inkCanvas.ActualWidth;
                        if (canvasWidth <= 0) canvasWidth = SystemParameters.PrimaryScreenWidth;
                        double left = Math.Round(Math.Max(0, (canvasWidth - maxW) / 2.0));
                        double top = 0;
                        foreach (var tile in tilesForRebuild)
                        {
                            var tileImg = new System.Windows.Controls.Image
                            {
                                Source = CreateBitmapImageFromFileOrMemory(tile),
                                Width = tile.PixelWidth,
                                Height = tile.PixelHeight,
                                Name = GeneratePhotoName(),
                                Tag = tile.SourceFilePath,
                                SnapsToDevicePixels = true,
                                UseLayoutRounding = true
                            };
                            System.Windows.Media.RenderOptions.SetBitmapScalingMode(tileImg, System.Windows.Media.BitmapScalingMode.HighQuality);
                            InkCanvas.SetLeft(tileImg, left);
                            InkCanvas.SetTop(tileImg, Math.Round(top));
                            loadedElements.Add(tileImg);
                            top += tile.PixelHeight;
                        }
                        LogHelper.WriteLogToFile($"文档页保存内容缺少照片元素，已从内存瓦片重建 {tilesForRebuild.Count} 张瓦片: {docPathForRebuild}", LogHelper.LogType.Trace);
                    }
                    else
                    {
                        var rebuilt = CreateDocumentPhotoImageElement(sourceFilePath);
                        if (rebuilt != null)
                        {
                            loadedElements.Add(rebuilt);
                            LogHelper.WriteLogToFile($"文档页保存内容缺少照片元素，已从缓存重建: {sourceFilePath}", LogHelper.LogType.Trace);
                        }
                    }
                }

                if (loadedStrokes == null && loadedElements.Count == 0) return false;

                // 全部加载完成，替换当前画布内容。
                // 必须标记为 CodeInput：进入白板/换页时模式切换过渡窗口已开启，
                // 若不标记，恢复的笔迹会被 StrokesOnStrokesChanged 误判为“残留用户笔迹”而拦截删除（白板笔迹消失）。
                _currentCommitType = CommitReason.CodeInput;
                inkCanvas.Strokes.Clear();
                inkCanvas.Children.Clear();
                currentPhotoImage = null;
                currentCameraImage = null;

                if (loadedStrokes != null)
                {
                    inkCanvas.Strokes.Add(loadedStrokes);
                }
                foreach (var el in loadedElements)
                {
                    // 恢复 currentPhotoImage 引用，确保后续翻页/操作能正确识别画板上的照片
                    if (el is System.Windows.Controls.Image img && img.Name != null && img.Name.StartsWith("photo_"))
                    {
                        currentPhotoImage = img;
                    }
                    inkCanvas.Children.Add(el);
                }
                _currentCommitType = CommitReason.UserInput;

                // 文档页恢复后，重进白板时画布宽度可能与插入时不同，
                // 沿用 XAML 中保存的 Left 会导致瓦片水平偏移。按当前画布宽度
                // 重新计算文档瓦片的水平居中位置，垂直方向按顺序累加堆叠。
                RecenterDocumentTilesOnCanvas(sourceFilePath);

                // 该页使用文件作为真相，清空内存历史避免 RestoreStrokes 重复加载
                TimeMachineHistories[pageIndex] = null;

                LogHelper.WriteLogToFile($"已恢复文档页内容: {baseFilePath}（笔迹 {inkCanvas.Strokes.Count}，元素 {inkCanvas.Children.Count}，元素文件 {Path.GetFileName(elementsFilePath)}）", LogHelper.LogType.Trace);
                return true;
            }
            catch (Exception ex)
            {
                LogHelper.WriteLogToFile($"恢复文档页内容失败: {ex.Message}", LogHelper.LogType.Error);
                return false;
            }
        }

        /// <summary>
        /// 将画布上属于指定文档路径的所有瓦片照片按当前画布宽度重新水平居中，
        /// 并按 child 顺序自顶向下垂直堆叠。重进白板时画布尺寸可能与插入时不同，
        /// 直接使用 XAML 中保存的 Left 会导致水平偏移；这里统一重算。
        /// </summary>
        private void RecenterDocumentTilesOnCanvas(string sourceFilePath)
        {
            try
            {
                if (inkCanvas == null) return;
                string docPath = StripChunkSuffix(sourceFilePath);
                if (string.IsNullOrEmpty(docPath) || !IsDocumentFilePath(docPath)) return;

                // 按 child 顺序收集所有属于该文档的瓦片 Image（保持原垂直顺序）
                var tiles = new List<System.Windows.Controls.Image>();
                foreach (var child in inkCanvas.Children)
                {
                    if (child is System.Windows.Controls.Image img &&
                        img.Tag is string tag &&
                        !string.IsNullOrEmpty(tag) &&
                        string.Equals(StripChunkSuffix(tag), docPath, StringComparison.OrdinalIgnoreCase))
                    {
                        tiles.Add(img);
                    }
                }
                if (tiles.Count == 0) return;

                // 水平居中以最宽瓦片为基准，垂直方向按当前 child 顺序累加高度
                double maxW = 0;
                foreach (var t in tiles)
                {
                    double w = t.Width > 0 ? t.Width : (t.Source as BitmapSource)?.PixelWidth ?? 0;
                    if (w > maxW) maxW = w;
                }
                if (maxW <= 0) return;

                double canvasWidth = inkCanvas.ActualWidth;
                if (canvasWidth <= 0)
                {
                    // 进入白板时画布可能尚未完成布局，ActualWidth 为 0。
                    // 等待一次 LayoutUpdated 后再重算，避免用主屏宽度误算导致水平偏移。
                    EventHandler layoutHandler = null;
                    layoutHandler = (s, e) =>
                    {
                        if (inkCanvas.ActualWidth <= 0) return;
                        inkCanvas.LayoutUpdated -= layoutHandler;
                        RecenterDocumentTilesOnCanvas(sourceFilePath);
                    };
                    inkCanvas.LayoutUpdated += layoutHandler;
                    return;
                }
                double left = Math.Round(Math.Max(0, (canvasWidth - maxW) / 2.0));
                double top = 0;
                foreach (var t in tiles)
                {
                    double h = t.Height > 0 ? t.Height : (t.Source as BitmapSource)?.PixelHeight ?? 0;
                    InkCanvas.SetLeft(t, left);
                    InkCanvas.SetTop(t, Math.Round(top));
                    top += h;
                }
            }
            catch (Exception ex)
            {
                LogHelper.WriteLogToFile($"重定位文档瓦片位置失败: {ex.Message}", LogHelper.LogType.Warning);
            }
        }

        /// <summary>
        /// 修正从文件加载的 Image 源路径。
        /// 若指定了 baseFolder，则优先在该文件夹及其 File Dependency 子文件夹中查找。
        /// </summary>
        private void FixLoadedImageSource(System.Windows.Controls.Image image, string baseFolder = null)
        {
            try
            {
                if (!(image.Source is BitmapImage bmi)) return;

                var uri = bmi.UriSource;
                bool needFix = uri == null || (uri != null && !File.Exists(uri.LocalPath));
                if (!needFix) return;

                string candidate = null;
                string tagPath = image.Tag as string;
                if (!string.IsNullOrEmpty(tagPath))
                {
                    if (!string.IsNullOrEmpty(baseFolder))
                    {
                        candidate = System.IO.Path.Combine(baseFolder, tagPath.Replace('/', '\\'));
                        if (!File.Exists(candidate))
                        {
                            candidate = System.IO.Path.Combine(baseFolder, "File Dependency", System.IO.Path.GetFileName(tagPath));
                        }
                    }
                    if (string.IsNullOrEmpty(candidate) || !File.Exists(candidate))
                    {
                        candidate = System.IO.Path.Combine(Settings.Automation.AutoSavedStrokesLocation, tagPath.Replace('/', '\\'));
                        if (!File.Exists(candidate))
                        {
                            candidate = System.IO.Path.Combine(Settings.Automation.AutoSavedStrokesLocation, "File Dependency", System.IO.Path.GetFileName(tagPath));
                        }
                    }
                }

                if ((candidate == null || !File.Exists(candidate)) && uri != null)
                {
                    string fname = System.IO.Path.GetFileName(uri.LocalPath);
                    if (!string.IsNullOrEmpty(fname))
                    {
                        if (!string.IsNullOrEmpty(baseFolder))
                        {
                            var fd = System.IO.Path.Combine(baseFolder, "File Dependency");
                            var tryPath = System.IO.Path.Combine(fd, fname);
                            if (File.Exists(tryPath)) candidate = tryPath;
                        }
                        if (candidate == null || !File.Exists(candidate))
                        {
                            var fd = System.IO.Path.Combine(Settings.Automation.AutoSavedStrokesLocation, "File Dependency");
                            var tryPath = System.IO.Path.Combine(fd, fname);
                            if (File.Exists(tryPath)) candidate = tryPath;
                        }
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
            catch { }
        }

        /// <summary>
        /// 获取指定文档来源路径对应的保存文件夹。
        /// 文件夹结构：AutoSavedStrokesLocation\Auto Saved - Documents\{safeFileName}
        /// </summary>
        private string GetDocumentPageFolderPath(string sourceFilePath)
        {
            if (string.IsNullOrEmpty(sourceFilePath)) return null;
            string safeFileName = SanitizePathSegment(System.IO.Path.GetFileNameWithoutExtension(sourceFilePath));
            if (string.IsNullOrEmpty(safeFileName)) safeFileName = "Untitled";
            return System.IO.Path.Combine(
                Settings.Automation.AutoSavedStrokesLocation,
                "Auto Saved - Documents",
                safeFileName);
        }

        /// <summary>文档照片本地缓存的状态</summary>
        private enum DocumentPhotoCacheState
        {
            /// <summary>本地没有转换缓存（或缓存文件不完整）</summary>
            Missing,
            /// <summary>缓存有效：同名文档且文件未被修改</summary>
            Valid,
            /// <summary>同名同路径文档，但文件内容已被修改，需要重新转换并替换原照片（保留笔迹）</summary>
            ModifiedSameDocument,
            /// <summary>同名但来源路径不同的另一份文档，需要重新转换并清除旧笔迹</summary>
            DifferentDocument
        }

        /// <summary>
        /// 打开文档（带本地转换缓存检查）：
        /// 1. 照片列表中已有该文档照片 → 直接返回；
        /// 2. 磁盘缓存有效（同名且未修改）→ 从缓存文件夹加载照片到照片列表；
        /// 3. 未转换过 / 同名但已修改 → 触发插件自动转换，完成后自动存储缓存并插入照片列表。
        /// </summary>
        public void OpenDocumentWithPhotoCache(string filePath)
        {
            try
            {
                if (string.IsNullOrEmpty(filePath) || !File.Exists(filePath)) return;

                // 1. 内存照片列表中已有该文档照片，无需重复加载/转换
                //    （分块照片来源路径带 "#块序号"，需剥离后按文档识别）
                if (capturedPhotos.Any(p =>
                    !string.IsNullOrEmpty(p.SourceFilePath) &&
                    string.Equals(StripChunkSuffix(p.SourceFilePath), filePath, StringComparison.OrdinalIgnoreCase)))
                {
                    ShowNotificationAsync("该文档照片已在照片列表中", true);
                    return;
                }

                var cacheState = GetDocumentPhotoCacheState(filePath);
                if (cacheState == DocumentPhotoCacheState.Valid)
                {
                    // 2. 已转换过且未修改：直接从缓存文件夹加载到照片列表
                    if (TryLoadDocumentPhotoFromCache(filePath))
                    {
                        // 让插件继续监视该文档的外部修改（不触发转换）
                        try { Plugins.PluginHost.Instance?.TriggerRoute("document-watch", filePath); } catch { }
                        ShowNotificationAsync("已从本地缓存加载该文档照片", true);
                        return;
                    }
                }
                else if (cacheState == DocumentPhotoCacheState.DifferentDocument)
                {
                    // 同名但路径不同的另一份文档：清除旧文档保存的笔迹/元素，避免串用
                    ClearDocumentPageSavedFiles(filePath);
                }
                else if (cacheState == DocumentPhotoCacheState.ModifiedSameDocument)
                {
                    // 同名文档已修改：移除内存中该文档的旧照片并清理旧分块缓存，
                    // 确保重新转换后照片列表与磁盘缓存一致（保留笔迹，笔迹由文档页保存文件管理）
                    RemoveDocumentPhotosFromList(filePath);
                    ClearDocumentChunkCache(filePath);
                }

                // 3. 未转换 / 已修改（ModifiedSameDocument 会重新转换并替换原照片，保留笔迹）
                var host = Plugins.PluginHost.Instance;
                if (host == null || !host.IsRouteAvailable("document-open"))
                {
                    MessageBox.Show("未安装文档查看器插件，请前往插件工坊安装。", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }
                host.TriggerRoute("document-open", filePath);
            }
            catch (Exception ex)
            {
                LogHelper.WriteLogToFile($"打开文档（缓存检查）失败: {ex.Message}", LogHelper.LogType.Error);
            }
        }

        /// <summary>文档转换照片的缓存文件路径（存放在对应文档的保存文件夹内）</summary>
        private string GetDocumentPhotoCacheFilePath(string sourceFilePath)
        {
            string folder = GetDocumentPageFolderPath(sourceFilePath);
            return string.IsNullOrEmpty(folder) ? null : System.IO.Path.Combine(folder, "photo.png");
        }

        /// <summary>文档某分块瓦片的缓存文件路径（chunk_{N}.png，N 为块序号）</summary>
        private string GetDocumentChunkCacheFilePath(string sourceFilePath)
        {
            // 传入的是带 "#块序号" 的来源路径
            if (string.IsNullOrEmpty(sourceFilePath)) return null;
            int chunkIndex = GetChunkIndex(sourceFilePath);
            string folder = GetDocumentPageFolderPath(StripChunkSuffix(sourceFilePath));
            return string.IsNullOrEmpty(folder) ? null : System.IO.Path.Combine(folder, $"chunk_{chunkIndex}.png");
        }

        /// <summary>文档转换缓存的元信息文件路径（记录来源路径、最后修改时间、文件长度）</summary>
        private string GetDocumentPhotoMetaFilePath(string sourceFilePath)
        {
            string folder = GetDocumentPageFolderPath(sourceFilePath);
            return string.IsNullOrEmpty(folder) ? null : System.IO.Path.Combine(folder, "photo.meta");
        }

        /// <summary>
        /// 判断文档照片的本地缓存状态：不存在 / 有效 / 同名文档已修改 / 同名不同文档。
        /// 支持两类缓存：单块文档使用 photo.png；多块文档使用 chunk_0.png / chunk_1.png ...。
        /// </summary>
        private DocumentPhotoCacheState GetDocumentPhotoCacheState(string sourceFilePath)
        {
            try
            {
                string metaPath = GetDocumentPhotoMetaFilePath(sourceFilePath);
                if (string.IsNullOrEmpty(metaPath) || !File.Exists(metaPath))
                {
                    return DocumentPhotoCacheState.Missing;
                }

                // 判断是单块（photo.png）还是多块（chunk_*.png）缓存
                string photoPath = GetDocumentPhotoCacheFilePath(sourceFilePath);
                bool hasSinglePhoto = !string.IsNullOrEmpty(photoPath) && File.Exists(photoPath);
                string folder = GetDocumentPageFolderPath(sourceFilePath);
                var chunkFiles = (hasSinglePhoto == false && !string.IsNullOrEmpty(folder) && Directory.Exists(folder))
                    ? Directory.GetFiles(folder, "chunk_*.png")
                    : new string[0];

                if (!hasSinglePhoto && chunkFiles.Length == 0)
                {
                    return DocumentPhotoCacheState.Missing;
                }

                var lines = File.ReadAllLines(metaPath);
                if (lines.Length < 3) return DocumentPhotoCacheState.Missing;

                // 同名但来源路径不同：视为另一份文档
                string savedSource = lines[0].Trim();
                if (!string.Equals(savedSource, sourceFilePath, StringComparison.OrdinalIgnoreCase))
                {
                    return DocumentPhotoCacheState.DifferentDocument;
                }

                // 同名同路径：对比最后修改时间与文件长度，判断文档是否被修改过
                var fi = new FileInfo(sourceFilePath);
                long savedTicks = long.TryParse(lines[1].Trim(), out long t) ? t : 0;
                long savedLength = long.TryParse(lines[2].Trim(), out long l) ? l : -1;
                if (savedTicks == fi.LastWriteTimeUtc.Ticks && savedLength == fi.Length)
                {
                    return DocumentPhotoCacheState.Valid;
                }
                return DocumentPhotoCacheState.ModifiedSameDocument;
            }
            catch
            {
                return DocumentPhotoCacheState.Missing;
            }
        }

        /// <summary>从本地缓存文件夹加载文档照片到照片列表（不重新转换）。支持单块（photo.png）与多块（chunk_*.png）缓存。</summary>
        private bool TryLoadDocumentPhotoFromCache(string sourceFilePath)
        {
            try
            {
                string photoPath = GetDocumentPhotoCacheFilePath(sourceFilePath);
                if (!string.IsNullOrEmpty(photoPath) && File.Exists(photoPath))
                {
                    var bi = new BitmapImage();
                    bi.BeginInit();
                    bi.UriSource = new Uri(photoPath, UriKind.Absolute);
                    bi.CacheOption = BitmapCacheOption.OnLoad;
                    bi.EndInit();
                    bi.Freeze();

                    var ci = new CapturedImage(bi, photoPath, sourceFilePath);
                    capturedPhotos.Insert(0, ci);
                    // 侧栏仅显示文档图标+文件名，全分辨率位图无需常驻，立即释放以节省内存
                    // （之后插入画板时由 CreateBitmapImageFromFileOrMemory 从缓存文件按需重新加载）
                    if (!ci.IsVideo) ci.ReleaseImageMemory();
                    UpdateCapturedPhotosDisplay();
                    Console.WriteLine($"已从缓存加载文档照片: {sourceFilePath}");
                    return true;
                }

                // 多块文档：加载所有 chunk_*.png，按块序号升序加入照片列表
                string folder = GetDocumentPageFolderPath(sourceFilePath);
                if (string.IsNullOrEmpty(folder) || !Directory.Exists(folder)) return false;

                var chunkPaths = Directory.GetFiles(folder, "chunk_*.png")
                    .OrderBy(f => GetChunkIndexFromFileName(Path.GetFileName(f)))
                    .ToList();
                if (chunkPaths.Count == 0) return false;

                foreach (var chunkPath in chunkPaths)
                {
                    int chunkIndex = GetChunkIndexFromFileName(Path.GetFileName(chunkPath));
                    string chunkSource = $"{sourceFilePath}#{chunkIndex}";
                    var bi = new BitmapImage();
                    bi.BeginInit();
                    bi.UriSource = new Uri(chunkPath, UriKind.Absolute);
                    bi.CacheOption = BitmapCacheOption.OnLoad;
                    bi.EndInit();
                    bi.Freeze();

                    var ci = new CapturedImage(bi, chunkPath, chunkSource);
                    capturedPhotos.Insert(0, ci);
                    // 侧栏仅显示文档图标+文件名，全分辨率位图无需常驻，立即释放以节省内存
                    if (!ci.IsVideo) ci.ReleaseImageMemory();
                    Console.WriteLine($"已从缓存加载文档分块照片: {chunkSource}");
                }

                UpdateCapturedPhotosDisplay();
                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"从缓存加载文档照片失败: {ex.Message}");
                return false;
            }
        }

        /// <summary>从缓存文件名（chunk_{N}.png）解析块序号 N。</summary>
        private static int GetChunkIndexFromFileName(string fileName)
        {
            if (string.IsNullOrEmpty(fileName)) return 0;
            string name = Path.GetFileNameWithoutExtension(fileName);
            if (name.StartsWith("chunk_") && int.TryParse(name.Substring("chunk_".Length), out int idx))
                return idx;
            return 0;
        }

        /// <summary>
        /// 将文档转换的照片写入本地缓存（photo.png，重名则替换原照片），
        /// 并写入 photo.meta（来源路径 + 最后修改时间 + 文件长度）用于同名修改检测。
        /// 缓存文件夹与该文档笔迹保存文件夹相同。
        /// </summary>
        private string SaveDocumentPhotoToCache(string sourceFilePath, BitmapImage image)
        {
            try
            {
                if (string.IsNullOrEmpty(sourceFilePath) || image == null) return null;
                string folderPath = GetDocumentPageFolderPath(sourceFilePath);
                if (string.IsNullOrEmpty(folderPath)) return null;
                if (!Directory.Exists(folderPath)) Directory.CreateDirectory(folderPath);

                string photoPath = GetDocumentPhotoCacheFilePath(sourceFilePath);
                var encoder = new PngBitmapEncoder();
                encoder.Frames.Add(BitmapFrame.Create(image));
                using (var fs = new FileStream(photoPath, FileMode.Create, FileAccess.Write))
                {
                    encoder.Save(fs);
                }

                // 元信息：用于下次打开时判断同名文档是否已被修改
                try
                {
                    var fi = new FileInfo(sourceFilePath);
                    File.WriteAllLines(GetDocumentPhotoMetaFilePath(sourceFilePath), new[]
                    {
                        sourceFilePath,
                        fi.LastWriteTimeUtc.Ticks.ToString(),
                        fi.Length.ToString()
                    });
                }
                catch { }

                return photoPath;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"保存文档照片缓存失败: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// 将文档分块瓦片写入本地缓存（chunk_{N}.png，N 为块序号），
        /// 并写入 photo.meta（来源路径 + 最后修改时间 + 文件长度）用于同名修改检测。
        /// 使多块文档下次打开时可直接从硬盘加载，仅当文档被修改时才重新转换。
        /// </summary>
        private string SaveDocumentChunkToCache(string chunkSourceFilePath, BitmapImage image)
        {
            try
            {
                if (string.IsNullOrEmpty(chunkSourceFilePath) || image == null) return null;
                string docPath = StripChunkSuffix(chunkSourceFilePath);
                string chunkPath = GetDocumentChunkCacheFilePath(chunkSourceFilePath);
                if (string.IsNullOrEmpty(chunkPath)) return null;

                string folderPath = GetDocumentPageFolderPath(docPath);
                if (string.IsNullOrEmpty(folderPath)) return null;
                if (!Directory.Exists(folderPath)) Directory.CreateDirectory(folderPath);

                var encoder = new PngBitmapEncoder();
                encoder.Frames.Add(BitmapFrame.Create(image));
                using (var fs = new FileStream(chunkPath, FileMode.Create, FileAccess.Write))
                {
                    encoder.Save(fs);
                }

                // 元信息：用于下次打开时判断同名文档是否已被修改
                try
                {
                    var fi = new FileInfo(docPath);
                    File.WriteAllLines(GetDocumentPhotoMetaFilePath(docPath), new[]
                    {
                        docPath,
                        fi.LastWriteTimeUtc.Ticks.ToString(),
                        fi.Length.ToString()
                    });
                }
                catch { }

                Console.WriteLine($"已保存文档分块缓存: {chunkPath}");
                return chunkPath;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"保存文档分块缓存失败: {ex.Message}");
                return null;
            }
        }

        /// <summary>清除指定文档文件夹下已保存的笔迹/元素文件（同名不同文档时使用，避免串用旧笔迹）</summary>
        private void ClearDocumentPageSavedFiles(string sourceFilePath)
        {
            try
            {
                string folderPath = GetDocumentPageFolderPath(sourceFilePath);
                if (string.IsNullOrEmpty(folderPath) || !Directory.Exists(folderPath)) return;
                foreach (var f in Directory.GetFiles(folderPath, "*.icstk")) File.Delete(f);
                foreach (var f in Directory.GetFiles(folderPath, "*.xaml")) File.Delete(f);
            }
            catch { }
        }

        /// <summary>从照片列表中移除指定文档的所有照片（含分块瓦片），用于文档被修改后重新转换前清理旧照片。</summary>
        private void RemoveDocumentPhotosFromList(string sourceFilePath)
        {
            try
            {
                if (string.IsNullOrEmpty(sourceFilePath)) return;
                var toRemove = capturedPhotos
                    .Where(p => !string.IsNullOrEmpty(p.SourceFilePath) &&
                                string.Equals(StripChunkSuffix(p.SourceFilePath), sourceFilePath, StringComparison.OrdinalIgnoreCase))
                    .ToList();
                foreach (var photo in toRemove)
                {
                    capturedPhotos.Remove(photo);
                    // 若移除的是当前选中的照片，清理选中状态
                    if (!string.IsNullOrEmpty(selectedPhotoTimestamp) && selectedPhotoTimestamp == photo.Timestamp)
                    {
                        selectedPhotoTimestamp = null;
                    }
                }
                if (toRemove.Count > 0)
                {
                    UpdateCapturedPhotosDisplay();
                    Console.WriteLine($"已从照片列表移除文档旧照片: {sourceFilePath}（{toRemove.Count} 张）");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"移除文档旧照片失败: {ex.Message}");
            }
        }

        /// <summary>清除指定文档文件夹下的旧分块缓存（chunk_*.png），用于文档被修改后重新转换前清理旧缓存。</summary>
        private void ClearDocumentChunkCache(string sourceFilePath)
        {
            try
            {
                string folderPath = GetDocumentPageFolderPath(sourceFilePath);
                if (string.IsNullOrEmpty(folderPath) || !Directory.Exists(folderPath)) return;
                foreach (var f in Directory.GetFiles(folderPath, "chunk_*.png")) File.Delete(f);
                // 单块缓存 photo.png 也一并清理，避免与新生成的分块缓存混用
                string photoPath = GetDocumentPhotoCacheFilePath(sourceFilePath);
                if (!string.IsNullOrEmpty(photoPath) && File.Exists(photoPath)) File.Delete(photoPath);
            }
            catch { }
        }

        /// <summary>
        /// 文档照片的插入适配：
        /// 无论如何都不调整缩放比例，保持照片原始像素尺寸插入，保证文字清晰不糊。
        /// 水平居中，垂直顶部对齐（超长/超宽时可拖动画面阅读全文）。
        /// 直接设置元素宽高与位置（不使用 RenderTransform），保证保存/恢复后几何一致。
        /// </summary>
        private void CenterAndScaleDocumentPhoto(FrameworkElement element)
        {
            double canvasWidth = inkCanvas.ActualWidth;
            if (canvasWidth <= 0) canvasWidth = SystemParameters.PrimaryScreenWidth;

            // 保持原始尺寸，不缩放，保证清晰。
            // 坐标取整到像素，避免子像素对齐导致图片被 WPF 隐式反锯齿而模糊。
            double left = Math.Round((canvasWidth - element.Width) / 2);
            double top = 0;
            InkCanvas.SetLeft(element, Math.Max(0, left));
            InkCanvas.SetTop(element, Math.Max(0, top));

            // 确保元素宽高为整数像素值
            if (element.Width > 0) element.Width = Math.Round(element.Width);
            if (element.Height > 0) element.Height = Math.Round(element.Height);
        }

        /// <summary>检查图片元素是否具有可显示的有效图像源</summary>
        private bool HasValidImageSource(System.Windows.Controls.Image image)
        {
            try
            {
                if (image?.Source is BitmapImage bi)
                {
                    if (bi.UriSource != null) return File.Exists(bi.UriSource.LocalPath);
                    return bi.PixelWidth > 0;
                }
                if (image?.Source is BitmapSource bs) return bs.PixelWidth > 0;
                return false;
            }
            catch { return false; }
        }

        /// <summary>图片源失效时，强制使用文档缓存的 photo.png 作为图像源</summary>
        private void TrySetImageSourceFromDocumentCache(System.Windows.Controls.Image image, string sourceFilePath)
        {
            try
            {
                string cachePath = GetDocumentPhotoCacheFilePath(sourceFilePath);
                if (string.IsNullOrEmpty(cachePath) || !File.Exists(cachePath)) return;
                var bi = new BitmapImage();
                bi.BeginInit();
                bi.UriSource = new Uri(cachePath, UriKind.Absolute);
                bi.CacheOption = BitmapCacheOption.OnLoad;
                bi.EndInit();
                bi.Freeze();
                image.Source = bi;
            }
            catch { }
        }

        /// <summary>
        /// 从文档缓存的 photo.png 重建居中的照片元素。
        /// 用于恢复内容中缺失照片元素（保存文件损坏/为空）时兜底，保证文档页一定能显示照片。
        /// </summary>
        private System.Windows.Controls.Image CreateDocumentPhotoImageElement(string sourceFilePath)
        {
            try
            {
                string cachePath = GetDocumentPhotoCacheFilePath(sourceFilePath);
                if (string.IsNullOrEmpty(cachePath) || !File.Exists(cachePath)) return null;
                var bi = new BitmapImage();
                bi.BeginInit();
                bi.UriSource = new Uri(cachePath, UriKind.Absolute);
                bi.CacheOption = BitmapCacheOption.OnLoad;
                bi.EndInit();
                bi.Freeze();

                var imageElement = new System.Windows.Controls.Image
                {
                    Source = bi,
                    Width = bi.PixelWidth,
                    Height = bi.PixelHeight,
                    Name = GeneratePhotoName(),
                    Tag = sourceFilePath,
                    Stretch = Stretch.Uniform,
                    SnapsToDevicePixels = true,
                    UseLayoutRounding = true
                };
                System.Windows.Media.RenderOptions.SetBitmapScalingMode(imageElement, System.Windows.Media.BitmapScalingMode.HighQuality);
                CenterAndScaleDocumentPhoto(imageElement);
                return imageElement;
            }
            catch { return null; }
        }

        private void UpdateCapturedPhotosDisplay()
        {
            try
            {
                object capturedPhotosStackPanelObject = CapturedPhotosStackPanel;
                if (!(capturedPhotosStackPanelObject is StackPanel capturedPhotosStackPanel)) return;

                capturedPhotosStackPanel.Children.Clear();

                // 已展示过的文档路径集合：同一文档只显示一个条目（文档名 + 文件类型图标），
                // 其余分块瓦片不再单独占位，点击该条目时自动插入该文档的全部图片。
                var shownDocuments = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                foreach (var photo in capturedPhotos)
                {
                    // 文档照片：归并为单一文档条目，列表只显示文档名与文件类型图标
                    if (IsDocumentPhoto(photo))
                    {
                        string docPath = StripChunkSuffix(photo.SourceFilePath);
                        if (string.IsNullOrEmpty(docPath) || shownDocuments.Contains(docPath)) continue;
                        shownDocuments.Add(docPath);

                        var docButton = CreateDocumentPhotoButton(docPath);
                        if (docButton != null)
                        {
                            capturedPhotosStackPanel.Children.Add(docButton);
                        }
                        continue;
                    }

                    var photoButton = CreatePhotoButton(photo);
                    capturedPhotosStackPanel.Children.Add(photoButton);
                }

                // 根据照片数量控制清除按钮的可见性
                if (BtnClearAllContent != null)
                {
                    BtnClearAllContent.Visibility = capturedPhotos.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"更新照片显示失败: {ex.Message}");
            }
        }

        // 仅同步侧栏照片的选中显示（不插入/删除画面），用于翻页或其他外部触发
        private void UpdatePhotoSelectionIndicators()
        {
            try
            {
                int currentPage = GetCurrentPageIndex();
                string timestampOnThisPage = null;

                // 查找当前页关联的照片时间戳
                foreach (var kvp in photoPageMapping)
                {
                    if (kvp.Value == currentPage)
                    {
                        timestampOnThisPage = kvp.Key;
                        break; // 每页最多一张照片
                    }
                }

                if (!string.IsNullOrEmpty(timestampOnThisPage) && capturedPhotos.Any(p => p.Timestamp == timestampOnThisPage))
                {
                    selectedPhotoTimestamp = timestampOnThisPage;
                }
                else
                {
                    selectedPhotoTimestamp = null; // 当前页没有关联照片或相册中不存在该照片
                }

                // 刷新侧栏按钮样式
                UpdateCapturedPhotosDisplay();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"同步照片选中显示失败: {ex.Message}");
            }
        }

        /// <summary>
        /// 为文档照片创建侧栏条目：只显示文档名称与文件类型图标（不逐张显示分块瓦片）。
        /// 点击该条目时自动将该文档的全部图片插入画板。
        /// </summary>
        private Button CreateDocumentPhotoButton(string docPath)
        {
            try
            {
                var tiles = GetDocumentTiles(docPath);
                if (tiles.Count == 0) return null;

                // 以第一张瓦片作为代表（时间戳/缩略图复用现有照片流程）
                var firstTile = tiles[0];
                bool isSelected = selectedPhotoTimestamp != null && selectedPhotoTimestamp == firstTile.Timestamp;

                string ext = System.IO.Path.GetExtension(docPath).ToLowerInvariant();
                string extLabel = ext.TrimStart('.').ToUpperInvariant();
                string docName = System.IO.Path.GetFileName(docPath);
                if (docName.Length > 24) docName = docName.Substring(0, 21) + "...";

                // 文件类型图标色块
                var iconColor = System.Windows.Media.Color.FromRgb(0x2E, 0x7D, 0x32); // 默认绿色
                if (ext == ".docx" || ext == ".doc") iconColor = System.Windows.Media.Color.FromRgb(0x2B, 0x57, 0xA1); // Word 蓝
                else if (ext == ".xls" || ext == ".xlsx") iconColor = System.Windows.Media.Color.FromRgb(0x1E, 0x7B, 0x3C); // Excel 绿
                else if (ext == ".pdf") iconColor = System.Windows.Media.Color.FromRgb(0xC6, 0x2A, 0x2A); // PDF 红

                var iconBorder = new Border
                {
                    Width = 60,
                    Height = 70,
                    CornerRadius = new CornerRadius(6),
                    Background = new SolidColorBrush(iconColor),
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                    Child = new TextBlock
                    {
                        Text = extLabel,
                        Foreground = System.Windows.Media.Brushes.White,
                        FontSize = 20,
                        FontWeight = FontWeights.Bold,
                        HorizontalAlignment = HorizontalAlignment.Center,
                        VerticalAlignment = VerticalAlignment.Center
                    }
                };

                var nameText = new TextBlock
                {
                    Text = docName,
                    FontSize = 13,
                    FontWeight = FontWeights.SemiBold,
                    Foreground = new SolidColorBrush(System.Windows.Media.Color.FromArgb(0xE0, 0xE0, 0xE0, 0xE0)),
                    TextTrimming = TextTrimming.CharacterEllipsis,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(0, 6, 0, 0)
                };

                var contentStack = new StackPanel
                {
                    Orientation = Orientation.Vertical,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                };
                contentStack.Children.Add(iconBorder);
                contentStack.Children.Add(nameText);

                var defaultBorderBrush = new SolidColorBrush(System.Windows.Media.Color.FromArgb(0x80, 0x80, 0x80, 0x80));
                var button = new Button
                {
                    Width = 230,
                    Height = 160,
                    Margin = new Thickness(3),
                    Background = System.Windows.Media.Brushes.Transparent,
                    BorderThickness = isSelected ? new Thickness(4) : new Thickness(1),
                    BorderBrush = isSelected ? System.Windows.Media.Brushes.SkyBlue : defaultBorderBrush,
                    Content = contentStack,
                    Tag = docPath
                };

                // 点击：自动插入该文档全部图片（分块瓦片按坐标拼接）
                button.Click += (s, e) =>
                {
                    selectedPhotoTimestamp = firstTile.Timestamp;
                    UpdateCapturedPhotosDisplay();

                    if (photoPageMapping.ContainsKey(firstTile.Timestamp))
                    {
                        int targetPage = photoPageMapping[firstTile.Timestamp];
                        Console.WriteLine($"文档 {docPath} 已存在于页码 {targetPage}，正在跳转...");
                        SwitchToPage(targetPage);
                    }
                    else
                    {
                        Console.WriteLine($"插入文档全部图片: {docPath}（{tiles.Count} 块）");
                        SwitchToNextBoardAndInsertPhoto(firstTile);
                    }
                };

                return button;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"创建文档照片条目失败: {ex.Message}");
                return null;
            }
        }

        private Button CreatePhotoButton(CapturedImage photo)
        {
            bool isSelected = selectedPhotoTimestamp != null && selectedPhotoTimestamp == photo.Timestamp;

            var defaultBorderBrush = new SolidColorBrush(System.Windows.Media.Color.FromArgb(0x80, 0x80, 0x80, 0x80));
            var image = new System.Windows.Controls.Image
            {
                Source = photo.Thumbnail,
                Stretch = Stretch.Uniform,
                Width = 220,
                Height = 140,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };

            // 提升缩放质量，减少缩略图缩放时的模糊
            System.Windows.Media.RenderOptions.SetBitmapScalingMode(image, System.Windows.Media.BitmapScalingMode.HighQuality);

            // 选中叠加层：居中显示天蓝色☑
            var checkOverlay = new TextBlock
            {
                Text = "☑",
                Foreground = System.Windows.Media.Brushes.SkyBlue,
                FontSize = 30,
                FontWeight = FontWeights.Bold,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Visibility = isSelected ? Visibility.Visible : Visibility.Collapsed
            };

            var contentGrid = new Grid();

            // 视频条目：右上角添加视频标记徽章
            if (photo.IsVideo)
            {
                var badge = new Border
                {
                    HorizontalAlignment = HorizontalAlignment.Right,
                    VerticalAlignment = VerticalAlignment.Top,
                    Margin = new Thickness(0, 5, 5, 0),
                    Padding = new Thickness(5, 2, 5, 2),
                    CornerRadius = new CornerRadius(4),
                    Background = new SolidColorBrush(System.Windows.Media.Color.FromArgb(0xCC, 0xE8, 0x3E, 0x3E))
                };
                var badgeContent = new StackPanel { Orientation = Orientation.Horizontal };
                badgeContent.Children.Add(new TextBlock
                {
                    Text = "\uE714",
                    FontFamily = (System.Windows.Media.FontFamily)Application.Current.TryFindResource("FluentIconFontFamily"),
                    FontSize = 10,
                    Foreground = System.Windows.Media.Brushes.White,
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(0, 0, 2, 0)
                });
                badgeContent.Children.Add(new TextBlock
                {
                    Text = "视频",
                    FontSize = 10,
                    FontWeight = FontWeights.SemiBold,
                    Foreground = System.Windows.Media.Brushes.White,
                    VerticalAlignment = VerticalAlignment.Center
                });
                badge.Child = badgeContent;
                contentGrid.Children.Add(badge);
            }

            contentGrid.Children.Add(image);
            contentGrid.Children.Add(checkOverlay);

            // 操作覆盖层（排序 + 删除），初始隐藏
            // 3 秒自动隐藏计时器
            var autoHideTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
            bool overlayShown = false;
            Grid actionOverlay = null;
            Button button = null;

            // === 交互状态 ===
            var longPressTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(350) };
            bool _touchActive = false;       // 触摸序列进行中
            bool _longPressFired = false;    // 长按已触发（显示覆盖层）
            bool _longPressReleased = false; // 长按松手后等待自动隐藏（抑制 Click 事件）
            bool _moved = false;             // 已触发拖拽
            DateTime _lastLongPressTime = DateTime.MinValue; // 上次触发长按覆盖层的时间，用于防抖
            System.Windows.Point _downPos = new System.Windows.Point();
            const int TouchMoveThreshold = 25;

            // 强制释放按钮占用的所有输入捕获（触摸/鼠标/手写笔），避免 suppressed 事件导致输入被卡住
            Action releaseAllCaptures = () =>
            {
                try { button?.ReleaseAllTouchCaptures(); } catch { }
                try { button?.ReleaseMouseCapture(); } catch { }
                try { button?.ReleaseStylusCapture(); } catch { }
            };

            // 显示覆盖层
            Action<bool> showOverlay = (startAutoHide) =>
            {
                HideActivePhotoActionOverlay();
                actionOverlay.Visibility = Visibility.Visible;
                _activePhotoActionOverlay = actionOverlay;
                overlayShown = true;
                autoHideTimer.Stop();
                if (startAutoHide) autoHideTimer.Start();
            };

            // 隐藏覆盖层
            Action hideOverlay = () =>
            {
                actionOverlay.Visibility = Visibility.Collapsed;
                if (_activePhotoActionOverlay == actionOverlay)
                    _activePhotoActionOverlay = null;
                overlayShown = false;
                autoHideTimer.Stop();
                // 重置所有交互状态标志，避免覆盖层被外部隐藏后残留状态导致下一次长按异常
                _touchActive = false;
                _longPressFired = false;
                _longPressReleased = false;
                _moved = false;
                // 释放可能残留的输入捕获，防止输入被卡住
                releaseAllCaptures();
            };

            actionOverlay = CreatePhotoActionOverlay(photo, hideOverlay);
            actionOverlay.Tag = new Action(hideOverlay);
            contentGrid.Children.Add(actionOverlay);

            // 点击覆盖层背景（两个按钮之外）→ 隐藏
            actionOverlay.PreviewMouseLeftButtonDown += (s, e) =>
            {
                if (e.OriginalSource == actionOverlay)
                {
                    hideOverlay();
                    e.Handled = true;
                }
            };

            // 触屏：点击覆盖层背景（两个按钮之外）→ 隐藏
            actionOverlay.PreviewTouchDown += (s, e) =>
            {
                if (e.OriginalSource == actionOverlay)
                {
                    hideOverlay();
                    e.Handled = true;
                }
            };

            button = new Button
            {
                Width = 230,
                Height = 160,
                Margin = new Thickness(3),
                Background = System.Windows.Media.Brushes.Transparent,
                BorderThickness = isSelected ? new Thickness(4) : new Thickness(1),
                BorderBrush = isSelected ? System.Windows.Media.Brushes.SkyBlue : defaultBorderBrush,
                Content = contentGrid,
                Tag = photo.Timestamp
            };

            // 点击动作
            Action performClick = () =>
            {
                if (photo.IsVideo)
                {
                    var host = Plugins.PluginHost.Instance;
                    if (host == null || !host.IsRouteAvailable("video-insert"))
                    {
                        MessageBox.Show("视频控件 plugin 不可用，无法插入视频。请在插件工坊中启用 videocontrols。",
                            "提示", MessageBoxButton.OK, MessageBoxImage.Information);
                        return;
                    }
                    selectedPhotoTimestamp = photo.Timestamp;
                    UpdateCapturedPhotosDisplay();
                    host.TriggerRoute("video-insert", photo.VideoFilePath);
                    Console.WriteLine($"视频已通过路由插入：{photo.VideoFilePath}");
                    return;
                }

                selectedPhotoTimestamp = photo.Timestamp;
                UpdateCapturedPhotosDisplay();

                if (photoPageMapping.ContainsKey(photo.Timestamp))
                {
                    int targetPage = photoPageMapping[photo.Timestamp];
                    Console.WriteLine($"照片 {photo.Timestamp} 已存在于页码 {targetPage}，正在跳转...");
                    SwitchToPage(targetPage);
                    Console.WriteLine($"已跳转到页码 {targetPage}，照片已存在，无需重新插入");
                }
                else
                {
                    Console.WriteLine("直接切换到下一页插入照片");
                    SwitchToNextBoardAndInsertPhoto(photo);
                }
            };

            autoHideTimer.Tick += (ts, te) =>
            {
                autoHideTimer.Stop();
                _longPressReleased = false;
                hideOverlay();
            };

            longPressTimer.Tick += (ts, te) =>
            {
                longPressTimer.Stop();
                if (!_moved && !_longPressFired)
                {
                    // 防抖：避免重复快速触发长按覆盖层
                    if ((DateTime.Now - _lastLongPressTime).TotalMilliseconds < 300) return;
                    _longPressFired = true;
                    _lastLongPressTime = DateTime.Now;
                    showOverlay(false);
                }
            };

            // 右键：立即显示操作覆盖层
            button.PreviewMouseRightButtonDown += (s, e) =>
            {
                showOverlay(true);
                e.Handled = true;
            };

            // === 触屏处理 ===
            // 始终设置 e.Handled = true 以完全抑制触摸到鼠标的提升，避免幽灵鼠标事件
            button.PreviewTouchDown += (s, e) =>
            {
                if (overlayShown)
                {
                    // 本按钮覆盖层已显示：不启动长按检测，让事件继续隧穿到覆盖层内的按钮
                    // （sortBtn/deleteBtn 的 PreviewTouchDown 会处理）
                    return;
                }

                // 隐藏其他按钮的覆盖层
                HideActivePhotoActionOverlay();

                _touchActive = true;
                _longPressFired = false;
                _longPressReleased = false;
                _moved = false;
                _downPos = e.GetTouchPoint(button).Position;
                e.TouchDevice.Capture(button, CaptureMode.SubTree);

                longPressTimer.Stop();
                longPressTimer.Start();
                e.Handled = true;
            };

            button.PreviewTouchMove += (s, e) =>
            {
                if (!_touchActive || _longPressFired) return;

                var currentPos = e.GetTouchPoint(button).Position;
                double dx = Math.Abs(currentPos.X - _downPos.X);
                double dy = Math.Abs(currentPos.Y - _downPos.Y);

                bool outsideSidebar = false;
                try
                {
                    object sidebarObj = VideoPresenterSidebar;
                    if (sidebarObj is FrameworkElement sidebar && sidebar.IsVisible)
                    {
                        var sidebarPos = e.GetTouchPoint(sidebar).Position;
                        if (sidebarPos.X < 0 || sidebarPos.X > sidebar.ActualWidth ||
                            sidebarPos.Y < 0 || sidebarPos.Y > sidebar.ActualHeight)
                        {
                            outsideSidebar = true;
                        }
                    }
                }
                catch { }

                if (!_moved)
                {
                    bool shouldDrag = outsideSidebar || dx > TouchMoveThreshold || dy > TouchMoveThreshold;
                    if (shouldDrag)
                    {
                        _moved = true;
                        longPressTimer.Stop();
                        // 必须先标记事件已处理并释放捕获，再进入拖拽模态循环
                        e.Handled = true;
                        try { e.TouchDevice.Capture(null); } catch { }
                        releaseAllCaptures();
                        if (!photo.IsVideo)
                        {
                            var data = new DataObject("CapturedPhoto", photo);
                            try
                            {
                                DragDrop.DoDragDrop(button, data, DragDropEffects.Copy);
                            }
                            finally
                            {
                                // 拖拽模态循环结束后强制清理状态，避免其他区域触摸手势失效
                                _touchActive = false;
                                releaseAllCaptures();
                                // 某些触屏设备在拖拽后会出现触摸锁死，重新注册触摸窗口以恢复
                                TouchLockFix.ReRegisterTouchWindow(this);
                            }
                        }
                    }
                }
            };

            button.PreviewTouchUp += (s, e) =>
            {
                // 始终抑制触摸到鼠标的提升
                e.Handled = true;

                try { e.TouchDevice.Capture(null); } catch { }
                releaseAllCaptures();
                longPressTimer.Stop();

                if (!_touchActive) return;
                _touchActive = false;

                if (_longPressFired)
                {
                    // 长按松手：保持覆盖层显示，启动 3 秒自动隐藏
                    // 设置独立标志 _longPressReleased 来抑制后续提升的 Click 事件
                    // _longPressFired 可在下一次按下时重置而不影响抑制逻辑
                    _longPressReleased = true;
                    autoHideTimer.Stop();
                    autoHideTimer.Start();
                    // 某些触屏设备在长按时会出现触摸锁死，重新注册触摸窗口以恢复
                    TouchLockFix.ReRegisterTouchWindow(this);
                    return;
                }

                if (_moved)
                {
                    _moved = false;
                    return;
                }

                // 短按：选中照片
                performClick();
            };

            // === 鼠标处理 ===
            button.PreviewMouseLeftButtonDown += (s, e) =>
            {
                if (_touchActive) return;
                if (overlayShown)
                {
                    // 覆盖层已显示：标记以跳过松手时的选中（覆盖层自身会处理隐藏）
                    _moved = true;
                    return;
                }

                // 隐藏其他按钮的覆盖层
                HideActivePhotoActionOverlay();

                _moved = false;
                _longPressFired = false;
                _longPressReleased = false;
                _downPos = e.GetPosition(null);
                longPressTimer.Stop();
                longPressTimer.Start();
            };

            button.PreviewMouseMove += (s, e) =>
            {
                if (_touchActive) return;
                if (e.LeftButton != MouseButtonState.Pressed || _moved) return;

                System.Windows.Point cur = e.GetPosition(null);
                if (Math.Abs(cur.X - _downPos.X) > SystemParameters.MinimumHorizontalDragDistance ||
                    Math.Abs(cur.Y - _downPos.Y) > SystemParameters.MinimumVerticalDragDistance)
                {
                    _moved = true;
                    longPressTimer.Stop();
                    if (!photo.IsVideo)
                    {
                        var data = new DataObject("CapturedPhoto", photo);
                        DragDrop.DoDragDrop(button, data, DragDropEffects.Copy);
                    }
                }
            };

            button.PreviewMouseLeftButtonUp += (s, e) =>
            {
                if (_touchActive) return;
                longPressTimer.Stop();
                // 抑制 Button.Click，由本方法直接处理选中逻辑
                e.Handled = true;

                if (_longPressFired)
                {
                    // 长按松手：保持覆盖层显示，启动 3 秒自动隐藏
                    _longPressReleased = true;
                    autoHideTimer.Stop();
                    autoHideTimer.Start();
                    return;
                }

                if (_moved)
                {
                    _moved = false;
                    return;
                }

                // 短按：选中照片
                performClick();
            };

            // Button.Click 回退路径：键盘激活（Enter/Space）或未被抑制的提升事件
            button.Click += (sender, e) =>
            {
                // 长按松手后 WPF 可能产生多次提升的 Click 事件（StylusUp + 鼠标提升）
                // _longPressReleased 持续抑制，直到 autoHideTimer 触发 hideOverlay 或下一次按下
                if (_longPressReleased) return;
                if (_longPressFired || _touchActive || _moved) return;
                performClick();
            };

            return button;
        }

        /// <summary>创建照片操作覆盖层（排序 + 删除两个圆形按钮）</summary>
        private Grid CreatePhotoActionOverlay(CapturedImage photo, Action hideOverlay)
        {
            var overlay = new Grid
            {
                Background = new SolidColorBrush(System.Windows.Media.Color.FromArgb(0x99, 0x00, 0x00, 0x00)),
                Visibility = Visibility.Collapsed
            };

            var btnPanel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };

            // 左侧：排序按钮
            var sortBtn = CreateCircularActionButton("\uE8CB", "#4A90D9");
            sortBtn.PreviewMouseLeftButtonDown += (s, e) =>
            {
                hideOverlay();
                StartPhotoReorder(photo, null);
                e.Handled = true;
            };
            sortBtn.PreviewTouchDown += (s, e) =>
            {
                hideOverlay();
                StartPhotoReorder(photo, e.TouchDevice);
                e.Handled = true;
            };
            btnPanel.Children.Add(sortBtn);

            // 间隔
            btnPanel.Children.Add(new Border { Width = 20 });

            // 右侧：删除按钮
            var deleteBtn = CreateCircularActionButton("\uE74D", "#E83E3E");
            deleteBtn.PreviewMouseLeftButtonDown += (s, e) =>
            {
                hideOverlay();
                DeletePhotoFromList(photo);
                e.Handled = true;
            };
            deleteBtn.PreviewTouchDown += (s, e) =>
            {
                hideOverlay();
                DeletePhotoFromList(photo);
                e.Handled = true;
            };
            btnPanel.Children.Add(deleteBtn);

            overlay.Children.Add(btnPanel);
            return overlay;
        }

        /// <summary>创建圆形操作按钮（Border 实现，支持 CornerRadius）</summary>
        private Border CreateCircularActionButton(string glyph, string hexColor)
        {
            var color = (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(hexColor);
            var border = new Border
            {
                Width = 42,
                Height = 42,
                CornerRadius = new CornerRadius(21),
                Background = new SolidColorBrush(color),
                Cursor = Cursors.Hand,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };
            var icon = new TextBlock
            {
                Text = glyph,
                FontFamily = (System.Windows.Media.FontFamily)Application.Current.TryFindResource("FluentIconFontFamily"),
                FontSize = 18,
                Foreground = System.Windows.Media.Brushes.White,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };
            border.Child = icon;
            return border;
        }

        /// <summary>隐藏当前显示的照片操作覆盖层</summary>
        private void HideActivePhotoActionOverlay()
        {
            if (_activePhotoActionOverlay != null)
            {
                if (_activePhotoActionOverlay.Tag is Action hideAction)
                    hideAction();
                else
                    _activePhotoActionOverlay.Visibility = Visibility.Collapsed;
                _activePhotoActionOverlay = null;
            }
        }


        /// <summary>从照片列表中删除指定照片</summary>
        private void DeletePhotoFromList(CapturedImage photo)
        {
            try
            {
                capturedPhotos.Remove(photo);
                photoPageMapping.Remove(photo.Timestamp);
                if (selectedPhotoTimestamp == photo.Timestamp)
                    selectedPhotoTimestamp = null;
                UpdateCapturedPhotosDisplay();
                Console.WriteLine($"已从照片列表删除: {photo.Timestamp}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"删除照片失败: {ex.Message}");
            }
        }

        /// <summary>开始照片手动排序（按下排序按钮后进入拖拽排序模式）</summary>
        private void StartPhotoReorder(CapturedImage photo, TouchDevice touchDevice)
        {
            int idx = capturedPhotos.IndexOf(photo);
            if (idx < 0) return;
            _photoReorderSourceIndex = idx;
            _photoReorderTargetIndex = idx;
            _isReorderingPhotos = true;

            // 先创建蓝色插入指示线（CaptureMouse 会同步派发 PreviewMouseMove，
            // 此时 UpdateReorderIndicatorPosition 需要 _reorderIndicatorLine 已实例化）
            if (_reorderIndicatorLine == null)
            {
                _reorderIndicatorLine = new Border
                {
                    Height = 3,
                    CornerRadius = new CornerRadius(1.5),
                    Background = new SolidColorBrush(System.Windows.Media.Color.FromRgb(0x4A, 0x90, 0xD9)),
                    HorizontalAlignment = HorizontalAlignment.Stretch,
                    Margin = new Thickness(0, 0, 0, 0),
                    IsHitTestVisible = false
                };
            }

            // 初始放置指示线（在捕获鼠标前，避免同步 MouseMove 时状态不一致）
            UpdateReorderIndicatorPosition();

            // 捕获输入设备以跟踪拖拽
            object spObj = CapturedPhotosStackPanel;
            if (spObj is StackPanel sp)
            {
                sp.CaptureMouse();
                if (touchDevice != null)
                {
                    touchDevice.Capture(sp, CaptureMode.SubTree);
                }
            }

            // 启动自动滚动计时器
            if (_reorderAutoScrollTimer == null)
            {
                _reorderAutoScrollTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(50) };
                _reorderAutoScrollTimer.Tick += ReorderAutoScrollTimer_Tick;
            }
            _reorderAutoScrollDirection = 0;
            _reorderAutoScrollTimer.Start();
        }

        /// <summary>自动滚动计时器：在列表顶部/底部边缘时自动滚动</summary>
        private void ReorderAutoScrollTimer_Tick(object sender, EventArgs e)
        {
            if (!_isReorderingPhotos) return;
            object svObj = CapturedPhotosScrollViewer;
            if (!(svObj is ScrollViewer sv)) return;

            if (_reorderAutoScrollDirection < 0)
            {
                sv.ScrollToVerticalOffset(Math.Max(0, sv.VerticalOffset - 15));
            }
            else if (_reorderAutoScrollDirection > 0)
            {
                sv.ScrollToVerticalOffset(sv.VerticalOffset + 15);
            }
        }

        /// <summary>更新蓝色指示线位置</summary>
        private void UpdateReorderIndicatorPosition()
        {
            if (!_isReorderingPhotos) return;
            if (_reorderIndicatorLine == null) return;
            object spObj = CapturedPhotosStackPanel;
            if (!(spObj is StackPanel sp)) return;

            // 移除旧指示线
            if (sp.Children.Contains(_reorderIndicatorLine))
                sp.Children.Remove(_reorderIndicatorLine);

            // _photoReorderTargetIndex 是数据索引（照片在列表中的位置）
            // 转换为视觉索引（在 sp.Children 中的插入位置）
            int dataCount = 0;
            int visualInsertIndex = sp.Children.Count;
            for (int i = 0; i < sp.Children.Count; i++)
            {
                if (dataCount == _photoReorderTargetIndex)
                {
                    visualInsertIndex = i;
                    break;
                }
                // 跳过非照片元素（不应存在），计数照片按钮
                dataCount++;
            }
            if (visualInsertIndex > sp.Children.Count) visualInsertIndex = sp.Children.Count;
            sp.Children.Insert(visualInsertIndex, _reorderIndicatorLine);
        }

        /// <summary>照片列表鼠标移动：手动排序时实时调整蓝线位置 + 自动滚动</summary>
        private void CapturedPhotosStackPanel_PreviewMouseMove(object sender, MouseEventArgs e)
        {
            if (!_isReorderingPhotos || _photoReorderSourceIndex < 0) return;
            object spObj = CapturedPhotosStackPanel;
            if (!(spObj is StackPanel sp)) return;
            object svObj = CapturedPhotosScrollViewer;
            if (!(svObj is ScrollViewer sv)) return;

            System.Windows.Point pos = e.GetPosition(sp);

            // 自动滚动检测：在 ScrollViewer 视口顶部/底部边缘时滚动
            System.Windows.Point svPos = e.GetPosition(sv);
            double edgeThreshold = 50;
            if (svPos.Y < edgeThreshold)
                _reorderAutoScrollDirection = -1;
            else if (svPos.Y > sv.ActualHeight - edgeThreshold)
                _reorderAutoScrollDirection = 1;
            else
                _reorderAutoScrollDirection = 0;

            // 收集所有照片按钮（排除指示线）及其中线位置
            var photoItems = new List<(int visualIndex, double midY)>();
            for (int i = 0; i < sp.Children.Count; i++)
            {
                if (sp.Children[i] == _reorderIndicatorLine) continue;
                if (sp.Children[i] is FrameworkElement child)
                {
                    double top = child.TransformToAncestor(sp).Transform(new System.Windows.Point(0, 0)).Y;
                    double mid = top + child.ActualHeight / 2;
                    photoItems.Add((i, mid));
                }
            }

            // 计算目标插入位置（基于照片中线判定上/下）
            // targetVisualIndex 指示线应插入到 sp.Children 的位置（视觉索引）
            int targetVisualIndex = sp.Children.Count; // 默认末尾
            foreach (var item in photoItems)
            {
                // 鼠标在该照片中线以上 → 蓝线显示在该照片上方
                if (pos.Y < item.midY)
                {
                    targetVisualIndex = item.visualIndex;
                    break;
                }
            }

            // 转换为数据索引（排除指示线后的照片索引）
            int targetDataIndex = 0;
            for (int i = 0; i < targetVisualIndex; i++)
            {
                if (sp.Children[i] != _reorderIndicatorLine) targetDataIndex++;
            }

            if (targetDataIndex != _photoReorderTargetIndex)
            {
                _photoReorderTargetIndex = targetDataIndex;
                UpdateReorderIndicatorPosition();
            }
        }

        /// <summary>照片列表鼠标释放：结束手动排序并同步数据</summary>
        private void CapturedPhotosStackPanel_PreviewMouseUp(object sender, MouseButtonEventArgs e)
        {
            if (_isReorderingPhotos)
            {
                _isReorderingPhotos = false;
                _reorderAutoScrollTimer.Stop();
                _reorderAutoScrollDirection = 0;

                object spObj = CapturedPhotosStackPanel;
                if (spObj is StackPanel sp)
                {
                    if (sp.IsMouseCaptured) sp.ReleaseMouseCapture();

                    // 移除指示线
                    if (sp.Children.Contains(_reorderIndicatorLine))
                        sp.Children.Remove(_reorderIndicatorLine);

                    // 计算实际目标数据索引
                    // _photoReorderTargetIndex 是纯数据索引（不含指示线）
                    int actualTarget = _photoReorderTargetIndex;
                    // 源在目标之前时，移除源后目标索引需要减 1
                    if (_photoReorderSourceIndex < actualTarget) actualTarget--;

                    // 移动数据
                    if (actualTarget >= 0 && actualTarget < capturedPhotos.Count && actualTarget != _photoReorderSourceIndex)
                    {
                        var photo = capturedPhotos[_photoReorderSourceIndex];
                        capturedPhotos.RemoveAt(_photoReorderSourceIndex);
                        capturedPhotos.Insert(actualTarget, photo);
                        Console.WriteLine($"照片排序：从 {_photoReorderSourceIndex} 移动到 {actualTarget}");
                    }
                }
                _photoReorderSourceIndex = -1;
                _photoReorderTargetIndex = -1;
                UpdateCapturedPhotosDisplay();
            }
            // 仅在左键点击空白区域（而非按钮上）时隐藏覆盖层
            // 注意：StackPanel 无背景，非捕获状态下仅当事件源是子元素时才会触发此处
            // 按钮自身的 PreviewMouseUp 已通过 e.Handled=true 阻止冒泡
            if (e.ChangedButton == MouseButton.Left && e.OriginalSource == sender)
            {
                HideActivePhotoActionOverlay();
            }
        }

        /// <summary>照片列表触摸移动：手动排序时实时调整蓝线位置 + 自动滚动</summary>
        private void CapturedPhotosStackPanel_PreviewTouchMove(object sender, TouchEventArgs e)
        {
            if (!_isReorderingPhotos || _photoReorderSourceIndex < 0) return;
            object spObj = CapturedPhotosStackPanel;
            if (!(spObj is StackPanel sp)) return;
            object svObj = CapturedPhotosScrollViewer;
            if (!(svObj is ScrollViewer sv)) return;

            System.Windows.Point pos = e.GetTouchPoint(sp).Position;

            // 自动滚动检测：在 ScrollViewer 视口顶部/底部边缘时滚动
            System.Windows.Point svPos = e.GetTouchPoint(sv).Position;
            double edgeThreshold = 50;
            if (svPos.Y < edgeThreshold)
                _reorderAutoScrollDirection = -1;
            else if (svPos.Y > sv.ActualHeight - edgeThreshold)
                _reorderAutoScrollDirection = 1;
            else
                _reorderAutoScrollDirection = 0;

            // 收集所有照片按钮（排除指示线）及其中线位置
            var photoItems = new List<(int visualIndex, double midY)>();
            for (int i = 0; i < sp.Children.Count; i++)
            {
                if (sp.Children[i] == _reorderIndicatorLine) continue;
                if (sp.Children[i] is FrameworkElement child)
                {
                    double top = child.TransformToAncestor(sp).Transform(new System.Windows.Point(0, 0)).Y;
                    double mid = top + child.ActualHeight / 2;
                    photoItems.Add((i, mid));
                }
            }

            // 计算目标插入位置（基于照片中线判定上/下）
            int targetVisualIndex = sp.Children.Count; // 默认末尾
            foreach (var item in photoItems)
            {
                // 触摸点在该照片中线以上 → 蓝线显示在该照片上方
                if (pos.Y < item.midY)
                {
                    targetVisualIndex = item.visualIndex;
                    break;
                }
            }

            // 转换为数据索引（排除指示线后的照片索引）
            int targetDataIndex = 0;
            for (int i = 0; i < targetVisualIndex; i++)
            {
                if (sp.Children[i] != _reorderIndicatorLine) targetDataIndex++;
            }

            if (targetDataIndex != _photoReorderTargetIndex)
            {
                _photoReorderTargetIndex = targetDataIndex;
                UpdateReorderIndicatorPosition();
            }
        }

        /// <summary>照片列表触摸释放：结束手动排序并同步数据</summary>
        private void CapturedPhotosStackPanel_PreviewTouchUp(object sender, TouchEventArgs e)
        {
            if (_isReorderingPhotos)
            {
                _isReorderingPhotos = false;
                _reorderAutoScrollTimer.Stop();
                _reorderAutoScrollDirection = 0;

                object spObj = CapturedPhotosStackPanel;
                if (spObj is StackPanel sp)
                {
                    // 强制释放所有输入捕获：排序模式启动时同时 CaptureMouse + Capture(touchDevice)，
                    // 某些情况下仅释放当前 TouchDevice 仍会导致鼠标/其他触摸捕获残留，从而全局触摸失灵
                    try { sp.ReleaseAllTouchCaptures(); } catch { }
                    if (e.TouchDevice.Captured == sp) sp.ReleaseTouchCapture(e.TouchDevice);
                    if (sp.IsMouseCaptured) sp.ReleaseMouseCapture();
                    try { sp.ReleaseStylusCapture(); } catch { }

                    // 移除指示线
                    if (sp.Children.Contains(_reorderIndicatorLine))
                        sp.Children.Remove(_reorderIndicatorLine);

                    // 计算实际目标数据索引
                    int actualTarget = _photoReorderTargetIndex;
                    if (_photoReorderSourceIndex < actualTarget) actualTarget--;

                    // 移动数据
                    if (actualTarget >= 0 && actualTarget < capturedPhotos.Count && actualTarget != _photoReorderSourceIndex)
                    {
                        var photo = capturedPhotos[_photoReorderSourceIndex];
                        capturedPhotos.RemoveAt(_photoReorderSourceIndex);
                        capturedPhotos.Insert(actualTarget, photo);
                        Console.WriteLine($"照片排序：从 {_photoReorderSourceIndex} 移动到 {actualTarget}");
                    }
                }
                _photoReorderSourceIndex = -1;
                _photoReorderTargetIndex = -1;
                UpdateCapturedPhotosDisplay();
                // 重新注册触摸窗口，修复某些触屏设备在排序操作后出现的触摸锁死
                TouchLockFix.ReRegisterTouchWindow(this);
            }
            // 仅当触摸释放于空白区域（而非按钮上）时隐藏覆盖层
            if (e.OriginalSource == sender)
            {
                HideActivePhotoActionOverlay();
            }
        }

        // ===== 画板拖拽插入照片 =====

        /// <summary>画板拖拽进入：显示虚线插入指示器</summary>
        private void inkCanvas_DragEnter(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent("CapturedPhoto"))
            {
                e.Effects = DragDropEffects.Copy;
                var photo = e.Data.GetData("CapturedPhoto") as CapturedImage;
                var size = ComputePhotoDisplaySize(photo);
                ShowPhotoDragIndicator(e.GetPosition(inkCanvas), size);
            }
            else
            {
                e.Effects = DragDropEffects.None;
            }
            e.Handled = true;
        }

        /// <summary>画板拖拽移动：更新虚线插入指示器位置</summary>
        private void inkCanvas_DragOver(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent("CapturedPhoto"))
            {
                e.Effects = DragDropEffects.Copy;
                var photo = e.Data.GetData("CapturedPhoto") as CapturedImage;
                var size = ComputePhotoDisplaySize(photo);
                ShowPhotoDragIndicator(e.GetPosition(inkCanvas), size);
            }
            else
            {
                e.Effects = DragDropEffects.None;
            }
            e.Handled = true;
        }

        /// <summary>画板拖拽离开：隐藏虚线插入指示器</summary>
        private void inkCanvas_DragLeave(object sender, DragEventArgs e)
        {
            HidePhotoDragIndicator();
            e.Handled = true;
        }

        /// <summary>画板拖拽释放：在当前位置插入照片并绑定页码</summary>
        private void inkCanvas_Drop(object sender, DragEventArgs e)
        {
            HidePhotoDragIndicator();
            if (e.Data.GetDataPresent("CapturedPhoto"))
            {
                var photo = e.Data.GetData("CapturedPhoto") as CapturedImage;
                if (photo != null)
                {
                    System.Windows.Point pos = e.GetPosition(inkCanvas);
                    InsertPhotoToCanvasAtPosition(photo, pos);
                }
            }
            e.Handled = true;
        }

        /// <summary>计算照片插入画板后的实际显示尺寸（与 InsertPhotoToCanvasAtPosition 一致）</summary>
        private System.Windows.Size ComputePhotoDisplaySize(CapturedImage photo)
        {
            try
            {
                double w, h;
                if (photo != null && photo.PixelWidth > 0)
                {
                    w = photo.PixelWidth;
                    h = photo.PixelHeight;
                }
                else
                {
                    // 视频或无原始尺寸：使用 16:9 默认比例
                    w = 640;
                    h = 360;
                }

                double maxWidth = inkCanvas.ActualWidth * 0.5;
                double maxHeight = inkCanvas.ActualHeight * 0.5;
                if (maxWidth <= 0) maxWidth = 400;
                if (maxHeight <= 0) maxHeight = 300;

                double scale = Math.Min(maxWidth / w, maxHeight / h);
                if (scale < 1.0)
                {
                    w *= scale;
                    h *= scale;
                }
                return new System.Windows.Size(w, h);
            }
            catch
            {
                return new System.Windows.Size(300, 200);
            }
        }

        /// <summary>显示拖拽插入虚线指示器（使用 Adorner 确保拖拽期间可见）</summary>
        private void ShowPhotoDragIndicator(System.Windows.Point position, System.Windows.Size indicatorSize)
        {
            try
            {
                if (_photoDragInsertionAdorner == null)
                {
                    AdornerLayer layer = AdornerLayer.GetAdornerLayer(inkCanvas);
                    if (layer == null)
                    {
                        Console.WriteLine("无法获取 inkCanvas 的 AdornerLayer");
                        return;
                    }
                    _photoDragInsertionAdorner = new DragInsertionAdorner(inkCanvas);
                    layer.Add(_photoDragInsertionAdorner);
                }
                _photoDragInsertionAdorner.SetSize(indicatorSize.Width, indicatorSize.Height);
                _photoDragInsertionAdorner.SetPosition(position);
                _photoDragInsertionAdorner.Visibility = Visibility.Visible;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"显示拖拽指示器失败: {ex.Message}");
            }
        }

        /// <summary>隐藏拖拽插入虚线指示器</summary>
        private void HidePhotoDragIndicator()
        {
            if (_photoDragInsertionAdorner != null)
            {
                _photoDragInsertionAdorner.Visibility = Visibility.Collapsed;
            }
        }

        /// <summary>在画板指定位置插入照片并绑定到当前页码</summary>
        private void InsertPhotoToCanvasAtPosition(CapturedImage photo, System.Windows.Point position)
        {
            try
            {
                int currentPage = GetCurrentPageIndex();

                // 多次拖入只记录最后一次插入的页面（覆盖旧映射）
                if (photoPageMapping.ContainsKey(photo.Timestamp))
                {
                    int existingPage = photoPageMapping[photo.Timestamp];
                    if (existingPage != currentPage)
                    {
                        Console.WriteLine($"照片 {photo.Timestamp} 从页面 {existingPage} 重新绑定到页面 {currentPage}");
                    }
                }

                // 不清除页面上已有的媒体内容，仅追加新照片
                // 仅更新当前照片引用（旧引用保留在画布上不受影响）

                // 创建图片元素
                var imageElement = new System.Windows.Controls.Image
                {
                    Source = CreateBitmapImageFromFileOrMemory(photo),
                    Width = photo.PixelWidth,
                    Height = photo.PixelHeight,
                    Name = GeneratePhotoName(),
                    Tag = photo.SourceFilePath,
                    SnapsToDevicePixels = true,
                    UseLayoutRounding = true
                };
                System.Windows.Media.RenderOptions.SetBitmapScalingMode(imageElement, System.Windows.Media.BitmapScalingMode.HighQuality);

                // 缩放到合理大小（以鼠标位置为中心）
                // 文档照片无论如何都不调整缩放比例，保持原始尺寸，避免按比例缩小导致模糊
                if (!IsDocumentPhoto(photo))
                {
                    double maxWidth = inkCanvas.ActualWidth * 0.5;
                    double maxHeight = inkCanvas.ActualHeight * 0.5;
                    double scale = Math.Min(maxWidth / imageElement.Width, maxHeight / imageElement.Height);
                    if (scale < 1.0)
                    {
                        imageElement.Width *= scale;
                        imageElement.Height *= scale;
                    }
                }

                // 文档照片：瓦片化前端拼接（拖拽时以鼠标位置为长图顶部，水平居中）
                if (IsDocumentPhoto(photo))
                {
                    string docPath = StripChunkSuffix(photo.SourceFilePath);
                    var tiles = GetDocumentTiles(docPath);
                    if (tiles.Count > 0)
                    {
                        int maxW = tiles.Max(t => t.PixelWidth);
                        double tileLeft = Math.Round(Math.Max(0, position.X - maxW / 2.0));
                        double tileTop = Math.Max(0, position.Y - 20);

                        System.Windows.Controls.Image firstTileImg = null;
                        // 分批异步插入瓦片：每批让出 UI 线程，避免大量 Image 一次性创建导致卡顿
                        int tileTotal = tiles.Count;
                        int tileDone = 0;
                        const int batchSize = 4;
                        for (int ti = 0; ti < tiles.Count; ti++)
                        {
                            var tile = tiles[ti];
                            var tileImg = new System.Windows.Controls.Image
                            {
                                Source = CreateBitmapImageFromFileOrMemory(tile),
                                Width = tile.PixelWidth,
                                Height = tile.PixelHeight,
                                Name = GeneratePhotoName(),
                                Tag = tile.SourceFilePath,
                                SnapsToDevicePixels = true,
                                UseLayoutRounding = true
                            };
                            System.Windows.Media.RenderOptions.SetBitmapScalingMode(tileImg, System.Windows.Media.BitmapScalingMode.HighQuality);
                            InkCanvas.SetLeft(tileImg, tileLeft);
                            InkCanvas.SetTop(tileImg, Math.Round(tileTop));
                            inkCanvas.Children.Add(tileImg);
                            if (firstTileImg == null) firstTileImg = tileImg;
                            photoPageMapping[tile.Timestamp] = currentPage;
                            tileTop += tile.PixelHeight;
                            tileDone++;

                            // 每批插入后让出 UI 线程处理渲染，并反馈进度
                            if (tileDone % batchSize == 0)
                            {
                                ShowNotificationAsync($"正在插入文档照片 {tileDone}/{tileTotal}", true);
                                try { inkCanvas.UpdateLayout(); } catch { }
                                System.Windows.Threading.Dispatcher.Yield(
                                    System.Windows.Threading.DispatcherPriority.Background);
                            }
                        }

                        currentPhotoImage = firstTileImg;
                        pageDocumentMapping[currentPage] = docPath;
                        timeMachine.CommitElementInsertHistory(firstTileImg);
                        selectedPhotoTimestamp = tiles[0].Timestamp;
                        UpdateCapturedPhotosDisplay();
                        SaveDocumentPageIfNeeded(currentPage);
                        Console.WriteLine($"文档照片瓦片化（拖拽）插入完成: {tiles.Count} 张, 文档: {docPath}");
                        ShowNotificationAsync($"文档照片插入完成（{tileTotal} 张）", true);
                        return;
                    }
                }

                // 设置位置（以鼠标位置为图片中心）
                double left = position.X - imageElement.Width / 2;
                double top = position.Y - imageElement.Height / 2;
                if (left < 0) left = 0;
                if (top < 0) top = 0;

                InkCanvas.SetLeft(imageElement, left);
                InkCanvas.SetTop(imageElement, top);
                inkCanvas.Children.Add(imageElement);

                currentPhotoImage = imageElement;
                // 记录照片与页码的关联
                photoPageMapping[photo.Timestamp] = currentPage;
                Console.WriteLine($"照片已通过拖拽插入到页面 {currentPage}，位置({left},{top})");

                // 记录文档页关联
                if (IsDocumentPhoto(photo))
                {
                    pageDocumentMapping[currentPage] = StripChunkSuffix(photo.SourceFilePath);
                }

                timeMachine.CommitElementInsertHistory(imageElement);

                selectedPhotoTimestamp = photo.Timestamp;
                UpdateCapturedPhotosDisplay();

                // 文档照片插入后，保存当前页全部内容
                if (IsDocumentPhoto(photo))
                {
                    SaveDocumentPageIfNeeded(currentPage);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"拖拽插入照片失败: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private bool IsDocumentPhoto(CapturedImage photo)
        {
            // 分块照片的来源路径带 "#块序号"，剥离后按文档识别
            return photo != null && IsDocumentFilePath(StripChunkSuffix(photo.SourceFilePath));
        }

        private void InsertPhotoToCanvas(CapturedImage photo)
        {
            try
            {
                // 去重判断：检查当前页面是否已经有该照片
                int currentPage = GetCurrentPageIndex();
                
                // 检查照片与页码的映射关系
                if (photoPageMapping.ContainsKey(photo.Timestamp))
                {
                    int existingPage = photoPageMapping[photo.Timestamp];
                    
                    // 如果照片已经存在于当前页面，则不再插入
                    if (existingPage == currentPage)
                    {
                        Console.WriteLine($"照片 {photo.Timestamp} 已经存在于当前页面 {currentPage}，跳过插入操作");
                        return;
                    }
                    else
                    {
                        // 如果照片存在于其他页面，更新映射关系到当前页面
                        photoPageMapping[photo.Timestamp] = currentPage;
                        Console.WriteLine($"照片 {photo.Timestamp} 从页面 {existingPage} 移动到页面 {currentPage}");
                    }
                }
                
                // 检查当前页面是否已经有照片元素或摄像头画面
                if (HasPhotoOnCurrentPage() || HasCameraFrameOnCurrentPage())
                {
                    Console.WriteLine($"当前页面 {currentPage} 已有照片或摄像头画面，将移除现有元素并插入新照片");
                    
                    // 移除当前页面的照片元素
                    if (currentPhotoImage != null)
                    {
                        inkCanvas.Children.Remove(currentPhotoImage);
                        currentPhotoImage = null;
                    }
                    
                    // 移除当前页面的摄像头画面元素
                    if (currentCameraImage != null)
                    {
                        inkCanvas.Children.Remove(currentCameraImage);
                        currentCameraImage = null;
                    }
                    
                    // 清除可能存在的其他照片元素
                    ClearPhotoElementsFromCanvas();
                    
                    // 清除可能存在的其他摄像头元素
                    ClearCameraElementsFromCanvas();
                }

                // 创建图片元素
                var imageElement = new System.Windows.Controls.Image
                {
                    Source = CreateBitmapImageFromFileOrMemory(photo),
                    Width = photo.PixelWidth,
                    Height = photo.PixelHeight,
                    Name = GeneratePhotoName(),
                    Tag = photo.SourceFilePath,
                    SnapsToDevicePixels = true,
                    UseLayoutRounding = true
                };
                System.Windows.Media.RenderOptions.SetBitmapScalingMode(imageElement, System.Windows.Media.BitmapScalingMode.HighQuality);

                // 居中并缩放
                if (IsDocumentPhoto(photo))
                {
                    // 文档照片：瓦片化前端拼接
                    // 从源文件路径剥离块序号，获取原始文档路径
                    string docPath = StripChunkSuffix(photo.SourceFilePath);

                    // 该文档的瓦片已经存在于当前页 → 跳过
                    if (HasDocumentTilesOnCurrentPage(docPath))
                    {
                        Console.WriteLine($"文档照片 {docPath} 已存在于当前页面 {currentPage}，跳过重复插入");
                        // 更新选中等
                        selectedPhotoTimestamp = photo.Timestamp;
                        UpdateCapturedPhotosDisplay();
                        return;
                    }

                    // 移除当前页面的所有照片/摄像头元素，准备插入新瓦片集
                    if (currentPhotoImage != null) { inkCanvas.Children.Remove(currentPhotoImage); currentPhotoImage = null; }
                    if (currentCameraImage != null) { inkCanvas.Children.Remove(currentCameraImage); currentCameraImage = null; }
                    ClearPhotoElementsFromCanvas();
                    ClearCameraElementsFromCanvas();

                    // 获取同一文档的所有瓦片，按块序号升序排列
                    var tiles = GetDocumentTiles(docPath);
                    if (tiles.Count == 0)
                    {
                        // 未找到该文档的分块瓦片（如单块文档），退化到单张插入：仅设置位置，交由统一逻辑添加
                        Console.WriteLine($"未找到文档 {docPath} 的分块瓦片，退化到单张插入");
                        CenterAndScaleDocumentPhoto(imageElement);
                    }
                    else
                    {
                        // 该文档的瓦片已存在 → 已有去重逻辑，此处直接异步插入
                        // 计算所有瓦片的最大宽度，以此为基准水平居中
                        int maxW = tiles.Max(t => t.PixelWidth);
                        double canvasWidth = inkCanvas.ActualWidth;
                        if (canvasWidth <= 0) canvasWidth = SystemParameters.PrimaryScreenWidth;
                        double left = Math.Round(Math.Max(0, (canvasWidth - maxW) / 2.0));

                        double top = 0;
                        System.Windows.Controls.Image firstImg = null;

                        // 分批异步插入瓦片：每批让出 UI 线程，避免大量 Image 一次性创建导致卡顿，
                        // 并通过进度提示反馈插入状态
                        int tileTotal = tiles.Count;
                        int tileDone = 0;
                        const int batchSize = 4;
                        for (int ti = 0; ti < tiles.Count; ti++)
                        {
                            var tile = tiles[ti];
                            var tileImg = new System.Windows.Controls.Image
                            {
                                Source = CreateBitmapImageFromFileOrMemory(tile),
                                Width = tile.PixelWidth,
                                Height = tile.PixelHeight,
                                Name = GeneratePhotoName(),
                                Tag = tile.SourceFilePath,
                                SnapsToDevicePixels = true,
                                UseLayoutRounding = true
                            };
                            System.Windows.Media.RenderOptions.SetBitmapScalingMode(tileImg, System.Windows.Media.BitmapScalingMode.HighQuality);

                            InkCanvas.SetLeft(tileImg, left);
                            InkCanvas.SetTop(tileImg, Math.Round(top));
                            inkCanvas.Children.Add(tileImg);

                            if (firstImg == null) firstImg = tileImg;

                            // 所有瓦片的时间戳关联到同一页码，点击任意瓦片都能跳转到正确页面
                            photoPageMapping[tile.Timestamp] = currentPage;
                            top += tile.PixelHeight;
                            tileDone++;

                            // 每批插入后让出 UI 线程处理渲染，并反馈进度
                            if (tileDone % batchSize == 0)
                            {
                                ShowNotificationAsync($"正在插入文档照片 {tileDone}/{tileTotal}", true);
                                try { inkCanvas.UpdateLayout(); } catch { }
                                System.Windows.Threading.Dispatcher.Yield(
                                    System.Windows.Threading.DispatcherPriority.Background);
                            }
                        }

                        // 记录当前照片引用（指向第一张瓦片，用于后续 HasPhotoOnCurrentPage 等判断）
                        currentPhotoImage = firstImg;

                        Console.WriteLine($"文档照片瓦片化插入完成: {tiles.Count} 张, 文档: {docPath}, 总高度: {Math.Round(top)}px");

                        // 选中第一张瓦片
                        selectedPhotoTimestamp = tiles[0].Timestamp;
                        UpdateCapturedPhotosDisplay();

                        // 记录历史（只记录第一张瓦片，保存/恢复时整个 InkCanvas 被序列化，包含所有瓦片）
                        timeMachine.CommitElementInsertHistory(firstImg);

                        // 记录文档页关联（使用原始文档路径，不含块序号），供后续保存/恢复使用
                        pageDocumentMapping[currentPage] = docPath;

                        // 文档页保存/恢复
                        bool restored = RestoreDocumentPageIfAvailable(currentPage);
                        if (!restored)
                        {
                            SaveDocumentPageIfNeeded(currentPage);
                        }

                        // 强制布局更新
                        try { inkCanvas.UpdateLayout(); } catch { }
                        Console.WriteLine($"文档照片瓦片已成功插入白板: {docPath}");
                        ShowNotificationAsync($"文档照片插入完成（{tileTotal} 张）", true);
                        return; // 已处理完毕，跳过后续单张照片逻辑
                    }
                }
                else
                {
                    CenterAndScaleElement(imageElement);
                    InkCanvas.SetLeft(imageElement, 0);
                    InkCanvas.SetTop(imageElement, 0);
                }

                // 添加到画布（非文档照片，或文档照片退化到单张插入时执行到这里）
                inkCanvas.Children.Add(imageElement);

                // 强制立即布局与渲染，避免新建页面插入的图片不显示
                try
                {
                    imageElement.UpdateLayout();
                    inkCanvas.UpdateLayout();
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"照片插入后布局更新失败: {ex.Message}");
                }

                // 记录当前照片元素引用
                currentPhotoImage = imageElement;

                // 记录照片与页码的关联
                photoPageMapping[photo.Timestamp] = currentPage;
                Console.WriteLine($"照片已记录到页码: {currentPage}");

                // 记录文档页关联（单张文档照片时使用原始路径；非文档照片无此关联）
                if (IsDocumentPhoto(photo))
                {
                    pageDocumentMapping[currentPage] = StripChunkSuffix(photo.SourceFilePath);
                }

                // 记录历史
                timeMachine.CommitElementInsertHistory(imageElement);

                // 显示成功提示
                Console.WriteLine($"照片已成功插入白板: {photo.Timestamp}");

                // 更新选中状态与侧栏样式
                selectedPhotoTimestamp = photo.Timestamp;
                UpdateCapturedPhotosDisplay();

                // 文档照片插入后，若该页此前已有保存的笔迹/元素，则优先恢复；否则保存当前页
                if (IsDocumentPhoto(photo))
                {
                    bool restored = RestoreDocumentPageIfAvailable(currentPage);
                    if (!restored)
                    {
                        SaveDocumentPageIfNeeded(currentPage);
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"插入照片失败: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        #endregion

        // 自动计算照片显示区域高度算法
        private void AutoCalculatePhotoAreaHeight()
        {
            try
            {
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    try
                    {
                        if (VideoPresenterSidebar.ActualHeight <= 0) return;
                        object capturedPhotosScrollViewerObject = CapturedPhotosScrollViewer;
                        object capturedPhotosBorderObject = CapturedPhotosBorder;
                        object capturedPhotosTitleTextBlockObject = CapturedPhotosTitleTextBlock;

                        if (!(capturedPhotosScrollViewerObject is ScrollViewer capturedPhotosScrollViewer) ||
                            !(capturedPhotosBorderObject is Border capturedPhotosBorder) ||
                            !(capturedPhotosTitleTextBlockObject is TextBlock capturedPhotosTitleTextBlock))
                        {
                            return;
                        }

                        double borderInnerHeight = capturedPhotosBorder.ActualHeight
                                                   - capturedPhotosBorder.Padding.Top
                                                   - capturedPhotosBorder.Padding.Bottom;

                        if (borderInnerHeight <= 0) return;

                        double titleHeight = capturedPhotosTitleTextBlock.ActualHeight
                                             + capturedPhotosTitleTextBlock.Margin.Top
                                             + capturedPhotosTitleTextBlock.Margin.Bottom;

                        double maxScrollViewerHeight = borderInnerHeight - titleHeight;

                        if (maxScrollViewerHeight > 0)
                        {
                            capturedPhotosScrollViewer.MaxHeight = maxScrollViewerHeight;
                        }

                        Console.WriteLine($"照片区域高度计算完成: 照片区域高度={maxScrollViewerHeight}");
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"自动计算照片区域高度失败: {ex.Message}");
                    }
                }), System.Windows.Threading.DispatcherPriority.Background);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"自动计算照片区域高度失败: {ex.Message}");
            }
        }

        // 初始化摄像头设备管理器
        private void InitializeCameraDeviceManager()
        {
            cameraDeviceManager = new CameraDeviceManager(this);
            cameraDeviceManager.RefreshCameraDevices();
            
            // 初始化摄像头画面更新定时器（30fps）
            cameraFrameTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(33) // ~30fps
            };
            cameraFrameTimer.Tick += CameraFrameTimer_Tick;
            
            // 设置拍照按钮的初始状态
            UpdateCapturePhotoButtonState();
        }

        // 摄像头控制按钮功能已移除，仅保留设备选择功能

        // 插入摄像头画面到白板
        public async void InsertCameraFrameToCanvas()
        {
            if (cameraDeviceManager == null) return;

            int currentPage = GetCurrentPageIndex();
            if (cameraFramesByPage.TryGetValue(currentPage, out var existing) && existing != null)
            {
                if (!inkCanvas.Children.Contains(existing))
                {
                    // 检查当前页面是否已经有照片或其他摄像头画面
                    if (HasPhotoOnCurrentPage() || HasCameraFrameOnCurrentPage())
                    {
                        Console.WriteLine($"当前页面 {currentPage} 已有照片或摄像头画面，将切换到下一页插入");
                        // 如果当前页面已有照片或摄像头画面，切换到下一页插入
                        SwitchToNextBoardAndInsertCameraFrame();
                        return;
                    }
                    
                    currentCameraImage = existing;
                    inkCanvas.Children.Add(existing);
                }
                else
                {
                    currentCameraImage = existing;
                }
                cameraFrameTimer?.Start();
                UpdateCapturePhotoButtonState();
                try { cameraDeviceManager?.BindCurrentCameraToPage(currentPage); } catch (Exception ex) { Console.WriteLine($"绑定摄像头设备到页码 {currentPage} 失败: {ex.Message}"); }
                try { cameraDeviceManager?.HandlePageChanged(GetCurrentPageIndex()); } catch (Exception ex) { Console.WriteLine($"插入摄像头画面后刷新设备选中显示失败: {ex.Message}"); }
                return;
            }

            // 直接切换到下一页插入，不再判断当前页面是否有内容
            Console.WriteLine($"直接切换到下一页插入摄像头画面");
            SwitchToNextBoardAndInsertCameraFrame();
        }

        // 检测当前页面是否有摄像头画面
        public bool HasCameraFrameOnCurrentPage()
        {
            // 首先检查currentCameraImage引用
            if (currentCameraImage != null) return true;
            
            // 如果currentCameraImage为null，再检查画布上是否有摄像头画面元素
            if (inkCanvas != null)
            {
                foreach (var child in inkCanvas.Children)
                {
                    if (child is System.Windows.Controls.Image image && 
                        image.Name != null && 
                        image.Name.StartsWith("camera_"))
                    {
                        // 找到摄像头画面元素，更新currentCameraImage引用
                        currentCameraImage = image;
                        return true;
                    }
                }
            }
            
            return false;
        }
        
        // 更新拍照按钮状态
        public void UpdateCapturePhotoButtonState()
        {
            if (BtnCapturePhoto == null) return;

            bool hasCameraFrame = HasCameraFrameOnCurrentPage();
            BtnCapturePhoto.IsEnabled = hasCameraFrame;

            // 根据按钮状态更新视觉样式
            if (hasCameraFrame)
            {
                BtnCapturePhoto.Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Colors.SkyBlue);
            }
            else
            {
                BtnCapturePhoto.Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(160, 160, 160));
            }
        }
        
        // 清除所有内容按钮点击事件
        public void BtnClearAllContent_Click(object sender, RoutedEventArgs e)
        {
            var notificationWindow = new YesOrNoNotificationWindow(
                "确定要清除所有白板内容吗？此操作不可恢复。",
                yesAction: () =>
                {
                    try
                    {
                        ClearAllWhiteboardContent();
                        ShowNotificationAsync("所有内容已清除", true);
                    }
                    catch (Exception ex)
                    {
                        LogHelper.WriteLogToFile($"清除所有内容失败: {ex.Message}", LogHelper.LogType.Error);
                        MessageBox.Show($"清除内容时发生错误: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                    }
                },
                noAction: () => { });

            Helpers.WindowMemoryHelper.ReleaseOnClose(notificationWindow);
            notificationWindow.Show();
        }

        // 导入文档按钮点击事件：先查本地转换缓存，需要转换时再由 document-viewer 插件处理
        public void BtnImportDocument_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var dialog = new Microsoft.Win32.OpenFileDialog
                {
                    Filter = "Office 文档|*.docx;*.xls;*.xlsx;*.pdf|Word 文档|*.docx|Excel 工作簿|*.xls;*.xlsx|PDF 文档|*.pdf|所有文件|*.*",
                    Title = "选择要导入的文档",
                    Multiselect = false
                };
                if (dialog.ShowDialog(this) == true)
                {
                    // 先查找本地转换缓存：已转换且未修改则直接加载照片；未转换或已修改则自动转换
                    OpenDocumentWithPhotoCache(dialog.FileName);
                }
            }
            catch (Exception ex)
            {
                LogHelper.WriteLogToFile($"导入文档失败: {ex.Message}", LogHelper.LogType.Error);
                MessageBox.Show($"导入文档时发生错误: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        /// <summary>
        /// 清除所有白板内容，恢复至初始默认状态
        /// </summary>
        private void ClearAllWhiteboardContent()
        {
            // 1. 清除摄像头设备选中状态并停止摄像头
            try
            {
                cameraDeviceManager?.ClearDeviceSelection();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"清除摄像头设备选中状态失败: {ex.Message}");
            }

            // 2. 停止摄像头画面定时器
            cameraFrameTimer?.Stop();

            // 3. 清除所有页面的墨迹历史
            for (int i = 0; i < TimeMachineHistories.Length; i++)
            {
                TimeMachineHistories[i] = null;
            }

            // 4. 清除时间机器的当前历史
            timeMachine?.ClearStrokeHistory();

            // 5. 清除当前画布上的所有墨迹和元素
            _currentCommitType = CommitReason.ClearingCanvas;
            inkCanvas.Strokes.Clear();
            inkCanvas.Children.Clear();
            _currentCommitType = CommitReason.UserInput;

            // 6. 重置页面计数到初始状态
            CurrentWhiteboardIndex = 1;
            WhiteboardTotalCount = 1;

            // 7. 设置第一页为空白页面
            TimeMachineHistories[1] = new TimeMachineHistory[0];

            // 8. 清除照片列表和页面映射（含文档页映射，否则关闭时会把空画布误存进文档保存文件）
            capturedPhotos.Clear();
            photoPageMapping.Clear();
            pageDocumentMapping.Clear();
            selectedPhotoTimestamp = null;

            // 9. 清除摄像头画面映射和引用
            cameraFramesByPage.Clear();
            currentCameraImage = null;
            currentPhotoImage = null;

            // 10. 恢复默认画笔状态（如果在白板模式下），不展开墨迹选项面板
            if (currentMode == 1)
            {
                try
                {
                    loadPenCanvas();
                    inkCanvas.EditingMode = InkCanvasEditingMode.Ink;
                    drawingShapeMode = 0;
                    forceEraser = false;
                    forcePointEraser = false;
                    InkCanvas_EditingModeChanged(inkCanvas, null);
                    CancelSingleFingerDragMode();
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"恢复默认画笔状态失败: {ex.Message}");
                }
            }

            // 12. 更新所有UI显示
            UpdateCapturedPhotosDisplay();
            UpdateIndexInfoDisplay();
            UpdateCapturePhotoButtonState();

            // 13. 确保导航至第一页并恢复页面内容
            try
            {
                RestoreStrokes(false);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"恢复第一页内容失败: {ex.Message}");
            }

            Console.WriteLine("所有白板内容已清除并恢复至默认状态");
        }

        // 检测当前页面是否有照片
        public bool HasPhotoOnCurrentPage()
        {
            // 首先检查currentPhotoImage引用
            if (currentPhotoImage != null) return true;
            
            // 如果currentPhotoImage为null，再检查画布上是否有照片元素
            if (inkCanvas != null)
            {
                foreach (var child in inkCanvas.Children)
                {
                    if (child is System.Windows.Controls.Image image && 
                        image.Name != null && 
                        image.Name.StartsWith("photo_"))
                    {
                        // 找到照片元素，更新currentPhotoImage引用
                        currentPhotoImage = image;
                        return true;
                    }
                }
            }
            
            return false;
        }

        private BitmapImage CreateBitmapImageFromFileOrMemory(CapturedImage photo)
        {
            try
            {
                if (!string.IsNullOrEmpty(photo.FilePath) && File.Exists(photo.FilePath))
                {
                    var bi = new BitmapImage();
                    bi.BeginInit();
                    bi.UriSource = new Uri(photo.FilePath, UriKind.Absolute);
                    // OnDemand：仅在真正渲染时解码像素，且支持按需释放，
                    // 避免大量分块照片同时保留完整像素数据导致内存激增
                    bi.CacheOption = BitmapCacheOption.OnDemand;
                    bi.EndInit();
                    bi.Freeze();
                    return bi;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"从文件加载照片失败 [{photo.FilePath}]: {ex.Message}");
            }

            // 文件不存在或加载失败时，从内存图像创建一份在 UI 线程完整初始化的冻结副本，
            // 避免后台线程创建的 BitmapImage 直接作为 Source 时出现渲染延迟或不显示的问题。
            try
            {
                if (photo.Image != null && photo.Image.PixelWidth > 0)
                {
                    var encoder = new PngBitmapEncoder();
                    encoder.Frames.Add(BitmapFrame.Create(photo.Image));
                    using (var ms = new System.IO.MemoryStream())
                    {
                        encoder.Save(ms);
                        ms.Position = 0;
                        var bi = new BitmapImage();
                        bi.BeginInit();
                        bi.StreamSource = ms;
                        bi.CacheOption = BitmapCacheOption.OnDemand;
                        bi.EndInit();
                        bi.Freeze();
                        return bi;
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"从内存复制照片失败: {ex.Message}");
            }

            return photo.Image;
        }

        private string SaveBitmapImageToPhotoFile(BitmapImage image, string sourceFilePath = null)
        {
            try
            {
                string baseDir = Settings.Automation.AutoSavedStrokesLocation + @"\Auto Saved - Photos";
                if (Settings.Automation.IsSaveScreenshotsInDateFolders)
                {
                    baseDir += @"\" + DateTime.Now.ToString("yyyy-MM-dd");
                }
                if (!Directory.Exists(baseDir)) Directory.CreateDirectory(baseDir);
                string suffix = string.IsNullOrEmpty(sourceFilePath)
                    ? string.Empty
                    : "_" + System.IO.Path.GetFileNameWithoutExtension(sourceFilePath);
                string fileName = DateTime.Now.ToString("yyyy-MM-dd HH-mm-ss-fff") + suffix + ".png";
                string path = System.IO.Path.Combine(baseDir, fileName);
                var encoder = new PngBitmapEncoder();
                encoder.Frames.Add(BitmapFrame.Create(image));
                using (var fs = new FileStream(path, FileMode.Create, FileAccess.Write))
                {
                    encoder.Save(fs);
                }
                // 文档照片：写入 .src 伴随文件记录原始文档路径，以便重启后恢复 SourceFilePath
                // （分块照片的路径带 "#块序号"，剥离后仍识别为文档，保证写入 .src）
                if (!string.IsNullOrEmpty(sourceFilePath) && IsDocumentFilePath(StripChunkSuffix(sourceFilePath)))
                {
                    try
                    {
                        File.WriteAllText(path + ".src", sourceFilePath);
                    }
                    catch { }
                }
                return path;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"保存照片失败: {ex.Message}");
                return null;
            }
        }

        private void LoadSavedPhotosToSidebar()
        {
            try
            {
                string baseDir = Settings.Automation.AutoSavedStrokesLocation + @"\Auto Saved - Photos";
                var dirs = new List<string>();
                if (Directory.Exists(baseDir))
                {
                    dirs.Add(baseDir);
                    try
                    {
                        foreach (var d in Directory.GetDirectories(baseDir)) dirs.Add(d);
                    }
                    catch { }
                }
                foreach (var dir in dirs)
                {
                    foreach (var file in Directory.GetFiles(dir, "*.png"))
                    {
                        try
                        {
                            // 去重：跳过已加载的照片（按 FilePath 匹配）
                            if (capturedPhotos.Any(p => !string.IsNullOrEmpty(p.FilePath) &&
                                string.Equals(p.FilePath, file, StringComparison.OrdinalIgnoreCase)))
                                continue;

                            var bi = new BitmapImage();
                            bi.BeginInit();
                            bi.UriSource = new Uri(file, UriKind.Absolute);
                            bi.CacheOption = BitmapCacheOption.OnLoad;
                            bi.EndInit();
                            bi.Freeze();

                            // 读取 .src 伴随文件恢复文档来源路径
                            string sourceFilePath = null;
                            try
                            {
                                string srcFile = file + ".src";
                                if (File.Exists(srcFile))
                                {
                                    sourceFilePath = File.ReadAllText(srcFile).Trim();
                                    if (string.IsNullOrEmpty(sourceFilePath)) sourceFilePath = null;
                                }
                            }
                            catch { }

                            // 去重：同一文档来源只保留最新一张
                            if (!string.IsNullOrEmpty(sourceFilePath) &&
                                capturedPhotos.Any(p => !string.IsNullOrEmpty(p.SourceFilePath) &&
                                    string.Equals(p.SourceFilePath, sourceFilePath, StringComparison.OrdinalIgnoreCase)))
                                continue;

                            var ci = sourceFilePath != null
                                ? new CapturedImage(bi, file, sourceFilePath)
                                : new CapturedImage(bi, file);
                            capturedPhotos.Insert(0, ci);
                        }
                        catch { }
                    }
                }
                UpdateCapturedPhotosDisplay();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"加载已保存照片失败: {ex.Message}");
            }
        }

        

        /// <summary>
        /// 获取当前页面上所有可旋转的图像元素
        /// </summary>
        private List<UIElement> GetRotatableElementsOnCurrentPage()
        {
            var rotatableElements = new List<UIElement>();

            foreach (var element in inkCanvas.Children)
            {
                // 检查图像元素（包括摄像头画面、照片列表照片、导入媒体图片）
                if (element is System.Windows.Controls.Image imageElement)
                {
                    // 检查元素名称前缀，包括所有类型的图像元素
                    if (imageElement.Name.StartsWith("camera_") || 
                        imageElement.Name.StartsWith("photo_") || 
                        imageElement.Name.StartsWith("img_"))
                    {
                        rotatableElements.Add(imageElement);
                    }
                }
                // 检查媒体元素（视频）
                else if (element is MediaElement mediaElement)
                {
                    if (mediaElement.Name.StartsWith("media_"))
                    {
                        rotatableElements.Add(mediaElement);
                    }
                }
            }

            return rotatableElements;
        }
        
        // 检测当前页面是否有摄像头画面或照片（两者只要有一种就返回true）
        public bool HasCameraFrameOrPhotoOnCurrentPage()
        {
            return HasCameraFrameOnCurrentPage() || HasPhotoOnCurrentPage();
        }

        // 清除画布上的所有照片元素
        private void ClearPhotoElementsFromCanvas()
        {
            try
            {
                if (inkCanvas == null) return;
                
                // 收集所有照片元素
                var photoElements = new List<System.Windows.Controls.Image>();
                
                foreach (var child in inkCanvas.Children)
                {
                    if (child is System.Windows.Controls.Image image && 
                        image.Name != null && 
                        image.Name.StartsWith("photo_"))
                    {
                        photoElements.Add(image);
                    }
                }
                
                // 移除所有照片元素
                foreach (var photoElement in photoElements)
                {
                    inkCanvas.Children.Remove(photoElement);
                }
                
                // 重置当前照片引用
                currentPhotoImage = null;
                
                Console.WriteLine($"已清除画布上的 {photoElements.Count} 个照片元素");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"清除照片元素失败: {ex.Message}");
            }
        }

        // 处理页面切换时的照片显示逻辑
        private void HandlePhotoDisplayOnPageChange(int newPageIndex)
        {
            try
            {
                // 若当前页为文档页且已从文件恢复完整内容，则不再重复插入照片，仅同步侧栏选中
                if (pageDocumentMapping.TryGetValue(newPageIndex, out string docSourcePath) &&
                    !string.IsNullOrEmpty(docSourcePath))
                {
                    string folderPath = GetDocumentPageFolderPath(docSourcePath);
                    // 页码可能因重启变化，使用回退搜索
                    string baseFilePath = FindDocumentPageBasePath(folderPath, newPageIndex);
                    if (baseFilePath != null)
                    {
                        var photo = capturedPhotos.FirstOrDefault(p =>
                            !string.IsNullOrEmpty(p.SourceFilePath) &&
                            string.Equals(p.SourceFilePath, docSourcePath, StringComparison.OrdinalIgnoreCase));
                        if (photo != null)
                        {
                            selectedPhotoTimestamp = photo.Timestamp;
                            UpdateCapturedPhotosDisplay();
                            Console.WriteLine($"页码 {newPageIndex} 的文档页已从文件恢复，跳过重复插入照片");
                            return;
                        }
                    }
                }

                // 清除当前照片显示
                if (currentPhotoImage != null)
                {
                    inkCanvas.Children.Remove(currentPhotoImage);
                    currentPhotoImage = null;
                }

                // 检查新页面是否有关联的照片
                bool hasPhotoOnNewPage = false;

                // 遍历photoPageMapping字典，查找与新页面关联的照片
                foreach (var kvp in photoPageMapping)
                {
                    if (kvp.Value == newPageIndex)
                    {
                        // 找到与新页面关联的照片
                        hasPhotoOnNewPage = true;

                        // 在照片集合中查找对应的照片
                        var photo = capturedPhotos.FirstOrDefault(p => p.Timestamp.Equals(kvp.Key));
                        if (photo != null)
                        {
                            // 在新页面上显示照片
                            InsertPhotoToCanvas(photo);
                            Console.WriteLine($"页码 {newPageIndex} 上的照片已恢复显示");
                            // 同步侧栏选中状态
                            selectedPhotoTimestamp = photo.Timestamp;
                            UpdateCapturedPhotosDisplay();
                        }
                        break; // 每个页面最多只能有一张照片
                    }
                }

                if (!hasPhotoOnNewPage)
                {
                    Console.WriteLine($"页码 {newPageIndex} 上没有关联的照片");
                    // 清除选中状态并刷新侧栏样式
                    selectedPhotoTimestamp = null;
                    UpdateCapturedPhotosDisplay();
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"处理页面切换时的照片显示失败: {ex.Message}");
            }
        }

        // 获取当前页码
        public int GetCurrentPageIndex()
        {
            return CurrentWhiteboardIndex;
        }

        // 切换到下一页白板并插入摄像头画面
        public void SwitchToNextBoardAndInsertCameraFrame()
        {
            try
            {
                // 直接调用BtnWhiteBoardAdd_Click方法，确保能正确添加新页面
                BtnWhiteBoardAdd_Click(null, null);
                int targetPage = GetCurrentPageIndex();
                
                // 延迟一小段时间确保白板切换完成，然后直接插入摄像头画面，避免递归调用
                System.Threading.Tasks.Task.Delay(300).ContinueWith(_ =>
                {
                    Dispatcher.BeginInvoke(new Action(async () =>
                    {
                        if (cameraDeviceManager == null) return;

                        if (GetCurrentPageIndex() != targetPage)
                        {
                            SwitchToPage(targetPage);
                        }

                        int currentPage = targetPage;
                        if (cameraFramesByPage.TryGetValue(currentPage, out var existing) && existing != null)
                        {
                            if (!inkCanvas.Children.Contains(existing))
                            {
                                currentCameraImage = existing;
                                inkCanvas.Children.Add(existing);
                            }
                            else
                            {
                                currentCameraImage = existing;
                            }
                            cameraFrameTimer?.Start();
                            UpdateCapturePhotoButtonState();
                            try { cameraDeviceManager?.BindCurrentCameraToPage(currentPage); } catch (Exception ex) { Console.WriteLine($"绑定摄像头设备到页码 {currentPage} 失败: {ex.Message}"); }
                            try { cameraDeviceManager?.HandlePageChanged(currentPage); } catch (Exception ex) { Console.WriteLine($"插入摄像头画面后刷新设备选中显示失败: {ex.Message}"); }
                            return;
                        }

                        ClearCameraElementsFromCanvas();

                        bool frameInserted = false;
                        for (int i = 0; i < 5; i++)
                        {
                            var frame = cameraDeviceManager.GetFrameCopy();
                            if (frame != null)
                            {
                                // 直接在UI线程上执行插入操作
                                // 转换Bitmap到BitmapImage
                                var bitmapImage = await Task.Run(() =>
                                {
                                    using (var memoryStream = new MemoryStream())
                                    {
                                        frame.Save(memoryStream, System.Drawing.Imaging.ImageFormat.Png);
                                        memoryStream.Position = 0;

                                        var bitmap = new BitmapImage();
                                        bitmap.BeginInit();
                                        bitmap.StreamSource = memoryStream;
                                        bitmap.CacheOption = BitmapCacheOption.OnLoad;
                                        bitmap.EndInit();
                                        bitmap.Freeze();

                                        return bitmap;
                                    }
                                });

                                // 创建图片元素
                                currentCameraImage = new System.Windows.Controls.Image
                                {
                                    Source = bitmapImage,
                                    Width = bitmapImage.PixelWidth,
                                    Height = bitmapImage.PixelHeight,
                                    Name = "camera_" + DateTime.Now.ToString("yyyyMMdd_HH_mm_ss_fff")
                                };

                                // 居中并缩放
                                CenterAndScaleElement(currentCameraImage);

                                // 添加到画布
                                InkCanvas.SetLeft(currentCameraImage, 0);
                                InkCanvas.SetTop(currentCameraImage, 0);
                                inkCanvas.Children.Add(currentCameraImage);

                                // 记录历史
                                timeMachine.CommitElementInsertHistory(currentCameraImage);
                                try
                                {
                                    cameraFramesByPage[currentPage] = currentCameraImage;
                                    cameraDeviceManager?.BindCurrentCameraToPage(currentPage);
                                }
                                catch { }

                                frame.Dispose();
                                frameInserted = true;
                                cameraFrameTimer?.Start();
                                UpdateCapturePhotoButtonState();
                                break;
                            }
                            await System.Threading.Tasks.Task.Delay(500);
                        }

                        if (!frameInserted)
                        {
                            Console.WriteLine("无法获取摄像头画面，可能是摄像头未初始化完成");
                        }

                        try
                        {
                            cameraDeviceManager?.HandlePageChanged(currentPage);
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine($"插入摄像头画面后刷新设备选中显示失败: {ex.Message}");
                        }
                    }));
                });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"切换到下一页并插入摄像头画面失败: {ex.Message}");
            }
        }
        
        // 切换到下一页白板并插入照片
        public async void SwitchToNextBoardAndInsertPhoto(CapturedImage photo)
        {
            try
            {
                // 直接调用BtnWhiteBoardAdd_Click方法，确保能正确添加新页面
                BtnWhiteBoardAdd_Click(null, null);
                int targetPage = GetCurrentPageIndex();
                
                // 等待页面切换完成
                await System.Threading.Tasks.Task.Delay(300);

                if (GetCurrentPageIndex() != targetPage)
                {
                    SwitchToPage(targetPage);
                }

                // 插入照片
                InsertPhotoToCanvas(photo);

                // 插入后同步一次摄像头设备侧栏选中显示（仅视觉同步，不触发逻辑）
                try
                {
                    cameraDeviceManager?.HandlePageChanged(targetPage);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"插入照片后刷新摄像头设备选中显示失败: {ex.Message}");
                }

                // 插入后再次同步侧栏选中显示，保证首次插入场景视觉稳定
                try
                {
                    UpdatePhotoSelectionIndicators();
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"插入照片后刷新侧栏选中显示失败: {ex.Message}");
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"切换到下一页并插入照片失败: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        // 跳转到指定页码
        public void SwitchToPage(int pageIndex)
        {
            try
            {
                int currentPage = GetCurrentPageIndex();
                if (pageIndex != currentPage)
                {
                    // 跳转页面前先排空残留输入并开启过渡窗口，再取消进行中笔画，避免卡顿时延迟提交的笔迹落到错误的页面
                    BeginBoardModeSwitch();
                    CancelInProgressStroke();

                    // 保存当前页面的墨迹
                    SaveStrokes(false);
                    // 保存当前文档页内容
                    SaveDocumentPageIfNeeded(currentPage);

                    // 清除当前画布
                    ClearStrokes(true);

                    // 重置摄像头画面和照片引用
                    currentCameraImage = null;
                    currentPhotoImage = null;

                    // 设置新的页码
                    CurrentWhiteboardIndex = pageIndex;

                    // 优先尝试恢复文档页；未恢复再尝试会话恢复与墨迹历史
                    bool documentRestored = false;
                    try { documentRestored = RestoreDocumentPageIfAvailable(pageIndex); } catch { }
                    if (!documentRestored)
                    {
                        try { RestorePageFromDiskIfAvailable(pageIndex); } catch { }
                        RestoreStrokes(false);
                    }

                    // 处理页面切换时的照片显示逻辑
                    HandlePhotoDisplayOnPageChange(pageIndex);
                    // 再次仅刷新侧栏选中状态，确保视觉完全同步
                    UpdatePhotoSelectionIndicators();
                    
                    // 通知摄像头管理器页面切换
                    cameraDeviceManager?.HandlePageChanged(pageIndex);
                    
                    // 更新页面显示
                    UpdateIndexInfoDisplay();
                    // 更新拍照按钮状态
                    UpdateCapturePhotoButtonState();
                    
                    Console.WriteLine($"已成功切换到页码: {pageIndex}");
                }
                else
                {
                    Console.WriteLine($"当前已在页码: {pageIndex}");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"切换到页码 {pageIndex} 失败: {ex.Message}");
            }
        }

        // 移除摄像头画面
        public void RemoveCameraFrame()
        {
            // 不停止定时器，以便翻页后可以继续使用
            
            // 移除页面上所有摄像头画面元素，避免残留导致重复
            ClearCameraElementsFromCanvas();
            
            // 更新拍照按钮状态
            UpdateCapturePhotoButtonState();
        }

        // 清除画布上的所有摄像头画面元素
        private void ClearCameraElementsFromCanvas()
        {
            try
            {
                if (inkCanvas == null) return;
                var cameraElements = new List<System.Windows.Controls.Image>();
                foreach (var child in inkCanvas.Children)
                {
                    if (child is System.Windows.Controls.Image image &&
                        image.Name != null &&
                        image.Name.StartsWith("camera_"))
                    {
                        cameraElements.Add(image);
                    }
                }
                foreach (var img in cameraElements)
                {
                    inkCanvas.Children.Remove(img);
                }
                currentCameraImage = null;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"清除摄像头画面元素失败: {ex.Message}");
            }
        }

        /// <summary>
        /// 摄像头画面更新定时器事件
        /// </summary>
        private async void CameraFrameTimer_Tick(object sender, EventArgs e)
        {
            if (cameraDeviceManager == null) return;

            Bitmap frame = null;
            try
            {
                if (currentCameraImage == null)
                {
                    foreach (var child in inkCanvas.Children)
                    {
                        if (child is System.Windows.Controls.Image image &&
                            image.Name != null &&
                            image.Name.StartsWith("camera_"))
                        {
                            currentCameraImage = image;
                            break;
                        }
                    }
                }

                if (currentCameraImage == null)
                {
                    cameraFrameTimer?.Stop();
                    return;
                }

                frame = cameraDeviceManager.GetFrameCopy();
                if (frame != null && currentCameraImage != null)
                {
                    await UpdateCameraFrameAsync(frame);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"摄像头画面定时器更新失败: {ex.Message}");
            }
            finally
            {
                frame?.Dispose();
            }
        }

        private async Task InsertCameraFrameAsync(Bitmap frame)
        {
            try
            {
                // 转换Bitmap到BitmapImage
                var bitmapImage = await Task.Run(() =>
                {
                    using (var memoryStream = new MemoryStream())
                    {
                        frame.Save(memoryStream, System.Drawing.Imaging.ImageFormat.Png);
                        memoryStream.Position = 0;

                        var bitmap = new BitmapImage();
                        bitmap.BeginInit();
                        bitmap.StreamSource = memoryStream;
                        bitmap.CacheOption = BitmapCacheOption.OnLoad;
                        bitmap.EndInit();
                        bitmap.Freeze();

                        return bitmap;
                    }
                });

                // 创建图片元素
                currentCameraImage = new System.Windows.Controls.Image
                {
                    Source = bitmapImage,
                    Width = bitmapImage.PixelWidth,
                    Height = bitmapImage.PixelHeight,
                    Name = "camera_" + DateTime.Now.ToString("yyyyMMdd_HH_mm_ss_fff")
                };

                // 居中并缩放
                CenterAndScaleElement(currentCameraImage);

                // 添加到画布
                InkCanvas.SetLeft(currentCameraImage, 0);
                InkCanvas.SetTop(currentCameraImage, 0);
                inkCanvas.Children.Add(currentCameraImage);

                // 记录历史
                timeMachine.CommitElementInsertHistory(currentCameraImage);
                try
                {
                    var page = GetCurrentPageIndex();
                    cameraFramesByPage[page] = currentCameraImage;
                }
                catch { }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"插入摄像头画面失败: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        // 更新摄像头画面
        private async Task UpdateCameraFrameAsync(Bitmap frame)
        {
            try
            {
                if (currentCameraImage == null) return;

                // 转换Bitmap到BitmapImage
                var bitmapImage = await Task.Run(() =>
                {
                    if (frame == null) return null;
                    
                    using (var memoryStream = new MemoryStream())
                    {
                        frame.Save(memoryStream, System.Drawing.Imaging.ImageFormat.Png);
                        memoryStream.Position = 0;

                        var bitmap = new BitmapImage();
                        bitmap.BeginInit();
                        bitmap.StreamSource = memoryStream;
                        bitmap.CacheOption = BitmapCacheOption.OnLoad;
                        bitmap.EndInit();
                        bitmap.Freeze();

                        return bitmap;
                    }
                });

                // 在UI线程更新图片源
                if (bitmapImage != null && currentCameraImage != null)
                {
                    await Dispatcher.BeginInvoke(new Action(() =>
                    {
                        if (currentCameraImage != null)
                        {
                            currentCameraImage.Source = bitmapImage;
                        }
                    }));
                }
            }
            catch (Exception ex)
            {
                // 静默处理更新错误，避免频繁弹窗
                Console.WriteLine($"更新摄像头画面失败: {ex.Message}");
            }
        }

        /// <summary>侧栏"插入媒体"按钮：导入图片/视频后统一显示在照片列表中</summary>
        private async void BtnInsertMediaInSidebar_Click(object sender, RoutedEventArgs e)
        {
            // 实时检查视频控件 plugin 是否可用
            var host = Plugins.PluginHost.Instance;
            bool videoAvailable = host != null && host.IsRouteAvailable("video-insert");

            string filter;
            if (videoAvailable)
            {
                filter = "图片/视频 (*.jpg;*.jpeg;*.png;*.bmp;*.mp4;*.avi;*.wmv)|*.jpg;*.jpeg;*.png;*.bmp;*.mp4;*.avi;*.wmv|图片 (*.jpg;*.jpeg;*.png;*.bmp)|*.jpg;*.jpeg;*.png;*.bmp|视频 (*.mp4;*.avi;*.wmv)|*.mp4;*.avi;*.wmv";
            }
            else
            {
                filter = "图片 (*.jpg;*.jpeg;*.png;*.bmp)|*.jpg;*.jpeg;*.png;*.bmp";
            }

            var openFileDialog = new Microsoft.Win32.OpenFileDialog { Filter = filter };
            if (openFileDialog.ShowDialog() != true) return;

            string filePath = openFileDialog.FileName;
            string ext = (System.IO.Path.GetExtension(filePath) ?? string.Empty).ToLowerInvariant();
            var imageExts = new HashSet<string> { ".jpg", ".jpeg", ".png", ".bmp" };
            var videoExts = new HashSet<string> { ".mp4", ".avi", ".wmv" };

            if (imageExts.Contains(ext))
            {
                // 图片：加载为 BitmapImage 后加入照片列表
                try
                {
                    var bitmap = new BitmapImage();
                    bitmap.BeginInit();
                    bitmap.CacheOption = BitmapCacheOption.OnLoad;
                    bitmap.UriSource = new Uri(filePath, UriKind.Absolute);
                    bitmap.EndInit();
                    bitmap.Freeze();

                    AddImportedImageToPhotoList(bitmap, filePath);
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"加载图片失败：{ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
            else if (videoExts.Contains(ext))
            {
                // 视频：检查插件可用性后加入照片列表（带视频标记）
                if (!videoAvailable)
                {
                    MessageBox.Show("未安装视频控件 plugin，无法导入视频。请到插件工坊安装 videocontrols。", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }
                AddImportedVideoToPhotoList(filePath);
            }
            else
            {
                MessageBox.Show("不支持的媒体格式", "插入媒体", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }

        /// <summary>将导入的图片加入照片列表</summary>
        private void AddImportedImageToPhotoList(BitmapImage bitmap, string filePath)
        {
            try
            {
                var captured = new CapturedImage(bitmap, filePath);
                capturedPhotos.Insert(0, captured);
                UpdateCapturedPhotosDisplay();
                Console.WriteLine($"图片已导入照片列表：{filePath}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"导入图片到照片列表失败：{ex.Message}");
            }
        }

        /// <summary>将导入的视频加入照片列表（使用占位缩略图 + 视频标记）</summary>
        private void AddImportedVideoToPhotoList(string videoFilePath)
        {
            try
            {
                var placeholder = CapturedImage.CreateVideoPlaceholderThumbnail();
                var captured = new CapturedImage(placeholder, videoFilePath, isVideo: true);
                capturedPhotos.Insert(0, captured);
                UpdateCapturedPhotosDisplay();
                Console.WriteLine($"视频已导入照片列表：{videoFilePath}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"导入视频到照片列表失败：{ex.Message}");
            }
        }

        // 查找当前摄像头画面元素
        private void FindCurrentCameraImage()
        {
            currentCameraImage = null;
            foreach (var child in inkCanvas.Children)
            {
                if (child is System.Windows.Controls.Image image && 
                    image.Name != null && 
                    image.Name.StartsWith("camera_"))
                {
                    currentCameraImage = image;
                    break;
                }
            }
        }

        /// <summary>拖拽插入位置的虚线框 Adorner，确保拖拽期间始终可见</summary>
        private class DragInsertionAdorner : Adorner
        {
            private readonly System.Windows.Media.Pen _pen;
            private readonly System.Windows.Media.Brush _fill;
            private System.Windows.Point _position;
            private double _indicatorWidth = 220;
            private double _indicatorHeight = 140;

            public DragInsertionAdorner(UIElement adornedElement) : base(adornedElement)
            {
                var strokeColor = System.Windows.Media.Color.FromRgb(0x4A, 0x90, 0xD9);
                _pen = new System.Windows.Media.Pen(new SolidColorBrush(strokeColor), 2);
                _pen.DashStyle = new DashStyle(new double[] { 5, 3 }, 0);
                _pen.Freeze();
                _fill = new SolidColorBrush(System.Windows.Media.Color.FromArgb(0x22, 0x4A, 0x90, 0xD9));
                _fill.Freeze();
                IsHitTestVisible = false;
            }

            public void SetPosition(System.Windows.Point pos)
            {
                _position = pos;
                InvalidateVisual();
            }

            public void SetSize(double width, double height)
            {
                _indicatorWidth = width;
                _indicatorHeight = height;
                InvalidateVisual();
            }

            protected override void OnRender(DrawingContext drawingContext)
            {
                double x = _position.X - _indicatorWidth / 2;
                double y = _position.Y - _indicatorHeight / 2;
                var rect = new System.Windows.Rect(x, y, _indicatorWidth, _indicatorHeight);
                drawingContext.DrawRoundedRectangle(_fill, _pen, rect, 4, 4);
            }
        }
    }
}
