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

        /*
        private void BtnCheckPPT_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                pptApplication = (Microsoft.Office.Interop.PowerPoint.Application)Marshal.GetActiveObject("PowerPoint.Application");
                //pptApplication.SlideShowWindows[1].View.Next();
                if (pptApplication != null)
                {
                    //获得演示文稿对象
                    presentation = pptApplication.ActivePresentation;
                    pptApplication.SlideShowBegin += PptApplication_SlideShowBegin;
                    pptApplication.SlideShowNextSlide += PptApplication_SlideShowNextSlide;
                    pptApplication.SlideShowEnd += PptApplication_SlideShowEnd;
                    // 获得幻灯片对象集合
                    slides = presentation.Slides;
                    // 获得幻灯片的数量
                    slidescount = slides.Count;
                    memoryStreams = new MemoryStream[slidescount + 2];
                    // 获得当前选中的幻灯片
                    try
                    {
                        // 在普通视图下这种方式可以获得当前选中的幻灯片对象
                        // 然而在阅读模式下，这种方式会出现异常
                        slide = slides[pptApplication.ActiveWindow.Selection.SlideRange.SlideNumber];
                    }
                    catch
                    {
                        // 在阅读模式下出现异常时，通过下面的方式来获得当前选中的幻灯片对象
                        slide = pptApplication.SlideShowWindows[1].View.Slide;
                    }
                }

                if (pptApplication == null) throw new Exception();
                //BtnCheckPPT.Visibility = Visibility.Collapsed;
                BtnPPTSlideShowEnd.Visibility = Visibility.Visible;
            }
            catch
            {
                //BtnCheckPPT.Visibility = Visibility.Visible;
                BtnPPTSlideShowEnd.Visibility = Visibility.Collapsed;
                PPTNavigationBottomLeft.Visibility = Visibility.Collapsed;
                PPTNavigationBottomRight.Visibility = Visibility.Collapsed;
                PPTNavigationSidesLeft.Visibility = Visibility.Collapsed;
                PPTNavigationSidesRight.Visibility = Visibility.Collapsed;
                MessageBox.Show("未找到幻灯片");
            }
        }
        */

        private void ToggleSwitchSupportWPS_Toggled(object sender, RoutedEventArgs e)
        {
            if (!isLoaded) return;

            Settings.PowerPointSettings.IsSupportWPS = ToggleSwitchSupportWPS.IsOn;
            SaveSettingsToFile();
        }

        public static bool IsWPSSupportOn => Settings.PowerPointSettings.IsSupportWPS;

        public static bool IsShowingRestoreHiddenSlidesWindow = false;

        private void TimerCheckPPT_Elapsed(object sender, ElapsedEventArgs e)
        {
            if (IsShowingRestoreHiddenSlidesWindow) return;
            if (DateTime.UtcNow < _suspendPptComCheckUntilUtc) return;
            try
            {
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

        private static bool IsPresentationProcessRunning()
        {
            // 仅当 PowerPoint / WPS 演示进程确实存在时才尝试 GetActiveObject，
            // 避免"未打开 PPT"时每秒抛一次 COMException（RPC_E_SERVER_UNAVAILABLE）刷屏调试输出。
            if (Process.GetProcessesByName("POWERPNT").Length > 0) return true;
            if (IsWPSSupportOn && Process.GetProcessesByName("wpp").Length > 0) return true;
            if (IsWPSSupportOn && Process.GetProcessesByName("et").Length > 0) return true;
            return false;
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

        private async Task<bool> RetryComAsync(Action action, int maxAttempts, int delayMs)
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
                    await Task.Delay(delayMs);
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
                if (!IsWPSSupportOn && Process.GetProcessesByName("wpp").Length > 0)
                {
                    return;
                }

                string className = ForegroundWindowInfo.WindowClassName();
                if (className == "Progman" || className == "WorkerW" || className == "Shell_TrayWnd")
                {
                    _suspendPptComCheckUntilUtc = DateTime.UtcNow.AddMilliseconds(1500);
                    return;
                }

                if (pptApplication != null && TryPingPptApplication(pptApplication)) return;

                // 进程都未运行，直接跳过，避免每秒 GetActiveObject 抛 COMException 刷屏
                if (!IsPresentationProcessRunning()) return;

                Microsoft.Office.Interop.PowerPoint.Application acquired = null;
                for (int attempt = 0; attempt < 6; attempt++)
                {
                    try
                    {
                        acquired = (Microsoft.Office.Interop.PowerPoint.Application)Marshal.GetActiveObject("PowerPoint.Application");
                        if (!TryPingPptApplication(acquired)) acquired = null;
                        if (acquired != null) break;
                    }
                    catch (Exception ex) when (IsRetryableComException(ex))
                    {
                        await Task.Delay(150);
                    }
                    catch
                    {
                        acquired = null;
                        break;
                    }
                }

                if (acquired == null) return;
                pptApplication = acquired;

                if (pptApplication != null)
                {
                    // 先注册事件，再停止定时器
                    try { pptApplication.PresentationClose -= PptApplication_PresentationClose; } catch { }
                    try { pptApplication.SlideShowBegin -= PptApplication_SlideShowBegin; } catch { }
                    try { pptApplication.SlideShowNextSlide -= PptApplication_SlideShowNextSlide; } catch { }
                    try { pptApplication.SlideShowEnd -= PptApplication_SlideShowEnd; } catch { }
                    pptApplication.PresentationClose += PptApplication_PresentationClose;
                    pptApplication.SlideShowBegin += PptApplication_SlideShowBegin;
                    pptApplication.SlideShowNextSlide += PptApplication_SlideShowNextSlide;
                    pptApplication.SlideShowEnd += PptApplication_SlideShowEnd;
                    
                    //获得演示文稿对象
                    bool gotPresentation = await RetryComAsync(() => { presentation = pptApplication.ActivePresentation; }, 4, 150);
                    if (presentation != null)
                    {
                        // 获得幻灯片对象集合
                        bool gotSlides = await RetryComAsync(() => { slides = presentation.Slides; }, 4, 150);
                        if (!gotPresentation || !gotSlides) return;

                        // 获得幻灯片的数量
                        bool gotCount = await RetryComAsync(() =>
                        {
                            slidescount = slides.Count;
                        }, 4, 150);
                        if (!gotCount) return;
                        // 获得当前选中的幻灯片
                        try
                        {
                            // 在普通视图下这种方式可以获得当前选中的幻灯片对象
                            // 然而在阅读模式下，这种方式会出现异常
                            slide = slides[pptApplication.ActiveWindow.Selection.SlideRange.SlideNumber];
                        }
                        catch
                        {
                            // 在阅读模式下出现异常时，通过下面的方式来获得当前选中的幻灯片对象
                            if (pptApplication.SlideShowWindows.Count >= 1)
                            {
                                slide = pptApplication.SlideShowWindows[1].View.Slide;
                            }
                        }
                        
                        timerCheckPPT.Stop();
                    }
                }

                if (pptApplication == null || presentation == null || slides == null) return;

                // 跳转到上次播放页
                        if (Settings.PowerPointSettings.IsNotifyPreviousPage)
                        {
                            _ = Application.Current.Dispatcher.BeginInvoke((Action)(() =>
                            {
                                string safePresentationName = SanitizePathSegment(presentation.Name);
                                string folderPath = System.IO.Path.Combine(
                                    Settings.Automation.AutoSavedStrokesLocation,
                                    "Auto Saved - Presentations",
                                    safePresentationName);
                                try
                                {
                                    if (File.Exists(folderPath + "/Position"))
                                    {
                                        if (int.TryParse(File.ReadAllText(folderPath + "/Position"), out var page))
                                        {
                                            if (page <= 0) return;
                                            var jumpNotification = new YesOrNoNotificationWindow($"上次播放到了第 {page} 页, 是否立即跳转", () =>
                                            {
                                                if (pptApplication.SlideShowWindows.Count >= 1)
                                                {
                                                    // 如果已经播放了的话, 跳转
                                                    presentation.SlideShowWindow.View.GotoSlide(page);
                                                }
                                                else
                                                {
                                                    presentation.Windows[1].View.GotoSlide(page);
                                                }
                                            });
                                            Helpers.WindowMemoryHelper.ReleaseOnClose(jumpNotification);
                                            jumpNotification.ShowDialog();
                                        }
                                    }
                                }
                                catch (Exception ex)
                                {
                                    LogHelper.WriteLogToFile(ex.ToString(), LogHelper.LogType.Error);
                                }
                            }));
                        }

                //检查是否有隐藏幻灯片
                if (Settings.PowerPointSettings.IsNotifyHiddenPage)
                {
                    bool isHaveHiddenSlide = false;
                    foreach (Slide slide in slides)
                    {
                        if (slide.SlideShowTransition.Hidden == Microsoft.Office.Core.MsoTriState.msoTrue)
                        {
                            isHaveHiddenSlide = true;
                            break;
                        }
                    }

                    _ = Application.Current.Dispatcher.BeginInvoke((Action)(() =>
                    {
                        if (isHaveHiddenSlide && !IsShowingRestoreHiddenSlidesWindow)
                        {
                            IsShowingRestoreHiddenSlidesWindow = true;
                            var hiddenSlidesNotification = new YesOrNoNotificationWindow("检测到此演示文档中包含隐藏的幻灯片，是否取消隐藏？",
                                () =>
                                {
                                    foreach (Slide slide in slides)
                                    {
                                        if (slide.SlideShowTransition.Hidden == Microsoft.Office.Core.MsoTriState.msoTrue)
                                        {
                                            slide.SlideShowTransition.Hidden = Microsoft.Office.Core.MsoTriState.msoFalse;
                                        }
                                    }
                                });
                            Helpers.WindowMemoryHelper.ReleaseOnClose(hiddenSlidesNotification);
                            hiddenSlidesNotification.ShowDialog();
                        }

                        // BtnPPTSlideShow.Visibility = Visibility.Visible;
                    }));
                }

                //检测是否有自动播放
                if (Settings.PowerPointSettings.IsNotifyAutoPlayPresentation
                    // && presentation.SlideShowSettings.AdvanceMode == PpSlideShowAdvanceMode.ppSlideShowUseSlideTimings
                    && BtnPPTSlideShowEnd.Visibility != Visibility.Visible)
                {
                    bool hasSlideTimings = false;
                    foreach (Slide slide in presentation.Slides)
                    {
                        if (slide.SlideShowTransition.AdvanceOnTime == MsoTriState.msoTrue && slide.SlideShowTransition.AdvanceTime > 0)
                        {
                            hasSlideTimings = true;
                            break;
                        }
                    }
                    if (hasSlideTimings)
                    {
                        _ = Application.Current.Dispatcher.BeginInvoke((Action)(() =>
                        {
                            var autoPlayNotification = new YesOrNoNotificationWindow("检测到此演示文档中自动播放或排练计时已经启用，可能导致幻灯片自动翻页，是否取消？",
                                () =>
                                {
                                    presentation.SlideShowSettings.AdvanceMode = PpSlideShowAdvanceMode.ppSlideShowManualAdvance;
                                });
                            Helpers.WindowMemoryHelper.ReleaseOnClose(autoPlayNotification);
                            autoPlayNotification.ShowDialog();
                        }));
                        presentation.SlideShowSettings.AdvanceMode = PpSlideShowAdvanceMode.ppSlideShowManualAdvance;
                    }
                }

                //如果检测到已经开始放映，则立即进入画板模式
                if (pptApplication.SlideShowWindows.Count >= 1)
                {
                    try
                    {
                        PptApplication_SlideShowBegin(pptApplication.SlideShowWindows[1]);
                    }
                    catch (Exception ex)
                    {
                        LogHelper.WriteLogToFile(string.Format("Failed to enter PPT mode: {0}", ex.ToString()), LogHelper.LogType.Error);
                    }
                }
            }
            catch (Exception ex)
            {
                LogHelper.WriteLogToFile(string.Format("TimerCheckPPT error: {0}", ex.ToString()), LogHelper.LogType.Error);
                /*
                Application.Current.Dispatcher.Invoke(() =>
                {
                    BtnPPTSlideShow.Visibility = Visibility.Collapsed;
                });
                */
                timerCheckPPT.Start();
            }
            finally
            {
                Interlocked.Exchange(ref _isCheckingPptCom, 0);
            }
        }

        private void PptApplication_PresentationClose(Presentation Pres)
        {
            if (pptApplication != null)
            {
                pptApplication.PresentationClose -= PptApplication_PresentationClose;
                pptApplication.SlideShowBegin -= PptApplication_SlideShowBegin;
                pptApplication.SlideShowNextSlide -= PptApplication_SlideShowNextSlide;
                pptApplication.SlideShowEnd -= PptApplication_SlideShowEnd;
                pptApplication = null;
            }
            
            timerCheckPPT?.Start();

            Application.Current.Dispatcher.Invoke(() =>
            {
                //BtnPPTSlideShow.Visibility = Visibility.Collapsed;
                if (BtnPPTSlideShowEnd != null)
                {
                    BtnPPTSlideShowEnd.Visibility = Visibility.Collapsed;
                }
            });

        }

        //bool isPresentationHaveBlackSpace = false;
        private string pptName = null;
        int currentShowPosition = -1;
        private void PptApplication_SlideShowBegin(SlideShowWindow Wn)
        {
            // 幂等保护：已在放映中则忽略重复的进入事件/定时器重连触发，
            // 避免把进入放映后所画的当前页笔迹误当作桌面批注备份而被清除/还原。
            if (_isPptSlideShowActive) return;

            if (Settings.Automation.IsAutoFoldInPPTSlideShow && !isFloatingBarFolded)
            {
                FoldFloatingBar_Click(null, null);
            }
            else if (isFloatingBarFolded)
            {
                UnFoldFloatingBar_MouseUp(null, null);
            }

            LogHelper.WriteLogToFile("PowerPoint Application Slide Show Begin", LogHelper.LogType.Event);
            Application.Current.Dispatcher.Invoke(() =>
            {
                _isPptSlideShowActive = true;
                ResetTouchState();

                // 确保备份的笔迹是独立的，不会被后续操作影响
                if (currentMode == 0 && inkCanvas.Strokes.Count > 0)
                {
                    try
                    {
                        // 创建独立的笔迹集合备份
                        _desktopStrokesBackupStrokes = new StrokeCollection();
                        foreach (Stroke stroke in inkCanvas.Strokes)
                        {
                            _desktopStrokesBackupStrokes.Add(stroke.Clone());
                        }
                        
                        // 同时创建流备份，确保双重保险
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
                /*
                //调整颜色
                double screenRatio = SystemParameters.PrimaryScreenWidth / SystemParameters.PrimaryScreenHeight;
                if (Math.Abs(screenRatio - 16.0 / 9) <= -0.01)
                {
                    if (Wn.Presentation.PageSetup.SlideWidth / Wn.Presentation.PageSetup.SlideHeight < 1.65)
                    {
                        isPresentationHaveBlackSpace = true;
                    }
                }
                else if (screenRatio == -256 / 135)
                {

                }
                */
                lastDesktopInkColor = 1;

                slidescount = Wn.Presentation.Slides.Count;
                previousSlideID = 0;
                currentSlideID = 0;
                _strokeCacheBySlideId.Clear();
                RebuildSlideIdMapping(Wn.Presentation.Slides);

                pptName = Wn.Presentation.Name;
                LogHelper.NewLog("Name: " + Wn.Presentation.Name);
                LogHelper.NewLog("Slides Count: " + slidescount.ToString());

                //检查是否有已有墨迹，并加载
                if (Settings.PowerPointSettings.IsAutoSaveStrokesInPowerPoint)
                {
                    string safePresentationName = SanitizePathSegment(Wn.Presentation.Name);
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
                    else
                    {
                        // 兼容旧版目录「名称_页数」：仅当新目录不存在时，将旧目录按位置迁移到当前 SlideID。
                        string legacyPath = folderBase + "_" + Wn.Presentation.Slides.Count;
                        if (Directory.Exists(legacyPath))
                        {
                            ImportLegacyPositionStrokes(legacyPath, Wn.Presentation.Slides);
                        }
                    }
                }

                BtnPPTSlideShowEnd.Visibility = Visibility.Visible;

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

                        //SaveStrokes();
                        ClearStrokes(true);
                    }
                    BtnHideInkCanvas_Click(null, null);
                }

                ClearStrokes(true);

                try
                {
                    int currentSlideIndex = Wn.View.CurrentShowPosition;
                    int slideId = GetSlideIdForPosition(currentSlideIndex, Wn);
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
                PptNavigationTextBlockBottom.Text = $"{Wn.View.CurrentShowPosition}/{Wn.Presentation.Slides.Count}";
                LogHelper.NewLog("PowerPoint Slide Show Loading process complete");

                new Thread(new ThreadStart(() =>
                {
                    Thread.Sleep(100);
                    Application.Current.Dispatcher.Invoke(() =>
                    {
                        ViewboxFloatingBarMarginAnimation();
                    });
                })).Start();
            });
        }

        bool isEnteredSlideShowEndEvent = false; //防止重复调用本函数导致墨迹保存失效
        private async void PptApplication_SlideShowEnd(Presentation Pres)
        {
            if (isFloatingBarFolded) UnFoldFloatingBar_MouseUp(null, null);

            LogHelper.WriteLogToFile(string.Format("PowerPoint Slide Show End"), LogHelper.LogType.Event);
            if (isEnteredSlideShowEndEvent)
            {
                LogHelper.WriteLogToFile("Detected previous entrance, returning");
                return;
            }
            isEnteredSlideShowEndEvent = true;
            if (Settings.PowerPointSettings.IsAutoSaveStrokesInPowerPoint)
            {
                string safePresentationName = SanitizePathSegment(Pres.Name);
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
                // 保存前按当前演示文稿重建 SlideID 映射，再按 SlideID 写盘。
                try { RebuildSlideIdMapping(Pres.Slides); } catch { }
                SaveStrokeCacheToFolder(folderPath);
            }

            Application.Current.Dispatcher.Invoke(() =>
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

                    // 若开启了"退出画板模式后隐藏墨迹"（Settings.Canvas.HideStrokeWhenSelecting），
                    // 退出放映后恢复的桌面批注应隐藏而非直接显示。
                    // 隐藏方式与"批注→鼠标"一致：收起墨迹画布（保留笔迹，便于后续恢复），参考 CursorIcon_Click。
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
            });

            await Task.Delay(150);
            ViewboxFloatingBarMarginAnimation();
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

        /// <summary>按键取缓存笔迹。</summary>
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

        private void PptApplication_SlideShowNextSlide(SlideShowWindow Wn)
        {
            LogHelper.WriteLogToFile(string.Format("PowerPoint Next Slide (Slide {0})", Wn.View.CurrentShowPosition), LogHelper.LogType.Event);
            if (Wn.View.CurrentShowPosition != previousSlideID)
            {
                // 记录离开页的 SlideID（进入该页时写入的 currentSlideID）与旧位置，
                // 切页回调触发时 Wn.View.CurrentShowPosition 已经是新位置。
                int leavingSlideID = currentSlideID;
                int oldPosition = previousSlideID;
                int newPosition = Wn.View.CurrentShowPosition;

                Application.Current.Dispatcher.Invoke(() =>
                {
                    MemoryStream ms = new MemoryStream();
                    inkCanvas.Strokes.Save(ms);
                    ms.Position = 0;
                    // 离开页的笔迹按该页 SlideID 缓存，插入/删除页后仍能正确对应。
                    _strokeCacheBySlideId[GetStrokeCacheKey(leavingSlideID, oldPosition)] = ms;

                    if (inkCanvas.Strokes.Count > Settings.Automation.MinimumAutomationStrokeNumber && Settings.PowerPointSettings.IsAutoSaveScreenShotInPowerPoint && !_isPptClickingBtnTurned)
                        SavePPTScreenshot(Wn.Presentation.Name + "/" + newPosition);
                    _isPptClickingBtnTurned = false;

                    ClearStrokes(true);
                    timeMachine.ClearStrokeHistory();

                    try
                    {
                        int targetSlideId = GetSlideIdForPosition(newPosition, Wn);
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

                    PptNavigationTextBlockBottom.Text = $"{newPosition}/{Wn.Presentation.Slides.Count}";
                });
                previousSlideID = newPosition;

            }
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
            // 控制居中
            if (!isFloatingBarFolded)
            {
                await Task.Delay(100);
                ViewboxFloatingBarMarginAnimation();
            }
        }

        /*
        private void BtnPPTSlideShow_Click(object sender, RoutedEventArgs e)
        {
            new Thread(new ThreadStart(() =>
            {
                try
                {
                    presentation.SlideShowSettings.Run();
                }
                catch { }
            })).Start();
        }
        */

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

                // 确保当前页的墨迹也被写入缓存
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
