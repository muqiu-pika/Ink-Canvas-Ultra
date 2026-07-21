using System;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Ink;
using iNKORE.UI.WPF.Modern;

namespace Ink_Canvas
{
    public partial class MW_Settings : Window
    {
        public MW_Settings()
        {
            InitializeComponent();
            Loaded += SettingsWindow_Loaded;
        }

        private void SettingsWindow_Loaded(object sender, RoutedEventArgs e)
        {
            InvokeMainWindowHandler("LoadSettings", false);
            if (AppVersionTextBlock != null)
                AppVersionTextBlock.Text = Assembly.GetExecutingAssembly().GetName().Version.ToString();
        }

        private void InvokeMainWindowHandler(string handlerName, params object[] args)
        {
            var window = Owner as MainWindow;
            if (window == null)
            {
                return;
            }

            var method = typeof(MainWindow).GetMethod(handlerName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (method == null)
            {
                throw new MissingMethodException(typeof(MainWindow).FullName, handlerName);
            }

            method.Invoke(window, args);
        }

        private void AutoSavedStrokesLocationButton_Click(object sender, RoutedEventArgs e)
        {
            InvokeMainWindowHandler(nameof(AutoSavedStrokesLocationButton_Click), sender, e);
        }

        private void AutoSavedStrokesLocationTextBox_TextChanged(object sender, RoutedEventArgs e)
        {
            InvokeMainWindowHandler(nameof(AutoSavedStrokesLocationTextBox_TextChanged), sender, e);
        }

        private void AutoStraightenLineThresholdSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            InvokeMainWindowHandler(nameof(AutoStraightenLineThresholdSlider_ValueChanged), sender, e);
        }

        private void AutoUpdateProxyTextBox_TextChanged(object sender, RoutedEventArgs e)
        {
            InvokeMainWindowHandler(nameof(AutoUpdateProxyTextBox_TextChanged), sender, e);
        }

        private void AutoUpdateWithSilenceEndTimeComboBox_SelectionChanged(object sender, RoutedEventArgs e)
        {
            InvokeMainWindowHandler(nameof(AutoUpdateWithSilenceEndTimeComboBox_SelectionChanged), sender, e);
        }

        private void AutoUpdateWithSilenceStartTimeComboBox_SelectionChanged(object sender, RoutedEventArgs e)
        {
            InvokeMainWindowHandler(nameof(AutoUpdateWithSilenceStartTimeComboBox_SelectionChanged), sender, e);
        }

        private void BorderCalculateMultiplier_TouchDown(object sender, TouchEventArgs e)
        {
            InvokeMainWindowHandler(nameof(BorderCalculateMultiplier_TouchDown), sender, e);
        }

        private void BtnCheckAutoUpdateProxyReturnedData_Click(object sender, RoutedEventArgs e)
        {
            InvokeMainWindowHandler(nameof(BtnCheckAutoUpdateProxyReturnedData_Click), sender, e);
        }

        private void BtnExit_Click(object sender, RoutedEventArgs e)
        {
            InvokeMainWindowHandler(nameof(BtnExit_Click), sender, e);
        }

        private void BtnOpenInitialSetup_Click(object sender, RoutedEventArgs e)
        {
            InvokeMainWindowHandler(nameof(BtnOpenInitialSetup_Click), sender, e);
        }

        private void BtnOpenPluginWorkshop_Click(object sender, RoutedEventArgs e)
        {
            InvokeMainWindowHandler(nameof(BtnOpenPluginWorkshop_Click), sender, e);
        }

        private void BtnResetAutoUpdateProxyToGHProxy_Click(object sender, RoutedEventArgs e)
        {
            InvokeMainWindowHandler(nameof(BtnResetAutoUpdateProxyToGHProxy_Click), sender, e);
        }

        private void BtnResetToSuggestion_Click(object sender, RoutedEventArgs e)
        {
            InvokeMainWindowHandler(nameof(BtnResetToSuggestion_Click), sender, e);
        }

        private void BtnRestart_Click(object sender, RoutedEventArgs e)
        {
            InvokeMainWindowHandler(nameof(BtnRestart_Click), sender, e);
        }

        private void BtnSetBlackboardScale_Click(object sender, RoutedEventArgs e)
        {
            InvokeMainWindowHandler(nameof(BtnSetBlackboardScale_Click), sender, e);
        }

        private void BtnSetFloatingBarMargin_Click(object sender, RoutedEventArgs e)
        {
            InvokeMainWindowHandler(nameof(BtnSetFloatingBarMargin_Click), sender, e);
        }

        private void BtnSetFloatingBarScale_Click(object sender, RoutedEventArgs e)
        {
            InvokeMainWindowHandler(nameof(BtnSetFloatingBarScale_Click), sender, e);
        }

        private void BtnSettings_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        private void ComboBoxAutoDelSavedFilesDaysThreshold_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            InvokeMainWindowHandler(nameof(ComboBoxAutoDelSavedFilesDaysThreshold_SelectionChanged), sender, e);
        }

        private void ComboBoxEraserSize_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            InvokeMainWindowHandler(nameof(ComboBoxEraserSize_SelectionChanged), sender, e);
        }

        private void ComboBoxHyperbolaAsymptoteOption_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            InvokeMainWindowHandler(nameof(ComboBoxHyperbolaAsymptoteOption_SelectionChanged), sender, e);
        }

        private void ComboBoxMatrixTransformCenterPoint_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            InvokeMainWindowHandler(nameof(ComboBoxMatrixTransformCenterPoint_SelectionChanged), sender, e);
        }

        private void ComboBoxTheme_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            InvokeMainWindowHandler(nameof(ComboBoxTheme_SelectionChanged), sender, e);
        }

        private void ComboBoxVideoPresenterSidebarPosition_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            InvokeMainWindowHandler(nameof(ComboBoxVideoPresenterSidebarPosition_SelectionChanged), sender, e);
        }

        private void FingerModeBoundsWidthEraserSizeSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            InvokeMainWindowHandler(nameof(FingerModeBoundsWidthEraserSizeSlider_ValueChanged), sender, e);
        }

        private void FingerModeBoundsWidthSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            InvokeMainWindowHandler(nameof(FingerModeBoundsWidthSlider_ValueChanged), sender, e);
        }

        private void FingerModeBoundsWidthThresholdValueSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            InvokeMainWindowHandler(nameof(FingerModeBoundsWidthThresholdValueSlider_ValueChanged), sender, e);
        }

        private void HyperlinkSourceToOringinalRepository_Click(object sender, RoutedEventArgs e)
        {
            InvokeMainWindowHandler(nameof(HyperlinkSourceToOringinalRepository_Click), sender, e);
        }

        private void HyperlinkSourceToPresentRepository_Click(object sender, RoutedEventArgs e)
        {
            InvokeMainWindowHandler(nameof(HyperlinkSourceToPresentRepository_Click), sender, e);
        }

        private void LineEndpointSnappingThresholdSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            InvokeMainWindowHandler(nameof(LineEndpointSnappingThresholdSlider_ValueChanged), sender, e);
        }

        private void LineStraightenSensitivitySlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            InvokeMainWindowHandler(nameof(LineStraightenSensitivitySlider_ValueChanged), sender, e);
        }

        private void NibModeBoundsWidthEraserSizeSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            InvokeMainWindowHandler(nameof(NibModeBoundsWidthEraserSizeSlider_ValueChanged), sender, e);
        }

        private void NibModeBoundsWidthSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            InvokeMainWindowHandler(nameof(NibModeBoundsWidthSlider_ValueChanged), sender, e);
        }

        private void NibModeBoundsWidthThresholdValueSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            InvokeMainWindowHandler(nameof(NibModeBoundsWidthThresholdValueSlider_ValueChanged), sender, e);
        }

        private void OperatingGuideWindowIcon_Click(object sender, RoutedEventArgs e)
        {
            InvokeMainWindowHandler(nameof(OperatingGuideWindowIcon_Click), sender, e);
        }

        private void SCManipulationBoundaryFeedback(object sender, ManipulationBoundaryFeedbackEventArgs e)
        {
            InvokeMainWindowHandler(nameof(SCManipulationBoundaryFeedback), sender, e);
        }

        private void SetAutoSavedStrokesLocationToDiskDButton_Click(object sender, RoutedEventArgs e)
        {
            InvokeMainWindowHandler(nameof(SetAutoSavedStrokesLocationToDiskDButton_Click), sender, e);
        }

        private void SetAutoSavedStrokesLocationToDocumentFolderButton_Click(object sender, RoutedEventArgs e)
        {
            InvokeMainWindowHandler(nameof(SetAutoSavedStrokesLocationToDocumentFolderButton_Click), sender, e);
        }

        private void SettingsNav_SelectionChanged(iNKORE.UI.WPF.Modern.Controls.NavigationView sender, iNKORE.UI.WPF.Modern.Controls.NavigationViewSelectionChangedEventArgs args)
        {
            InvokeMainWindowHandler(nameof(SettingsNav_SelectionChanged), sender, args);
        }

        private void SideControlMinimumAutomationSlider_ValueChanged(object sender, RoutedEventArgs e)
        {
            InvokeMainWindowHandler(nameof(SideControlMinimumAutomationSlider_ValueChanged), sender, e);
        }

        private void SliderBlackboardScale_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            InvokeMainWindowHandler(nameof(SliderBlackboardScale_ValueChanged), sender, e);
        }

        private void SliderFloatingBarBottomMargin_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            InvokeMainWindowHandler(nameof(SliderFloatingBarBottomMargin_ValueChanged), sender, e);
        }

        private void SliderFloatingBarScale_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            InvokeMainWindowHandler(nameof(SliderFloatingBarScale_ValueChanged), sender, e);
        }

        private void StopTimingErrorSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            InvokeMainWindowHandler(nameof(StopTimingErrorSlider_ValueChanged), sender, e);
        }

        private void StopTimingThresholdMsSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            InvokeMainWindowHandler(nameof(StopTimingThresholdMsSlider_ValueChanged), sender, e);
        }

        private void ToggleSwitchAutoDelSavedFiles_Toggled(object sender, RoutedEventArgs e)
        {
            InvokeMainWindowHandler(nameof(ToggleSwitchAutoDelSavedFiles_Toggled), sender, e);
        }

        private void ToggleSwitchAutoFoldInEasiCamera_Toggled(object sender, RoutedEventArgs e)
        {
            InvokeMainWindowHandler(nameof(ToggleSwitchAutoFoldInEasiCamera_Toggled), sender, e);
        }

        private void ToggleSwitchAutoFoldInEasiNote3C_Toggled(object sender, RoutedEventArgs e)
        {
            InvokeMainWindowHandler(nameof(ToggleSwitchAutoFoldInEasiNote3C_Toggled), sender, e);
        }

        private void ToggleSwitchAutoFoldInEasiNoteIgnoreDesktopAnno_Toggled(object sender, RoutedEventArgs e)
        {
            InvokeMainWindowHandler(nameof(ToggleSwitchAutoFoldInEasiNoteIgnoreDesktopAnno_Toggled), sender, e);
        }

        private void ToggleSwitchAutoFoldInEasiNote_Toggled(object sender, RoutedEventArgs e)
        {
            InvokeMainWindowHandler(nameof(ToggleSwitchAutoFoldInEasiNote_Toggled), sender, e);
        }

        private void ToggleSwitchAutoFoldInHiteCamera_Toggled(object sender, RoutedEventArgs e)
        {
            InvokeMainWindowHandler(nameof(ToggleSwitchAutoFoldInHiteCamera_Toggled), sender, e);
        }

        private void ToggleSwitchAutoFoldInHiteTouchPro_Toggled(object sender, RoutedEventArgs e)
        {
            InvokeMainWindowHandler(nameof(ToggleSwitchAutoFoldInHiteTouchPro_Toggled), sender, e);
        }

        private void ToggleSwitchAutoFoldInMSWhiteboard_Toggled(object sender, RoutedEventArgs e)
        {
            InvokeMainWindowHandler(nameof(ToggleSwitchAutoFoldInMSWhiteboard_Toggled), sender, e);
        }

        private void ToggleSwitchAutoFoldInOldZyBoard_Toggled(object sender, RoutedEventArgs e)
        {
            InvokeMainWindowHandler(nameof(ToggleSwitchAutoFoldInOldZyBoard_Toggled), sender, e);
        }

        private void ToggleSwitchAutoFoldInPPTSlideShow_Toggled(object sender, RoutedEventArgs e)
        {
            InvokeMainWindowHandler(nameof(ToggleSwitchAutoFoldInPPTSlideShow_Toggled), sender, e);
        }

        private void ToggleSwitchAutoFoldInSeewoPincoTeacher_Toggled(object sender, RoutedEventArgs e)
        {
            InvokeMainWindowHandler(nameof(ToggleSwitchAutoFoldInSeewoPincoTeacher_Toggled), sender, e);
        }

        private void ToggleSwitchAutoFoldInWxBoardMain_Toggled(object sender, RoutedEventArgs e)
        {
            InvokeMainWindowHandler(nameof(ToggleSwitchAutoFoldInWxBoardMain_Toggled), sender, e);
        }

        private void ToggleSwitchAutoKillEasiNote_Toggled(object sender, RoutedEventArgs e)
        {
            InvokeMainWindowHandler(nameof(ToggleSwitchAutoKillEasiNote_Toggled), sender, e);
        }

        private void ToggleSwitchAutoKillPptService_Toggled(object sender, RoutedEventArgs e)
        {
            InvokeMainWindowHandler(nameof(ToggleSwitchAutoKillPptService_Toggled), sender, e);
        }

        private void ToggleSwitchAutoSaveScreenShotInPowerPoint_Toggled(object sender, RoutedEventArgs e)
        {
            InvokeMainWindowHandler(nameof(ToggleSwitchAutoSaveScreenShotInPowerPoint_Toggled), sender, e);
        }

        private void ToggleSwitchAutoSaveStrokesAtClear_Toggled(object sender, RoutedEventArgs e)
        {
            InvokeMainWindowHandler(nameof(ToggleSwitchAutoSaveStrokesAtClear_Toggled), sender, e);
        }

        private void ToggleSwitchAutoSaveStrokesAtScreenshot_Toggled(object sender, RoutedEventArgs e)
        {
            InvokeMainWindowHandler(nameof(ToggleSwitchAutoSaveStrokesAtScreenshot_Toggled), sender, e);
        }

        private void ToggleSwitchAutoSaveStrokesInPowerPoint_Toggled(object sender, RoutedEventArgs e)
        {
            InvokeMainWindowHandler(nameof(ToggleSwitchAutoSaveStrokesInPowerPoint_Toggled), sender, e);
        }

        private void ToggleSwitchAutoStraightenLine_Toggled(object sender, RoutedEventArgs e)
        {
            InvokeMainWindowHandler(nameof(ToggleSwitchAutoStraightenLine_Toggled), sender, e);
        }

        private void ToggleSwitchAutoSwitchTwoFingerGesture_Toggled(object sender, RoutedEventArgs e)
        {
            InvokeMainWindowHandler(nameof(ToggleSwitchAutoSwitchTwoFingerGesture_Toggled), sender, e);
        }

        private void ToggleSwitchCompressPicturesUploaded_Toggled(object sender, RoutedEventArgs e)
        {
            InvokeMainWindowHandler(nameof(ToggleSwitchCompressPicturesUploaded_Toggled), sender, e);
        }

        private void ToggleSwitchEnableDisPlayFloatBarText_Toggled(object sender, RoutedEventArgs e)
        {
            InvokeMainWindowHandler(nameof(ToggleSwitchEnableDisPlayFloatBarText_Toggled), sender, e);
        }

        private void ToggleSwitchEnableDisPlayNibModeToggle_Toggled(object sender, RoutedEventArgs e)
        {
            InvokeMainWindowHandler(nameof(ToggleSwitchEnableDisPlayNibModeToggle_Toggled), sender, e);
        }

        private void ToggleSwitchEnableFingerGestureSlideShowControl_Toggled(object sender, RoutedEventArgs e)
        {
            InvokeMainWindowHandler(nameof(ToggleSwitchEnableFingerGestureSlideShowControl_Toggled), sender, e);
        }

        private void ToggleSwitchEnableInkToShape_Toggled(object sender, RoutedEventArgs e)
        {
            InvokeMainWindowHandler(nameof(ToggleSwitchEnableInkToShape_Toggled), sender, e);
        }

        private void ToggleSwitchEnableRectangleRecognition_Toggled(object sender, RoutedEventArgs e)
        {
            InvokeMainWindowHandler(nameof(ToggleSwitchEnableRectangleRecognition_Toggled), sender, e);
        }

        private void ToggleSwitchEnableTriangleRecognition_Toggled(object sender, RoutedEventArgs e)
        {
            InvokeMainWindowHandler(nameof(ToggleSwitchEnableTriangleRecognition_Toggled), sender, e);
        }

        private void ToggleSwitchEnableTwoFingerGestureInPresentationMode_Toggled(object sender, RoutedEventArgs e)
        {
            InvokeMainWindowHandler(nameof(ToggleSwitchEnableTwoFingerGestureInPresentationMode_Toggled), sender, e);
        }

        private void ToggleSwitchEnableTwoFingerRotation_Toggled(object sender, RoutedEventArgs e)
        {
            InvokeMainWindowHandler(nameof(ToggleSwitchEnableTwoFingerRotation_Toggled), sender, e);
        }

        private void ToggleSwitchFoldAtStartup_Toggled(object sender, RoutedEventArgs e)
        {
            InvokeMainWindowHandler(nameof(ToggleSwitchFoldAtStartup_Toggled), sender, e);
        }

        private void ToggleSwitchHideStrokeWhenSelecting_Toggled(object sender, RoutedEventArgs e)
        {
            InvokeMainWindowHandler(nameof(ToggleSwitchHideStrokeWhenSelecting_Toggled), sender, e);
        }

        private void ToggleSwitchIsAutoUpdateWithProxy_Toggled(object sender, RoutedEventArgs e)
        {
            InvokeMainWindowHandler(nameof(ToggleSwitchIsAutoUpdateWithProxy_Toggled), sender, e);
        }

        private void ToggleSwitchIsAutoUpdateWithSilence_Toggled(object sender, RoutedEventArgs e)
        {
            InvokeMainWindowHandler(nameof(ToggleSwitchIsAutoUpdateWithSilence_Toggled), sender, e);
        }

        private void ToggleSwitchIsAutoUpdate_Toggled(object sender, RoutedEventArgs e)
        {
            InvokeMainWindowHandler(nameof(ToggleSwitchIsAutoUpdate_Toggled), sender, e);
        }

        private void ToggleSwitchIsColorfulViewboxFloatingBar_Toggled(object sender, RoutedEventArgs e)
        {
            InvokeMainWindowHandler(nameof(ToggleSwitchIsColorfulViewboxFloatingBar_Toggled), sender, e);
        }

        private void ToggleSwitchIsEnableEdgeGestureUtil_Toggled(object sender, RoutedEventArgs e)
        {
            InvokeMainWindowHandler(nameof(ToggleSwitchIsEnableEdgeGestureUtil_Toggled), sender, e);
        }

        private void ToggleSwitchIsEnableSilentRestartOnCrash_Toggled(object sender, RoutedEventArgs e)
        {
            InvokeMainWindowHandler(nameof(ToggleSwitchIsEnableSilentRestartOnCrash_Toggled), sender, e);
        }

        private void ToggleSwitchIsLogEnabled_Toggled(object sender, RoutedEventArgs e)
        {
            InvokeMainWindowHandler(nameof(ToggleSwitchIsLogEnabled_Toggled), sender, e);
        }

        private void ToggleSwitchIsQuadIR_Toggled(object sender, RoutedEventArgs e)
        {
            InvokeMainWindowHandler(nameof(ToggleSwitchIsQuadIR_Toggled), sender, e);
        }

        private void ToggleSwitchIsSecondConfimeWhenShutdownApp_Toggled(object sender, RoutedEventArgs e)
        {
            InvokeMainWindowHandler(nameof(ToggleSwitchIsSecondConfimeWhenShutdownApp_Toggled), sender, e);
        }

        private void ToggleSwitchIsSpecialScreen_OnToggled(object sender, RoutedEventArgs e)
        {
            InvokeMainWindowHandler(nameof(ToggleSwitchIsSpecialScreen_OnToggled), sender, e);
        }

        private void ToggleSwitchLineEndpointSnapping_Toggled(object sender, RoutedEventArgs e)
        {
            InvokeMainWindowHandler(nameof(ToggleSwitchLineEndpointSnapping_Toggled), sender, e);
        }

        private void ToggleSwitchNotifyAutoPlayPresentation_Toggled(object sender, RoutedEventArgs e)
        {
            InvokeMainWindowHandler(nameof(ToggleSwitchNotifyAutoPlayPresentation_Toggled), sender, e);
        }

        private void ToggleSwitchNotifyHiddenPage_Toggled(object sender, RoutedEventArgs e)
        {
            InvokeMainWindowHandler(nameof(ToggleSwitchNotifyHiddenPage_Toggled), sender, e);
        }

        private void ToggleSwitchNotifyPreviousPage_Toggled(object sender, RoutedEventArgs e)
        {
            InvokeMainWindowHandler(nameof(ToggleSwitchNotifyPreviousPage_Toggled), sender, e);
        }

        private void ToggleSwitchRunAtStartup_Toggled(object sender, RoutedEventArgs e)
        {
            InvokeMainWindowHandler(nameof(ToggleSwitchRunAtStartup_Toggled), sender, e);
        }

        private void ToggleSwitchSaveScreenshotsInDateFolders_Toggled(object sender, RoutedEventArgs e)
        {
            InvokeMainWindowHandler(nameof(ToggleSwitchSaveScreenshotsInDateFolders_Toggled), sender, e);
        }

        private void ToggleSwitchShowButtonPPTNavigationBottom_OnToggled(object sender, RoutedEventArgs e)
        {
            InvokeMainWindowHandler(nameof(ToggleSwitchShowButtonPPTNavigationBottom_OnToggled), sender, e);
        }

        private void ToggleSwitchShowButtonPPTNavigationSides_OnToggled(object sender, RoutedEventArgs e)
        {
            InvokeMainWindowHandler(nameof(ToggleSwitchShowButtonPPTNavigationSides_OnToggled), sender, e);
        }

        private void ToggleSwitchShowCanvasAtNewSlideShow_Toggled(object sender, RoutedEventArgs e)
        {
            InvokeMainWindowHandler(nameof(ToggleSwitchShowCanvasAtNewSlideShow_Toggled), sender, e);
        }

        private void ToggleSwitchShowCursor_Toggled(object sender, RoutedEventArgs e)
        {
            InvokeMainWindowHandler(nameof(ToggleSwitchShowCursor_Toggled), sender, e);
        }

        private void ToggleSwitchShowPPTNavigationPanelBottom_OnToggled(object sender, RoutedEventArgs e)
        {
            InvokeMainWindowHandler(nameof(ToggleSwitchShowPPTNavigationPanelBottom_OnToggled), sender, e);
        }

        private void ToggleSwitchShowPPTNavigationPanelSide_OnToggled(object sender, RoutedEventArgs e)
        {
            InvokeMainWindowHandler(nameof(ToggleSwitchShowPPTNavigationPanelSide_OnToggled), sender, e);
        }

        private void ToggleSwitchStopTimingStraighten_Toggled(object sender, RoutedEventArgs e)
        {
            InvokeMainWindowHandler(nameof(ToggleSwitchStopTimingStraighten_Toggled), sender, e);
        }

        private void ToggleSwitchSupportPowerPoint_Toggled(object sender, RoutedEventArgs e)
        {
            InvokeMainWindowHandler(nameof(ToggleSwitchSupportPowerPoint_Toggled), sender, e);
        }

        private void ToggleSwitchSupportWPS_Toggled(object sender, RoutedEventArgs e)
        {
            InvokeMainWindowHandler(nameof(ToggleSwitchSupportWPS_Toggled), sender, e);
        }

        private void TouchMultiplierSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            InvokeMainWindowHandler(nameof(TouchMultiplierSlider_ValueChanged), sender, e);
        }
    }
}
