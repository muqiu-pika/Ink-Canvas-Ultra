using Ink_Canvas.Helpers;
using Microsoft.Office.Interop.PowerPoint;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Timers;
using System.Windows;
using System.Windows.Ink;
using System.Windows.Input;
using System.Windows.Media;
using Application = System.Windows.Application;
using File = System.IO.File;
using Microsoft.Office.Core;

namespace Ink_Canvas
{
    public partial class MainWindow : System.Windows.Window
    {
        public static Microsoft.Office.Interop.PowerPoint.Application pptApplication = null;
        public static Microsoft.Office.Interop.PowerPoint.Presentation presentation = null;
        public static Microsoft.Office.Interop.PowerPoint.Slides slides = null;
        public static Microsoft.Office.Interop.PowerPoint.Slide slide = null;
        public static int slidescount = 0;
        private int _isCheckingPptCom = 0;
        private DateTime _suspendPptComCheckUntilUtc = DateTime.MinValue;

        private void ToggleSwitchSupportWPS_Toggled(object sender, RoutedEventArgs e)
        {
            if (!isLoaded) return;

            Settings.PowerPointSettings.IsSupportWPS = ToggleSwitchSupportWPS.IsOn;
            SaveSettingsToFile();

            if (ToggleSwitchSupportWPS.IsOn && Settings.PowerPointSettings.PowerPointSupport)
            {
                // 开启 WPS 支持后立即恢复检测（timerCheckPPT 可能已因之前连接成功而停止）
                try { timerCheckPPT.Start(); } catch { }
            }
        }

        /// <summary>上一次 ROT 优选结果（-1=未知，0=未命中，1=命中），用于仅在状态变化时写日志，避免每轮 timer 刷屏。</summary>
        private static int _lastRotOutcome = -1;

        /// <summary>ROT 命中/未命中状态变化时记一条日志；其余轮次静默。</summary>
        private static void LogRotOutcomeIfChanged(bool hit, int priority)
        {
            int state = hit ? 1 : 0;
            if (state == _lastRotOutcome) return;
            _lastRotOutcome = state;
            LogHelper.WriteLogToFile(hit
                ? $"PPT ROT 优选命中（优先级 {priority}），跳过 COM 轮询"
                : "PPT ROT 未命中，转 COM 轮询兜底");
        }

        private void ToggleSwitchEnableRotFirst_Toggled(object sender, RoutedEventArgs e)
        {
            if (!isLoaded) return;

            Settings.PowerPointSettings.EnableRotFirst = ToggleSwitchEnableRotFirst.IsOn;
            SaveSettingsToFile();

            // 关闭时不重启/重建任何连接，仅下一轮 timer 生效
        }

        /// <summary>
        /// 按设置应用四个翻页控件的位置：底部两块可靠左/居中/靠右，
        /// 侧面两块可靠上/居中/靠下。0/1/2 之外的值按默认（0）处理。
        /// </summary>
        private void ApplyPPTNavigationPositions()
        {
            try
            {
                var s = Settings.PowerPointSettings;
                int bottomLeft = Math.Max(0, Math.Min(2, s.PptNavBottomLeftPosition));
                int bottomRight = Math.Max(0, Math.Min(2, s.PptNavBottomRightPosition));
                int sideLeft = Math.Max(0, Math.Min(2, s.PptNavSideLeftPosition));
                int sideRight = Math.Max(0, Math.Min(2, s.PptNavSideRightPosition));

                // 底部面板：水平位置可调，垂直保持贴底
                PPTNavigationBottomLeft.HorizontalAlignment = bottomLeft == 1 ? HorizontalAlignment.Center
                                                           : bottomLeft == 2 ? HorizontalAlignment.Right
                                                           : HorizontalAlignment.Left;
                PPTNavigationBottomLeft.Margin = bottomLeft == 1 ? new Thickness(0, 0, 0, 2) : new Thickness(2, 0, 0, 2);

                PPTNavigationBottomRight.HorizontalAlignment = bottomRight == 1 ? HorizontalAlignment.Center
                                                             : bottomRight == 2 ? HorizontalAlignment.Right
                                                             : HorizontalAlignment.Left;
                PPTNavigationBottomRight.Margin = bottomRight == 1 ? new Thickness(0, 0, 0, 2) : new Thickness(2, 0, 0, 2);

                // 侧面面板：垂直位置可调，水平保持贴边
                PPTNavigationSidesLeft.VerticalAlignment = (VerticalAlignment)sideLeft;
                PPTNavigationSidesRight.VerticalAlignment = (VerticalAlignment)sideRight;
            }
            catch (Exception ex)
            {
                LogHelper.WriteLogToFile("应用 PPT 翻页控件位置失败 " + ex.Message, LogHelper.LogType.Warning);
            }
        }

        public static bool IsWPSSupportOn => Settings.PowerPointSettings.IsSupportWPS;

        public static bool IsShowingRestoreHiddenSlidesWindow = false;

        private void TimerCheckPPT_Elapsed(object sender, ElapsedEventArgs e)
        {
            if (IsShowingRestoreHiddenSlidesWindow) return;
            if (DateTime.UtcNow < _suspendPptComCheckUntilUtc) return;
            try
            {
                if (!IsPresentationProcessRunning()) return;
                if (!IsWPSSupportOn && _procIdsWpp.Count > 0) return;

                if (Application.Current?.Dispatcher != null)
                {
                    Application.Current.Dispatcher.BeginInvoke((Action)(() =>
                    {
                        try
                        {
                            EnsurePptComConnectedFromTimer();
                        }
                        catch { }
                    }));
                }
            }
            catch { }
        }

        private static bool IsRetryableComException(Exception ex)
        {
            if (ex is COMException comEx)
            {
                int hr = comEx.HResult;
                if (hr == unchecked((int)0x80010001)) return true;
                if (hr == unchecked((int)0x8001010A)) return true;
                if (hr == unchecked((int)0x80010108)) return false;
                if (hr == unchecked((int)0x800401E3)) return false;
            }
            return false;
        }

        // ===== 演示进程快照（CPU/内存优化） =====
        // PPT 检测每秒一次，原先每个检测周期会用 GetProcessesByName 反复全量枚举进程表
        // （存在性判断、失效检查、WPS 候选判断合计最多十余次），每次枚举都遍历全部进程
        // 并为每个进程分配托管 Process 对象，是常驻的 CPU 与 GC 开销。
        // 改为一次全量枚举生成快照（含各演示进程的 PID 列表），同一周期内共享，
        // 快照有效期略小于检测间隔，对判断结果无感知影响。
        private static readonly object _presentationProcessScanLock = new object();
        private static DateTime _presentationProcessScanUtc = DateTime.MinValue;
        private static List<int> _procIdsPowerPnt = new List<int>();
        private static List<int> _procIdsWpp = new List<int>();
        private static List<int> _procIdsEt = new List<int>();
        private static List<int> _procIdsWpsOffice = new List<int>();
        private const int PresentationProcessSnapshotTtlMs = 900;

        private static void RefreshPresentationProcessSnapshot()
        {
            lock (_presentationProcessScanLock)
            {
                var now = DateTime.UtcNow;
                if ((now - _presentationProcessScanUtc).TotalMilliseconds < PresentationProcessSnapshotTtlMs) return;

                var powerPnt = new List<int>();
                var wpp = new List<int>();
                var et = new List<int>();
                var wpsOffice = new List<int>();
                try
                {
                    foreach (var process in Process.GetProcesses())
                    {
                        try
                        {
                            switch ((process.ProcessName ?? string.Empty).ToLowerInvariant())
                            {
                                case "powerpnt": powerPnt.Add(process.Id); break;
                                case "wpp": wpp.Add(process.Id); break;
                                case "et": et.Add(process.Id); break;
                                case "wpsoffice": wpsOffice.Add(process.Id); break;
                            }
                        }
                        catch
                        {
                            // 进程恰好退出时读取属性会抛：跳过该进程
                        }
                        finally
                        {
                            try { process.Dispose(); } catch { }
                        }
                    }
                }
                catch
                {
                    // 枚举失败视为本轮无演示进程，下一 tick 重试
                }

                _procIdsPowerPnt = powerPnt;
                _procIdsWpp = wpp;
                _procIdsEt = et;
                _procIdsWpsOffice = wpsOffice;
                _presentationProcessScanUtc = now;
            }
        }

        private static bool IsPresentationProcessRunning()
        {
            // 仅当 PowerPoint / WPS 演示进程确实存在时才尝试 GetActiveObject，
            // 避免"未打开 PPT"时每秒抛一次 COMException（RPC_E_SERVER_UNAVAILABLE）刷屏调试输出。
            RefreshPresentationProcessSnapshot();
            if (_procIdsPowerPnt.Count > 0) return true;
            if (IsWPSSupportOn)
            {
                if (_procIdsWpp.Count > 0) return true;
                if (_procIdsEt.Count > 0) return true;
                // 部分 WPS 版本以统一主进程 wpsoffice 运行演示组件，重装/升级后进程名可能变化
                if (_procIdsWpsOffice.Count > 0) return true;
            }
            return false;
        }

        [DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

        /// <summary>
        /// 判断已持有的 RCW 指向的演示程序服务进程是否已失效（重装 WPS/Office、旧进程被新进程取代等）。
        /// 失效后必须解除事件订阅并整体重连，否则会一直停在旧连接上——表现为"识别不出 WPS"，
        /// 且重新开关 PPT 支持也无效（旧引用未被清理）。
        /// </summary>
        private static bool IsPptComServerStale(Microsoft.Office.Interop.PowerPoint.Application app)
        {
            try
            {
                IntPtr hwnd = new IntPtr(app.HWND);
                if (hwnd == IntPtr.Zero) return false;
                uint pid;
                GetWindowThreadProcessId(hwnd, out pid);
                if (pid == 0) return false;
                RefreshPresentationProcessSnapshot();
                if (_procIdsPowerPnt.Contains((int)pid)) return false;
                if (IsWPSSupportOn)
                {
                    if (_procIdsWpp.Contains((int)pid)) return false;
                    if (_procIdsEt.Contains((int)pid)) return false;
                    if (_procIdsWpsOffice.Contains((int)pid)) return false;
                }
                // 句柄所属进程不在当前演示进程列表中：RCW 指向的是残留的旧进程
                return true;
            }
            catch
            {
                // 访问已退出的服务进程会抛 COMException 等：RCW 已失效
                return true;
            }
        }

        /// <summary>
        /// 解除事件订阅并清空全部 PPT COM 静态引用。用于服务进程失效、关闭 PPT 支持时的整体断开。
        /// </summary>
        private void ReleasePptComReferences()
        {
            var app = pptApplication;
            if (app != null)
            {
                try { app.PresentationClose -= PptApplication_PresentationClose; } catch { }
                try { app.SlideShowBegin -= PptApplication_SlideShowBegin; } catch { }
                try { app.SlideShowNextSlide -= PptApplication_SlideShowNextSlide; } catch { }
                try { app.SlideShowEnd -= PptApplication_SlideShowEnd; } catch { }
            }
            pptApplication = null;
            presentation = null;
            slides = null;
            slide = null;
            slidescount = 0;
        }

        private static bool TryPingPptApplication(Microsoft.Office.Interop.PowerPoint.Application app)
        {
            if (app == null) return false;
            try
            {
                _ = app.HWND;
                return true;
            }
            catch { return false; }
        }

        /// <summary>后台线程使用的同步 COM 重试：可重试异常则等待后重试，其余异常立即失败。</summary>
        private static bool RetryComSync(Action action, int maxAttempts, int delayMs)
        {
            for (int attempt = 0; attempt < maxAttempts; attempt++)
            {
                try
                {
                    action();
                    return true;
                }
                catch (Exception ex) when (IsRetryableComException(ex))
                {
                    Thread.Sleep(delayMs);
                }
                catch
                {
                    return false;
                }
            }
            return false;
        }

        private async void EnsurePptComConnectedFromTimer()
        {
            if (Interlocked.Exchange(ref _isCheckingPptCom, 1) == 1) return;
            try
            {
                RefreshPresentationProcessSnapshot();
                if (!IsWPSSupportOn && _procIdsWpp.Count > 0)
                {
                    return;
                }

                string className = ForegroundWindowInfo.WindowClassName();
                if (className == "Progman" || className == "WorkerW" || className == "Shell_TrayWnd")
                {
                    _suspendPptComCheckUntilUtc = DateTime.UtcNow.AddMilliseconds(1500);
                    return;
                }

                // 该状态只能由 UI 线程读取（"结束放映"按钮是否可见），先取快照再交给后台线程
                bool slideShowEndBtnVisible = BtnPPTSlideShowEnd != null
                    && BtnPPTSlideShowEnd.Visibility == Visibility.Visible;

                // 关键改动：COM 连接与文稿枚举全部移到后台线程执行。
                // 过去这段在 UI 线程同步执行 GetActiveObject、文稿属性轮询与事件订阅，
                // WPS/Office 一旦无响应（重装后首次启动、许可/升级对话框阻塞等），
                // 整个画板 UI 就会被卡死——表现为提示框弹出后按钮全部失灵、无法退出、书写偶发延迟。
                // 后台线程完成后只把纯托管结果带回 UI 线程创建提示框。
                PptComConnectResult result = await Task.Run(() => ConnectPptComFromWorker(slideShowEndBtnVisible));

                if (result == null || !result.Connected) return;
                if (pptApplication == null || presentation == null || slides == null) return;

                // 连接成功：停止周期性检测（PresentationClose / 设置开关会重新启动）
                timerCheckPPT.Stop();

                if (Settings.PowerPointSettings.IsNotifyPreviousPage && result.LastViewedPage > 0)
                {
                    // 同一文稿同一页码的跳转提示在短时间内只弹一次，避免反复重连时连环弹窗
                    string jumpKey = (result.PresentationName ?? "") + "#" + result.LastViewedPage;
                    if (jumpKey == _lastJumpPromptKey && (DateTime.UtcNow - _lastJumpPromptUtc).TotalMinutes < 5)
                    {
                        return;
                    }
                    _lastJumpPromptKey = jumpKey;
                    _lastJumpPromptUtc = DateTime.UtcNow;

                    int page = result.LastViewedPage;
                    var jumpNotification = new YesOrNoNotificationWindow($"上次播放到了第 {page} 页, 是否立即跳转", () =>
                    {
                        // 跳页是跨进程 COM 调用，WPS/Office 无响应时可能长时间阻塞：
                        // 必须放到后台线程执行，UI 线程绝不等它（否则提示框按钮会全部失灵、无法退出）
                        var appRef = pptApplication;
                        var presRef = presentation;
                        int cachedCount = slidescount;
                        System.Threading.Tasks.Task.Run(() =>
                        {
                            try
                            {
                                // 实时读取当前文稿页数，读取失败回落到连接时缓存的 slidescount
                                int totalSlides;
                                try { totalSlides = presRef.Slides.Count; }
                                catch { totalSlides = cachedCount; }

                                if (page > totalSlides)
                                {
                                    _ = Application.Current.Dispatcher.BeginInvoke((Action)(() =>
                                        ShowNotificationAsync($"记录的页码（第 {page} 页）超出演示文稿当前页数（{totalSlides} 页），已跳过跳转")));
                                    return;
                                }

                                if (appRef.SlideShowWindows.Count >= 1)
                                {
                                    presRef.SlideShowWindow.View.GotoSlide(page);
                                }
                                else
                                {
                                    presRef.Windows[1].View.GotoSlide(page);
                                }
                            }
                            catch (Exception jumpEx)
                            {
                                // COM 调用随时可能因文稿/窗口状态变化失败，记录日志而非让 UI 崩溃
                                LogHelper.WriteLogToFile($"跳转到上次播放页失败: {jumpEx}", LogHelper.LogType.Error);
                                _ = Application.Current.Dispatcher.BeginInvoke((Action)(() =>
                                    ShowNotificationAsync("跳转到上次播放页失败，演示文稿可能已发生变化")));
                            }
                        });
                    });
                    Helpers.WindowMemoryHelper.ReleaseOnClose(jumpNotification);
                    EnqueuePPTNotification(jumpNotification);
                }

                if (Settings.PowerPointSettings.IsNotifyHiddenPage && result.HasHiddenSlides && !IsShowingRestoreHiddenSlidesWindow)
                {
                    IsShowingRestoreHiddenSlidesWindow = true;
                    var hiddenSlidesNotification = new YesOrNoNotificationWindow("检测到此演示文档中包含隐藏的幻灯片，是否取消隐藏？",
                        () =>
                        {
                            // 遍历幻灯片并修改属性是跨进程 COM 调用，放到后台线程，UI 不等待
                            var slidesRef = slides;
                            System.Threading.Tasks.Task.Run(() =>
                            {
                                try
                                {
                                    foreach (Slide hiddenSlide in slidesRef)
                                    {
                                        if (hiddenSlide.SlideShowTransition.Hidden == Microsoft.Office.Core.MsoTriState.msoTrue)
                                        {
                                            hiddenSlide.SlideShowTransition.Hidden = Microsoft.Office.Core.MsoTriState.msoFalse;
                                        }
                                    }
                                }
                                catch (Exception unhideEx)
                                {
                                    LogHelper.WriteLogToFile("取消隐藏幻灯片失败: " + unhideEx.Message, LogHelper.LogType.Error);
                                }
                            });
                        });
                    Helpers.WindowMemoryHelper.ReleaseOnClose(hiddenSlidesNotification);
                    EnqueuePPTNotification(hiddenSlidesNotification);
                }

                if (Settings.PowerPointSettings.IsNotifyAutoPlayPresentation
                    && !slideShowEndBtnVisible
                    && result.HasSlideTimings)
                {
                    var autoPlayNotification = new YesOrNoNotificationWindow("检测到此演示文档中自动播放或排练计时已经启用，可能导致幻灯片自动翻页，是否取消？",
                        () =>
                        {
                            var presRef = presentation;
                            System.Threading.Tasks.Task.Run(() =>
                            {
                                try
                                {
                                    presRef.SlideShowSettings.AdvanceMode = PpSlideShowAdvanceMode.ppSlideShowManualAdvance;
                                }
                                catch (Exception autoPlayEx)
                                {
                                    LogHelper.WriteLogToFile("取消自动播放设置失败: " + autoPlayEx.Message, LogHelper.LogType.Error);
                                }
                            });
                        });
                    Helpers.WindowMemoryHelper.ReleaseOnClose(autoPlayNotification);
                    EnqueuePPTNotification(autoPlayNotification);
                }
            }
            catch (Exception ex)
            {
                LogHelper.WriteLogToFile(string.Format("TimerCheckPPT error: {0}", ex.ToString()), LogHelper.LogType.Error);
                timerCheckPPT.Start();
            }
            finally
            {
                Interlocked.Exchange(ref _isCheckingPptCom, 0);
            }
        }

        /// <summary>EnsurePptComConnectedFromTimer 后台阶段产出的纯托管结果（不携带任何 COM 对象）。</summary>
        private sealed class PptComConnectResult
        {
            public bool Connected;
            public string PresentationName;
            public int LastViewedPage;
            public bool HasHiddenSlides;
            public bool HasSlideTimings;
        }

        // 上次"上次播放到第 N 页"提示的键与时间，用于短期去重，避免反复重连时连环弹窗
        private string _lastJumpPromptKey = null;
        private DateTime _lastJumpPromptUtc = DateTime.MinValue;

        /// <summary>
        /// 在后台线程完成 PPT/WPS 的 COM 连接与文稿信息枚举。任何 COM 调用的阻塞都只影响本线程，不影响 UI。
        /// </summary>
        private PptComConnectResult ConnectPptComFromWorker(bool slideShowEndBtnVisible)
        {
            var result = new PptComConnectResult();
            try
            {
                // 1) 已有连接且服务进程仍存活：无事可做（保持与旧版一致的早退语义）
                if (pptApplication != null && TryPingPptApplication(pptApplication) && !IsPptComServerStale(pptApplication))
                {
                    return result;
                }

                // 2) 连接已失效（重装 WPS/Office、旧进程退出等）：解除事件订阅并整体重连，
                //    否则会一直停在旧连接上，重新开关 PPT 支持也无效
                ReleasePptComReferences();

                // 进程都未运行，直接跳过，避免每秒 GetActiveObject 抛 COMException 刷屏
                if (!IsPresentationProcessRunning()) return result;

                Microsoft.Office.Interop.PowerPoint.Application acquired = null;

                // 3) ROT 优选（第一优先连接源）：枚举运行对象表，按放映优先级挑选最佳实例
                //    （多开演示文稿时连到正在放映的那个）。未命中/异常/健康检查不通过时，
                //    acquired 保持 null，原样走下方 COM 轮询兜底，兜底路径语义不变。
                //    开关关闭（EnableRotFirst=false）时整块跳过，行为与未移植前完全一致。
                if (Settings.PowerPointSettings.EnableRotFirst)
                {
                    int rotPriority;
                    var rotApp = Helpers.PPTROTConnectionHelper.TryGetBestSlideShowApplication(
                        Settings.PowerPointSettings.IsSupportWPS, out rotPriority);
                    if (rotApp != null && rotPriority > 0 && TryPingPptApplication(rotApp))
                    {
                        acquired = rotApp;
                    }
                    LogRotOutcomeIfChanged(acquired != null, rotPriority);
                }

                // 4) 获取运行中的演示程序 COM 对象（COM 兜底）。WPS 重装/新版可能不再以
                //    PowerPoint.Application 注册 ROT，这里在 wpp/wpsoffice 进程存在时
                //    附加 WPS 专用 ProgID 依次尝试。ROT 已命中时 for 条件直接短路。
                var candidateProgIds = new List<string> { "PowerPoint.Application" };
                RefreshPresentationProcessSnapshot();
                if (_procIdsWpp.Count > 0 || _procIdsWpsOffice.Count > 0)
                {
                    candidateProgIds.Add("KWPP.Application");
                    candidateProgIds.Add("wpp.Application");
                }

                for (int attempt = 0; attempt < 6 && acquired == null; attempt++)
                {
                    foreach (string progId in candidateProgIds)
                    {
                        try
                        {
                            object raw = Marshal.GetActiveObject(progId);
                            if (raw is Microsoft.Office.Interop.PowerPoint.Application asPpt && TryPingPptApplication(asPpt))
                            {
                                acquired = asPpt;
                                break;
                            }
                        }
                        catch (Exception ex) when (IsRetryableComException(ex))
                        {
                            // ROT 尚未就绪等可重试情形：稍后整体重试
                        }
                        catch
                        {
                            // 该 ProgID 当前不可用（未注册/未运行）：尝试下一个候选
                        }
                    }
                    if (acquired == null && attempt < 5) Thread.Sleep(150);
                }

                if (acquired == null) return result;
                pptApplication = acquired;

                try { pptApplication.PresentationClose -= PptApplication_PresentationClose; } catch { }
                try { pptApplication.SlideShowBegin -= PptApplication_SlideShowBegin; } catch { }
                try { pptApplication.SlideShowNextSlide -= PptApplication_SlideShowNextSlide; } catch { }
                try { pptApplication.SlideShowEnd -= PptApplication_SlideShowEnd; } catch { }
                pptApplication.PresentationClose += PptApplication_PresentationClose;
                pptApplication.SlideShowBegin += PptApplication_SlideShowBegin;
                pptApplication.SlideShowNextSlide += PptApplication_SlideShowNextSlide;
                pptApplication.SlideShowEnd += PptApplication_SlideShowEnd;

                if (!RetryComSync(() => { presentation = pptApplication.ActivePresentation; }, 4, 150)) return result;
                if (presentation == null) return result;
                if (!RetryComSync(() => { slides = presentation.Slides; }, 4, 150)) return result;
                if (!RetryComSync(() => { slidescount = slides.Count; }, 4, 150)) return result;

                try
                {
                    // 在普通视图下这种方式可以获得当前选中的幻灯片对象
                    // 然而在阅读模式下，这种方式会出现异常
                    slide = slides[pptApplication.ActiveWindow.Selection.SlideRange.SlideNumber];
                }
                catch
                {
                    // 在阅读模式下出现异常时，通过下面的方式来获得当前选中的幻灯片对象
                    try
                    {
                        if (pptApplication.SlideShowWindows.Count >= 1)
                        {
                            slide = pptApplication.SlideShowWindows[1].View.Slide;
                        }
                    }
                    catch { }
                }

                result.Connected = true;
                try { result.PresentationName = presentation.Name; } catch { }

                // 4) 上次播放页（读盘，无 COM）：以文稿名对应目录下的 Position 文件为准
                try
                {
                    string safePresentationName = SanitizePathSegment(result.PresentationName);
                    string positionPath = System.IO.Path.Combine(
                        Settings.Automation.AutoSavedStrokesLocation,
                        "Auto Saved - Presentations",
                        safePresentationName,
                        "Position");
                    if (File.Exists(positionPath) && int.TryParse(File.ReadAllText(positionPath), out var page))
                    {
                        result.LastViewedPage = page;
                    }
                }
                catch { }

                // 5) 隐藏幻灯片检测
                if (Settings.PowerPointSettings.IsNotifyHiddenPage)
                {
                    try
                    {
                        foreach (Slide workerSlide in slides)
                        {
                            if (workerSlide.SlideShowTransition.Hidden == Microsoft.Office.Core.MsoTriState.msoTrue)
                            {
                                result.HasHiddenSlides = true;
                                break;
                            }
                        }
                    }
                    catch (Exception hiddenEx)
                    {
                        LogHelper.WriteLogToFile("检测隐藏幻灯片失败: " + hiddenEx.Message, LogHelper.LogType.Error);
                    }
                }

                // 6) 自动播放/排练计时检测（命中时直接改为手动翻页，与旧行为一致）
                if (Settings.PowerPointSettings.IsNotifyAutoPlayPresentation && !slideShowEndBtnVisible)
                {
                    try
                    {
                        foreach (Slide workerSlide in presentation.Slides)
                        {
                            if (workerSlide.SlideShowTransition.AdvanceOnTime == MsoTriState.msoTrue && workerSlide.SlideShowTransition.AdvanceTime > 0)
                            {
                                result.HasSlideTimings = true;
                                break;
                            }
                        }
                        if (result.HasSlideTimings)
                        {
                            presentation.SlideShowSettings.AdvanceMode = PpSlideShowAdvanceMode.ppSlideShowManualAdvance;
                        }
                    }
                    catch (Exception timingEx)
                    {
                        LogHelper.WriteLogToFile("检测排练计时失败: " + timingEx.Message, LogHelper.LogType.Error);
                    }
                }

                // 7) 如果检测到已经开始放映，则立即进入画板模式
                try
                {
                    if (pptApplication.SlideShowWindows.Count >= 1)
                    {
                        PptApplication_SlideShowBegin(pptApplication.SlideShowWindows[1]);
                    }
                }
                catch (Exception ex)
                {
                    LogHelper.WriteLogToFile(string.Format("Failed to enter PPT mode: {0}", ex.ToString()), LogHelper.LogType.Error);
                }

                return result;
            }
            catch (Exception ex)
            {
                LogHelper.WriteLogToFile(string.Format("TimerCheckPPT (worker) error: {0}", ex.ToString()), LogHelper.LogType.Error);
                return result;
            }
        }

        private void PptApplication_PresentationClose(Presentation Pres)
        {
            // pptApplication 的 RCW 绑定在创建它的线程（UI 线程），而本事件可能在其他线程派发；
            // 解绑/访问都可能抛 InvalidCastException(RPC_E_WRONG_THREAD)，故整体兜底避免异常冒泡卡死。
            try
            {
                if (pptApplication != null)
                {
                    pptApplication.PresentationClose -= PptApplication_PresentationClose;
                    pptApplication.SlideShowBegin -= PptApplication_SlideShowBegin;
                    pptApplication.SlideShowNextSlide -= PptApplication_SlideShowNextSlide;
                    pptApplication.SlideShowEnd -= PptApplication_SlideShowEnd;
                    pptApplication = null;
                }
            }
            catch (Exception ex)
            {
                LogHelper.WriteLogToFile("PowerPoint 演示文稿关闭事件清理失败: " + ex.Message, LogHelper.LogType.Error);
            }

            timerCheckPPT?.Start();

            // BeginInvoke：本事件可能在非 UI 线程派发，同步 Invoke 会与正被 COM 调用阻塞的
            // UI 线程互相等待（死锁）。只投递 UI 操作，不等它完成。
            _ = Application.Current.Dispatcher.BeginInvoke((Action)(() =>
            {
                //BtnPPTSlideShow.Visibility = Visibility.Collapsed;
                if (BtnPPTSlideShowEnd != null)
                {
                    BtnPPTSlideShowEnd.Visibility = Visibility.Collapsed;
                }
            }));

        }

        //bool isPresentationHaveBlackSpace = false;
        private string pptName = null;
        int currentShowPosition = -1;
        private void PptApplication_SlideShowBegin(SlideShowWindow Wn)
        {
            // 幂等保护：已在放映中则忽略重复的进入事件/定时器重连触发，
            // 避免把进入放映后所画的当前页笔迹误当作桌面批注备份而被清除/还原。
            if (_isPptSlideShowActive) return;

            // 与 SlideShowNextSlide 同理：事件可能由放映线程（非 UI 线程）派发，Wn 的 RCW 绑定在事件线程上，
            // 因此先在事件线程取齐 COM 数据，Dispatcher 内只使用纯托管值。
            int totalSlides;
            string presentationName;
            int showPosition;
            int fallbackSlideId = 0;
            Slides slidesObj = null;
            try
            {
                slidesObj = Wn.Presentation.Slides;
                totalSlides = slidesObj.Count;
                presentationName = Wn.Presentation.Name;
                showPosition = Wn.View.CurrentShowPosition;
                try { fallbackSlideId = Wn.View.Slide.SlideID; } catch { }
            }
            catch (Exception ex)
            {
                LogHelper.WriteLogToFile("PowerPoint Slide Show Begin: 读取放映信息失败 " + ex.Message, LogHelper.LogType.Error);
                return;
            }

            // SlideID 映射与旧版笔迹迁移同样在事件线程完成，避免把 Slides COM 对象带过线程边界。
            // 注意顺序：先清缓存再迁移，否则迁移进来的笔迹会被 UI 线程随后的 Clear 抹掉。
            _strokeCacheBySlideId.Clear();
            RebuildSlideIdMapping(slidesObj);
            if (Settings.PowerPointSettings.IsAutoSaveStrokesInPowerPoint)
            {
                string migrateFolderBase = System.IO.Path.Combine(
                    Settings.Automation.AutoSavedStrokesLocation,
                    "Auto Saved - Presentations",
                    SanitizePathSegment(presentationName));
                if (!Directory.Exists(migrateFolderBase))
                {
                    // 兼容旧版目录「名称_页数」：仅当新目录不存在时才迁移
                    string legacyPath = migrateFolderBase + "_" + totalSlides;
                    if (Directory.Exists(legacyPath))
                    {
                        ImportLegacyPositionStrokes(legacyPath, slidesObj);
                    }
                }
            }

            LogHelper.WriteLogToFile("PowerPoint Application Slide Show Begin", LogHelper.LogType.Event);
            // 本方法可能由放映事件线程或后台 COM 线程调用：必须用 BeginInvoke 异步投递，
            // 同步 Invoke 在 UI 线程正被 COM 调用阻塞时会互相等待形成恶性卡死
            _ = Application.Current.Dispatcher.BeginInvoke((Action)(() =>
            {
                if (Settings.Automation.IsAutoFoldInPPTSlideShow && !isFloatingBarFolded)
                {
                    FoldFloatingBar_Click(null, null);
                }
                else if (isFloatingBarFolded)
                {
                    UnFoldFloatingBar_MouseUp(null, null);
                }

                _isPptSlideShowActive = true;
                ResetTouchState();

                // 确保备份的笔迹是独立的，不会被后续操作影响
                if (currentMode == 0 && inkCanvas.Strokes.Count > 0)
                {
                    try
                    {
                        _desktopStrokesBackupStrokes = new StrokeCollection();
                        foreach (Stroke stroke in inkCanvas.Strokes)
                        {
                            _desktopStrokesBackupStrokes.Add(stroke.Clone());
                        }
                        
                        _desktopStrokesBackup = new MemoryStream();
                        inkCanvas.Strokes.Save(_desktopStrokesBackup);
                        _desktopStrokesBackup.Position = 0; // 确保流位置正确
                    }
                    catch (Exception ex)
                    {
                        LogHelper.WriteLogToFile("Failed to backup desktop strokes: " + ex.Message, LogHelper.LogType.Error);
                        _desktopStrokesBackup = null;
                        _desktopStrokesBackupStrokes = null;
                    }
                }
                else
                {
                    _desktopStrokesBackup = null;
                    _desktopStrokesBackupStrokes = null;
                }

                if (currentMode != 0)
                {
                    ImageBlackboard_Click(null, null);
                }
                lastDesktopInkColor = 1;

                slidescount = totalSlides;
                previousSlideID = 0;
                currentSlideID = 0;

                pptName = presentationName;
                LogHelper.NewLog("Name: " + presentationName);
                LogHelper.NewLog("Slides Count: " + slidescount.ToString());

                if (Settings.PowerPointSettings.IsAutoSaveStrokesInPowerPoint)
                {
                    string safePresentationName = SanitizePathSegment(presentationName);
                    string folderBase = System.IO.Path.Combine(
                        Settings.Automation.AutoSavedStrokesLocation,
                        "Auto Saved - Presentations",
                        safePresentationName);
                    if (Directory.Exists(folderBase))
                    {
                        LogHelper.WriteLogToFile("Found saved strokes", LogHelper.LogType.Trace);
                        int count = LoadStrokeCacheFromFolder(folderBase);
                        LogHelper.WriteLogToFile(string.Format("Loaded {0} saved strokes", count.ToString()));
                    }
                    // 旧版目录（名称_页数）的迁移已在事件线程完成，此处不再触碰 COM 对象
                }

                BtnPPTSlideShowEnd.Visibility = Visibility.Visible;

                ApplyPPTNavigationPositions();

                if (Settings.PowerPointSettings.IsShowBottomPPTNavigationPanel)
                {
                    AnimationsHelper.ShowWithScaleFromBottom(PPTNavigationBottomLeft);
                    AnimationsHelper.ShowWithScaleFromBottom(PPTNavigationBottomRight);
                }
                else
                {
                    PPTNavigationBottomLeft.Visibility = Visibility.Collapsed;
                    PPTNavigationBottomRight.Visibility = Visibility.Collapsed;
                }
                if (Settings.PowerPointSettings.IsShowSidePPTNavigationPanel)
                {
                    AnimationsHelper.ShowWithScaleFromLeft(PPTNavigationSidesLeft);
                    AnimationsHelper.ShowWithScaleFromRight(PPTNavigationSidesRight);
                }
                else
                {
                    PPTNavigationSidesLeft.Visibility = Visibility.Collapsed;
                    PPTNavigationSidesRight.Visibility = Visibility.Collapsed;
                }

                //BtnPPTSlideShow.Visibility = Visibility.Collapsed;
                BtnPPTSlideShowEnd.Visibility = Visibility.Visible;

                if (Settings.Appearance.IsColorfulViewboxFloatingBar)
                {
                    ViewboxFloatingBar.Opacity = 0.8;
                }
                else
                {
                    ViewboxFloatingBar.Opacity = 0.75;
                }

                if (Settings.PowerPointSettings.IsShowCanvasAtNewSlideShow && Main_Grid.Background == Brushes.Transparent)
                {
                    if (currentMode != 0)
                    {
                        currentMode = 0;
                        GridBackgroundCover.Visibility = Visibility.Collapsed;
                        AnimationsHelper.HideWithSlideAndFade(BlackboardLeftSide);
                        AnimationsHelper.HideWithSlideAndFade(BlackboardCenterSide);
                        AnimationsHelper.HideWithSlideAndFade(BlackboardRightSide);

                        ClearStrokes(true);
                    }
                    BtnHideInkCanvas_Click(null, null);
                }

                ClearStrokes(true);

                try
                {
                    int currentSlideIndex = showPosition;
                    int slideId = GetSlideIdForPosition(currentSlideIndex);
                    if (slideId == 0) slideId = fallbackSlideId;
                    currentSlideID = slideId;
                    MemoryStream initialMs = GetCachedStrokes(slideId, currentSlideIndex);
                    if (initialMs != null && initialMs.Length > 0)
                    {
                        initialMs.Position = 0;
                        inkCanvas.Strokes.Add(new StrokeCollection(initialMs));
                    }
                    currentShowPosition = currentSlideIndex;
                    previousSlideID = currentSlideIndex;
                }
                catch (Exception ex)
                {
                    LogHelper.WriteLogToFile("Failed to load initial slide strokes: " + ex.Message, LogHelper.LogType.Error);
                }

                BorderFloatingBarMainControls.Visibility = Visibility.Visible;

                if (Settings.PowerPointSettings.IsShowCanvasAtNewSlideShow)
                {
                    BtnColorRed_Click(null, null);
                }

                isEnteredSlideShowEndEvent = false;
                PptNavigationTextBlockBottom.Text = $"{showPosition}/{totalSlides}";
                LogHelper.NewLog("PowerPoint Slide Show Loading process complete");

                new Thread(new ThreadStart(() =>
                {
                    Thread.Sleep(100);
                    _ = Application.Current.Dispatcher.BeginInvoke((Action)(() =>
                    {
                        ViewboxFloatingBarMarginAnimation();
                    }));
                })).Start();
            }));
        }

        bool isEnteredSlideShowEndEvent = false; //防止重复调用本函数导致墨迹保存失效
        private async void PptApplication_SlideShowEnd(Presentation Pres)
        {
            // 与 SlideShowNextSlide/Begin 同理：事件线程可能与 UI 线程不同，
            // 因此 UI 操作统一走 Dispatcher（异步投递，绝不同步等待 UI 线程），COM 对象（Pres）只在事件线程上读取。
            _ = Application.Current.Dispatcher.BeginInvoke((Action)(() =>
            {
                if (isFloatingBarFolded) UnFoldFloatingBar_MouseUp(null, null);
            }));

            LogHelper.WriteLogToFile(string.Format("PowerPoint Slide Show End"), LogHelper.LogType.Event);
            if (isEnteredSlideShowEndEvent)
            {
                LogHelper.WriteLogToFile("Detected previous entrance, returning");
                return;
            }
            isEnteredSlideShowEndEvent = true;

            // await BeginInvoke：保证与后续保存/恢复阶段的顺序（旧版用同步 Invoke 的顺序语义），
            // 同时事件线程只是异步挂起，不会卡住 WPS/Office 的事件派发
            await Application.Current.Dispatcher.BeginInvoke((Action)(() =>
            {
                _isPptSlideShowActive = false;
                _isRestoringDesktopStrokesAfterPpt = true;
                try
                {
                    ResetTouchState();

                    //isPresentationHaveBlackSpace = false;

                    //BtnPPTSlideShow.Visibility = Visibility.Visible;
                    BtnPPTSlideShowEnd.Visibility = Visibility.Collapsed;
                    BtnPPTSlideShowEnd.Visibility = Visibility.Collapsed;
                    PPTNavigationBottomLeft.Visibility = Visibility.Collapsed;
                    PPTNavigationBottomRight.Visibility = Visibility.Collapsed;
                    PPTNavigationSidesLeft.Visibility = Visibility.Collapsed;
                    PPTNavigationSidesRight.Visibility = Visibility.Collapsed;

                    if (currentMode != 0)
                    {
                        ImageBlackboard_Click(null, null);
                    }
                }
                finally
                {
                    _isRestoringDesktopStrokesAfterPpt = false;
                }
            }));

            if (Settings.PowerPointSettings.IsAutoSaveStrokesInPowerPoint)
            {
                string safePresentationName;
                try { safePresentationName = SanitizePathSegment(Pres.Name); }
                catch
                {
                    // 放映结束瞬间 Pres 可能已不可访问，取不到名字就不写盘，避免异常中断后续清理
                    safePresentationName = SanitizePathSegment(pptName);
                    if (string.IsNullOrEmpty(safePresentationName)) safePresentationName = "Presentation";
                }
                // 自动保存目录不再附加页数，插入/删除页后目录名不变，笔迹不会因页数变化而丢失。
                string folderPath = System.IO.Path.Combine(
                    Settings.Automation.AutoSavedStrokesLocation,
                    "Auto Saved - Presentations",
                    safePresentationName);
                if (!Directory.Exists(folderPath))
                {
                    Directory.CreateDirectory(folderPath);
                }
                try
                {
                    File.WriteAllText(folderPath + "/Position", previousSlideID.ToString());
                }
                catch { }
                // 必须等这一步完成再落盘：SaveStrokeCacheToFolder 依赖刚写入的当前页缓存
                await Application.Current.Dispatcher.BeginInvoke((Action)(() =>
                {
                    try
                    {
                        MemoryStream ms = new MemoryStream();
                        inkCanvas.Strokes.Save(ms);
                        ms.Position = 0;
                        _strokeCacheBySlideId[GetStrokeCacheKey(currentSlideID, currentShowPosition)] = ms;
                    }
                    catch { }
                }));
                // 保存前按当前演示文稿重建 SlideID 映射，再按 SlideID 写盘。
                try { RebuildSlideIdMapping(Pres.Slides); } catch { }
                SaveStrokeCacheToFolder(folderPath);
            }

            // 阶段二（UI）：清空画布并恢复放映开始前备份的桌面墨迹（异步投递并等待完成）
            await Application.Current.Dispatcher.BeginInvoke((Action)(() =>
            {
                _isRestoringDesktopStrokesAfterPpt = true;
                try
                {
                    // Clear current PPT slide strokes before potentially restoring desktop strokes
                    ClearStrokes(true);
                    timeMachine.ClearStrokeHistory();

                    bool restored = false;

                    if (_desktopStrokesBackupStrokes != null && _desktopStrokesBackupStrokes.Count > 0)
                    {
                        try
                        {
                            inkCanvas.Strokes.Add(_desktopStrokesBackupStrokes);
                            LogHelper.WriteLogToFile("Restored desktop strokes from backup strokes collection", LogHelper.LogType.Trace);
                            restored = true;
                        }
                        catch (Exception ex)
                        {
                            LogHelper.WriteLogToFile("Failed to restore desktop strokes from backup strokes: " + ex.Message, LogHelper.LogType.Error);
                        }
                    }

                    if (!restored && _desktopStrokesBackup != null && _desktopStrokesBackup.Length > 0)
                    {
                        try
                        {
                            _desktopStrokesBackup.Position = 0;
                            var strokesFromStream = new StrokeCollection(_desktopStrokesBackup);
                            if (strokesFromStream.Count > 0)
                            {
                                inkCanvas.Strokes.Add(strokesFromStream);
                                LogHelper.WriteLogToFile("Restored desktop strokes from backup stream", LogHelper.LogType.Trace);
                                restored = true;
                            }
                        }
                        catch (Exception ex)
                        {
                            LogHelper.WriteLogToFile("Failed to restore desktop strokes from backup stream: " + ex.Message, LogHelper.LogType.Error);
                        }
                    }

                    _desktopStrokesBackupStrokes = null;
                    _desktopStrokesBackup?.Dispose();
                    _desktopStrokesBackup = null;

                    LogHelper.WriteLogToFile("Desktop strokes restore " + (restored ? "successful" : "failed"), LogHelper.LogType.Trace);

                    if (Main_Grid.Background != Brushes.Transparent)
                    {
                        BtnHideInkCanvas_Click(null, null);
                    }

                    if (Settings.Appearance.IsColorfulViewboxFloatingBar)
                    {
                        ViewboxFloatingBar.Opacity = 0.95;
                    }
                    else
                    {
                        ViewboxFloatingBar.Opacity = 1;
                    }

                    if (Settings.Canvas.HideStrokeWhenSelecting && inkCanvas.Strokes.Count > 0)
                    {
                        try
                        {
                            inkCanvas.Visibility = Visibility.Collapsed;
                            inkCanvas.Select(new StrokeCollection()); // 取消选中，避免后续再次显示时残留选择框
                            GridInkCanvasSelectionCover.Visibility = Visibility.Collapsed;
                        }
                        catch (Exception ex) { LogHelper.WriteLogToFile("退出放映后隐藏桌面批注失败: " + ex.Message, LogHelper.LogType.Error); }
                    }
                }
                finally
                {
                    _isRestoringDesktopStrokesAfterPpt = false;
                }
            }));

            await Task.Delay(150);
            // Task.Delay 后可能回到线程池线程（事件线程无同步上下文），必须回到 UI 线程再动 UI
            try { _ = Application.Current.Dispatcher.BeginInvoke((Action)(() => ViewboxFloatingBarMarginAnimation())); }
            catch { }
        }

        // 当前页位置与 SlideID。previousSlideID 仍表示"当前页码位置"（供跳转/截图命名使用），
        // currentSlideID 表示当前页对应的 Slide.SlideID，在插入/删除页后保持稳定。
        int previousSlideID = 0;
        int currentSlideID = 0;
        MemoryStream _desktopStrokesBackup = null;
        StrokeCollection _desktopStrokesBackupStrokes = null;
        bool _isRestoringDesktopStrokesAfterPpt = false;
        bool _isPptSlideShowActive = false;

        // 位置 -> SlideID 映射；按 SlideID 组织的笔迹缓存（键见 GetStrokeCacheKey）。
        Dictionary<int, int> _positionToSlideId = new Dictionary<int, int>();
        Dictionary<int, MemoryStream> _strokeCacheBySlideId = new Dictionary<int, MemoryStream>();

        /// <summary>重建"位置 -> SlideID"映射；WPS/PowerPoint 均支持 Slide.SlideID。</summary>
        private void RebuildSlideIdMapping(Slides slidesObj)
        {
            _positionToSlideId.Clear();
            if (slidesObj == null) return;
            try
            {
                int count = slidesObj.Count;
                for (int i = 1; i <= count; i++)
                {
                    try { _positionToSlideId[i] = slidesObj[i].SlideID; }
                    catch { _positionToSlideId[i] = 0; }
                }
            }
            catch (Exception ex)
            {
                LogHelper.WriteLogToFile("重建 SlideID 映射失败: " + ex.Message, LogHelper.LogType.Error);
            }
        }

        /// <summary>取某位置对应的 SlideID；映射缺失时回退读取放映视图的当前 Slide。</summary>
        private int GetSlideIdForPosition(int position, SlideShowWindow Wn = null)
        {
            int id;
            if (_positionToSlideId.TryGetValue(position, out id) && id != 0) return id;
            if (Wn != null)
            {
                try { return Wn.View.Slide.SlideID; }
                catch { }
            }
            return 0;
        }

        /// <summary>笔迹缓存键：优先 SlideID；读取失败时用负位置兜底，避免与真实小整数 SlideID 冲突。</summary>
        private static int GetStrokeCacheKey(int slideId, int position)
        {
            return slideId != 0 ? slideId : -position;
        }

        private MemoryStream GetCachedStrokes(int slideId, int position)
        {
            MemoryStream ms;
            if (_strokeCacheBySlideId.TryGetValue(GetStrokeCacheKey(slideId, position), out ms)) return ms;
            return null;
        }

        /// <summary>从目录加载按 SlideID 命名的笔迹文件（{slideID:00000000}.icstk/.icart）。</summary>
        private int LoadStrokeCacheFromFolder(string folderPath)
        {
            int loaded = 0;
            try
            {
                if (!Directory.Exists(folderPath)) return loaded;
                foreach (FileInfo file in new DirectoryInfo(folderPath).GetFiles())
                {
                    string name = Path.GetFileNameWithoutExtension(file.Name);
                    if (name == "Position" || name == "SlideMap") continue;
                    int sid;
                    if (!int.TryParse(name, out sid) || sid <= 0) continue;
                    try
                    {
                        _strokeCacheBySlideId[sid] = new MemoryStream(File.ReadAllBytes(file.FullName)) { Position = 0 };
                        loaded++;
                    }
                    catch (Exception ex)
                    {
                        LogHelper.WriteLogToFile("加载第 " + sid + " 页笔迹失败: " + ex.Message, LogHelper.LogType.Error);
                    }
                }
            }
            catch (Exception ex)
            {
                LogHelper.WriteLogToFile("从目录加载笔迹失败: " + ex.Message, LogHelper.LogType.Error);
            }
            return loaded;
        }

        /// <summary>迁移旧版"按位置命名"的笔迹目录（名称_页数）到当前 SlideID 缓存。</summary>
        private void ImportLegacyPositionStrokes(string folderPath, Slides slidesObj)
        {
            try
            {
                if (!Directory.Exists(folderPath)) return;
                foreach (FileInfo file in new DirectoryInfo(folderPath).GetFiles())
                {
                    string name = Path.GetFileNameWithoutExtension(file.Name);
                    if (name == "Position" || name == "SlideMap") continue;
                    int pos;
                    if (!int.TryParse(name, out pos) || pos <= 0) continue;
                    int sid = 0;
                    try { if (slidesObj != null && pos <= slidesObj.Count) sid = slidesObj[pos].SlideID; } catch { }
                    if (sid == 0) continue;
                    try { _strokeCacheBySlideId[sid] = new MemoryStream(File.ReadAllBytes(file.FullName)) { Position = 0 }; } catch { }
                }
            }
            catch (Exception ex)
            {
                LogHelper.WriteLogToFile("迁移旧版笔迹失败: " + ex.Message, LogHelper.LogType.Error);
            }
        }

        /// <summary>将按 SlideID 组织的缓存笔迹写入目录，并输出 SlideMap.txt 便于诊断。</summary>
        private void SaveStrokeCacheToFolder(string folderPath)
        {
            if (!Directory.Exists(folderPath)) Directory.CreateDirectory(folderPath);
            foreach (var kv in _positionToSlideId)
            {
                int position = kv.Key;
                int sid = kv.Value;
                if (sid == 0) continue;
                MemoryStream ms;
                if (!_strokeCacheBySlideId.TryGetValue(sid, out ms) || ms == null) continue;
                try
                {
                    string baseFilePath = folderPath + @"\" + sid.ToString("00000000");
                    string icartFilePath = baseFilePath + ".icart";
                    string icstkFilePath = baseFilePath + ".icstk";

                    if (ms.Length > 8)
                    {
                        byte[] srcBuf = new byte[ms.Length];
                        ms.Position = 0;
                        int byteLength = ms.Read(srcBuf, 0, srcBuf.Length);

                        if (File.Exists(icartFilePath))
                        {
                            File.WriteAllBytes(icartFilePath, srcBuf);
                            LogHelper.WriteLogToFile(string.Format("Saved strokes for Slide {0} (pos {1}) as .icart, size={2}, byteLength={3}", sid.ToString(), position.ToString(), ms.Length, byteLength));
                        }
                        else
                        {
                            File.WriteAllBytes(icstkFilePath, srcBuf);
                            LogHelper.WriteLogToFile(string.Format("Saved strokes for Slide {0} (pos {1}) as .icstk, size={2}, byteLength={3}", sid.ToString(), position.ToString(), ms.Length, byteLength));
                        }
                    }
                    else
                    {
                        File.Delete(icartFilePath);
                        File.Delete(icstkFilePath);
                    }
                }
                catch (Exception ex)
                {
                    LogHelper.WriteLogToFile("保存第 " + sid + " 页笔迹失败: " + ex.Message, LogHelper.LogType.Error);
                }
            }

            // 输出 SlideID -> 位置 映射，便于诊断笔迹与页的对应关系
            try
            {
                System.Text.StringBuilder map = new System.Text.StringBuilder();
                foreach (var kv in _positionToSlideId)
                {
                    map.AppendLine(kv.Value + "=" + kv.Key);
                }
                File.WriteAllText(folderPath + "/SlideMap.txt", map.ToString());
            }
            catch { }
        }

        private async void PptApplication_SlideShowNextSlide(SlideShowWindow Wn)
        {
            // PowerPoint/WPS 的 SlideShowNextSlide 事件可能由放映线程（非 UI 线程）派发，
            // Wn 的 RCW 绑定在事件线程上：跨 Dispatcher 到 UI 线程再访问 Wn 会抛
            // InvalidCastException（QueryInterface 失败，RPC_E_WRONG_THREAD「已为另一线程整理的接口」）。
            // 因此必须先在事件线程上取齐全部 COM 数据，再跨 Dispatcher 只传纯托管值。
            int newPosition;
            int totalSlides;
            string presentationName;
            int fallbackSlideId = 0;
            try
            {
                newPosition = Wn.View.CurrentShowPosition;
                totalSlides = Wn.Presentation.Slides.Count;
                presentationName = Wn.Presentation.Name;
                // 映射缺失时的兜底 SlideID，也需在事件线程读取（GetSlideIdForPosition 原在此处传 Wn）
                try { fallbackSlideId = Wn.View.Slide.SlideID; } catch { }
            }
            catch (Exception ex)
            {
                // 放映窗口可能正在关闭，读取失败时忽略本次翻页
                LogHelper.WriteLogToFile("PowerPoint Next Slide: 读取放映状态失败 " + ex.Message, LogHelper.LogType.Error);
                return;
            }

            LogHelper.WriteLogToFile(string.Format("PowerPoint Next Slide (Slide {0})", newPosition), LogHelper.LogType.Event);
            if (newPosition == previousSlideID) return;

            // 记录离开页的 SlideID（进入该页时写入的 currentSlideID）与旧位置，
            // 切页回调触发时 CurrentShowPosition 已经是新位置。
            int leavingSlideID = currentSlideID;
            int oldPosition = previousSlideID;

            // await BeginInvoke：异步投递且保证本事件处理完才返回，
            // 既不会与被 COM 阻塞的 UI 线程互相等待（死锁），又保住"缓存旧页→恢复新页"的顺序语义
            await Application.Current.Dispatcher.BeginInvoke((Action)(() =>
            {
                // 放映已经结束（结束放映的清理可能排在本事件之前执行）：迟到的切页事件
                // 绝不能再"缓存当前画布 → 清空 → 恢复该页笔迹"，否则上一页/当前页的
                // PPT 笔迹会被画回桌面画布（"退出放映后 PPT 墨迹出现在浮动栏层的画布上"）。
                if (!_isPptSlideShowActive)
                {
                    LogHelper.WriteLogToFile("PowerPoint Next Slide skipped: slideshow already ended", LogHelper.LogType.Trace);
                    return;
                }
                // 画板模式下画布是白板墨迹（批注墨迹已由进入画板时的 SaveStrokes(true) 暂存到
                // TimeMachineHistories[0]），不能把画布内容缓存为放映页笔迹，
                // 否则白板墨迹会污染演示文稿的页笔迹缓存与自动截图。
                if (currentMode == 0)
                {
                    MemoryStream ms = new MemoryStream();
                    inkCanvas.Strokes.Save(ms);
                    ms.Position = 0;
                    // 离开页的笔迹按该页 SlideID 缓存，插入/删除页后仍能正确对应。
                    _strokeCacheBySlideId[GetStrokeCacheKey(leavingSlideID, oldPosition)] = ms;
                }

                if (currentMode == 0 && inkCanvas.Strokes.Count > Settings.Automation.MinimumAutomationStrokeNumber && Settings.PowerPointSettings.IsAutoSaveScreenShotInPowerPoint && !_isPptClickingBtnTurned)
                    SavePPTScreenshot(presentationName + "/" + newPosition);
                _isPptClickingBtnTurned = false;

                ClearStrokes(true);
                timeMachine.ClearStrokeHistory();

                try
                {
                    int targetSlideId = GetSlideIdForPosition(newPosition);
                    if (targetSlideId == 0) targetSlideId = fallbackSlideId;
                    currentSlideID = targetSlideId;
                    MemoryStream targetMs = GetCachedStrokes(targetSlideId, newPosition);
                    if (targetMs != null && targetMs.Length > 0)
                    {
                        targetMs.Position = 0;
                        inkCanvas.Strokes.Add(new StrokeCollection(targetMs));
                    }
                    currentShowPosition = newPosition;
                }
                catch { }

                PptNavigationTextBlockBottom.Text = $"{newPosition}/{totalSlides}";
            }));
            previousSlideID = newPosition;

        }

        private bool _isPptClickingBtnTurned = false;

        private static string SanitizePathSegment(string name)
        {
            if (string.IsNullOrEmpty(name)) return string.Empty;
            var invalidChars = System.IO.Path.GetInvalidFileNameChars();
            var result = name;
            foreach (var ch in invalidChars)
            {
                result = result.Replace(ch, '_');
            }
            result = result.Replace('\\', '_').Replace('/', '_');
            return result.Trim();
        }

        // PPT 提示框串行队列：多个 YesOrNoNotificationWindow 原本各自 BeginInvoke + ShowDialog，
        // 而 ShowDialog 会泵 dispatcher，导致后一个在前一个未关时就弹出，形成嵌套模态框叠死
        // （顶层窗口冻住无法操作、底层需要的窗口被遮住）。改为入队、逐个 ShowDialog，关一个弹下一个。
        private readonly List<YesOrNoNotificationWindow> _pptNotificationQueue = new List<YesOrNoNotificationWindow>();
        private bool _pptNotificationShowing = false;

        private void EnqueuePPTNotification(YesOrNoNotificationWindow window)
        {
            if (window == null) return;
            // ReleaseOnClose 仍由各调用点在创建后调用，这里只负责串行弹出
            _pptNotificationQueue.Add(window);
            if (!_pptNotificationShowing)
            {
                _pptNotificationShowing = true;
                ShowNextPPTNotification();
            }
        }

        private void ShowNextPPTNotification()
        {
            if (_pptNotificationQueue.Count == 0)
            {
                _pptNotificationShowing = false;
                return;
            }
            var window = _pptNotificationQueue[0];
            _pptNotificationQueue.RemoveAt(0);
            bool advancedByClosedHandler = false;
            // 关闭后通过 BeginInvoke 回到 dispatcher 队列再弹下一个，避免递归嵌套
            window.Closed += (s, e) =>
            {
                advancedByClosedHandler = true;
                Application.Current.Dispatcher.BeginInvoke((Action)(() => ShowNextPPTNotification()));
            };
            try
            {
                window.ShowDialog();
            }
            catch (Exception showEx)
            {
                // ShowDialog 抛异常（窗口已被关闭/Owner 失效等）时绝不能让队列卡死：
                // 尝试关闭本窗口，由 finally 兜底推进队列，后续提示框仍能正常弹出
                LogHelper.WriteLogToFile($"PPT 提示框显示失败: {showEx}", LogHelper.LogType.Error);
                try { window.Close(); } catch { }
            }
            finally
            {
                if (!advancedByClosedHandler && _pptNotificationShowing)
                {
                    Application.Current.Dispatcher.BeginInvoke((Action)(() => ShowNextPPTNotification()));
                }
            }
        }

        private void BtnPPTSlidesUp_Click(object sender, RoutedEventArgs e)
        {
            if (currentMode == 1)
            {
                GridBackgroundCover.Visibility = Visibility.Collapsed;
                AnimationsHelper.HideWithSlideAndFade(BlackboardLeftSide);
                AnimationsHelper.HideWithSlideAndFade(BlackboardCenterSide);
                AnimationsHelper.HideWithSlideAndFade(BlackboardRightSide);
                currentMode = 0;
            }

            _isPptClickingBtnTurned = true;

            if (inkCanvas.Strokes.Count > Settings.Automation.MinimumAutomationStrokeNumber &&
                Settings.PowerPointSettings.IsAutoSaveScreenShotInPowerPoint)
                SavePPTScreenshot(pptApplication.SlideShowWindows[1].Presentation.Name + "/" + pptApplication.SlideShowWindows[1].View.CurrentShowPosition);

            try
            {
                new Thread(new ThreadStart(() =>
                {
                    try
                    {
                        pptApplication.SlideShowWindows[1].Activate();
                    }
                    catch { }
                    try
                    {
                        pptApplication.SlideShowWindows[1].View.Previous();
                    }
                    catch { } // Without this catch{}, app will crash when click the pre-page button in the fir page in some special env.
                })).Start();
            }
            catch { }
        }

        private void BtnPPTSlidesDown_Click(object sender, RoutedEventArgs e)
        {
            if (currentMode == 1)
            {
                GridBackgroundCover.Visibility = Visibility.Collapsed;
                AnimationsHelper.HideWithSlideAndFade(BlackboardLeftSide);
                AnimationsHelper.HideWithSlideAndFade(BlackboardCenterSide);
                AnimationsHelper.HideWithSlideAndFade(BlackboardRightSide);
                currentMode = 0;
            }
            _isPptClickingBtnTurned = true;
            if (inkCanvas.Strokes.Count > Settings.Automation.MinimumAutomationStrokeNumber &&
                Settings.PowerPointSettings.IsAutoSaveScreenShotInPowerPoint)
                SavePPTScreenshot(pptApplication.SlideShowWindows[1].Presentation.Name + "/" + pptApplication.SlideShowWindows[1].View.CurrentShowPosition);
            try
            {
                new Thread(new ThreadStart(() =>
                {
                    try
                    {
                        pptApplication.SlideShowWindows[1].Activate();
                    }
                    catch { }
                    try
                    {
                        pptApplication.SlideShowWindows[1].View.Next();
                    }
                    catch { }
                })).Start();
            }
            catch { }
        }

        private async void PPTNavigationBtn_Click(object sender, MouseButtonEventArgs e)
        {
            if (lastBorderMouseDownObject != sender) return;
            Main_Grid.Background = new SolidColorBrush(StringToColor("#01FFFFFF"));
            CursorIcon_Click(null, null);
            try
            {
                pptApplication.SlideShowWindows[1].SlideNavigation.Visible = true;
            }
            catch { }
            if (!isFloatingBarFolded)
            {
                await Task.Delay(100);
                ViewboxFloatingBarMarginAnimation();
            }
        }

        private async void BtnPPTSlideShowEnd_Click(object sender, RoutedEventArgs e)
        {
            new Thread(new ThreadStart(() =>
            {
                try
                {
                    pptApplication.SlideShowWindows[1].View.Exit();
                }
                catch { }
            })).Start();

            HideSubPanels("cursor");
            await Task.Delay(150);
            ViewboxFloatingBarMarginAnimation();
        }

        private void GridPPTControlPrevious_MouseUp(object sender, MouseButtonEventArgs e)
        {
            if (lastBorderMouseDownObject != sender) return;
            BtnPPTSlidesUp_Click(null, null);
        }

        private void GridPPTControlNext_MouseUp(object sender, MouseButtonEventArgs e)
        {
            if (lastBorderMouseDownObject != sender) return;
            BtnPPTSlidesDown_Click(null, null);
        }

        /// <summary>
        /// 在重启前保存当前演示文稿所有已缓存页面的墨迹快照，以及当前位置。
        /// 该方法复用 SlideShowEnd 的保存逻辑，但不进行任何 UI 切换或退出放映。
        /// </summary>
        private void SavePptSlidesSnapshotBeforeRestart()
        {
            try
            {
                Presentation pres = null;
                try { pres = pptApplication?.SlideShowWindows[1]?.Presentation; } catch { pres = null; }
                if (pres == null) return;

                string folderPath = Settings.Automation.AutoSavedStrokesLocation + @"\Auto Saved - Presentations\" + pres.Name;
                if (!Directory.Exists(folderPath))
                {
                    Directory.CreateDirectory(folderPath);
                }
                try
                {
                    File.WriteAllText(folderPath + "/Position", previousSlideID.ToString());
                }
                catch { }

                try
                {
                    Application.Current.Dispatcher.Invoke(() =>
                    {
                        try
                        {
                            MemoryStream ms = new MemoryStream();
                            inkCanvas.Strokes.Save(ms);
                            ms.Position = 0;
                            _strokeCacheBySlideId[GetStrokeCacheKey(currentSlideID, currentShowPosition)] = ms;
                        }
                        catch { }
                    });
                }
                catch { }

                // 保存前按当前演示文稿重建 SlideID 映射，再按 SlideID 写盘。
                try { RebuildSlideIdMapping(pres.Slides); } catch { }
                SaveStrokeCacheToFolder(folderPath);

                LogHelper.WriteLogToFile("Saved PPT slides snapshot before restart", LogHelper.LogType.Event);
            }
            catch (Exception ex)
            {
                LogHelper.WriteLogToFile("Failed to save PPT snapshot before restart | " + ex.ToString(), LogHelper.LogType.Error);
            }
        }
    }
}
