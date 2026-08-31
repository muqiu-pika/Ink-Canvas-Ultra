using Ink_Canvas.Helpers;
using Newtonsoft.Json;
using OSVersionExtension;
using System;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using File = System.IO.File;
using Directory = System.IO.Directory;

namespace Ink_Canvas
{
    public partial class MainWindow : Window
    {
        #region Behavior

        private void ToggleSwitchIsAutoUpdate_Toggled(object sender, RoutedEventArgs e)
        {
            if (!isLoaded || _isLoadingSettings) return;
            Settings.Startup.IsAutoUpdate = ToggleSwitchIsAutoUpdate.IsOn;
            IsAutoUpdateWithSilenceBlock.Visibility = ToggleSwitchIsAutoUpdate.IsOn ? Visibility.Visible : Visibility.Collapsed;
            // 关闭"自动检查更新"时一并停止已排期的静默安装定时器，避免到点仍被自动下载安装
            if (!ToggleSwitchIsAutoUpdate.IsOn)
            {
                try { timerCheckAutoUpdateWithSilence.Stop(); } catch { }
                _silentInstallVersion = null;
            }
            SaveSettingsToFile();
            var wizard = System.Windows.Application.Current.Windows.OfType<InitialSetupWindow>().FirstOrDefault();
            wizard?.Dispatcher.Invoke(() =>
            {
                wizard.CheckBoxIsAutoUpdate.IsChecked = Settings.Startup.IsAutoUpdate;
                wizard.CheckBoxIsAutoUpdateWithSilence.Visibility = Settings.Startup.IsAutoUpdate ? Visibility.Visible : Visibility.Collapsed;
                if (!Settings.Startup.IsAutoUpdate)
                {
                    wizard.CheckBoxIsAutoUpdateWithSilence.IsChecked = false;
                    wizard.SilencePeriodPanel.Visibility = Visibility.Collapsed;
                }
                else
                {
                    wizard.SilencePeriodPanel.Visibility = Settings.Startup.IsAutoUpdateWithSilence ? Visibility.Visible : Visibility.Collapsed;
                }
            });
        }
        private void ToggleSwitchIsAutoUpdateWithSilence_Toggled(object sender, RoutedEventArgs e)
        {
            if (!isLoaded || _isLoadingSettings) return;
            Settings.Startup.IsAutoUpdateWithSilence = ToggleSwitchIsAutoUpdateWithSilence.IsOn;
            AutoUpdateTimePeriodBlock.Visibility = Settings.Startup.IsAutoUpdateWithSilence ? Visibility.Visible : Visibility.Collapsed;
            // 关闭"静默更新"时停止已启动的静默安装定时器，尊重用户"不再静默更新"的明确意图
            if (!ToggleSwitchIsAutoUpdateWithSilence.IsOn)
            {
                try { timerCheckAutoUpdateWithSilence.Stop(); } catch { }
                _silentInstallVersion = null;
            }
            SaveSettingsToFile();
            var wizard = System.Windows.Application.Current.Windows.OfType<InitialSetupWindow>().FirstOrDefault();
            wizard?.Dispatcher.Invoke(() =>
            {
                if (wizard.CheckBoxIsAutoUpdate.IsChecked == true)
                {
                    wizard.SilencePeriodPanel.Visibility = Settings.Startup.IsAutoUpdateWithSilence ? Visibility.Visible : Visibility.Collapsed;
                }
                else
                {
                    wizard.SilencePeriodPanel.Visibility = System.Windows.Visibility.Collapsed;
                }
            });
        }

        private void ToggleSwitchIsAutoUpdateWithProxy_Toggled(object sender, RoutedEventArgs e)
        {
            if (!isLoaded || _isLoadingSettings) return;
            Settings.Startup.IsAutoUpdateWithProxy = ToggleSwitchIsAutoUpdateWithProxy.IsOn;
            AutoUpdateWithProxy_Title.Visibility = Settings.Startup.IsAutoUpdateWithProxy ? Visibility.Visible : Visibility.Collapsed;
            SaveSettingsToFile();
        }

        private void AutoUpdateProxyTextBox_TextChanged(object sender, RoutedEventArgs e)
        {
            if (!isLoaded || _isLoadingSettings) return;
            Settings.Startup.AutoUpdateProxy = AutoUpdateProxyTextBox.Text;
            SaveSettingsToFile();
        }

        private void BtnResetAutoUpdateProxyToGHProxy_Click(object sender, RoutedEventArgs e)
        {
            AutoUpdateProxyTextBox.Text = "https://ghproxy.net/";
        }

        private async void BtnCheckAutoUpdateProxyReturnedData_Click(object sender, RoutedEventArgs e)
        {
            string ProxyReturnedData = await AutoUpdateHelper.GetRemoteVersion(Settings.Startup.AutoUpdateProxy + "https://raw.githubusercontent.com/muqiu-pika/Ink-Canvas-Ultra/master/AutomaticUpdateVersionControl.txt");
            ShowNotificationAsync(ProxyReturnedData);
        }

        /// <summary>设置窗口"立即检查更新"：忽略静默设置，手动对比版本号并执行更新。</summary>
        private async void BtnCheckUpdateNow_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // 记录本次检测时间并刷新按钮旁提示
                Settings.Startup.LastUpdateCheckTime = DateTime.Now.ToString("yyyy/M/d");
                SaveSettingsToFile();
                UpdateManualCheckInfoText();

                // 防止重复点击
                try { if (BtnCheckUpdateNow != null) BtnCheckUpdateNow.IsEnabled = false; } catch { }
                ShowManualUpdateStatus("正在检查更新...", isIndeterminate: true);

                string latest = null;
                AutoUpdateHelper.UpdateCheckResult checkResult;
                if (Settings.Startup.IsAutoUpdateWithProxy) checkResult = await AutoUpdateHelper.CheckForUpdatesDetailed(Settings.Startup.AutoUpdateProxy);
                else checkResult = await AutoUpdateHelper.CheckForUpdatesDetailed();

                // 区分"网络异常"与"确实没有新版本"，避免断网时误报"您已安装最新版"
                if (checkResult.IsNetworkError)
                {
                    HideManualUpdateProgress();
                    ShowNotificationAsync("检查更新失败：无法连接服务器，请检查网络后重试。");
                    return;
                }
                latest = checkResult.LatestVersion;

                if (string.IsNullOrEmpty(latest))
                {
                    // 无更新：提示已是最新版本（与插件启用提示同一种通知），按钮改为"您已是最新版！"（本次设置窗口会话内有效）
                    string currentVersion = AutoUpdateHelper.GetDisplayVersion();
                    ShowNotificationAsync($"您已安装最新版 v{currentVersion}");
                    try { if (BtnCheckUpdateNow != null) BtnCheckUpdateNow.Content = "您已是最新版！"; } catch { }
                    HideManualUpdateProgress();
                    return;
                }

                // 有更新：忽略静默设置，弹窗询问是否更新（与自动更新/静默更新共用的提示弹窗一致）
                var confirm = MessageBox.Show(
                    $"检测到 Ink Canvas Ultra 新版本 v{latest}，是否立即更新？",
                    "Ink Canvas Ultra New Version Available",
                    MessageBoxButton.YesNo, MessageBoxImage.Question);
                if (confirm != MessageBoxResult.Yes)
                {
                    HideManualUpdateProgress();
                    return;
                }

                // 下载安装包并显示更新进度
                ShowManualUpdateStatus("正在下载更新安装包...", isIndeterminate: true);
                bool downloadOk;
                if (Settings.Startup.IsAutoUpdateWithProxy)
                    downloadOk = await AutoUpdateHelper.DownloadSetupFileAndSaveStatus(latest, Settings.Startup.AutoUpdateProxy, pct => UpdateManualDownloadProgress(pct));
                else
                    downloadOk = await AutoUpdateHelper.DownloadSetupFileAndSaveStatus(latest, "", pct => UpdateManualDownloadProgress(pct));

                if (!downloadOk)
                {
                    HideManualUpdateProgress();
                    ShowNotificationAsync("更新安装包下载失败，请检查网络后重试。");
                    return;
                }

                // 下载完成：询问是否安装并重启以启用最新版（提示重启可能导致笔迹丢失）
                var restart = MessageBox.Show(
                    "更新安装包已下载完成。是否立即安装并重启以启用最新版本？\n\n提示：重启可能会导致未保存的笔迹丢失。",
                    "Ink Canvas Ultra New Version Available",
                    MessageBoxButton.YesNo, MessageBoxImage.Question);
                if (restart == MessageBoxResult.Yes)
                {
                    AutoUpdateHelper.InstallNewVersionApp(latest, false);
                }
                else
                {
                    HideManualUpdateProgress();
                }
            }
            catch (Exception ex)
            {
                LogHelper.WriteLogToFile($"手动检查更新失败: {ex.Message}", LogHelper.LogType.Error);
                HideManualUpdateProgress();
                ShowNotificationAsync("检查更新失败，请检查网络后重试。");
            }
            finally
            {
                // 应用可能已被 InstallNewVersionApp 关闭，此处仅兜底恢复按钮
                try { if (BtnCheckUpdateNow != null) BtnCheckUpdateNow.IsEnabled = true; } catch { }
            }
        }

        /// <summary>刷新"立即检查更新"按钮旁的提示（上次检测时间 + 当前版本号）。</summary>
        private void UpdateManualCheckInfoText()
        {
            try
            {
                if (TextBlockUpdateCheckInfo == null) return;
                string lastCheck = string.IsNullOrEmpty(Settings.Startup.LastUpdateCheckTime) ? "从未检测" : Settings.Startup.LastUpdateCheckTime;
                TextBlockUpdateCheckInfo.Text = $"上次检测：{lastCheck} · 当前版本号：{AutoUpdateHelper.GetDisplayVersion()}";
            }
            catch { }
        }

        private void ShowManualUpdateStatus(string status, bool isIndeterminate)
        {
            try
            {
                if (ManualUpdateProgressBlock == null) return;
                ManualUpdateProgressBlock.Visibility = Visibility.Visible;
                if (TextBlockManualUpdateStatus != null) TextBlockManualUpdateStatus.Text = status;
                if (ProgressBarManualUpdate != null)
                {
                    ProgressBarManualUpdate.IsIndeterminate = isIndeterminate;
                    ProgressBarManualUpdate.Value = 0;
                }
            }
            catch { }
        }

        /// <summary>更新下载进度条（进度回调来自后台线程，需切回 UI 线程）。</summary>
        private void UpdateManualDownloadProgress(double pct)
        {
            Dispatcher.Invoke(() =>
            {
                try
                {
                    if (ManualUpdateProgressBlock == null) return;
                    ManualUpdateProgressBlock.Visibility = Visibility.Visible;
                    if (ProgressBarManualUpdate != null)
                    {
                        ProgressBarManualUpdate.IsIndeterminate = pct < 0;
                        if (pct >= 0) ProgressBarManualUpdate.Value = Math.Min(100, pct);
                    }
                    if (TextBlockManualUpdateStatus != null)
                        TextBlockManualUpdateStatus.Text = pct >= 0
                            ? $"正在下载更新安装包 {Math.Min(100, (int)pct)}%..."
                            : "正在下载更新安装包...";
                }
                catch { }
            });
        }

        private void HideManualUpdateProgress()
        {
            try
            {
                if (ManualUpdateProgressBlock == null) return;
                ManualUpdateProgressBlock.Visibility = Visibility.Collapsed;
            }
            catch { }
        }

        private void AutoUpdateWithSilenceStartTimeComboBox_SelectionChanged(object sender, RoutedEventArgs e)
        {
            if (!isLoaded || _isLoadingSettings) return;
            Settings.Startup.AutoUpdateWithSilenceStartTime = (string)AutoUpdateWithSilenceStartTimeComboBox.SelectedItem;
            SaveSettingsToFile();
        }

        private void AutoUpdateWithSilenceEndTimeComboBox_SelectionChanged(object sender, RoutedEventArgs e)
        {
            if (!isLoaded || _isLoadingSettings) return;
            Settings.Startup.AutoUpdateWithSilenceEndTime = (string)AutoUpdateWithSilenceEndTimeComboBox.SelectedItem;
            SaveSettingsToFile();
        }

        private void ToggleSwitchRunAtStartup_Toggled(object sender, RoutedEventArgs e)
        {
            if (!isLoaded || _isLoadingSettings) return;
            if (ToggleSwitchRunAtStartup.IsOn)
            {
                StartAutomaticallyDel("InkCanvas");
                StartAutomaticallyDel("Ink Canvas Annotation");
                StartAutomaticallyCreate("Ink Canvas Ultra");
            }
            else
            {
                StartAutomaticallyDel("InkCanvas");
                StartAutomaticallyDel("Ink Canvas Annotation");
                StartAutomaticallyDel("Ink Canvas Ultra");
            }
            var wizard = System.Windows.Application.Current.Windows.OfType<InitialSetupWindow>().FirstOrDefault();
            wizard?.Dispatcher.Invoke(() =>
            {
                wizard.CheckBoxRunAtStartup.IsChecked = ToggleSwitchRunAtStartup.IsOn;
            });
        }

        private void ToggleSwitchFoldAtStartup_Toggled(object sender, RoutedEventArgs e)
        {
            if (!isLoaded || _isLoadingSettings) return;
            Settings.Startup.IsFoldAtStartup = ToggleSwitchFoldAtStartup.IsOn;
            SaveSettingsToFile();
        }

        private void ToggleSwitchSupportPowerPoint_Toggled(object sender, RoutedEventArgs e)
        {
            if (!isLoaded || _isLoadingSettings) return;

            Settings.PowerPointSettings.PowerPointSupport = ToggleSwitchSupportPowerPoint.IsOn;
            SaveSettingsToFile();

            if (Settings.PowerPointSettings.PowerPointSupport)
            {
                timerCheckPPT.Start();
            }
            else
            {
                timerCheckPPT.Stop();
            }
        }

        private void ToggleSwitchShowCanvasAtNewSlideShow_Toggled(object sender, RoutedEventArgs e)
        {
            if (!isLoaded || _isLoadingSettings) return;

            Settings.PowerPointSettings.IsShowCanvasAtNewSlideShow = ToggleSwitchShowCanvasAtNewSlideShow.IsOn;
            SaveSettingsToFile();
        }

        #endregion

        public void SetAutoUpdateEnabled(bool enabled)
        {
            if (!isLoaded || _isLoadingSettings) return;
            if (ToggleSwitchIsAutoUpdate == null) { Settings.Startup.IsAutoUpdate = enabled; return; }
            ToggleSwitchIsAutoUpdate.IsOn = enabled;
            ToggleSwitchIsAutoUpdate_Toggled(ToggleSwitchIsAutoUpdate, new RoutedEventArgs());
        }

        public void SetAutoUpdateWithSilenceEnabled(bool enabled)
        {
            if (!isLoaded || _isLoadingSettings) return;
            if (ToggleSwitchIsAutoUpdateWithSilence == null) { Settings.Startup.IsAutoUpdateWithSilence = enabled; return; }
            ToggleSwitchIsAutoUpdateWithSilence.IsOn = enabled;
            ToggleSwitchIsAutoUpdateWithSilence_Toggled(ToggleSwitchIsAutoUpdateWithSilence, new RoutedEventArgs());
        }

        public void SetRunAtStartupEnabled(bool enabled)
        {
            if (!isLoaded || _isLoadingSettings) return;
            if (ToggleSwitchRunAtStartup == null)
            {
                if (enabled) StartAutomaticallyCreate("Ink Canvas Ultra");
                else StartAutomaticallyDel("Ink Canvas Ultra");
                return;
            }
            ToggleSwitchRunAtStartup.IsOn = enabled;
            ToggleSwitchRunAtStartup_Toggled(ToggleSwitchRunAtStartup, new RoutedEventArgs());
        }

        #region Startup

        private void ToggleSwitchEnableNibMode_Toggled(object sender, RoutedEventArgs e)
        {
            if (!isLoaded || _isLoadingSettings) return;
            if (sender == ToggleSwitchEnableNibMode)
            {
                BoardToggleSwitchEnableNibMode.IsOn = ToggleSwitchEnableNibMode.IsOn;
            }
            else
            {
                ToggleSwitchEnableNibMode.IsOn = BoardToggleSwitchEnableNibMode.IsOn;
            }
            Settings.Startup.IsEnableNibMode = ToggleSwitchEnableNibMode.IsOn;

            if (Settings.Startup.IsEnableNibMode)
            {
                BoundsWidth = Settings.Advanced.NibModeBoundsWidth;
            }
            else
            {
                BoundsWidth = Settings.Advanced.FingerModeBoundsWidth;
            }
            SaveSettingsToFile();
        }
        #endregion

        #region Appearance
        private void ToggleSwitchEnableDisPlayFloatBarText_Toggled(object sender, RoutedEventArgs e)
        {
            if (!isLoaded || _isLoadingSettings) return;
            Settings.Appearance.IsEnableDisPlayFloatBarText = ToggleSwitchEnableDisPlayFloatBarText.IsOn;
            SaveSettingsToFile();
            LoadSettings();
        }

        private void ToggleSwitchEnableDisPlayNibModeToggle_Toggled(object sender, RoutedEventArgs e)
        {
            if (!isLoaded || _isLoadingSettings) return;
            Settings.Appearance.IsEnableDisPlayNibModeToggler = ToggleSwitchEnableDisPlayNibModeToggle.IsOn;
            SaveSettingsToFile();
            LoadSettings();
        }

        private void ToggleSwitchIsColorfulViewboxFloatingBar_Toggled(object sender, RoutedEventArgs e)
        {
            if (!isLoaded || _isLoadingSettings) return;
            Settings.Appearance.IsColorfulViewboxFloatingBar = ToggleSwitchColorfulViewboxFloatingBar.IsOn;
            SaveSettingsToFile();
            LoadSettings();
        }

        private void ApplyScaling()
        {
            Settings.Appearance.FloatingBarScale = NormalizeFloatingBarScale(Settings.Appearance.FloatingBarScale);
            double floatingBarScaleFactor = Settings.Appearance.FloatingBarScale / 100.0;
            ViewboxFloatingBarScaleTransform.ScaleX = floatingBarScaleFactor;
            ViewboxFloatingBarScaleTransform.ScaleY = floatingBarScaleFactor;

            double blackboardScaleFactor = Settings.Appearance.BlackboardScale / 100.0;
            ViewboxBlackboardLeftSideScaleTransform.ScaleX = blackboardScaleFactor;
            ViewboxBlackboardLeftSideScaleTransform.ScaleY = blackboardScaleFactor;
            ViewboxBlackboardCenterSideScaleTransform.ScaleX = blackboardScaleFactor;
            ViewboxBlackboardCenterSideScaleTransform.ScaleY = blackboardScaleFactor;
            ViewboxBlackboardRightSideScaleTransform.ScaleX = blackboardScaleFactor;
            ViewboxBlackboardRightSideScaleTransform.ScaleY = blackboardScaleFactor;

            // auto align
            ViewboxFloatingBarMarginAnimation();
        }

        private void ApplyVideoPresenterSidebarPosition()
        {
            if (VideoPresenterSidebar != null)
            {
                string position = Settings.Appearance.VideoPresenterSidebarPosition ?? "Left";
                
                if (position == "Right")
                {
                    VideoPresenterSidebar.HorizontalAlignment = HorizontalAlignment.Right;
                }
                else
                {
                    VideoPresenterSidebar.HorizontalAlignment = HorizontalAlignment.Left;
                }
            }
        }

        private void SliderFloatingBarScale_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (!isLoaded || _isLoadingSettings) return;
            Settings.Appearance.FloatingBarScale = e.NewValue;
            ApplyScaling(); // Apply the change visually
            SaveSettingsToFile();
        }

        private void SliderBlackboardScale_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (!isLoaded || _isLoadingSettings) return;
            Settings.Appearance.BlackboardScale = e.NewValue;
            ApplyScaling(); // Apply the change visually
            SaveSettingsToFile();
        }

        private void BtnSetFloatingBarScale_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag != null && double.TryParse(btn.Tag.ToString(), out double scalePercent))
            {
                SliderFloatingBarScale.Value = scalePercent; // This will trigger ValueChanged
            }
        }

        private void SliderFloatingBarBottomMargin_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (!isLoaded || _isLoadingSettings) return;
            Settings.Appearance.FloatingBarBottomMargin = e.NewValue;
            ViewboxFloatingBarMarginAnimation();
            SaveSettingsToFile();
        }

        private void BtnSetFloatingBarMargin_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag != null && double.TryParse(btn.Tag.ToString(), out double margin))
            {
                SliderFloatingBarBottomMargin.Value = margin;
            }
        }

        private void BtnSetBlackboardScale_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag != null && double.TryParse(btn.Tag.ToString(), out double scalePercent))
            {
                SliderBlackboardScale.Value = scalePercent; // This will trigger ValueChanged
            }
        }

        private void ToggleSwitchShowButtonPPTNavigationBottom_OnToggled(object sender, RoutedEventArgs e)
        {
            if (!isLoaded || _isLoadingSettings) return;
            Settings.PowerPointSettings.IsShowPPTNavigationBottom = ToggleSwitchShowButtonPPTNavigationBottom.IsOn;
            PptNavigationBottomBtn.Visibility = Settings.PowerPointSettings.IsShowPPTNavigationBottom ? Visibility.Visible : Visibility.Collapsed;
            SaveSettingsToFile();
        }

        private void ToggleSwitchShowButtonPPTNavigationSides_OnToggled(object sender, RoutedEventArgs e)
        {
            if (!isLoaded || _isLoadingSettings) return;
            Settings.PowerPointSettings.IsShowPPTNavigationSides = ToggleSwitchShowButtonPPTNavigationSides.IsOn;
            PptNavigationSidesBtn.Visibility = Settings.PowerPointSettings.IsShowPPTNavigationSides ? Visibility.Visible : Visibility.Collapsed;
            SaveSettingsToFile();
        }

        private void ToggleSwitchShowPPTNavigationPanelBottom_OnToggled(object sender, RoutedEventArgs e)
        {
            if (!isLoaded || _isLoadingSettings) return;
            Settings.PowerPointSettings.IsShowBottomPPTNavigationPanel = ToggleSwitchShowPPTNavigationPanelBottom.IsOn;
            if (BtnPPTSlideShowEnd.Visibility == Visibility.Visible)
            {
                PPTNavigationBottomLeft.Visibility = Settings.PowerPointSettings.IsShowBottomPPTNavigationPanel ? Visibility.Visible : Visibility.Collapsed;
                PPTNavigationBottomRight.Visibility = Settings.PowerPointSettings.IsShowBottomPPTNavigationPanel ? Visibility.Visible : Visibility.Collapsed;
            }
            SaveSettingsToFile();
        }

        private void ToggleSwitchShowPPTNavigationPanelSide_OnToggled(object sender, RoutedEventArgs e)
        {
            if (!isLoaded || _isLoadingSettings) return;
            Settings.PowerPointSettings.IsShowSidePPTNavigationPanel = ToggleSwitchShowPPTNavigationPanelSide.IsOn;
            if (BtnPPTSlideShowEnd.Visibility == Visibility.Visible)
            {
                PPTNavigationSidesLeft.Visibility = Settings.PowerPointSettings.IsShowSidePPTNavigationPanel ? Visibility.Visible : Visibility.Collapsed;
                PPTNavigationSidesRight.Visibility = Settings.PowerPointSettings.IsShowSidePPTNavigationPanel ? Visibility.Visible : Visibility.Collapsed;
            }
            SaveSettingsToFile();
        }

        private void ToggleSwitchCompressPicturesUploaded_Toggled(object sender, RoutedEventArgs e)
        {
            if (!isLoaded || _isLoadingSettings) return;
            Settings.Canvas.IsCompressPicturesUploaded = ToggleSwitchCompressPicturesUploaded.IsOn;
            SaveSettingsToFile();
        }

        private void ToggleSwitchShowCursor_Toggled(object sender, RoutedEventArgs e)
        {
            if (!isLoaded || _isLoadingSettings) return;
            Settings.Canvas.IsShowCursor = ToggleSwitchShowCursor.IsOn;
            InkCanvas_EditingModeChanged(inkCanvas, null);
            SaveSettingsToFile();
        }

        #endregion

        #region Canvas

        private void ComboBoxPenStyle_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!isLoaded || _isLoadingSettings) return;
            if (sender == ComboBoxPenStyle)
            {
                Settings.Canvas.InkStyle = ComboBoxPenStyle.SelectedIndex;
                BoardComboBoxPenStyle.SelectedIndex = ComboBoxPenStyle.SelectedIndex;
            }
            else
            {
                Settings.Canvas.InkStyle = BoardComboBoxPenStyle.SelectedIndex;
                ComboBoxPenStyle.SelectedIndex = BoardComboBoxPenStyle.SelectedIndex;
            }
            SaveSettingsToFile();
        }

        private void ComboBoxEraserSize_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!isLoaded || _isLoadingSettings) return;
            Settings.Canvas.EraserSize = ComboBoxEraserSize.SelectedIndex;
            SaveSettingsToFile();
        }

        private void ComboBoxHyperbolaAsymptoteOption_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!isLoaded || _isLoadingSettings) return;
            Settings.Canvas.HyperbolaAsymptoteOption = (OptionalOperation)ComboBoxHyperbolaAsymptoteOption.SelectedIndex;
            SaveSettingsToFile();
        }

        private void ComboBoxVideoPresenterSidebarPosition_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!isLoaded || _isLoadingSettings) return;
            if (ComboBoxVideoPresenterSidebarPosition.SelectedItem is ComboBoxItem item && item.Tag != null)
            {
                Settings.Appearance.VideoPresenterSidebarPosition = item.Tag.ToString();
                ApplyVideoPresenterSidebarPosition();
                SaveSettingsToFile();
            }
        }

        #endregion

        #region Automation

        private void StartOrStoptimerCheckAutoFold()
        {
            if (Settings.Automation.IsEnableAutoFold)
            {
                timerCheckAutoFold.Start();
            }
            else
            {
                timerCheckAutoFold.Stop();
            }
        }

        private void ToggleSwitchAutoFoldInEasiNote_Toggled(object sender, RoutedEventArgs e)
        {
            if (!isLoaded || _isLoadingSettings) return;
            Settings.Automation.IsAutoFoldInEasiNote = ToggleSwitchAutoFoldInEasiNote.IsOn;
            SaveSettingsToFile();
            StartOrStoptimerCheckAutoFold();
        }

        private void ToggleSwitchAutoFoldInEasiNoteIgnoreDesktopAnno_Toggled(object sender, RoutedEventArgs e)
        {
            if (!isLoaded || _isLoadingSettings) return;
            Settings.Automation.IsAutoFoldInEasiNoteIgnoreDesktopAnno = ToggleSwitchAutoFoldInEasiNoteIgnoreDesktopAnno.IsOn;
            SaveSettingsToFile();
        }

        private void ToggleSwitchAutoFoldInEasiCamera_Toggled(object sender, RoutedEventArgs e)
        {
            if (!isLoaded || _isLoadingSettings) return;
            Settings.Automation.IsAutoFoldInEasiCamera = ToggleSwitchAutoFoldInEasiCamera.IsOn;
            SaveSettingsToFile();
            StartOrStoptimerCheckAutoFold();
        }

        private void ToggleSwitchAutoFoldInEasiNote3C_Toggled(object sender, RoutedEventArgs e)
        {
            if (!isLoaded || _isLoadingSettings) return;
            Settings.Automation.IsAutoFoldInEasiNote3C = ToggleSwitchAutoFoldInEasiNote3C.IsOn;
            SaveSettingsToFile();
            StartOrStoptimerCheckAutoFold();
        }

        private void ToggleSwitchAutoFoldInSeewoPincoTeacher_Toggled(object sender, RoutedEventArgs e)
        {
            if (!isLoaded || _isLoadingSettings) return;
            Settings.Automation.IsAutoFoldInSeewoPincoTeacher = ToggleSwitchAutoFoldInSeewoPincoTeacher.IsOn;
            SaveSettingsToFile();
            StartOrStoptimerCheckAutoFold();
        }

        private void ToggleSwitchAutoFoldInHiteTouchPro_Toggled(object sender, RoutedEventArgs e)
        {
            if (!isLoaded || _isLoadingSettings) return;
            Settings.Automation.IsAutoFoldInHiteTouchPro = ToggleSwitchAutoFoldInHiteTouchPro.IsOn;
            SaveSettingsToFile();
            StartOrStoptimerCheckAutoFold();
        }

        private void ToggleSwitchAutoFoldInHiteCamera_Toggled(object sender, RoutedEventArgs e)
        {
            if (!isLoaded || _isLoadingSettings) return;
            Settings.Automation.IsAutoFoldInHiteCamera = ToggleSwitchAutoFoldInHiteCamera.IsOn;
            SaveSettingsToFile();
            StartOrStoptimerCheckAutoFold();
        }

        private void ToggleSwitchAutoFoldInWxBoardMain_Toggled(object sender, RoutedEventArgs e)
        {
            if (!isLoaded || _isLoadingSettings) return;
            Settings.Automation.IsAutoFoldInWxBoardMain = ToggleSwitchAutoFoldInWxBoardMain.IsOn;
            SaveSettingsToFile();
            StartOrStoptimerCheckAutoFold();
        }

        private void ToggleSwitchAutoFoldInOldZyBoard_Toggled(object sender, RoutedEventArgs e)
        {
            if (!isLoaded || _isLoadingSettings) return;
            Settings.Automation.IsAutoFoldInOldZyBoard = ToggleSwitchAutoFoldInOldZyBoard.IsOn;
            SaveSettingsToFile();
            StartOrStoptimerCheckAutoFold();
        }

        private void ToggleSwitchAutoFoldInMSWhiteboard_Toggled(object sender, RoutedEventArgs e)
        {
            if (!isLoaded || _isLoadingSettings) return;
            Settings.Automation.IsAutoFoldInMSWhiteboard = ToggleSwitchAutoFoldInMSWhiteboard.IsOn;
            SaveSettingsToFile();
            StartOrStoptimerCheckAutoFold();
        }

        private void ToggleSwitchAutoFoldInPPTSlideShow_Toggled(object sender, RoutedEventArgs e)
        {
            if (!isLoaded || _isLoadingSettings) return;
            Settings.Automation.IsAutoFoldInPPTSlideShow = ToggleSwitchAutoFoldInPPTSlideShow.IsOn;
            SaveSettingsToFile();
            StartOrStoptimerCheckAutoFold();
        }

        private void ToggleSwitchAutoKillPptService_Toggled(object sender, RoutedEventArgs e)
        {
            if (!isLoaded || _isLoadingSettings) return;
            Settings.Automation.IsAutoKillPptService = ToggleSwitchAutoKillPptService.IsOn;
            SaveSettingsToFile();

            if (Settings.Automation.IsAutoKillEasiNote || Settings.Automation.IsAutoKillPptService)
            {
                timerKillProcess.Start();
            }
            else
            {
                timerKillProcess.Stop();
            }
        }

        private void ToggleSwitchAutoKillEasiNote_Toggled(object sender, RoutedEventArgs e)
        {
            if (!isLoaded || _isLoadingSettings) return;
            Settings.Automation.IsAutoKillEasiNote = ToggleSwitchAutoKillEasiNote.IsOn;
            SaveSettingsToFile();
            if (Settings.Automation.IsAutoKillEasiNote || Settings.Automation.IsAutoKillPptService)
            {
                timerKillProcess.Start();
            }
            else
            {
                timerKillProcess.Stop();
            }
        }

        private void ToggleSwitchSaveScreenshotsInDateFolders_Toggled(object sender, RoutedEventArgs e)
        {
            if (!isLoaded || _isLoadingSettings) return;
            Settings.Automation.IsSaveScreenshotsInDateFolders = ToggleSwitchSaveScreenshotsInDateFolders.IsOn;
            SaveSettingsToFile();
        }

        private void ToggleSwitchAutoSaveStrokesAtScreenshot_Toggled(object sender, RoutedEventArgs e)
        {
            if (!isLoaded || _isLoadingSettings) return;
            Settings.Automation.IsAutoSaveStrokesAtScreenshot = ToggleSwitchAutoSaveStrokesAtScreenshot.IsOn;
            ToggleSwitchAutoSaveStrokesAtClear.Header = ToggleSwitchAutoSaveStrokesAtScreenshot.IsOn ? "清屏时自动截图并保存墨迹" : "清屏时自动截图";
            SaveSettingsToFile();
        }

        private void ToggleSwitchAutoSaveStrokesAtClear_Toggled(object sender, RoutedEventArgs e)
        {
            if (!isLoaded || _isLoadingSettings) return;
            Settings.Automation.IsAutoSaveStrokesAtClear = ToggleSwitchAutoSaveStrokesAtClear.IsOn;
            SaveSettingsToFile();
        }

        private void ToggleSwitchHideStrokeWhenSelecting_Toggled(object sender, RoutedEventArgs e)
        {
            if (!isLoaded || _isLoadingSettings) return;
            Settings.Canvas.HideStrokeWhenSelecting = ToggleSwitchHideStrokeWhenSelecting.IsOn;
            SaveSettingsToFile();
        }

        private void ToggleSwitchAutoSaveStrokesInPowerPoint_Toggled(object sender, RoutedEventArgs e)
        {
            if (!isLoaded || _isLoadingSettings) return;
            Settings.PowerPointSettings.IsAutoSaveStrokesInPowerPoint = ToggleSwitchAutoSaveStrokesInPowerPoint.IsOn;
            SaveSettingsToFile();
        }

        private void ToggleSwitchNotifyPreviousPage_Toggled(object sender, RoutedEventArgs e)
        {
            if (!isLoaded || _isLoadingSettings) return;
            Settings.PowerPointSettings.IsNotifyPreviousPage = ToggleSwitchNotifyPreviousPage.IsOn;
            SaveSettingsToFile();
        }

        private void ToggleSwitchNotifyHiddenPage_Toggled(object sender, RoutedEventArgs e)
        {
            if (!isLoaded || _isLoadingSettings) return;
            Settings.PowerPointSettings.IsNotifyHiddenPage = ToggleSwitchNotifyHiddenPage.IsOn;
            SaveSettingsToFile();
        }

        private void ToggleSwitchNotifyAutoPlayPresentation_Toggled(object sender, RoutedEventArgs e)
        {
            if (!isLoaded || _isLoadingSettings) return;
            Settings.PowerPointSettings.IsNotifyAutoPlayPresentation = ToggleSwitchNotifyAutoPlayPresentation.IsOn;
            SaveSettingsToFile();
        }

        private void SideControlMinimumAutomationSlider_ValueChanged(object sender, RoutedEventArgs e)
        {
            if (!isLoaded || _isLoadingSettings) return;
            Settings.Automation.MinimumAutomationStrokeNumber = (int)SideControlMinimumAutomationSlider.Value;
            SaveSettingsToFile();
        }

        private void AutoSavedStrokesLocationTextBox_TextChanged(object sender, RoutedEventArgs e)
        {
            if (!isLoaded || _isLoadingSettings) return;
            Settings.Automation.AutoSavedStrokesLocation = AutoSavedStrokesLocation.Text;
            SaveSettingsToFile();
        }

        private void PhotoClarityDpiSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (!isLoaded || _isLoadingSettings) return;
            int dpi = (int)Math.Round(e.NewValue);
            Settings.Automation.PhotoClarityDpi = dpi;
            SaveSettingsToFile();
        }

        private void AutoSavedStrokesLocationButton_Click(object sender, RoutedEventArgs e)
        {
            System.Windows.Forms.FolderBrowserDialog folderBrowser = new System.Windows.Forms.FolderBrowserDialog();
            folderBrowser.ShowDialog();
            if (folderBrowser.SelectedPath.Length > 0) AutoSavedStrokesLocation.Text = folderBrowser.SelectedPath;
        }

        private void SetAutoSavedStrokesLocationToDiskDButton_Click(object sender, RoutedEventArgs e)
        {
            if (AutoSavedStrokesLocation != null)
                AutoSavedStrokesLocation.Text = @"D:\Ink Canvas";
            Settings.Automation.AutoSavedStrokesLocation = @"D:\Ink Canvas";
        }

        private void SetAutoSavedStrokesLocationToDocumentFolderButton_Click(object sender, RoutedEventArgs e)
        {
            var path = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments) + @"\Ink Canvas";
            if (AutoSavedStrokesLocation != null)
                AutoSavedStrokesLocation.Text = path;
            Settings.Automation.AutoSavedStrokesLocation = path;
        }

        private void ToggleSwitchAutoDelSavedFiles_Toggled(object sender, RoutedEventArgs e)
        {
            if (!isLoaded || _isLoadingSettings) return;
            Settings.Automation.AutoDelSavedFiles = ToggleSwitchAutoDelSavedFiles.IsOn;
            SaveSettingsToFile();
        }

        private void ComboBoxAutoDelSavedFilesDaysThreshold_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!isLoaded || _isLoadingSettings) return;
            if (ComboBoxAutoDelSavedFilesDaysThreshold.SelectedItem is ComboBoxItem item
                && item.Content != null
                && int.TryParse(item.Content.ToString(), out int days))
            {
                Settings.Automation.AutoDelSavedFilesDaysThreshold = days;
                SaveSettingsToFile();
            }
        }

        private void ToggleSwitchAutoSaveScreenShotInPowerPoint_Toggled(object sender, RoutedEventArgs e)
        {
            if (!isLoaded || _isLoadingSettings) return;
            Settings.PowerPointSettings.IsAutoSaveScreenShotInPowerPoint = ToggleSwitchAutoSaveScreenShotInPowerPoint.IsOn;
            SaveSettingsToFile();
        }

        #endregion

        #region Gesture

        private void ComboBoxMatrixTransformCenterPoint_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!isLoaded || _isLoadingSettings) return;
            Settings.Gesture.MatrixTransformCenterPoint = (MatrixTransformCenterPointOptions)ComboBoxMatrixTransformCenterPoint.SelectedIndex;
            SaveSettingsToFile();
        }

        private void ToggleSwitchEnableFingerGestureSlideShowControl_Toggled(object sender, RoutedEventArgs e)
        {
            if (!isLoaded || _isLoadingSettings) return;
            Settings.PowerPointSettings.IsEnableFingerGestureSlideShowControl = ToggleSwitchEnableFingerGestureSlideShowControl.IsOn;
            SaveSettingsToFile();
        }

        private void ToggleSwitchAutoSwitchTwoFingerGesture_Toggled(object sender, RoutedEventArgs e)
        {
            if (!isLoaded || _isLoadingSettings) return;
            Settings.Gesture.AutoSwitchTwoFingerGesture = ToggleSwitchAutoSwitchTwoFingerGesture.IsOn;
            SaveSettingsToFile();
        }

        private void ToggleSwitchEnableTwoFingerZoom_Toggled(object sender, RoutedEventArgs e)
        {
            if (!isLoaded || _isLoadingSettings) return;
            if (sender == ToggleSwitchEnableTwoFingerZoom)
            {
                BoardToggleSwitchEnableTwoFingerZoom.IsOn = ToggleSwitchEnableTwoFingerZoom.IsOn;
            }
            else
            {
                ToggleSwitchEnableTwoFingerZoom.IsOn = BoardToggleSwitchEnableTwoFingerZoom.IsOn;
            }
            Settings.Gesture.IsEnableTwoFingerZoom = ToggleSwitchEnableTwoFingerZoom.IsOn;
            CheckEnableTwoFingerGestureBtnColorPrompt();
            SaveSettingsToFile();
        }

        private void ToggleSwitchEnableMultiTouchMode_Toggled(object sender, RoutedEventArgs e)
        {
            //if (!isLoaded || _isLoadingSettings) return;
            if (sender == ToggleSwitchEnableMultiTouchMode)
            {
                BoardToggleSwitchEnableMultiTouchMode.IsOn = ToggleSwitchEnableMultiTouchMode.IsOn;
            }
            else
            {
                ToggleSwitchEnableMultiTouchMode.IsOn = BoardToggleSwitchEnableMultiTouchMode.IsOn;
            }
            if (ToggleSwitchEnableMultiTouchMode.IsOn)
            {
                if (!isInMultiTouchMode) BorderMultiTouchMode_MouseUp(null, null);
            }
            else
            {
                if (isInMultiTouchMode) BorderMultiTouchMode_MouseUp(null, null);
            }
            Settings.Gesture.IsEnableMultiTouchMode = ToggleSwitchEnableMultiTouchMode.IsOn;
            CheckEnableTwoFingerGestureBtnColorPrompt();
            SaveSettingsToFile();
        }

        private void ToggleSwitchEnableTwoFingerTranslate_Toggled(object sender, RoutedEventArgs e)
        {
            if (!isLoaded || _isLoadingSettings) return;
            if (sender == ToggleSwitchEnableTwoFingerTranslate)
            {
                BoardToggleSwitchEnableTwoFingerTranslate.IsOn = ToggleSwitchEnableTwoFingerTranslate.IsOn;
            }
            else
            {
                ToggleSwitchEnableTwoFingerTranslate.IsOn = BoardToggleSwitchEnableTwoFingerTranslate.IsOn;
            }
            Settings.Gesture.IsEnableTwoFingerTranslate = ToggleSwitchEnableTwoFingerTranslate.IsOn;
            CheckEnableTwoFingerGestureBtnColorPrompt();
            SaveSettingsToFile();
        }

        private void ToggleSwitchEnableTwoFingerRotation_Toggled(object sender, RoutedEventArgs e)
        {
            if (!isLoaded || _isLoadingSettings) return;

            if (sender == ToggleSwitchEnableTwoFingerRotation)
            {
                BoardToggleSwitchEnableTwoFingerRotation.IsOn = ToggleSwitchEnableTwoFingerRotation.IsOn;
            }
            else
            {
                ToggleSwitchEnableTwoFingerRotation.IsOn = BoardToggleSwitchEnableTwoFingerRotation.IsOn;
            }
            Settings.Gesture.IsEnableTwoFingerRotation = ToggleSwitchEnableTwoFingerRotation.IsOn;
            Settings.Gesture.IsEnableTwoFingerRotationOnSelection = ToggleSwitchEnableTwoFingerRotationOnSelection.IsOn;
            CheckEnableTwoFingerGestureBtnColorPrompt();
            SaveSettingsToFile();
        }

        private void ToggleSwitchEnableTwoFingerGestureInPresentationMode_Toggled(object sender, RoutedEventArgs e)
        {
            if (!isLoaded || _isLoadingSettings) return;
            Settings.PowerPointSettings.IsEnableTwoFingerGestureInPresentationMode = ToggleSwitchEnableTwoFingerGestureInPresentationMode.IsOn;
            SaveSettingsToFile();
        }

        #endregion

        #region Reset

        public static void SetSettingsToRecommendation()
        {
            Settings = new Settings();
        }

        private void BtnResetToSuggestion_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                isLoaded = false;
                SetSettingsToRecommendation();
                SaveSettingsToFile();
                LoadSettings();
                isLoaded = true;
                if (ToggleSwitchRunAtStartup != null)
                    ToggleSwitchRunAtStartup.IsOn = true;
            }
            catch (Exception ex)
            {
                LogHelper.WriteLogToFile($"MW_Settings | Error reading touch multiplier range: {ex.Message}", LogHelper.LogType.Error);
            }
            ShowNotificationAsync("设置已重置为默认推荐设置~");
        }

        private void BtnOpenInitialSetup_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var wizard = new InitialSetupWindow
                {
                    Owner = this,
                    Topmost = true
                };
                Helpers.WindowMemoryHelper.ReleaseOnClose(wizard);
                wizard.Show();
            }
            catch (Exception ex)
            {
                LogHelper.WriteLogToFile($"MW_Settings | Error opening initial setup: {ex.Message}", LogHelper.LogType.Error);
            }
        }

        private async void SpecialVersionResetToSuggestion_Click()
        {
            await Task.Delay(1000);
            try
            {
                isLoaded = false;
                SetSettingsToRecommendation();
                SaveSettingsToFile();
                LoadSettings();
                isLoaded = true;
            }
            catch (Exception ex)
            {
                LogHelper.WriteLogToFile($"MW_Settings | Error resetting settings: {ex.Message}", LogHelper.LogType.Error);
            }
        }

        #endregion

        #region Ink To Shape

        // 主开关状态写入的防重入标记，避免三处开关互相触发 Toggled 造成死循环
        private bool _syncingInkToShapeToggles = false;

        private void ToggleSwitchEnableInkToShape_Toggled(object sender, RoutedEventArgs e)
        {
            if (!isLoaded || _isLoadingSettings) return;
            if (_syncingInkToShapeToggles) return;

            // 以触发事件的开关状态为准（设置窗口 / 浮动栏 / 白板任一开关都能触发）
            bool isOn = false;
            try
            {
                if (sender is iNKORE.UI.WPF.Modern.Controls.ToggleSwitch ts)
                    isOn = ts.IsOn;
            }
            catch { }

            Settings.InkToShape.IsInkToShapeEnabled = isOn;
            SaveSettingsToFile();

            // 联动：设置窗口 / 浮动栏 / 白板三处开关保持同一状态
            _syncingInkToShapeToggles = true;
            try
            {
                if (ToggleSwitchEnableInkToShape != null)
                    ToggleSwitchEnableInkToShape.IsOn = isOn;
                if (ToggleSwitchEnableInkToShapeFloatBar != null)
                    ToggleSwitchEnableInkToShapeFloatBar.IsOn = isOn;
                if (ToggleSwitchEnableInkToShapeBoard != null)
                    ToggleSwitchEnableInkToShapeBoard.IsOn = isOn;
            }
            catch { }
            finally
            {
                _syncingInkToShapeToggles = false;
            }

            // 主开关关闭时隐藏子开关并置为关闭；重新启用时恢复显示并还原之前状态
            UpdateInkRecognitionSubOptionsVisibility(isOn);
        }

        // 子开关状态备份（关闭主开关前记住，重新启用时还原）
        private bool _inkSubOptionsBackedUp = false;
        private bool _backupTriangle = false;
        private bool _backupRectangle = false;
        private bool _backupCircle = false;
        private bool _backupAutoStraighten = false;
        private bool _backupLineEndpointSnapping = false;
        private bool _backupStopTiming = false;

        private void UpdateInkRecognitionSubOptionsVisibility(bool enabled)
        {
            try
            {
                if (InkRecognitionSubOptions == null) return;

                if (enabled)
                {
                    // 重新启用：显示子开关并还原之前备份的状态
                    if (_inkSubOptionsBackedUp)
                    {
                        if (ToggleSwitchEnableTriangleRecognition != null)
                            ToggleSwitchEnableTriangleRecognition.IsOn = _backupTriangle;
                        if (ToggleSwitchEnableRectangleRecognition != null)
                            ToggleSwitchEnableRectangleRecognition.IsOn = _backupRectangle;
                        if (ToggleSwitchEnableCircleRecognition != null)
                            ToggleSwitchEnableCircleRecognition.IsOn = _backupCircle;
                        if (ToggleSwitchAutoStraightenLine != null)
                            ToggleSwitchAutoStraightenLine.IsOn = _backupAutoStraighten;
                        if (ToggleSwitchLineEndpointSnapping != null)
                            ToggleSwitchLineEndpointSnapping.IsOn = _backupLineEndpointSnapping;
                        if (ToggleSwitchStopTimingStraighten != null)
                            ToggleSwitchStopTimingStraighten.IsOn = _backupStopTiming;
                        _inkSubOptionsBackedUp = false;
                    }
                    InkRecognitionSubOptions.Visibility = Visibility.Visible;
                }
                else
                {
                    // 关闭主开关：备份当前子开关状态，然后全部置为关闭并隐藏
                    if (!_inkSubOptionsBackedUp)
                    {
                        _backupTriangle = ToggleSwitchEnableTriangleRecognition?.IsOn ?? false;
                        _backupRectangle = ToggleSwitchEnableRectangleRecognition?.IsOn ?? false;
                        _backupCircle = ToggleSwitchEnableCircleRecognition?.IsOn ?? false;
                        _backupAutoStraighten = ToggleSwitchAutoStraightenLine?.IsOn ?? false;
                        _backupLineEndpointSnapping = ToggleSwitchLineEndpointSnapping?.IsOn ?? false;
                        _backupStopTiming = ToggleSwitchStopTimingStraighten?.IsOn ?? false;
                        _inkSubOptionsBackedUp = true;
                    }
                    if (ToggleSwitchEnableTriangleRecognition != null)
                        ToggleSwitchEnableTriangleRecognition.IsOn = false;
                    if (ToggleSwitchEnableRectangleRecognition != null)
                        ToggleSwitchEnableRectangleRecognition.IsOn = false;
                    if (ToggleSwitchEnableCircleRecognition != null)
                        ToggleSwitchEnableCircleRecognition.IsOn = false;
                    if (ToggleSwitchAutoStraightenLine != null)
                        ToggleSwitchAutoStraightenLine.IsOn = false;
                    if (ToggleSwitchLineEndpointSnapping != null)
                        ToggleSwitchLineEndpointSnapping.IsOn = false;
                    if (ToggleSwitchStopTimingStraighten != null)
                        ToggleSwitchStopTimingStraighten.IsOn = false;
                    InkRecognitionSubOptions.Visibility = Visibility.Collapsed;
                }
            }
            catch { }
        }

        #endregion

        #region Advanced

        private void ToggleSwitchIsSpecialScreen_OnToggled(object sender, RoutedEventArgs e)
        {
            if (!isLoaded || _isLoadingSettings) return;
            Settings.Advanced.IsSpecialScreen = ToggleSwitchIsSpecialScreen.IsOn;
            TouchMultiplierSlider.Visibility = ToggleSwitchIsSpecialScreen.IsOn ? Visibility.Visible : Visibility.Collapsed;
            SaveSettingsToFile();
        }

        private void TouchMultiplierSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (!isLoaded || _isLoadingSettings) return;
            Settings.Advanced.TouchMultiplier = e.NewValue;
            SaveSettingsToFile();
        }

        private void BorderCalculateMultiplier_TouchDown(object sender, TouchEventArgs e)
        {
            var args = e.GetTouchPoint(null).Bounds;
            double value;
            if (!Settings.Advanced.IsQuadIR) value = args.Width;
            else value = Math.Sqrt(args.Width * args.Height); //四边红外
            // 计算推荐触摸倍数
            double recommended = 5 / (value * 1.1);
            TextBlockShowCalculatedMultiplier.Text = recommended.ToString();

            // 防御性：在应用到设置与滑块之前进行范围夹紧，避免超出 Slider 的上下限导致异常
            double touchMultiplierMin = 0.0;
            double touchMultiplierMax = 5.0;
            try
            {
                // 若控件已加载，优先以控件的范围为准
                if (TouchMultiplierSlider != null)
                {
                    touchMultiplierMin = TouchMultiplierSlider.Minimum;
                    touchMultiplierMax = TouchMultiplierSlider.Maximum;
                }
            }
            catch (Exception ex)
            {
                LogHelper.WriteLogToFile($"MW_Settings | Error resetting settings: {ex.Message}", LogHelper.LogType.Error);
            }
            double recommendedClamped = Math.Max(touchMultiplierMin, Math.Min(touchMultiplierMax, recommended));

            // 新增：提示并自动调整相关参数
            try
            {
                var promptText = $"检测到推荐触摸倍数为 {recommended:F2}。\n\n是否启用特殊屏幕并自动调整相关参数？\n将执行：\n- TouchMultiplier = 推荐值\n- IsSpecialScreen = 启用\n- ThresholdValue = 2.5\n- EraserSize 因子 = 0.8\n- BoundsWidth 保持当前模式值";
                var result = MessageBox.Show(promptText, "应用推荐设置", MessageBoxButton.YesNo, MessageBoxImage.Question);
                if (result == MessageBoxResult.Yes)
                {
                    // 应用推荐设置
                    Settings.Advanced.IsSpecialScreen = true;
                    Settings.Advanced.TouchMultiplier = recommendedClamped;
                    Settings.Advanced.NibModeBoundsWidthThresholdValue = 2.5;
                    Settings.Advanced.FingerModeBoundsWidthThresholdValue = 2.5;
                    Settings.Advanced.NibModeBoundsWidthEraserSize = 0.8;
                    Settings.Advanced.FingerModeBoundsWidthEraserSize = 0.8;

                    // 更新界面控件（若存在，避免异常）
                    try
                    {
                        // 设置滑块值前同样进行范围检查
                        if (TouchMultiplierSlider != null)
                        {
                            var v = Settings.Advanced.TouchMultiplier;
                            TouchMultiplierSlider.Value = Math.Max(TouchMultiplierSlider.Minimum, Math.Min(TouchMultiplierSlider.Maximum, v));
                        }
                        NibModeBoundsWidthThresholdValueSlider.Value = Settings.Advanced.NibModeBoundsWidthThresholdValue;
                        FingerModeBoundsWidthThresholdValueSlider.Value = Settings.Advanced.FingerModeBoundsWidthThresholdValue;
                        NibModeBoundsWidthEraserSizeSlider.Value = Settings.Advanced.NibModeBoundsWidthEraserSize;
                        FingerModeBoundsWidthEraserSizeSlider.Value = Settings.Advanced.FingerModeBoundsWidthEraserSize;
                    }
                    catch (Exception ex)
                    {
                        LogHelper.WriteLogToFile($"MW_Settings | Error applying touch multiplier settings: {ex.Message}", LogHelper.LogType.Error);
                    }

                    SaveSettingsToFile();
                    MessageBox.Show("已应用推荐设置并调整相关参数。", "完成", MessageBoxButton.OK, MessageBoxImage.Information);
                }
            }
            catch (Exception ex)
            {
                LogHelper.WriteLogToFile($"MW_Settings | Error calculating touch multiplier: {ex.Message}", LogHelper.LogType.Error);
            }
        }

        private void NibModeBoundsWidthSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (!isLoaded || _isLoadingSettings) return;
            Settings.Advanced.NibModeBoundsWidth = (int)e.NewValue;
            BoundsWidth = Settings.Startup.IsEnableNibMode ? Settings.Advanced.NibModeBoundsWidth : Settings.Advanced.FingerModeBoundsWidth;
            SaveSettingsToFile();
        }

        private void FingerModeBoundsWidthSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (!isLoaded || _isLoadingSettings) return;
            Settings.Advanced.FingerModeBoundsWidth = (int)e.NewValue;
            BoundsWidth = Settings.Startup.IsEnableNibMode ? Settings.Advanced.NibModeBoundsWidth : Settings.Advanced.FingerModeBoundsWidth;
            SaveSettingsToFile();
        }

        private void NibModeBoundsWidthThresholdValueSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (!isLoaded || _isLoadingSettings) return;
            Settings.Advanced.NibModeBoundsWidthThresholdValue = (double)e.NewValue;
            SaveSettingsToFile();
        }

        private void FingerModeBoundsWidthThresholdValueSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (!isLoaded || _isLoadingSettings) return;
            Settings.Advanced.FingerModeBoundsWidthThresholdValue = (double)e.NewValue;
            SaveSettingsToFile();
        }

        private void NibModeBoundsWidthEraserSizeSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (!isLoaded || _isLoadingSettings) return;
            Settings.Advanced.NibModeBoundsWidthEraserSize = (double)e.NewValue;
            SaveSettingsToFile();
        }

        private void FingerModeBoundsWidthEraserSizeSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (!isLoaded || _isLoadingSettings) return;
            Settings.Advanced.FingerModeBoundsWidthEraserSize = (double)e.NewValue;
            SaveSettingsToFile();
        }

        private void ToggleSwitchIsQuadIR_Toggled(object sender, RoutedEventArgs e)
        {
            if (!isLoaded || _isLoadingSettings) return;
            Settings.Advanced.IsQuadIR = ToggleSwitchIsQuadIR.IsOn;
            SaveSettingsToFile();
        }

        private void ToggleSwitchIsLogEnabled_Toggled(object sender, RoutedEventArgs e)
        {
            if (!isLoaded || _isLoadingSettings) return;
            Settings.Advanced.IsLogEnabled = ToggleSwitchIsLogEnabled.IsOn;
            SaveSettingsToFile();
        }

        private void ToggleSwitchIsSecondConfimeWhenShutdownApp_Toggled(object sender, RoutedEventArgs e)
        {
            if (!isLoaded || _isLoadingSettings) return;
            Settings.Advanced.IsSecondConfimeWhenShutdownApp = ToggleSwitchIsSecondConfimeWhenShutdownApp.IsOn;
            SaveSettingsToFile();
        }

        private void ToggleSwitchIsEnableEdgeGestureUtil_Toggled(object sender, RoutedEventArgs e)
        {
            if (!isLoaded || _isLoadingSettings) return;
            Settings.Advanced.IsEnableEdgeGestureUtil = ToggleSwitchIsEnableEdgeGestureUtil.IsOn;
            if (OSVersion.GetOperatingSystem() >= OSVersionExtension.OperatingSystem.Windows10) EdgeGestureUtil.DisableEdgeGestures(new WindowInteropHelper(this).Handle, ToggleSwitchIsEnableEdgeGestureUtil.IsOn);
            SaveSettingsToFile();
        }

        private void ToggleSwitchIsEnableSilentRestartOnCrash_Toggled(object sender, RoutedEventArgs e)
        {
            if (!isLoaded || _isLoadingSettings) return;
            Settings.Advanced.IsEnableSilentRestartOnCrash = ToggleSwitchIsEnableSilentRestartOnCrash.IsOn;
            SaveSettingsToFile();
        }

        private void ToggleSwitchAutoStraightenLine_Toggled(object sender, RoutedEventArgs e)
        {
            if (!isLoaded || _isLoadingSettings) return;
            Settings.Canvas.AutoStraightenLine = ToggleSwitchAutoStraightenLine.IsOn;
            SaveSettingsToFile();
        }

        private void AutoStraightenLineThresholdSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (!isLoaded || _isLoadingSettings) return;
            Settings.Canvas.AutoStraightenLineThreshold = (int)e.NewValue;
            SaveSettingsToFile();
        }

        private void ToggleSwitchLineEndpointSnapping_Toggled(object sender, RoutedEventArgs e)
        {
            if (!isLoaded || _isLoadingSettings) return;
            Settings.Canvas.LineEndpointSnapping = ToggleSwitchLineEndpointSnapping.IsOn;
            SaveSettingsToFile();
        }

        private void LineEndpointSnappingThresholdSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (!isLoaded || _isLoadingSettings) return;
            Settings.Canvas.LineEndpointSnappingThreshold = (int)e.NewValue;
            SaveSettingsToFile();
        }

        private void LineStraightenSensitivitySlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (!isLoaded || _isLoadingSettings) return;
            Settings.InkToShape.LineStraightenSensitivity = e.NewValue;
            SaveSettingsToFile();
        }

        private void ToggleSwitchStopTimingStraighten_Toggled(object sender, RoutedEventArgs e)
        {
            if (!isLoaded || _isLoadingSettings) return;
            Settings.Canvas.StopTimingStraighten = ToggleSwitchStopTimingStraighten.IsOn;
            SaveSettingsToFile();
        }

        private void StopTimingThresholdMsSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (!isLoaded || _isLoadingSettings) return;
            Settings.Canvas.StopTimingThresholdMs = (int)e.NewValue;
            SaveSettingsToFile();
        }

        private void StopTimingErrorSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (!isLoaded || _isLoadingSettings) return;
            Settings.Canvas.StopTimingError = e.NewValue;
            SaveSettingsToFile();
        }

        private void ToggleSwitchEnableTriangleRecognition_Toggled(object sender, RoutedEventArgs e)
        {
            if (!isLoaded || _isLoadingSettings) return;
            Settings.InkToShape.IsInkToShapeTriangle = ToggleSwitchEnableTriangleRecognition.IsOn;
            SaveSettingsToFile();
        }

        private void ToggleSwitchEnableRectangleRecognition_Toggled(object sender, RoutedEventArgs e)
        {
            if (!isLoaded || _isLoadingSettings) return;
            Settings.InkToShape.IsInkToShapeRectangle = ToggleSwitchEnableRectangleRecognition.IsOn;
            SaveSettingsToFile();
        }

        private void ToggleSwitchEnableCircleRecognition_Toggled(object sender, RoutedEventArgs e)
        {
            if (!isLoaded || _isLoadingSettings) return;
            Settings.InkToShape.IsInkToShapeCircle = ToggleSwitchEnableCircleRecognition.IsOn;
            SaveSettingsToFile();
        }

        #endregion

        public static void SaveSettingsToFile()
        {
            string text = JsonConvert.SerializeObject(Settings, Formatting.Indented);

            // 优先写入用户数据目录（始终可写，避免安装到 Program Files 时静默保存失败）
            string userFile = App.UserDataPath + settingsFileName;
            try
            {
                if (!Directory.Exists(App.UserDataPath))
                {
                    try { Directory.CreateDirectory(App.UserDataPath); } catch { }
                }
                File.WriteAllText(userFile, text);
            }
            catch (Exception ex)
            {
                LogHelper.WriteLogToFile($"MW_Settings | Error saving settings to user data path: {ex.Message}", LogHelper.LogType.Error);

                // 回退尝试写入 exe 目录（兼容便携模式 / 旧版本行为）
                string legacyFile = App.RootPath + settingsFileName;
                try
                {
                    File.WriteAllText(legacyFile, text);
                }
                catch (Exception ex2)
                {
                    LogHelper.WriteLogToFile($"MW_Settings | Error saving settings to legacy path: {ex2.Message}", LogHelper.LogType.Error);
                }
            }
        }

        private void SCManipulationBoundaryFeedback(object sender, ManipulationBoundaryFeedbackEventArgs e)
        {
            e.Handled = true;
        }

        private void HyperlinkSourceToPresentRepository_Click(object sender, RoutedEventArgs e)
        {
            Process.Start("https://github.com/muqiu-pika/Ink-Canvas-Ultra");
            HideSubPanels();
        }

        private void HyperlinkSourceToOringinalRepository_Click(object sender, RoutedEventArgs e)
        {
            Process.Start("https://github.com/WXRIW/Ink-Canvas");
            HideSubPanels();
        }
    }
}
