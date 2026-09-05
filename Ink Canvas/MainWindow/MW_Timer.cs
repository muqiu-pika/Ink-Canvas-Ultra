using Ink_Canvas.Helpers;
using System;
using System.Diagnostics;
using System.Timers;
using System.Windows;
using MessageBox = System.Windows.MessageBox;

namespace Ink_Canvas
{
    public partial class MainWindow : System.Windows.Window
    {
        Timer timerCheckPPT = new Timer();
        Timer timerKillProcess = new Timer();
        Timer timerCheckAutoFold = new Timer();
        Timer timerFixFloatingBarZOrder = new Timer();
        string AvailableLatestVersion = null;
        // 待静默安装的版本：仅在"下载成功"时赋值，避免被后续检查（如网络抖动返回 null）
        // 覆盖 AvailableLatestVersion 后，定时器拿 null 拼安装包路径导致静默更新永远不触发。
        string _silentInstallVersion = null;
        Timer timerCheckAutoUpdateWithSilence = new Timer();
        bool isHidingSubPanelsWhenInking = false; // 避免书写时触发二次关闭二级菜单导致动画不连续
        DateTime _lastFixFloatingBarZOrderTimeUtc = DateTime.MinValue;

        private void InitTimers()
        {
            timerCheckPPT.Elapsed += TimerCheckPPT_Elapsed;
            timerCheckPPT.Interval = 1000;
            timerKillProcess.Elapsed += TimerKillProcess_Elapsed;
            timerKillProcess.Interval = 5000;
            timerCheckAutoFold.Elapsed += TimerCheckAutoFold_Elapsed;
            timerCheckAutoFold.Interval = 1500;
            timerFixFloatingBarZOrder.Elapsed += TimerFixFloatingBarZOrder_Elapsed;
            timerFixFloatingBarZOrder.Interval = 900;
            timerCheckAutoUpdateWithSilence.Elapsed += TimerCheckAutoUpdateWithSilence_Elapsed;
            timerCheckAutoUpdateWithSilence.Interval = 1000 * 60 * 10;
        }

        private void TimerKillProcess_Elapsed(object sender, ElapsedEventArgs e)
        {
            try
            {
                // 希沃相关： easinote swenserver RemoteProcess EasiNote.MediaHttpService smartnote.cloud EasiUpdate smartnote EasiUpdate3 EasiUpdate3Protect SeewoP2P CefSharp.BrowserSubprocess SeewoUploadService
                string arg = "/F";
                if (Settings.Automation.IsAutoKillPptService)
                {
                    Process[] processes = Process.GetProcessesByName("PPTService");
                    if (processes.Length > 0)
                    {
                        arg += " /IM PPTService.exe";
                    }
                    processes = Process.GetProcessesByName("SeewoIwbAssistant");
                    if (processes.Length > 0)
                    {
                        arg += " /IM SeewoIwbAssistant.exe" + " /IM Sia.Guard.exe";
                    }
                }
                if (Settings.Automation.IsAutoKillEasiNote)
                {
                    Process[] processes = Process.GetProcessesByName("EasiNote");
                    if (processes.Length > 0)
                    {
                        arg += " /IM EasiNote.exe";
                    }
                }
                if (arg != "/F")
                {
                    var p = new Process
                    {
                        StartInfo = new ProcessStartInfo("taskkill", arg)
                        {
                            WindowStyle = ProcessWindowStyle.Hidden
                        }
                    };
                    p.Start();

                    if (arg.Contains("EasiNote"))
                    {
                        BtnSwitch_Click(null, null);
                        MessageBox.Show("“希沃白板 5”已自动关闭");
                    }
                }
            }
            catch { }
        }


        bool foldFloatingBarByUser = false, // 保持收纳操作不受自动收纳的控制
            unfoldFloatingBarByUser = false; // 允许用户在希沃软件内进行展开操作

        private void TimerCheckAutoFold_Elapsed(object sender, ElapsedEventArgs e)
        {
            if (isFloatingBarChangingHideMode)
            {
                // 自愈：收起/展开锁应只持续一两秒；若超时仍未复位（如异常导致），
                // 强制解锁，确保关闭 seewo 视频展台等外部窗口后仍能自动展开。
                if (Environment.TickCount - _floatingBarHideModeChangeTick < 3000) return;
                isFloatingBarChangingHideMode = false;
            }
            try
            {
                string windowProcessName = ForegroundWindowInfo.ProcessName();
                string windowTitle = ForegroundWindowInfo.WindowTitle();
                //LogHelper.WriteLogToFile("windowTitle | " + windowTitle + " | windowProcessName | " + windowProcessName);

                if (Settings.Automation.IsAutoFoldInEasiNote && windowProcessName == "EasiNote" // 希沃白板
                    && (!(windowTitle.Length == 0 && ForegroundWindowInfo.WindowRect().Height < 500) || !Settings.Automation.IsAutoFoldInEasiNoteIgnoreDesktopAnno)
                    || Settings.Automation.IsAutoFoldInEasiCamera && windowProcessName == "EasiCamera" // 希沃视频展台
                    || Settings.Automation.IsAutoFoldInEasiNote3C && windowProcessName == "EasiNote" // 希沃轻白板
                    || Settings.Automation.IsAutoFoldInSeewoPincoTeacher && (windowProcessName == "BoardService" || windowProcessName == "seewoPincoTeacher") // 希沃品课
                    || Settings.Automation.IsAutoFoldInHiteCamera && windowProcessName == "HiteCamera" // 鸿合视频展台
                    || Settings.Automation.IsAutoFoldInHiteTouchPro && windowProcessName == "HiteTouchPro" // 鸿合白板
                    || Settings.Automation.IsAutoFoldInWxBoardMain && windowProcessName == "WxBoardMain" // 文香白板
                    || Settings.Automation.IsAutoFoldInMSWhiteboard && (windowProcessName == "MicrosoftWhiteboard" || windowProcessName == "msedgewebview2") // 微软白板
                    || Settings.Automation.IsAutoFoldInOldZyBoard && // 中原旧白板
                    (WinTabWindowsChecker.IsWindowExisted("WhiteBoard - DrawingWindow")
                    || WinTabWindowsChecker.IsWindowExisted("InstantAnnotationWindow")))
                {
                    if (!unfoldFloatingBarByUser && !isFloatingBarFolded)
                    {
                        FoldFloatingBar_Click(null, null);
                    }
                }
                else if (WinTabWindowsChecker.IsWindowExisted("幻灯片放映", false))
                { // 处于幻灯片放映状态
                    if (!Settings.Automation.IsAutoFoldInPPTSlideShow && isFloatingBarFolded && !foldFloatingBarByUser)
                    {
                        UnFoldFloatingBar_MouseUp(null, null);
                    }
                }
                else
                {
                    if (isFloatingBarFolded && !foldFloatingBarByUser)
                    {
                        UnFoldFloatingBar_MouseUp(null, null);
                    }
                    unfoldFloatingBarByUser = false;
                }
            }
            catch { }
        }

        private async void TimerCheckAutoUpdateWithSilence_Elapsed(object sender, ElapsedEventArgs e)
        {
            bool shouldSkipSilentUpdate = false;
            Dispatcher.Invoke(() =>
            {
                try
                {
                    // 在 UI 线程读取前台/笔迹状态，结果透出到外层方法决定是否跳过本 tick。
                    // 不再依赖 Topmost：批注等模式下 Topmost 为 false，若以此为准会永不被触发，
                    // 导致静默更新永远装不上。这里只要求当前无笔迹（避免安装关窗破坏书写内容）。
                    shouldSkipSilentUpdate = (inkCanvas.Strokes.Count > 0);
                }
                catch (Exception ex)
                {
                    LogHelper.WriteLogToFile(ex.ToString(), LogHelper.LogType.Error);
                }
            });
            // 必须先于静默安装判断——方法级 return 才能真的阻止安装
            if (shouldSkipSilentUpdate) return;
            try
            {
                if (AutoUpdateWithSilenceTimeComboBox.CheckIsInSilencePeriod(Settings.Startup.AutoUpdateWithSilenceStartTime, Settings.Startup.AutoUpdateWithSilenceEndTime))
                {
                    // 使用"静默分支记录"的 _silentInstallVersion，而非共享字段 AvailableLatestVersion：
                    // 后者会被后续检查无条件覆盖（网络异常时为 null），拿 null 拼路径会永远安装不上。
                    if (string.IsNullOrEmpty(_silentInstallVersion))
                    {
                        // 待安装版本缺失（异常兜底）：停止定时器，避免永久空转
                        timerCheckAutoUpdateWithSilence.Stop();
                        return;
                    }

                    // 静默时段到点：先下载安装包（若此前未下载/下载失败会重新下载；已下载完成则直接复用）。
                    bool downloaded = false;
                    if (Settings.Startup.IsAutoUpdateWithProxy) downloaded = await AutoUpdateHelper.DownloadSetupFileAndSaveStatus(_silentInstallVersion, Settings.Startup.AutoUpdateProxy);
                    else downloaded = await AutoUpdateHelper.DownloadSetupFileAndSaveStatus(_silentInstallVersion);

                    if (!downloaded)
                    {
                        // 本次下载失败：不停止定时器，等下一个 tick（10 分钟后）自动重试
                        LogHelper.WriteLogToFile($"静默更新下载失败，将于 10 分钟后自动重试: v{_silentInstallVersion}", LogHelper.LogType.Warning);
                        return;
                    }

                    AutoUpdateHelper.InstallNewVersionApp(_silentInstallVersion, true);
                    timerCheckAutoUpdateWithSilence.Stop();
                }
            }
            catch (Exception ex)
            {
                LogHelper.WriteLogToFile(ex.ToString(), LogHelper.LogType.Error);
            }
        }

        private void TimerFixFloatingBarZOrder_Elapsed(object sender, ElapsedEventArgs e)
        {
            try
            {
                // ViewboxFloatingBar 是 DispatcherObject（UI 元素）。System.Timers.Timer.Elapsed
                // 在 ThreadPool 线程上触发，若直接读取其 .Visibility 会抛出 InvalidOperationException
                // （"调用线程无法访问此对象，因为另一个线程拥有它"）。
                // 把"判空 / 可见性 / 窗口类名 / 置顶"整体封送到 UI 线程执行：
                // 既消除跨线程异常，也修复此前因提前抛异常而导致窗口置顶逻辑从未真正执行的问题。
                Dispatcher.BeginInvoke((Action)(() =>
                {
                    try
                    {
                        if (currentMode != 0) return;
                        if (ViewboxFloatingBar == null) return;
                        if (ViewboxFloatingBar.Visibility != Visibility.Visible) return;

                        var nowUtc = DateTime.UtcNow;
                        if (nowUtc - _lastFixFloatingBarZOrderTimeUtc < TimeSpan.FromMilliseconds(900)) return;

                        string className = ForegroundWindowInfo.WindowClassName();
                        if (className != "Progman" && className != "WorkerW" && className != "Shell_TrayWnd") return;

                        _lastFixFloatingBarZOrderTimeUtc = nowUtc;
                        WindowFocusHelper.EnsureWindowTopmost(this, true);
                    }
                    catch { }
                }));
            }
            catch { }
        }
    }
}
