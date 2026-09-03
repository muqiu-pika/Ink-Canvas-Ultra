using System;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Ink;
using System.Windows.Media;
using iNKORE.UI.WPF.Modern;
using Ink_Canvas.Helpers;

namespace Ink_Canvas
{
    public partial class MW_Settings : Window
    {
        public MW_Settings()
        {
            InitializeComponent();
            // 初始设为透明，待设置项填充完成后再淡入，避免“先显示空白再跳变”的闪烁与卡顿感
            Opacity = 0;
            Loaded += SettingsWindow_Loaded;
        }

        private void SettingsWindow_Loaded(object sender, RoutedEventArgs e)
        {
            // 轻量信息立即填充（几乎不耗时）
            if (AppVersionTextBlock != null)
                AppVersionTextBlock.Text = AutoUpdateHelper.GetDisplayVersion();

            // 同步「文档转照片清晰度」滑块与数值显示
            try
            {
                int dpi = MainWindow.Settings?.Automation?.PhotoClarityDpi ?? 150;
                if (PhotoClarityDpiSlider != null)
                    PhotoClarityDpiSlider.Value = dpi;
                if (PhotoClarityDpiValueText != null)
                    PhotoClarityDpiValueText.Text = dpi.ToString();
            }
            catch { }

            // 触控优化：将本窗口内所有滑块设为 IsMoveToPointEnabled，
            // 触屏上轻触轨道即可让缩略块跳到触点并跟随拖动，无需精确抓取极小缩略块。
            // 仅设置该行为属性，不改模板/外观，外观与之前完全一致。
            SetTouchFriendlySliders(this);

            // 将重量级设置填充（数百个控件赋值 + ApplyScaling 等）推迟到窗口首次绘制之后执行，
            // 这样点击设置后窗口会立即出现（此时透明），主线程不会被长时间占用而导致卡顿；
            // 填充完成后再把窗口淡入显示，体验更顺滑。
            Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Background, new Action(() =>
            {
                try
                {
                    InvokeMainWindowHandler("LoadSettings", false);
                }
                finally
                {
                    // 预构建阶段（空闲时后台渲染）不淡入，保持透明直至被真正打开；
                    // 普通打开阶段才淡入显示，避免停留在不可见状态。
                    if (!MainWindow.IsSettingsPrebuilding)
                    {
                        this.BeginAnimation(System.Windows.Window.OpacityProperty,
                            new System.Windows.Media.Animation.DoubleAnimation(1, TimeSpan.FromMilliseconds(120)));
                    }
                }
            }));
        }

        /// <summary>
        /// 重新从内存中的 Settings 填充设置窗口控件（用于窗口复用/重新打开时刷新最新值）。
        /// </summary>
        public void ReloadContents()
        {
            InvokeMainWindowHandler("LoadSettings", false);
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

        /// <summary>
        /// 触控优化：把指定视觉子树中的所有 Slider 设为 IsMoveToPointEnabled=True，
        /// 并禁用触摸"长按手势/轻拂"（否则按住滑块会被识别成长按/轻拂而无法拖动）。
        /// 滑块按住期间会暂时关闭所在 ScrollViewer 的触摸平移，避免整页上下滑动抢占拖拽。
        /// 仅设属性/事件，不改模板与外观。
        /// </summary>
        private static void SetTouchFriendlySliders(DependencyObject root)
        {
            try
            {
                int count = VisualTreeHelper.GetChildrenCount(root);
                for (int i = 0; i < count; i++)
                {
                    var child = VisualTreeHelper.GetChild(root, i);
                    if (child is Slider slider)
                    {
                        ConfigureTouchSlider(slider);
                    }
                    SetTouchFriendlySliders(child);
                }
            }
            catch { }
        }

        /// <summary>
        /// 为单个 Slider 配置触控拖动：关闭长按/轻拂手势，扩大命中区，
        /// 并在按住期间屏蔽所在滚动容器的触摸平移（防止整页上下滑动）。
        /// </summary>
        private static void ConfigureTouchSlider(Slider slider)
        {
            slider.IsMoveToPointEnabled = true;
            slider.MinHeight = 36; // 扩大轨道可点/可拖区域，便于手指命中
            Stylus.SetIsPressAndHoldEnabled(slider, false); // 手指按住即可流畅拖动，不被长按手势拦截
            Stylus.SetIsFlicksEnabled(slider, false);       // 避免横向拖拽被识别成"轻拂"手势

            slider.PreviewStylusDown += Slider_PressStart;
            slider.PreviewStylusUp += Slider_PressEnd;
            slider.StylusUp += Slider_PressEnd;
            slider.LostStylusCapture += Slider_PressEnd;
        }

        private static void Slider_PressStart(object sender, StylusEventArgs e)
        {
            try
            {
                var slider = sender as Slider;
                if (slider == null) return;
                var sv = FindAncestorScrollViewer(slider);
                if (sv == null) return;
                // 记录本次按住前的平移模式，把手抬起/捕获丢失后再恢复
                if (slider.Tag == null) slider.Tag = sv.PanningMode;
                sv.PanningMode = PanningMode.None; // 屏蔽触摸平移，避免整页上下滑动抢占拖拽
            }
            catch { }
        }

        private static void Slider_PressEnd(object sender, StylusEventArgs e)
        {
            try
            {
                var slider = sender as Slider;
                if (slider == null) return;
                var sv = FindAncestorScrollViewer(slider);
                if (sv == null || slider.Tag == null) return;
                sv.PanningMode = (PanningMode)slider.Tag; // 恢复滚动容器的触摸平移
                slider.Tag = null;
            }
            catch { }
        }

        private static ScrollViewer FindAncestorScrollViewer(DependencyObject obj)
        {
            var cur = obj as DependencyObject;
            while (cur != null)
            {
                if (cur is ScrollViewer sv) return sv;
                cur = VisualTreeHelper.GetParent(cur) ?? LogicalTreeHelper.GetParent(cur);
            }
            return null;
        }

        private void AutoSavedStrokesLocationButton_Click(object sender, RoutedEventArgs e)
        {
            InvokeMainWindowHandler(nameof(AutoSavedStrokesLocationButton_Click), sender, e);
        }

        private void AutoSavedStrokesLocationTextBox_TextChanged(object sender, RoutedEventArgs e)
        {
            InvokeMainWindowHandler(nameof(AutoSavedStrokesLocationTextBox_TextChanged), sender, e);
        }

        private void PhotoClarityDpiSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            InvokeMainWindowHandler(nameof(PhotoClarityDpiSlider_ValueChanged), sender, e);
            if (PhotoClarityDpiValueText != null)
                PhotoClarityDpiValueText.Text = ((int)Math.Round(e.NewValue)).ToString();
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

        private void BtnCheckUpdateNow_Click(object sender, RoutedEventArgs e)
        {
            InvokeMainWindowHandler(nameof(BtnCheckUpdateNow_Click), sender, e);
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
            // 真正关闭并释放窗口内存（不再 Hide 缓存复用）。
            // 关闭后主窗口会清空缓存实例，并在应用空闲时重新预构建，兼顾“再次打开较快”与“关闭即释放内存”。
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

        private void ToggleSwitchEnableCircleRecognition_Toggled(object sender, RoutedEventArgs e)
        {
            InvokeMainWindowHandler(nameof(ToggleSwitchEnableCircleRecognition_Toggled), sender, e);
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
