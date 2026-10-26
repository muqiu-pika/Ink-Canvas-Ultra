using Ink_Canvas.Helpers;
using Newtonsoft.Json;
using OSVersionExtension;
using System;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using File = System.IO.File;

namespace Ink_Canvas
{
    public partial class MainWindow : Window
    {
        private double NormalizeFloatingBarScale(double value)
        {
            if (double.IsNaN(value) || double.IsInfinity(value) || value <= 0)
            {
                value = 100;
            }
            else if (value <= 3)
            {
                value *= 100;
            }

            double min = 50;
            double max = 150;
            if (SliderFloatingBarScale != null)
            {
                min = SliderFloatingBarScale.Minimum > 0 ? SliderFloatingBarScale.Minimum : 50;
                max = SliderFloatingBarScale.Maximum > 0 ? SliderFloatingBarScale.Maximum : 150;
            }

            if (value < min)
            {
                value = min;
            }
            else if (value > max)
            {
                value = max;
            }

            return value;
        }

        private void LoadSettings(bool isStartup = false)
        {
            _isLoadingSettings = true;
            try
            {
            // 仅在启动时、或内存中尚无设置对象时，才从磁盘读取并反序列化设置文件。
            // 普通“打开设置窗口”场景下，内存中的 Settings 已由各开关的 Toggled 处理持续同步为最新，
            // 跳过此处可避免每次打开都进行不必要的文件读取与 JSON 解析，减少卡顿。
            bool needReloadFromDisk = isStartup || Settings == null || Settings.Startup == null;
            if (needReloadFromDisk)
            {
                try
                {
                    // 优先从用户数据目录（始终可写）读取，其次回退到 exe 目录（兼容旧版本/便携模式）
                    string userFile = App.UserDataPath + settingsFileName;
                    string legacyFile = App.RootPath + settingsFileName;
                    string targetFile = null;

                    if (File.Exists(userFile))
                    {
                        targetFile = userFile;
                    }
                    else if (File.Exists(legacyFile))
                    {
                        targetFile = legacyFile;
                    }

                    if (targetFile != null)
                    {
                        Settings loaded;
                        if (TryLoadSettingsFile(targetFile, out loaded))
                        {
                            Settings = loaded;
                        }
                        else if (TryLoadSettingsFile(targetFile + ".bak", out loaded))
                        {
                            // 主文件损坏，自动备份仍可用 —— 直接用备份恢复，
                            // 避免用户"设置莫名其妙变回默认、向导重新弹出"
                            LogHelper.WriteLogToFile(
                                "Settings.json is unreadable, restored from backup: " + targetFile + ".bak",
                                LogHelper.LogType.Error);
                            Settings = loaded;
                        }
                        else
                        {
                            // 主文件与备份都不可用，才真正放弃（走默认值）。
                            // 损坏现场另存为 .corrupt，便于事后排查而不是被直接覆盖抹掉。
                            TryQuarantineCorruptSettings(targetFile);
                        }
                    }
                    else
                    {
                        BtnResetToSuggestion_Click(null, null);
                    }
                }
                catch (Exception ex)
                {
                    LogHelper.WriteLogToFile(ex.ToString(), LogHelper.LogType.Error);
                }
            }

            if (Settings == null) Settings = new Settings();
            if (Settings.Advanced == null) Settings.Advanced = new Advanced();
            if (Settings.Appearance == null) Settings.Appearance = new Appearance();
            if (Settings.Automation == null) Settings.Automation = new Automation();
            if (string.IsNullOrWhiteSpace(Settings.Automation.AutoSavedStrokesLocation))
            {
                Settings.Automation.AutoSavedStrokesLocation = App.DefaultAutoSavedStrokesLocation;
            }
            else if (App.IsForeignUserProfilePath(Settings.Automation.AutoSavedStrokesLocation))
            {
                // 便携包整目录拷贝 / 旧版 exe 目录设置迁移会带入其它电脑用户的路径
                // （如 C:\Users\muqiu\Documents\Ink Canvas）——本机无此目录，保存必失败，重置为当前用户默认目录
                LogHelper.WriteLogToFile(
                    $"AutoSavedStrokesLocation 指向本机不存在的他人用户目录，已重置为默认目录。原值: {Settings.Automation.AutoSavedStrokesLocation}",
                    LogHelper.LogType.Warning);
                Settings.Automation.AutoSavedStrokesLocation = App.DefaultAutoSavedStrokesLocation;
            }
            if (Settings.PowerPointSettings == null) Settings.PowerPointSettings = new PowerPointSettings();
            if (Settings.Canvas == null) Settings.Canvas = new Canvas();
            if (Settings.Gesture == null) Settings.Gesture = new Gesture();
            if (Settings.InkToShape == null) Settings.InkToShape = new InkToShape();
            if (Settings.Startup == null) Settings.Startup = new Startup();
            if (Settings.RandSettings == null) Settings.RandSettings = new RandSettings();
            if (Settings.Camera == null) Settings.Camera = new CameraSettings();

            double normalizedFloatingBarScale = NormalizeFloatingBarScale(Settings.Appearance.FloatingBarScale);
            bool floatingBarScaleWasNormalized = Math.Abs(Settings.Appearance.FloatingBarScale - normalizedFloatingBarScale) > double.Epsilon;
            Settings.Appearance.FloatingBarScale = normalizedFloatingBarScale;

            if (isStartup)
            {
                CursorIcon_Click(null, null);
            }
            // 始终运行：更新浮动栏/黑板宿主中的外观元素（无论设置窗口是否打开）
            if (Settings.Appearance != null)
            {
                if (Settings.Appearance.IsEnableDisPlayFloatBarText)
                {
                    FloatBarSelectIconTextBlock.Visibility = Visibility.Visible;
                    Icon_Pen.Height = 22;
                    Icon_Eraser1.Height = 22;
                    Icon_Eraser2.Height = 22;
                    Icon_Eraser2.Margin = new Thickness(5, -22, 0, -8);
                    Icon_EraserByStrokes1.Height = 22;
                    Icon_EraserByStrokes2.Height = 22;
                    Icon_EraserByStrokes2.Margin = new Thickness(12, -22, 0, -8);
                    Icon_Select1.Height = 22;
                    Icon_Select2.Height = 22;
                    Icon_Select2.Margin = new Thickness(6, -18, 0, -8);
                    Icon_Undo.Margin = new Thickness(0, 1.5, 0, -1.5);
                    Icon_Redo.Margin = new Thickness(0, 1.5, 0, -1.5);
                }
                else
                {
                    FloatBarSelectIconTextBlock.Visibility = Visibility.Collapsed;
                    Icon_Pen.Height = 32;
                    Icon_Eraser1.Height = 32;
                    Icon_Eraser2.Height = 32;
                    Icon_Eraser2.Margin = new Thickness(5, -32, 0, -8);
                    Icon_EraserByStrokes1.Height = 32;
                    Icon_EraserByStrokes2.Height = 32;
                    Icon_EraserByStrokes2.Margin = new Thickness(12, -32, 0, -8);
                    Icon_Select1.Height = 32;
                    Icon_Select2.Height = 32;
                    Icon_Select2.Margin = new Thickness(6, -28, 0, -8);
                    Icon_Undo.Margin = new Thickness(0);
                    Icon_Redo.Margin = new Thickness(0);
                }
                // Nib Mode Toggler 可见性
                if (Settings.Appearance.IsEnableDisPlayNibModeToggler)
                {
                    NibModeSimpleStackPanel.Visibility = Visibility.Visible;
                    BoardNibModeSimpleStackPanel.Visibility = Visibility.Visible;
                }
                else
                {
                    NibModeSimpleStackPanel.Visibility = Visibility.Collapsed;
                    BoardNibModeSimpleStackPanel.Visibility = Visibility.Collapsed;
                }
                SystemEvents_UserPreferenceChanged(null, null);
                if (Settings.Appearance.IsColorfulViewboxFloatingBar)
                {
                    var gradientBrush = new LinearGradientBrush
                    {
                        StartPoint = new Point(0, 0),
                        EndPoint = new Point(1, 1),
                        GradientStops = new GradientStopCollection
                        {
                            new GradientStop(Color.FromArgb(0x95, 0x80, 0xB0, 0xFF), 0),
                            new GradientStop(Color.FromArgb(0x95, 0xC0, 0xFF, 0xC0), 1)
                        }
                    };
                    EnableTwoFingerGestureBorder.Background = gradientBrush;
                    BorderFloatingBarMainControls.Background = gradientBrush;
                    BorderFloatingBarMoveControls.Background = gradientBrush;
                    BtnPPTSlideShowEnd.Background = gradientBrush;
                }
                ApplyScaling();
                ApplyVideoPresenterSidebarPosition();
            }
            SyncPenPaletteControlsFromSettings();

            // 以下设置 UI 控件填充仅在设置窗口打开时执行
            if (SettingsWindow != null)
            {
                try
                {
                    if (File.Exists(Environment.GetFolderPath(Environment.SpecialFolder.Startup) + "\\Ink Canvas Annotation.lnk"))
                    {
                    StartAutomaticallyDel("Ink Canvas Annotation");
                    StartAutomaticallyCreate("Ink Canvas Ultra");
                    ToggleSwitchRunAtStartup.IsOn = true;
                }
                else if (File.Exists(Environment.GetFolderPath(Environment.SpecialFolder.Startup) + "\\Ink Canvas Ultra.lnk"))
                {
                    ToggleSwitchRunAtStartup.IsOn = true;
                }
            }
            catch (Exception ex)
            {
                LogHelper.WriteLogToFile(ex.ToString(), LogHelper.LogType.Error);
            }
            if (Settings.Startup != null)
            {
                if (isStartup)
                {
                    if (Settings.Automation.AutoDelSavedFiles)
                    {
                        DelAutoSavedFiles.DeleteFilesOlder(Settings.Automation.AutoSavedStrokesLocation, Settings.Automation.AutoDelSavedFilesDaysThreshold);
                    }
                    if (Settings.Startup.IsFoldAtStartup)
                    {
                        FoldFloatingBar_Click(Fold_Icon, null);
                    }
                }
                if (Settings.Startup.IsAutoUpdate)
                {
                    ToggleSwitchIsAutoUpdate.IsOn = true;
                    // 注：AutoUpdate() 的实际调用已移到 ApplySettingsSideEffects()，且仅在启动时触发。
                    // 此处不再直接调用，否则每打开一次设置窗口就会重复发起一次版本检测请求。
                }
                ToggleSwitchIsAutoUpdateWithProxy.IsOn = Settings.Startup.IsAutoUpdateWithProxy;
                AutoUpdateWithProxy_Title.Visibility = Settings.Startup.IsAutoUpdateWithProxy ? Visibility.Visible : Visibility.Collapsed;
                AutoUpdateProxyTextBox.Text = Settings.Startup.AutoUpdateProxy;
                IsAutoUpdateWithSilenceBlock.Visibility = Settings.Startup.IsAutoUpdate ? Visibility.Visible : Visibility.Collapsed;
                if (Settings.Startup.IsAutoUpdateWithSilence)
                {
                    ToggleSwitchIsAutoUpdateWithSilence.IsOn = true;
                }
                AutoUpdateTimePeriodBlock.Visibility = Settings.Startup.IsAutoUpdateWithSilence ? Visibility.Visible : Visibility.Collapsed;

                AutoUpdateWithSilenceTimeComboBox.InitializeAutoUpdateWithSilenceTimeComboBoxOptions(AutoUpdateWithSilenceStartTimeComboBox, AutoUpdateWithSilenceEndTimeComboBox);
                AutoUpdateWithSilenceStartTimeComboBox.SelectedItem = Settings.Startup.AutoUpdateWithSilenceStartTime;
                AutoUpdateWithSilenceEndTimeComboBox.SelectedItem = Settings.Startup.AutoUpdateWithSilenceEndTime;

                // 手动检查更新：每次打开设置窗口都复位按钮为"立即检查"并刷新提示文本（上次检测时间 + 当前版本号）
                try
                {
                    if (BtnCheckUpdateNow != null)
                    {
                        BtnCheckUpdateNow.Content = "立即检查";
                        BtnCheckUpdateNow.IsEnabled = true;
                    }
                    UpdateManualCheckInfoText();
                    HideManualUpdateProgress();
                }
                catch { }

                ToggleSwitchFoldAtStartup.IsOn = Settings.Startup.IsFoldAtStartup;
            }
            // 注：此处原有一个 else{ Settings.Startup = new Startup(); } 分支，附着于上面
            // 的 if (Settings.Startup != null)。由于第 129 行已保证 Startup 非空，该分支
            // 实为死代码，且一旦 Startup 为空它会重建默认对象、读到的设置全部作废，
            // 与 129 行的兜底重复。现直接删除，空值兜底统一收敛到 129 行。
            if (Settings.InkToShape != null)
            {
                ToggleSwitchEnableInkToShape.IsOn = Settings.InkToShape.IsInkToShapeEnabled;
                ToggleSwitchEnableTriangleRecognition.IsOn = Settings.InkToShape.IsInkToShapeTriangle;
                ToggleSwitchEnableRectangleRecognition.IsOn = Settings.InkToShape.IsInkToShapeRectangle;
                ToggleSwitchEnableCircleRecognition.IsOn = Settings.InkToShape.IsInkToShapeCircle;

                LineStraightenSensitivitySlider.Value = Settings.InkToShape.LineStraightenSensitivity;
            }
            // 根据主开关初始化子开关的可见性与状态
            UpdateInkRecognitionSubOptionsVisibility(Settings.InkToShape?.IsInkToShapeEnabled ?? true);
            if (Settings.Canvas != null)
            {
                ToggleSwitchAutoStraightenLine.IsOn = Settings.Canvas.AutoStraightenLine;
                AutoStraightenLineThresholdSlider.Value = Settings.Canvas.AutoStraightenLineThreshold;
                ToggleSwitchLineEndpointSnapping.IsOn = Settings.Canvas.LineEndpointSnapping;
                LineEndpointSnappingThresholdSlider.Value = Settings.Canvas.LineEndpointSnappingThreshold;
                ToggleSwitchStopTimingStraighten.IsOn = Settings.Canvas.StopTimingStraighten;
                StopTimingThresholdMsSlider.Value = Settings.Canvas.StopTimingThresholdMs;
                StopTimingErrorSlider.Value = Settings.Canvas.StopTimingError;
            }
            if (Settings.Appearance != null)
            {
                ComboBoxTheme.SelectedIndex = Settings.Appearance.Theme;

                if (Settings.Appearance.IsEnableDisPlayFloatBarText)
                {
                    FloatBarSelectIconTextBlock.Visibility = Visibility.Visible;
                    Icon_Pen.Height = 22;
                    Icon_Eraser1.Height = 22;
                    Icon_Eraser2.Height = 22;
                    Icon_Eraser2.Margin = new Thickness(5, -22, 0, -8);
                    Icon_EraserByStrokes1.Height = 22;
                    Icon_EraserByStrokes2.Height = 22;
                    Icon_EraserByStrokes2.Margin = new Thickness(12, -22, 0, -8);
                    Icon_Select1.Height = 22;
                    Icon_Select2.Height = 22;
                    Icon_Select2.Margin = new Thickness(6, -18, 0, -8);
                    Icon_Undo.Margin = new Thickness(0,1.5,0,-1.5);
                    Icon_Redo.Margin = new Thickness(0, 1.5, 0, -1.5);
                    ToggleSwitchEnableDisPlayFloatBarText.IsOn = true;
                }
                else
                {
                    FloatBarSelectIconTextBlock.Visibility = Visibility.Collapsed;
                    Icon_Pen.Height = 32;
                    Icon_Eraser1.Height = 32;
                    Icon_Eraser2.Height = 32;
                    Icon_Eraser2.Margin = new Thickness(5, -32, 0, -8);
                    Icon_EraserByStrokes1.Height = 32;
                    Icon_EraserByStrokes2.Height = 32;
                    Icon_EraserByStrokes2.Margin = new Thickness(12, -32, 0, -8);
                    Icon_Select1.Height = 32;
                    Icon_Select2.Height = 32;
                    Icon_Select2.Margin = new Thickness(6, -28, 0, -8);
                    Icon_Undo.Margin = new Thickness(0);
                    Icon_Redo.Margin = new Thickness(0);
                    ToggleSwitchEnableDisPlayFloatBarText.IsOn = false;
                }
                if (Settings.Appearance.IsEnableDisPlayNibModeToggler)
                {
                    NibModeSimpleStackPanel.Visibility = Visibility.Visible;
                    BoardNibModeSimpleStackPanel.Visibility = Visibility.Visible;
                    ToggleSwitchEnableDisPlayNibModeToggle.IsOn = true;
                }
                else
                {
                    NibModeSimpleStackPanel.Visibility = Visibility.Collapsed;
                    BoardNibModeSimpleStackPanel.Visibility = Visibility.Collapsed;
                    ToggleSwitchEnableDisPlayNibModeToggle.IsOn = false;
                }

                SystemEvents_UserPreferenceChanged(null, null);

                if (Settings.Appearance.IsColorfulViewboxFloatingBar) // 浮动工具栏背景色
                {
                    var gradientBrush = new LinearGradientBrush
                    {
                        StartPoint = new Point(0, 0),
                        EndPoint = new Point(1, 1),
                        GradientStops = new GradientStopCollection
                        {
                            new GradientStop(Color.FromArgb(0x95, 0x80, 0xB0, 0xFF), 0),
                            new GradientStop(Color.FromArgb(0x95, 0xC0, 0xFF, 0xC0), 1)
                        }
                    };
                    EnableTwoFingerGestureBorder.Background = gradientBrush;
                    BorderFloatingBarMainControls.Background = gradientBrush;
                    BorderFloatingBarMoveControls.Background = gradientBrush;
                    BtnPPTSlideShowEnd.Background = gradientBrush;
                    ToggleSwitchColorfulViewboxFloatingBar.IsOn = true;
                }
                else
                {
                    ToggleSwitchColorfulViewboxFloatingBar.IsOn = false;
                }

                SliderFloatingBarScale.Value = Settings.Appearance.FloatingBarScale;
                SliderBlackboardScale.Value = Settings.Appearance.BlackboardScale;
                SliderPenPaletteScale.Value = NormalizeFloatingBarScale(
                    Settings.Appearance.PenPaletteScale ?? Settings.Appearance.FloatingBarScale);
                SliderDrawShapeScale.Value = NormalizeFloatingBarScale(
                    Settings.Appearance.DrawShapeScale ?? Settings.Appearance.FloatingBarScale);
                SliderFloatingBarBottomMargin.Value = Settings.Appearance.FloatingBarBottomMargin;
                SliderLaserPointerHold.Value = Settings.Canvas.LaserPointerHoldSeconds;
                SliderLaserPointerHighlight.Value = Settings.Canvas.LaserPointerHighlightWidth;
                SliderLaserPointerCore.Value = Settings.Canvas.LaserPointerCoreWidth;
                ApplyScaling();
                
                // Apply Video Presenter Sidebar Position
                if (!string.IsNullOrEmpty(Settings.Appearance.VideoPresenterSidebarPosition))
                {
                    if (Settings.Appearance.VideoPresenterSidebarPosition == "Right")
                    {
                        ComboBoxVideoPresenterSidebarPosition.SelectedIndex = 1;
                    }
                    else
                    {
                        ComboBoxVideoPresenterSidebarPosition.SelectedIndex = 0;
                    }
                }
                ApplyVideoPresenterSidebarPosition();
            }
            else
            {
                Settings.Appearance = new Appearance();

                SliderFloatingBarScale.Value = Settings.Appearance.FloatingBarScale;
                SliderBlackboardScale.Value = Settings.Appearance.BlackboardScale;
                SliderPenPaletteScale.Value = NormalizeFloatingBarScale(
                    Settings.Appearance.PenPaletteScale ?? Settings.Appearance.FloatingBarScale);
                SliderDrawShapeScale.Value = NormalizeFloatingBarScale(
                    Settings.Appearance.DrawShapeScale ?? Settings.Appearance.FloatingBarScale);
                SliderFloatingBarBottomMargin.Value = Settings.Appearance.FloatingBarBottomMargin;
                SliderLaserPointerHold.Value = Settings.Canvas.LaserPointerHoldSeconds;
                SliderLaserPointerHighlight.Value = Settings.Canvas.LaserPointerHighlightWidth;
                SliderLaserPointerCore.Value = Settings.Canvas.LaserPointerCoreWidth;
                ApplyScaling();
                
                // Initialize Video Presenter Sidebar Position
                ComboBoxVideoPresenterSidebarPosition.SelectedIndex = 0;
                ApplyVideoPresenterSidebarPosition();
            }
            // PowerPointSettings
            if (Settings.PowerPointSettings != null)
            {
                PptNavigationBottomBtn.Visibility = Settings.PowerPointSettings.IsShowPPTNavigationBottom ? Visibility.Visible : Visibility.Collapsed;
                ToggleSwitchShowButtonPPTNavigationBottom.IsOn = Settings.PowerPointSettings.IsShowPPTNavigationBottom;
                ToggleSwitchShowButtonPPTNavigationSides.IsOn = Settings.PowerPointSettings.IsShowPPTNavigationSides;
                ToggleSwitchShowPPTNavigationPanelBottom.IsOn = Settings.PowerPointSettings.IsShowBottomPPTNavigationPanel;
                ToggleSwitchShowPPTNavigationPanelSide.IsOn = Settings.PowerPointSettings.IsShowSidePPTNavigationPanel;
                ToggleSwitchSupportPowerPoint.IsOn = Settings.PowerPointSettings.PowerPointSupport;
                // 注：timerCheckPPT 的启停已移到 ApplySettingsSideEffects()，
                // 与设置窗口是否打开无关，否则启动后不检测 PPT，必须打开一次设置才进 PPT 模式。
                if (Settings.PowerPointSettings.IsShowCanvasAtNewSlideShow)
                {
                    ToggleSwitchShowCanvasAtNewSlideShow.IsOn = true;
                }
                else
                {
                    ToggleSwitchShowCanvasAtNewSlideShow.IsOn = false;
                }
                if (Settings.PowerPointSettings.IsEnableTwoFingerGestureInPresentationMode)
                {
                    ToggleSwitchEnableTwoFingerGestureInPresentationMode.IsOn = true;
                }
                else
                {
                    ToggleSwitchEnableTwoFingerGestureInPresentationMode.IsOn = false;
                }
                if (Settings.PowerPointSettings.IsEnableFingerGestureSlideShowControl)
                {
                    ToggleSwitchEnableFingerGestureSlideShowControl.IsOn = true;
                }
                else
                {
                    ToggleSwitchEnableFingerGestureSlideShowControl.IsOn = false;
                }

                if (Settings.PowerPointSettings.IsAutoSaveStrokesInPowerPoint)
                {
                    ToggleSwitchAutoSaveStrokesInPowerPoint.IsOn = true;
                }
                else
                {
                    ToggleSwitchAutoSaveStrokesInPowerPoint.IsOn = false;
                }

                if (Settings.PowerPointSettings.IsNotifyPreviousPage)
                {
                    ToggleSwitchNotifyPreviousPage.IsOn = true;
                }
                else
                {
                    ToggleSwitchNotifyPreviousPage.IsOn = false;
                }

                if (Settings.PowerPointSettings.IsNotifyHiddenPage)
                {
                    ToggleSwitchNotifyHiddenPage.IsOn = true;
                }
                else
                {
                    ToggleSwitchNotifyHiddenPage.IsOn = false;
                }
                if (Settings.PowerPointSettings.IsNotifyAutoPlayPresentation)
                {
                    ToggleSwitchNotifyAutoPlayPresentation.IsOn = true;
                }
                else
                {
                    ToggleSwitchNotifyAutoPlayPresentation.IsOn = false;
                }
                if (Settings.PowerPointSettings.IsSupportWPS)
                {
                    ToggleSwitchSupportWPS.IsOn = true;
                }
                else
                {
                    ToggleSwitchSupportWPS.IsOn = false;
                }
                if (Settings.PowerPointSettings.IsAutoSaveScreenShotInPowerPoint)
                {
                    ToggleSwitchAutoSaveScreenShotInPowerPoint.IsOn = true;
                }
                else
                {
                    ToggleSwitchAutoSaveScreenShotInPowerPoint.IsOn = false;
                }
            }
            else
            {
                Settings.PowerPointSettings = new PowerPointSettings();
            }
            if (Settings.Gesture != null)
            {
                ComboBoxMatrixTransformCenterPoint.SelectedIndex = (int)Settings.Gesture.MatrixTransformCenterPoint;
                if (Settings.Gesture.IsEnableMultiTouchMode)
                {
                    ToggleSwitchEnableMultiTouchMode.IsOn = true;
                }
                else
                {
                    ToggleSwitchEnableMultiTouchMode.IsOn = false;
                }
                if (Settings.Gesture.IsEnableTwoFingerZoom)
                {
                    ToggleSwitchEnableTwoFingerZoom.IsOn = true;
                    BoardToggleSwitchEnableTwoFingerZoom.IsOn = true;
                }
                else
                {
                    ToggleSwitchEnableTwoFingerZoom.IsOn = false;
                    BoardToggleSwitchEnableTwoFingerZoom.IsOn = false;
                }
                if (Settings.Gesture.IsEnableTwoFingerTranslate)
                {
                    ToggleSwitchEnableTwoFingerTranslate.IsOn = true;
                    BoardToggleSwitchEnableTwoFingerTranslate.IsOn = true;
                }
                else
                {
                    ToggleSwitchEnableTwoFingerTranslate.IsOn = false;
                    BoardToggleSwitchEnableTwoFingerTranslate.IsOn = false;
                }
                if (Settings.Gesture.IsEnableTwoFingerRotation)
                {
                    ToggleSwitchEnableTwoFingerRotation.IsOn = true;
                    BoardToggleSwitchEnableTwoFingerRotation.IsOn = true;
                }
                else
                {
                    ToggleSwitchEnableTwoFingerRotation.IsOn = false;
                    BoardToggleSwitchEnableTwoFingerRotation.IsOn = false;
                }
                if (Settings.Gesture.AutoSwitchTwoFingerGesture)
                {
                    ToggleSwitchAutoSwitchTwoFingerGesture.IsOn = true;
                }
                else
                {
                    ToggleSwitchAutoSwitchTwoFingerGesture.IsOn = false;
                }
                if (Settings.Gesture.IsEnableTwoFingerRotation)
                {
                    ToggleSwitchEnableTwoFingerRotation.IsOn = true;
                }
                else
                {
                    ToggleSwitchEnableTwoFingerRotation.IsOn = false;
                }
                if (Settings.Gesture.IsEnableTwoFingerRotationOnSelection)
                {
                    ToggleSwitchEnableTwoFingerRotationOnSelection.IsOn = true;
                }
                else
                {
                    ToggleSwitchEnableTwoFingerRotationOnSelection.IsOn = false;
                }
                if (Settings.Gesture.AutoSwitchTwoFingerGesture)
                {
                    if (Topmost)
                    {
                        // 置顶状态（类似幻灯片模式）：禁用所有手势，除了选中元素双指旋转
                        ToggleSwitchEnableTwoFingerTranslate.IsOn = false;
                        BoardToggleSwitchEnableTwoFingerTranslate.IsOn = false;
                        ToggleSwitchEnableTwoFingerZoom.IsOn = false;
                        BoardToggleSwitchEnableTwoFingerZoom.IsOn = false;
                        ToggleSwitchEnableTwoFingerRotation.IsOn = false;
                        ToggleSwitchEnableMultiTouchMode.IsOn = false;
                        ToggleSwitchEnableTwoFingerRotationOnSelection.IsOn = true;
                        Settings.Gesture.IsEnableTwoFingerTranslate = false;
                        Settings.Gesture.IsEnableTwoFingerZoom = false;
                        Settings.Gesture.IsEnableTwoFingerRotation = false;
                        Settings.Gesture.IsEnableMultiTouchMode = false;
                        Settings.Gesture.IsEnableTwoFingerRotationOnSelection = true;
                        isInMultiTouchMode = false;
                    }
                    else
                    {
                        // 非置顶状态（类似画板模式）：启用双指平移和缩放，禁用多指书写
                        ToggleSwitchEnableTwoFingerTranslate.IsOn = true;
                        BoardToggleSwitchEnableTwoFingerTranslate.IsOn = true;
                        ToggleSwitchEnableTwoFingerZoom.IsOn = true;
                        BoardToggleSwitchEnableTwoFingerZoom.IsOn = true;
                        ToggleSwitchEnableTwoFingerRotation.IsOn = false;
                        ToggleSwitchEnableMultiTouchMode.IsOn = false;
                        ToggleSwitchEnableTwoFingerRotationOnSelection.IsOn = true;
                        Settings.Gesture.IsEnableTwoFingerTranslate = true;
                        Settings.Gesture.IsEnableTwoFingerZoom = true;
                        Settings.Gesture.IsEnableTwoFingerRotation = false;
                        Settings.Gesture.IsEnableMultiTouchMode = false;
                        Settings.Gesture.IsEnableTwoFingerRotationOnSelection = true;
                        isInMultiTouchMode = false;
                    }
                }
                CheckEnableTwoFingerGestureBtnColorPrompt();
            }
            else
            {
                Settings.Gesture = new Gesture();
            }
            if (Settings.Canvas != null)
            {

                if (Settings.Canvas.UsingWhiteboard)
                {
                    GridBackgroundCover.Background = new SolidColorBrush(StringToColor("#FFF2F2F2"));
                    lastBoardInkColor = 0;
                }
                else
                {
                    GridBackgroundCover.Background = new SolidColorBrush(StringToColor("#FF1F1F1F"));
                    lastBoardInkColor = 5;
                }

                if (Settings.Canvas.IsShowCursor)
                {
                    ToggleSwitchShowCursor.IsOn = true;
                    inkCanvas.ForceCursor = true;
                }
                else
                {
                    ToggleSwitchShowCursor.IsOn = false;
                    inkCanvas.ForceCursor = false;
                }

                // 同步"上传照片时压缩画质"开关，避免重启后 UI 始终为关
                ToggleSwitchCompressPicturesUploaded.IsOn = Settings.Canvas.IsCompressPicturesUploaded;

                ComboBoxEraserSize.SelectedIndex = Settings.Canvas.EraserSize;

                if (Settings.Canvas.HideStrokeWhenSelecting)
                {
                    ToggleSwitchHideStrokeWhenSelecting.IsOn = true;
                }
                else
                {
                    ToggleSwitchHideStrokeWhenSelecting.IsOn = false;
                }
            }
            else
            {
                Settings.Canvas = new Canvas();
            }
            if (Settings.Advanced != null)
            {
                TouchMultiplierSlider.Value = Settings.Advanced.TouchMultiplier;
                FingerModeBoundsWidthSlider.Value = Settings.Advanced.FingerModeBoundsWidth;
                NibModeBoundsWidthSlider.Value = Settings.Advanced.NibModeBoundsWidth;
                FingerModeBoundsWidthThresholdValueSlider.Value = Settings.Advanced.FingerModeBoundsWidthThresholdValue;
                NibModeBoundsWidthThresholdValueSlider.Value = Settings.Advanced.NibModeBoundsWidthThresholdValue;
                FingerModeBoundsWidthEraserSizeSlider.Value = Settings.Advanced.FingerModeBoundsWidthEraserSize;
                NibModeBoundsWidthEraserSizeSlider.Value = Settings.Advanced.NibModeBoundsWidthEraserSize;
                if (Settings.Advanced.IsLogEnabled)
                {
                    ToggleSwitchIsLogEnabled.IsOn = true;
                }
                else
                {
                    ToggleSwitchIsLogEnabled.IsOn = false;
                }
                if (Settings.Advanced.IsSecondConfimeWhenShutdownApp)
                {
                    ToggleSwitchIsSecondConfimeWhenShutdownApp.IsOn = true;
                }
                else
                {
                    ToggleSwitchIsSecondConfimeWhenShutdownApp.IsOn = false;
                }

                if (Settings.Advanced.IsSpecialScreen)
                {
                    ToggleSwitchIsSpecialScreen.IsOn = true;
                }
                else
                {
                    ToggleSwitchIsSpecialScreen.IsOn = false;
                }
                TouchMultiplierSlider.Visibility = ToggleSwitchIsSpecialScreen.IsOn ? Visibility.Visible : Visibility.Collapsed;

                ToggleSwitchIsQuadIR.IsOn = Settings.Advanced.IsQuadIR;

                ToggleSwitchIsEnableEdgeGestureUtil.IsOn = Settings.Advanced.IsEnableEdgeGestureUtil;
                // 注：DisableEdgeGestures 的实际调用已移到 ApplySettingsSideEffects()

                ToggleSwitchIsEnableSilentRestartOnCrash.IsOn = Settings.Advanced.IsEnableSilentRestartOnCrash;
            }
            else
            {
                Settings.Advanced = new Advanced();
            }
            if (Settings.InkToShape != null)
            {
                if (Settings.InkToShape.IsInkToShapeEnabled)
                {
                    ToggleSwitchEnableInkToShape.IsOn = true;
                }
                else
                {
                    ToggleSwitchEnableInkToShape.IsOn = false;
                }
            }
            else
            {
                Settings.InkToShape = new InkToShape();
            }
            if (Settings.RandSettings != null)
            {
            }
            else
            {
                Settings.RandSettings = new RandSettings();
            }
            if (Settings.Automation != null)
            {
                // 注：StartOrStoptimerCheckAutoFold() 已移到 ApplySettingsSideEffects()
                if (Settings.Automation.IsAutoFoldInEasiNote)
                {
                    ToggleSwitchAutoFoldInEasiNote.IsOn = true;
                }
                else
                {
                    ToggleSwitchAutoFoldInEasiNote.IsOn = false;
                }
                // 同步"进入希沃白板时忽略桌面批注"开关，避免重启后 UI 与实际设置不一致
                ToggleSwitchAutoFoldInEasiNoteIgnoreDesktopAnno.IsOn = Settings.Automation.IsAutoFoldInEasiNoteIgnoreDesktopAnno;
                if (Settings.Automation.IsAutoFoldInEasiCamera)
                {
                    ToggleSwitchAutoFoldInEasiCamera.IsOn = true;
                }
                else
                {
                    ToggleSwitchAutoFoldInEasiCamera.IsOn = false;
                }
                if (Settings.Automation.IsAutoFoldInEasiNote3C)
                {
                    ToggleSwitchAutoFoldInEasiNote3C.IsOn = true;
                }
                else
                {
                    ToggleSwitchAutoFoldInEasiNote3C.IsOn = false;
                }
                if (Settings.Automation.IsAutoFoldInSeewoPincoTeacher)
                {
                    ToggleSwitchAutoFoldInSeewoPincoTeacher.IsOn = true;
                }
                else
                {
                    ToggleSwitchAutoFoldInSeewoPincoTeacher.IsOn = false;
                }
                if (Settings.Automation.IsAutoFoldInHiteTouchPro)
                {
                    ToggleSwitchAutoFoldInHiteTouchPro.IsOn = true;
                }
                else
                {
                    ToggleSwitchAutoFoldInHiteTouchPro.IsOn = false;
                }
                if (Settings.Automation.IsAutoFoldInHiteCamera)
                {
                    ToggleSwitchAutoFoldInHiteCamera.IsOn = true;
                }
                else
                {
                    ToggleSwitchAutoFoldInHiteCamera.IsOn = false;
                }
                if (Settings.Automation.IsAutoFoldInWxBoardMain)
                {
                    ToggleSwitchAutoFoldInWxBoardMain.IsOn = true;
                }
                else
                {
                    ToggleSwitchAutoFoldInWxBoardMain.IsOn = false;
                }
                if (Settings.Automation.IsAutoFoldInOldZyBoard)
                {
                    ToggleSwitchAutoFoldInOldZyBoard.IsOn = true;
                }
                else
                {
                    ToggleSwitchAutoFoldInOldZyBoard.IsOn = false;
                }
                if (Settings.Automation.IsAutoFoldInMSWhiteboard)
                {
                    ToggleSwitchAutoFoldInMSWhiteboard.IsOn = true;
                }
                else
                {
                    ToggleSwitchAutoFoldInMSWhiteboard.IsOn = false;
                }
                if (Settings.Automation.IsAutoFoldInPPTSlideShow)
                {
                    ToggleSwitchAutoFoldInPPTSlideShow.IsOn = true;
                }
                else
                {
                    ToggleSwitchAutoFoldInPPTSlideShow.IsOn = false;
                }
                // 注：timerKillProcess 的启停已移到 ApplySettingsSideEffects()，
                // 否则启动后不查杀希沃 PPT 小工具（PPTService / SeewoIwbAssistant），
                // 表现为希沃小工具与 ICU 悬浮栏并存，且重启系统后复发。

                if (Settings.Automation.IsAutoKillEasiNote)
                {
                    ToggleSwitchAutoKillEasiNote.IsOn = true;
                }
                else
                {
                    ToggleSwitchAutoKillEasiNote.IsOn = false;
                }
                if (Settings.Automation.IsAutoKillPptService)
                {
                    ToggleSwitchAutoKillPptService.IsOn = true;
                }
                else
                {
                    ToggleSwitchAutoKillPptService.IsOn = false;
                }
                if (Settings.Automation.IsAutoSaveStrokesAtClear)
                {
                    ToggleSwitchAutoSaveStrokesAtClear.IsOn = true;
                }
                else
                {
                    ToggleSwitchAutoSaveStrokesAtClear.IsOn = false;
                }
                if (Settings.Automation.IsSaveScreenshotsInDateFolders)
                {
                    ToggleSwitchSaveScreenshotsInDateFolders.IsOn = true;
                }
                else
                {
                    ToggleSwitchSaveScreenshotsInDateFolders.IsOn = false;
                }
                if (Settings.Automation.IsAutoSaveStrokesAtScreenshot)
                {
                    ToggleSwitchAutoSaveStrokesAtScreenshot.IsOn = true;
                }
                else
                {
                    ToggleSwitchAutoSaveStrokesAtScreenshot.IsOn = false;
                }
                if (Settings.Automation.IsCopyScreenshotToClipboard)
                {
                    ToggleSwitchCopyScreenshotToClipboard.IsOn = true;
                }
                else
                {
                    ToggleSwitchCopyScreenshotToClipboard.IsOn = false;
                }
                SideControlMinimumAutomationSlider.Value = Settings.Automation.MinimumAutomationStrokeNumber;

                AutoSavedStrokesLocation.Text = Settings.Automation.AutoSavedStrokesLocation;
                ToggleSwitchAutoDelSavedFiles.IsOn = Settings.Automation.AutoDelSavedFiles;
                string thresholdText = Settings.Automation.AutoDelSavedFilesDaysThreshold.ToString();
                ComboBoxItem matchedItem = null;
                foreach (var entry in ComboBoxAutoDelSavedFilesDaysThreshold.Items)
                {
                    if (entry is ComboBoxItem item && item.Content != null && item.Content.ToString() == thresholdText)
                    {
                        matchedItem = item;
                        break;
                    }
                }
                if (matchedItem != null)
                {
                    ComboBoxAutoDelSavedFilesDaysThreshold.SelectedItem = matchedItem;
                }
                else
                {
                    ComboBoxAutoDelSavedFilesDaysThreshold.Text = thresholdText;
                }
            }
            else
            {
                Settings.Automation = new Automation();
            }
            } // end if (SettingsWindow != null) - 设置 UI 填充结束

            ApplySettingsSideEffects(isStartup);

            ViewboxFloatingBarMarginAnimation();

            if (floatingBarScaleWasNormalized)
            {
                SaveSettingsToFile();
            }
            }
            finally
            {
                _isLoadingSettings = false;
                _settingsReadyForSave = true;
            }
        }

        /// <summary>
        /// 从当前内存中的 Settings 对象重新应用设置到主窗口 UI。
        /// （不重新从磁盘读取，用于设置被外部窗口修改后快速刷新界面）
        /// </summary>
        public void ReloadSettingsFromSettingsObject()
        {
            LoadSettings();
        }

        private static bool TryLoadSettingsFile(string path, out Settings result)
        {
            result = null;
            try
            {
                if (!File.Exists(path)) return false;

                string text = File.ReadAllText(path);
                // 空文件/纯空白：截断写入被打断的典型产物，不能当作合法 JSON
                if (string.IsNullOrWhiteSpace(text)) return false;

                var parsed = JsonConvert.DeserializeObject<Settings>(text);
                // DeserializeObject 对 "null" 这类输入会返回 null，同样视为无效
                if (parsed == null) return false;

                result = parsed;
                return true;
            }
            catch (Exception ex)
            {
                LogHelper.WriteLogToFile(
                    "Failed to parse settings file '" + path + "': " + ex.Message,
                    LogHelper.LogType.Error);
                return false;
            }
        }

        /// <summary>
        /// 把确认已损坏的设置文件另存为 .corrupt，保留现场便于排查（不直接删除，也不让它被后续保存覆盖）。
        /// </summary>
        private static void TryQuarantineCorruptSettings(string path)
        {
            try
            {
                if (!File.Exists(path)) return;
                string corruptPath = path + ".corrupt";
                File.Copy(path, corruptPath, true);
                LogHelper.WriteLogToFile(
                    "Settings.json is corrupted and unrecoverable; kept a copy at: " + corruptPath,
                    LogHelper.LogType.Error);
            }
            catch { }
        }

        private void ApplySettingsSideEffects(bool isStartup)
        {
            try
            {
                // 启动时应用「默认画笔颜色」设置（-1 = 不干预，保持内置默认：批注红 / 白板随背景黑或白）。
                // 只在启动时应用：否则每次打开设置窗口都会把用户当前选的色重置回默认值。
                if (isStartup) ApplyStartupDefaultPenColor();
            }
            catch (Exception ex)
            {
                LogHelper.WriteLogToFile("ApplySettingsSideEffects | default pen color: " + ex.Message, LogHelper.LogType.Warning);
            }

            try
            {
                // PPT 支持：启动 PPT 检测定时器，才能在放映时自动进入 PPT 模式
                if (Settings.PowerPointSettings != null)
                {
                    if (Settings.PowerPointSettings.PowerPointSupport)
                    {
                        timerCheckPPT.Start();
                    }
                    else
                    {
                        timerCheckPPT.Stop();
                    }
                }
            }
            catch (Exception ex)
            {
                LogHelper.WriteLogToFile("ApplySettingsSideEffects | PPT timer: " + ex.Message, LogHelper.LogType.Error);
            }

            try
            {
                if (Settings.Automation != null)
                {
                    if (Settings.Automation.IsAutoKillEasiNote || Settings.Automation.IsAutoKillPptService)
                    {
                        timerKillProcess.Start();
                    }
                    else
                    {
                        timerKillProcess.Stop();
                    }
                }
            }
            catch (Exception ex)
            {
                LogHelper.WriteLogToFile("ApplySettingsSideEffects | kill timer: " + ex.Message, LogHelper.LogType.Error);
            }

            try
            {
                // 自动收纳/展开浮动栏（此前靠 Window_Loaded 里的补丁启动，现统一到这里）
                if (Settings.Automation != null)
                {
                    StartOrStoptimerCheckAutoFold();
                }
            }
            catch (Exception ex)
            {
                LogHelper.WriteLogToFile("ApplySettingsSideEffects | auto fold timer: " + ex.Message, LogHelper.LogType.Error);
            }

            try
            {
                // 禁用边缘手势（仅 Win10 及以上）
                if (Settings.Advanced != null && Settings.Advanced.IsEnableEdgeGestureUtil)
                {
                    if (OSVersion.GetOperatingSystem() >= OSVersionExtension.OperatingSystem.Windows10)
                    {
                        EdgeGestureUtil.DisableEdgeGestures(new WindowInteropHelper(this).Handle, true);
                    }
                }
            }
            catch (Exception ex)
            {
                LogHelper.WriteLogToFile("ApplySettingsSideEffects | edge gesture: " + ex.Message, LogHelper.LogType.Error);
            }

            // 启动自动检查更新：仅在启动加载时触发一次。
            // 不在"打开设置窗口"时触发，避免每次打开设置都重复发起版本检测网络请求
            // （设置窗口里"立即检查"按钮才是用户主动触发的入口）。
            if (isStartup)
            {
                try
                {
                    if (Settings.Startup != null && Settings.Startup.IsAutoUpdate)
                    {
                        AutoUpdate();
                    }
                }
                catch (Exception ex)
                {
                    LogHelper.WriteLogToFile("ApplySettingsSideEffects | auto update: " + ex.Message, LogHelper.LogType.Error);
                }
            }
        }

        /// <summary>
        /// 把设置里的「墨迹选项」面板状态同步到浮动栏与白板栏控件（笔尖模式、粗细、不透明度、
        /// 笔锋、双曲线 asymptote 选项、墨迹识别主开关）。每次 LoadSettings 都调用——
        /// 含启动时（设置窗口未打开），因为这些控件位于浮动栏/白板栏 XAML，本就该随设置刷新。
        /// </summary>
        private void SyncPenPaletteControlsFromSettings()
        {
            try
            {
                // 笔尖模式：浮动栏 + 白板栏两个开关，以及触摸判定宽度
                bool nibMode = Settings.Startup?.IsEnableNibMode ?? false;
                if (ToggleSwitchEnableNibMode != null) ToggleSwitchEnableNibMode.IsOn = nibMode;
                if (BoardToggleSwitchEnableNibMode != null) BoardToggleSwitchEnableNibMode.IsOn = nibMode;
                BoundsWidth = nibMode
                    ? Settings.Advanced.NibModeBoundsWidth
                    : Settings.Advanced.FingerModeBoundsWidth;

                if (Settings.Canvas != null)
                {
                    double width = Settings.Canvas.InkWidth;
                    if (drawingAttributes != null)
                    {
                        drawingAttributes.Height = width;
                        drawingAttributes.Width = width;
                    }
                    if (InkWidthSlider != null) InkWidthSlider.Value = width * 2;
                    if (BoardInkWidthSlider != null) BoardInkWidthSlider.Value = width * 2;
                    if (InkAlphaSlider != null) InkAlphaSlider.Value = Settings.Canvas.InkAlpha;
                    if (BoardInkAlphaSlider != null) BoardInkAlphaSlider.Value = Settings.Canvas.InkAlpha;
                    if (ComboBoxHyperbolaAsymptoteOption != null)
                        ComboBoxHyperbolaAsymptoteOption.SelectedIndex = (int)Settings.Canvas.HyperbolaAsymptoteOption;
                    if (ComboBoxPenStyle != null) ComboBoxPenStyle.SelectedIndex = Settings.Canvas.InkStyle;
                    if (BoardComboBoxPenStyle != null) BoardComboBoxPenStyle.SelectedIndex = Settings.Canvas.InkStyle;
                }

                // 墨迹识别主开关：浮动栏 + 白板栏
                if (Settings.InkToShape != null)
                {
                    if (ToggleSwitchEnableInkToShapeFloatBar != null)
                        ToggleSwitchEnableInkToShapeFloatBar.IsOn = Settings.InkToShape.IsInkToShapeEnabled;
                    if (ToggleSwitchEnableInkToShapeBoard != null)
                        ToggleSwitchEnableInkToShapeBoard.IsOn = Settings.InkToShape.IsInkToShapeEnabled;
                }
            }
            catch (Exception ex)
            {
                LogHelper.WriteLogToFile("同步墨迹选项面板状态失败 | " + ex.Message, LogHelper.LogType.Warning);
            }
        }

        /// <summary>
        /// 如果是首次启动且尚未完成初始向导，则尝试显示初始设置向导窗口。
        /// </summary>
        private void TryShowInitialSetupWizard()
        {
            try
            {
                if (Settings?.Startup == null)
                {
                    LogHelper.WriteLogToFile("TryShowInitialSetupWizard: Settings/Startup 为 null，跳过", LogHelper.LogType.Warning);
                    return;
                }

                if (Settings.Startup.IsInitialSetupCompleted) return;

                // 仅在确实要弹出向导时留痕（正常启动不产生日志）
                LogHelper.WriteLogToFile("首次启动：显示初始化设置向导", LogHelper.LogType.Event);

                // 避免在特殊无界面模式下弹窗（如某些自动化场景），此处仅在主窗口正常显示后调用
                // 不指定 Owner：由 PopupWindowLayerHelper 统一绑到主窗口
                var wizard = new InitialSetupWindow();
                Helpers.WindowMemoryHelper.ReleaseOnClose(wizard);
                wizard.Show();
                Helpers.PopupWindowLayerHelper.BringToFront(wizard);
            }
            catch (Exception ex)
            {
                LogHelper.WriteLogToFile("TryShowInitialSetupWizard failed | " + ex, LogHelper.LogType.Error);
            }
        }
    }
}
