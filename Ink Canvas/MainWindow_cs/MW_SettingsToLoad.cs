using Ink_Canvas.Helpers;
using Newtonsoft.Json;
using OSVersionExtension;
using System;
using System.Reflection;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using File = System.IO.File;

namespace Ink_Canvas
{
    public partial class MainWindow : Window
    {
        private void LoadSettings(bool isStartup = false)
        {
            try
            {
                if (File.Exists(App.RootPath + settingsFileName))
                {
                    try
                    {
                        string text = File.ReadAllText(App.RootPath + settingsFileName);
                        Settings = JsonConvert.DeserializeObject<Settings>(text);
                    }
                    catch { }
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
            // Startup
            if (isStartup)
            {
                CursorIcon_Click(null, null);
            }
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
                if (Settings.Startup.IsEnableNibMode)
                {
                    ToggleSwitchEnableNibMode.IsOn = true;
                    BoardToggleSwitchEnableNibMode.IsOn = true;
                    BoundsWidth = Settings.Advanced.NibModeBoundsWidth;
                }
                else
                {
                    ToggleSwitchEnableNibMode.IsOn = false;
                    BoardToggleSwitchEnableNibMode.IsOn = false;
                    BoundsWidth = Settings.Advanced.FingerModeBoundsWidth;
                }
                if (Settings.Startup.IsAutoUpdate)
                {
                    ToggleSwitchIsAutoUpdate.IsOn = true;
                    AutoUpdate();
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

                ToggleSwitchFoldAtStartup.IsOn = Settings.Startup.IsFoldAtStartup;
            }
            else
            {
                Settings.Startup = new Startup();
            }
            if (Settings.InkToShape != null)
            {
                ToggleSwitchEnableInkToShape.IsOn = Settings.InkToShape.IsInkToShapeEnabled;
                ToggleSwitchEnableTriangleRecognition.IsOn = Settings.InkToShape.IsInkToShapeTriangle;
                ToggleSwitchEnableRectangleRecognition.IsOn = Settings.InkToShape.IsInkToShapeRectangle;
                ToggleSwitchNoFakePressureRectangle.IsOn = Settings.InkToShape.IsInkToShapeNoFakePressureRectangle;
                ToggleSwitchNoFakePressureTriangle.IsOn = Settings.InkToShape.IsInkToShapeNoFakePressureTriangle;
                LineStraightenSensitivitySlider.Value = Settings.InkToShape.LineStraightenSensitivity;
            }
            if (Settings.Canvas != null)
            {
                ToggleSwitchAutoStraightenLine.IsOn = Settings.Canvas.AutoStraightenLine;
                AutoStraightenLineThresholdSlider.Value = Settings.Canvas.AutoStraightenLineThreshold;
                ToggleSwitchLineEndpointSnapping.IsOn = Settings.Canvas.LineEndpointSnapping;
                LineEndpointSnappingThresholdSlider.Value = Settings.Canvas.LineEndpointSnappingThreshold;
            }
            // Appearance
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
                SliderFloatingBarBottomMargin.Value = Settings.Appearance.FloatingBarBottomMargin;
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
                SliderFloatingBarBottomMargin.Value = Settings.Appearance.FloatingBarBottomMargin;
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
                if (Settings.PowerPointSettings.PowerPointSupport)
                {
                    ToggleSwitchSupportPowerPoint.IsOn = true;
                    timerCheckPPT.Start();
                }
                else
                {
                    ToggleSwitchSupportPowerPoint.IsOn = false;
                    timerCheckPPT.Stop();
                }
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
            // Gesture
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
            // Canvas
            if (Settings.Canvas != null)
            {
                drawingAttributes.Height = Settings.Canvas.InkWidth;
                drawingAttributes.Width = Settings.Canvas.InkWidth;

                InkWidthSlider.Value = Settings.Canvas.InkWidth * 2;
                BoardInkWidthSlider.Value = Settings.Canvas.InkWidth * 2;
                InkAlphaSlider.Value = Settings.Canvas.InkAlpha;
                BoardInkAlphaSlider.Value = Settings.Canvas.InkAlpha;

                ComboBoxHyperbolaAsymptoteOption.SelectedIndex = (int)Settings.Canvas.HyperbolaAsymptoteOption;

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

                ComboBoxPenStyle.SelectedIndex = Settings.Canvas.InkStyle;
                BoardComboBoxPenStyle.SelectedIndex = Settings.Canvas.InkStyle;

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
            // Advanced
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
                if (Settings.Advanced.IsEnableEdgeGestureUtil)
                {
                    if (OSVersion.GetOperatingSystem() >= OSVersionExtension.OperatingSystem.Windows10) EdgeGestureUtil.DisableEdgeGestures(new WindowInteropHelper(this).Handle, true);
                }

                ToggleSwitchIsEnableSilentRestartOnCrash.IsOn = Settings.Advanced.IsEnableSilentRestartOnCrash;
            }
            else
            {
                Settings.Advanced = new Advanced();
            }
            // InkToShape
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
            // RandSettings
            if (Settings.RandSettings != null)
            {
            }
            else
            {
                Settings.RandSettings = new RandSettings();
            }
            // Automation
            if (Settings.Automation != null)
            {
                StartOrStoptimerCheckAutoFold();
                if (Settings.Automation.IsAutoFoldInEasiNote)
                {
                    ToggleSwitchAutoFoldInEasiNote.IsOn = true;
                }
                else
                {
                    ToggleSwitchAutoFoldInEasiNote.IsOn = false;
                }
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
                if (Settings.Automation.IsAutoKillEasiNote || Settings.Automation.IsAutoKillPptService)
                {
                    timerKillProcess.Start();
                }
                else
                {
                    timerKillProcess.Stop();
                }

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
                SideControlMinimumAutomationSlider.Value = Settings.Automation.MinimumAutomationStrokeNumber;

                AutoSavedStrokesLocation.Text = Settings.Automation.AutoSavedStrokesLocation;
                ToggleSwitchAutoDelSavedFiles.IsOn = Settings.Automation.AutoDelSavedFiles;
                ComboBoxAutoDelSavedFilesDaysThreshold.Text = Settings.Automation.AutoDelSavedFilesDaysThreshold.ToString();
            }
            else
            {
                Settings.Automation = new Automation();
            }
            ViewboxFloatingBarMarginAnimation();
        }

        /// <summary>
        /// 从当前内存中的 Settings 对象重新应用设置到主窗口 UI。
        /// （不重新从磁盘读取，用于设置被外部窗口修改后快速刷新界面）
        /// </summary>
        public void ReloadSettingsFromSettingsObject()
        {
            LoadSettings();
        }

        /// <summary>
        /// 如果是首次启动且尚未完成初始向导，则尝试显示初始设置向导窗口。
        /// </summary>
        private void TryShowInitialSetupWizard()
        {
            try
            {
                if (Settings?.Startup == null) return;
                if (Settings.Startup.IsInitialSetupCompleted) return;

                // 避免在特殊无界面模式下弹窗（如某些自动化场景），此处仅在主窗口正常显示后调用
                var wizard = new InitialSetupWindow
                {
                    Owner = this
                };
                wizard.Show();
            }
            catch (Exception ex)
            {
                LogHelper.WriteLogToFile("TryShowInitialSetupWizard failed | " + ex, LogHelper.LogType.Error);
            }
        }
    }
}
