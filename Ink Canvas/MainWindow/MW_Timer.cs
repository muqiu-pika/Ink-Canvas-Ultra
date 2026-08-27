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
            if (isFloatingBarChangingHideMode) return;
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

        private void TimerCheckAutoUpdateWithSilence_Elapsed(object sender, ElapsedEventArgs e)
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
                    AutoUpdateHelper.InstallNewVersionApp(AvailableLatestVersion, true);
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
                if (currentMode != 0) return;
                if (ViewboxFloatingBar == null) return;
                if (ViewboxFloatingBar.Visibility != Visibility.Visible) return;

                var nowUtc = DateTime.UtcNow;
                if (nowUtc - _lastFixFloatingBarZOrderTimeUtc < TimeSpan.FromMilliseconds(900)) return;

                string className = ForegroundWindowInfo.WindowClassName();
                if (className != "Progman" && className != "WorkerW" && className != "Shell_TrayWnd") return;

                _lastFixFloatingBarZOrderTimeUtc = nowUtc;
                Dispatcher.BeginInvoke((Action)(() =>
                {
                    try
                    {
                        WindowFocusHelper.EnsureWindowTopmost(this, true);
                    }
                    catch { }
                }));
            }
            catch { }
        }
    }
}
