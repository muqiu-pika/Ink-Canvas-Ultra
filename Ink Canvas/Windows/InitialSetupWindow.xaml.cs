using Ink_Canvas.Helpers;
using iNKORE.UI.WPF.Modern;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Animation;
using System.Windows.Media;
using System.Globalization;
using System.Windows.Ink;

namespace Ink_Canvas
{
    public partial class InitialSetupWindow : Window
    {
        private int _currentStep = 1;
        private readonly MainWindow _mainWindow;
        private bool _navLocked = false;

        public InitialSetupWindow()
        {
            InitializeComponent();

            _mainWindow = Application.Current.MainWindow as MainWindow;

            // 纳入统一弹出层：Owner 统一绑到主窗口（而不是打开它的设置窗口），
            // 这样初始化向导与设置窗口属于同一层，点击谁谁在前
            Helpers.PopupWindowLayerHelper.Register(this);

            // 进入动画（仿 Windows OOBE 轻微上滑+淡入）
            AnimationsHelper.ShowWithSlideFromBottomAndFade(this, 0.25);

            // 根据主窗口主题应用弹窗样式
            try
            {
                if (_mainWindow != null)
                {
                    bool isLight = _mainWindow.GetMainWindowTheme() == "Light";
                    ThemeManager.SetRequestedTheme(this, isLight ? ElementTheme.Light : ElementTheme.Dark);
                    // 去重合并，避免每次打开窗口都往应用级资源里堆一份字典
                    ResourceDictionaryHelper.ApplyPopupWindowTheme(isLight);
                    try
                    {
                        var baseBrush = FindResource("PopupWindowDarkBlueBorderBackground") as System.Windows.Media.SolidColorBrush;
                        if (baseBrush != null)
                        {
                            var c = baseBrush.Color;
                            byte r = (byte)Math.Max(0, c.R - 18);
                            byte g = (byte)Math.Max(0, c.G - 18);
                            byte b = (byte)Math.Max(0, c.B - 18);
                            var hoverBrush = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(r, g, b));
                            hoverBrush.Opacity = Math.Min(1.0, baseBrush.Opacity + 0.05);
                            Resources["PopupWindowHoverBackground"] = hoverBrush;
                        }
                    }
                    catch { }
                }
            }
            catch { }

            LoadFromSettings();
            UpdateStepVisual();
            
            // 初始化完成，允许滑块值变化事件正常工作
            _isInitializing = false;
        }

        #region 初始化与设置读写

        private void LoadFromSettings()
        {
            try
            {
                if (MainWindow.Settings == null) return;

                // Step1 - 启动选项
                var startup = MainWindow.Settings.Startup;
                if (startup != null)
                {
                    CheckBoxIsAutoUpdate.IsChecked = startup.IsAutoUpdate;
                    CheckBoxIsAutoUpdateWithSilence.IsChecked = startup.IsAutoUpdateWithSilence;
                    try
                    {
                        AutoUpdateWithSilenceTimeComboBox.InitializeAutoUpdateWithSilenceTimeComboBoxOptions(ComboBoxAutoUpdateSilenceStartTime, ComboBoxAutoUpdateSilenceEndTime);
                        ComboBoxAutoUpdateSilenceStartTime.SelectedItem = startup.AutoUpdateWithSilenceStartTime;
                        ComboBoxAutoUpdateSilenceEndTime.SelectedItem = startup.AutoUpdateWithSilenceEndTime;
                    }
                    catch { }
                    try
                    {
                        var link = Environment.GetFolderPath(Environment.SpecialFolder.Startup) + "\\Ink Canvas Ultra.lnk";
                        CheckBoxRunAtStartup.IsChecked = System.IO.File.Exists(link);
                    }
                    catch { }
                }

                // Step2 - 墨迹识别选项
                var inkToShape = MainWindow.Settings.InkToShape;
                var canvas = MainWindow.Settings.Canvas;
                if (inkToShape != null)
                {
                    CheckBoxEnableInkToShape.IsChecked = inkToShape.IsInkToShapeEnabled;
                    CheckBoxInkToShapeTriangle.IsChecked = inkToShape.IsInkToShapeTriangle;
                    CheckBoxInkToShapeRectangle.IsChecked = inkToShape.IsInkToShapeRectangle;

                }
                if (canvas != null)
                {
                    CheckBoxAutoStraightenLine.IsChecked = canvas.AutoStraightenLine;
                    CheckBoxLineEndpointSnapping.IsChecked = canvas.LineEndpointSnapping;
                    try { CheckBoxShowCursorWizard.IsChecked = canvas.IsShowCursor; } catch { }
                    try { CheckBoxAutoClearOnExitWizard.IsChecked = canvas.HideStrokeWhenSelecting; } catch { }
                }

                // Step3 - 外观选项
                var appearance = MainWindow.Settings.Appearance;
                if (appearance != null)
                {
                    try
                {
                    double v = appearance.FloatingBarScale;
                    // 修复：当值为0或未初始化时，使用默认值100
                    if (v <= 0)
                    {
                        v = 100.0;
                    }
                    else if (v > 0 && v <= 3)
                    {
                        v *= 100;
                    }
                    if (v < SliderFloatingBarScaleWizard.Minimum) v = SliderFloatingBarScaleWizard.Minimum;
                    if (v > SliderFloatingBarScaleWizard.Maximum) v = SliderFloatingBarScaleWizard.Maximum;
                    SliderFloatingBarScaleWizard.Value = v;
                }
                catch { }
                    CheckBoxEnableFloatBarText.IsChecked = appearance.IsEnableDisPlayFloatBarText;
                    try
                    {
                        if (!string.IsNullOrEmpty(appearance.VideoPresenterSidebarPosition) && appearance.VideoPresenterSidebarPosition == "Right")
                            ComboBoxVideoPresenterSidebarPositionWizard.SelectedIndex = 1;
                        else
                            ComboBoxVideoPresenterSidebarPositionWizard.SelectedIndex = 0;
                    }
                    catch { }
                }
                var ppt = MainWindow.Settings.PowerPointSettings;
                if (ppt != null)
                {
                    CheckBoxShowPPTNavigationPanelBottom.IsChecked = ppt.IsShowBottomPPTNavigationPanel;
                    CheckBoxShowButtonPPTNavigationBottom.IsChecked = ppt.IsShowPPTNavigationBottom;
                    CheckBoxShowPPTNavigationPanelSide.IsChecked = ppt.IsShowSidePPTNavigationPanel;
                    CheckBoxShowButtonPPTNavigationSides.IsChecked = ppt.IsShowPPTNavigationSides;
                }

                // Step4 - PPT 选项
                if (ppt != null)
                {
                    CheckBoxSupportPowerPoint.IsChecked = ppt.PowerPointSupport;
                    CheckBoxSupportWPS.IsChecked = ppt.IsSupportWPS;
                    CheckBoxAutoSaveScreenShotInPowerPoint.IsChecked = ppt.IsAutoSaveScreenShotInPowerPoint;
                    CheckBoxNotifyHiddenPage.IsChecked = ppt.IsNotifyHiddenPage;
                    CheckBoxNotifyAutoPlayPresentation.IsChecked = ppt.IsNotifyAutoPlayPresentation;
                }

                // Step5 - 高级选项
                var adv = MainWindow.Settings.Advanced;
                if (adv != null)
                {
                    CheckBoxIsSpecialScreen.IsChecked = adv.IsSpecialScreen;
                    try { SliderTouchMultiplier.Value = adv.TouchMultiplier; } catch { }
                    CheckBoxIsQuadIR.IsChecked = adv.IsQuadIR;
                    CheckBoxIsSecondConfimeWhenShutdownApp.IsChecked = adv.IsSecondConfimeWhenShutdownApp;
                    CheckBoxIsEnableSilentRestartOnCrash.IsChecked = adv.IsEnableSilentRestartOnCrash;
                }
            }
            catch { }
            UpdateSilencePeriodVisibility();
        }

        private void SaveToSettings()
        {
            try
            {
                if (MainWindow.Settings == null) return;

                // Step1 - 启动选项
                if (MainWindow.Settings.Startup != null)
                {
                    MainWindow.Settings.Startup.IsAutoUpdate = CheckBoxIsAutoUpdate.IsChecked == true;
                    MainWindow.Settings.Startup.IsAutoUpdateWithSilence = CheckBoxIsAutoUpdateWithSilence.IsChecked == true;
                    try
                    {
                        MainWindow.Settings.Startup.AutoUpdateWithSilenceStartTime = (string)ComboBoxAutoUpdateSilenceStartTime.SelectedItem;
                        MainWindow.Settings.Startup.AutoUpdateWithSilenceEndTime = (string)ComboBoxAutoUpdateSilenceEndTime.SelectedItem;
                    }
                    catch { }
                    try
                    {
                        if (CheckBoxRunAtStartup.IsChecked == true)
                        {
                            MainWindow.StartAutomaticallyDel("InkCanvas");
                            MainWindow.StartAutomaticallyDel("Ink Canvas Annotation");
                            MainWindow.StartAutomaticallyCreate("Ink Canvas Ultra");
                        }
                        else
                        {
                            MainWindow.StartAutomaticallyDel("InkCanvas");
                            MainWindow.StartAutomaticallyDel("Ink Canvas Annotation");
                            MainWindow.StartAutomaticallyDel("Ink Canvas Ultra");
                        }
                    }
                    catch { }
                }

                // Step2 - 墨迹识别选项
                if (MainWindow.Settings.InkToShape != null)
                {
                    MainWindow.Settings.InkToShape.IsInkToShapeEnabled = CheckBoxEnableInkToShape.IsChecked == true;
                    MainWindow.Settings.InkToShape.IsInkToShapeTriangle = CheckBoxInkToShapeTriangle.IsChecked == true;
                    MainWindow.Settings.InkToShape.IsInkToShapeRectangle = CheckBoxInkToShapeRectangle.IsChecked == true;

                }
                if (MainWindow.Settings.Canvas != null)
                {
                    MainWindow.Settings.Canvas.AutoStraightenLine = CheckBoxAutoStraightenLine.IsChecked == true;
                    MainWindow.Settings.Canvas.LineEndpointSnapping = CheckBoxLineEndpointSnapping.IsChecked == true;
                    // 保存画笔与笔迹相关设置
                    MainWindow.Settings.Canvas.IsShowCursor = CheckBoxShowCursorWizard.IsChecked == true;
                }
                
                // 保存退出画板模式后隐藏墨迹的设置
                if (MainWindow.Settings.Canvas != null)
                {
                    MainWindow.Settings.Canvas.HideStrokeWhenSelecting = CheckBoxAutoClearOnExitWizard.IsChecked == true;
                }

                // Step3 - 外观选项
                if (MainWindow.Settings.Appearance != null)
                {
                    MainWindow.Settings.Appearance.FloatingBarScale = SliderFloatingBarScaleWizard.Value;
                    MainWindow.Settings.Appearance.IsEnableDisPlayFloatBarText = CheckBoxEnableFloatBarText.IsChecked == true;
                    try
                    {
                        var item = ComboBoxVideoPresenterSidebarPositionWizard.SelectedItem as ComboBoxItem;
                        if (item?.Tag != null)
                        {
                            MainWindow.Settings.Appearance.VideoPresenterSidebarPosition = item.Tag.ToString();
                        }
                    }
                    catch { }
                }
                if (MainWindow.Settings.PowerPointSettings != null)
                {
                    MainWindow.Settings.PowerPointSettings.IsShowBottomPPTNavigationPanel = CheckBoxShowPPTNavigationPanelBottom.IsChecked == true;
                    MainWindow.Settings.PowerPointSettings.IsShowPPTNavigationBottom = CheckBoxShowButtonPPTNavigationBottom.IsChecked == true;
                    MainWindow.Settings.PowerPointSettings.IsShowSidePPTNavigationPanel = CheckBoxShowPPTNavigationPanelSide.IsChecked == true;
                    MainWindow.Settings.PowerPointSettings.IsShowPPTNavigationSides = CheckBoxShowButtonPPTNavigationSides.IsChecked == true;
                }

                // Step4 - PPT 选项
                if (MainWindow.Settings.PowerPointSettings != null)
                {
                    MainWindow.Settings.PowerPointSettings.PowerPointSupport = CheckBoxSupportPowerPoint.IsChecked == true;
                    MainWindow.Settings.PowerPointSettings.IsSupportWPS = CheckBoxSupportWPS.IsChecked == true;
                    MainWindow.Settings.PowerPointSettings.IsAutoSaveScreenShotInPowerPoint = CheckBoxAutoSaveScreenShotInPowerPoint.IsChecked == true;
                    MainWindow.Settings.PowerPointSettings.IsNotifyHiddenPage = CheckBoxNotifyHiddenPage.IsChecked == true;
                    MainWindow.Settings.PowerPointSettings.IsNotifyAutoPlayPresentation = CheckBoxNotifyAutoPlayPresentation.IsChecked == true;
                }

                // Step5 - 高级选项
                if (MainWindow.Settings.Advanced != null)
                {
                    MainWindow.Settings.Advanced.IsSpecialScreen = CheckBoxIsSpecialScreen.IsChecked == true;
                    MainWindow.Settings.Advanced.TouchMultiplier = SliderTouchMultiplier.Value;
                    MainWindow.Settings.Advanced.IsQuadIR = CheckBoxIsQuadIR.IsChecked == true;
                    MainWindow.Settings.Advanced.IsSecondConfimeWhenShutdownApp = CheckBoxIsSecondConfimeWhenShutdownApp.IsChecked == true;
                    MainWindow.Settings.Advanced.IsEnableSilentRestartOnCrash = CheckBoxIsEnableSilentRestartOnCrash.IsChecked == true;
                }

                // 注册 URI 协议
                try
                {
                    App.RegisterUriScheme();
                }
                catch { }

                // 标记首次向导已完成
                if (MainWindow.Settings.Startup != null)
                {
                    MainWindow.Settings.Startup.IsInitialSetupCompleted = true;
                }

                MainWindow.SaveSettingsToFile();
                _mainWindow?.ReloadSettingsFromSettingsObject();
            }
            catch { }
        }

        #endregion

        #region 步骤切换与动画

        private void UpdateStepVisual()
        {
            StepIndicatorTextBlock.Text = $"步骤 {_currentStep} / 5";
            BtnPrevious.Visibility = _currentStep > 1 ? Visibility.Visible : Visibility.Collapsed;

            BtnNextTextBlock.Text = _currentStep == 5 ? "完成" : "下一步";

            // 圆点指示
            DotStep1.Fill = _currentStep == 1 ? (System.Windows.Media.Brush)FindResource("PopupWindowDarkBlueBorderBackground") : new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromArgb(0x40, 0x80, 0x80, 0x80));
            DotStep2.Fill = _currentStep == 2 ? (System.Windows.Media.Brush)FindResource("PopupWindowDarkBlueBorderBackground") : new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromArgb(0x40, 0x80, 0x80, 0x80));
            DotStep3.Fill = _currentStep == 3 ? (System.Windows.Media.Brush)FindResource("PopupWindowDarkBlueBorderBackground") : new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromArgb(0x40, 0x80, 0x80, 0x80));
            DotStep4.Fill = _currentStep == 4 ? (System.Windows.Media.Brush)FindResource("PopupWindowDarkBlueBorderBackground") : new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromArgb(0x40, 0x80, 0x80, 0x80));
            DotStep5.Fill = _currentStep == 5 ? (System.Windows.Media.Brush)FindResource("PopupWindowDarkBlueBorderBackground") : new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromArgb(0x40, 0x80, 0x80, 0x80));

            // 简单淡入淡出动画
            ShowStepGrid(Step1Grid, _currentStep == 1);
            ShowStepGrid(Step2Grid, _currentStep == 2);
            ShowStepGrid(Step3Grid, _currentStep == 3);
            ShowStepGrid(Step4Grid, _currentStep == 4);
            ShowStepGrid(Step5Grid, _currentStep == 5);

            UpdateEmojiVisual();
        }

        private void UpdateSilencePeriodVisibility()
        {
            try
            {
                bool autoUpdateOn = CheckBoxIsAutoUpdate.IsChecked == true;
                CheckBoxIsAutoUpdateWithSilence.Visibility = autoUpdateOn ? Visibility.Visible : Visibility.Collapsed;
                if (!autoUpdateOn)
                {
                    CheckBoxIsAutoUpdateWithSilence.IsChecked = false;
                }
                SilencePeriodPanel.Visibility = (autoUpdateOn && CheckBoxIsAutoUpdateWithSilence.IsChecked == true) ? Visibility.Visible : Visibility.Collapsed;
            }
            catch { }
        }

        private void CheckBoxIsAutoUpdateWithSilence_Click(object sender, RoutedEventArgs e)
        {
            UpdateSilencePeriodVisibility();
            try { _mainWindow?.SetAutoUpdateWithSilenceEnabled(CheckBoxIsAutoUpdateWithSilence.IsChecked == true); } catch { }
        }

        #region 特殊屏幕自动校准（采样 → 平均值 → 静默写入设置）

        /// <summary>采样保留的最大数量：单次采样即可应用，用户多写几次就取平均（超出后丢弃最早样本，便于重新校准）</summary>
        private const int CalibrationMaxSamples = 10;
        /// <summary>面积擦橡皮圆的目标直径（像素）：沿用原面积擦测试的口径，保证擦除手感一致</summary>
        private const double CalibrationEraserDiameterTarget = 45.0;
        /// <summary>上报宽度与目标值之间的比例系数（沿用原实现）</summary>
        private const double CalibrationWidthFactor = 1.1;
        /// <summary>触摸倍数下限：运行时倍数为 0 会在特殊屏幕下直接禁用手指擦除，静默写 0 等于悄悄关掉功能</summary>
        private const double CalibrationMinTouchMultiplier = 0.05;
        /// <summary>擦除阈值上下限（与运行时判断一致的可读范围）</summary>
        private const double CalibrationMinThreshold = 1.1;
        private const double CalibrationMaxThreshold = 10.0;

        /// <summary>「触摸/书写轨迹测试」采集到的书写触点宽度样本</summary>
        private readonly List<double> _writeContactSamples = new List<double>();
        /// <summary>「面积擦检测」采集到的面积擦触点宽度样本</summary>
        private readonly List<double> _eraseContactSamples = new List<double>();

        /// <summary>当前生效的基准宽度（笔尖模式 / 手指模式），运行时以「基准宽 × 阈值」判定是否面积擦</summary>
        private double GetCurrentBoundsWidth()
        {
            var adv = MainWindow.Settings?.Advanced;
            if (adv == null) return 30;
            double width = MainWindow.Settings.Startup.IsEnableNibMode ? adv.NibModeBoundsWidth : adv.FingerModeBoundsWidth;
            return width > 0 ? width : 30;
        }

        /// <summary>读取本次接触宽度：四边红外屏用宽高几何平均估计</summary>
        private double GetContactWidth(TouchEventArgs e)
        {
            var bounds = e.GetTouchPoint(null).Bounds;
            return (MainWindow.Settings?.Advanced?.IsQuadIR == true)
                ? Math.Sqrt(bounds.Width * bounds.Height)
                : bounds.Width;
        }

        /// <summary>记录一次采样（丢弃无效值；超出上限后丢弃最早样本）</summary>
        private void AddContactSample(List<double> samples, double value)
        {
            if (double.IsNaN(value) || double.IsInfinity(value) || value <= 0) return;
            samples.Add(value);
            while (samples.Count > CalibrationMaxSamples) samples.RemoveAt(0);
        }

        /// <summary>
        /// 求平均接触宽度：单次采样直接采用该值（不要求必须写多次）；
        /// 多次采样先按中位数剔除离群值（&gt; 2×中位数，多为手掌/误触造成的异常上报）再取平均。
        /// </summary>
        private bool TryGetAverageContactWidth(List<double> samples, out double average, out int usedCount)
        {
            average = 0;
            usedCount = 0;
            if (samples == null || samples.Count == 0) return false;

            var ordered = samples.OrderBy(v => v).ToList();
            double median = ordered[ordered.Count / 2];
            var valid = samples.Where(v => v <= median * 2.0).ToList();
            if (valid.Count == 0) valid = ordered;

            average = valid.Average();
            usedCount = valid.Count;
            return true;
        }

        /// <summary>
        /// 按运行时语义推导校准值并静默写入设置（不弹窗）。
        ///
        /// 橡皮圆：运行时「面积擦橡皮圆直径 = 接触宽 × EraserSize × TouchMultiplier」（见 Main_Grid_TouchDown），
        /// 因此把 EraserSize 固定为 1.0、令 TouchMultiplier = 目标直径 ÷ (面积擦接触宽 × 1.1)，
        /// 特殊屏上的橡皮圆也能保持同一可见大小（≈45px），不受驱动上报单位影响；
        /// 同时打开「特殊屏幕模式」开关（倍数为 0 时该开关会禁用手指擦除，故倍数有下限保护）。
        ///
        /// 擦除阈值 T 的约束是「书写接触宽 &lt; 基准宽 × T &lt; 面积擦接触宽」：
        ///   两个样本都有 → 取几何中点；只有面积擦样本 → 0.7 × 面积擦宽（原有口径）；
        ///   只有书写样本 → 2.5 × 书写宽（普通屏上正好等于默认阈值 2.5，不改变默认手感）。
        /// </summary>
        /// <param name="fromAreaEraserTest">true 表示本次由「面积擦检测」触发（用于选择反馈文本的显示位置）</param>
        private void RecomputeAndApplyCalibration(bool fromAreaEraserTest)
        {
            try
            {
                var adv = MainWindow.Settings?.Advanced;
                if (adv == null) return;

                bool hasWrite = TryGetAverageContactWidth(_writeContactSamples, out double avgWrite, out int writeCount);
                bool hasErase = TryGetAverageContactWidth(_eraseContactSamples, out double avgErase, out int eraseCount);
                if (!hasWrite && !hasErase) return;

                double boundsWidth = GetCurrentBoundsWidth();

                double? threshold = null;
                if (hasWrite && hasErase) threshold = Math.Sqrt(avgWrite * avgErase) / boundsWidth;
                else if (hasErase) threshold = (avgErase / boundsWidth) * 0.7;
                else if (hasWrite) threshold = (avgWrite / boundsWidth) * 2.5;

                double? multiplier = null;
                // 橡皮圆尺寸只能由「面积擦」样本推导（它测的是真实擦除动作的接触面积）；
                // 只有书写样本时不猜倍数，避免把擦除手感改坏
                if (hasErase) multiplier = CalibrationEraserDiameterTarget / (avgErase * CalibrationWidthFactor);

                double? appliedMultiplier = null;
                if (multiplier.HasValue)
                {
                    double m = Math.Max(CalibrationMinTouchMultiplier,
                                        Math.Min(SliderTouchMultiplier.Maximum, multiplier.Value));
                    adv.TouchMultiplier = m;
                    adv.NibModeBoundsWidthEraserSize = 1.0;
                    adv.FingerModeBoundsWidthEraserSize = 1.0;
                    adv.IsSpecialScreen = true;
                    CheckBoxIsSpecialScreen.IsChecked = true;
                    SliderTouchMultiplier.Value = m;
                    appliedMultiplier = m;
                }

                double? appliedThreshold = null;
                if (threshold.HasValue)
                {
                    double t = Math.Max(CalibrationMinThreshold, Math.Min(CalibrationMaxThreshold, threshold.Value));
                    adv.NibModeBoundsWidthThresholdValue = t;
                    adv.FingerModeBoundsWidthThresholdValue = t;
                    appliedThreshold = t;
                }

                MainWindow.SaveSettingsToFile();

                // 反馈文本：采样次数与均值 + 本次实际写入的值（静默应用，但要让用户看到改了什么）
                var parts = new List<string>();
                if (hasWrite) parts.Add($"书写触点均值 {avgWrite:F2}（{writeCount} 次）");
                if (hasErase) parts.Add($"面积擦触点均值 {avgErase:F2}（{eraseCount} 次）");
                var applied = new List<string>();
                if (appliedMultiplier.HasValue) applied.Add($"触摸倍数 {appliedMultiplier.Value:F2}");
                if (appliedThreshold.HasValue) applied.Add($"擦除阈值 {appliedThreshold.Value:F2}");
                if (appliedMultiplier.HasValue) applied.Add("已开启特殊屏幕模式");
                string text = $"{string.Join("｜", parts)}{Environment.NewLine}已自动应用：{(applied.Count > 0 ? string.Join("、", applied) : "无")}";

                if (fromAreaEraserTest)
                {
                    if (TextBlockShowAreaEraserWizard != null) TextBlockShowAreaEraserWizard.Text = text;
                }
                else
                {
                    if (TextBlockShowCalculatedMultiplierWizard != null) TextBlockShowCalculatedMultiplierWizard.Text = text;
                }
            }
            catch { }
        }

        #endregion

        private void InkCanvasTraceTest_PreviewTouchDown(object sender, TouchEventArgs e)
        {
            try
            {
                // 「触摸/书写轨迹测试」：单次触摸即可参与校准，多写几次则取平均
                AddContactSample(_writeContactSamples, GetContactWidth(e));
                RecomputeAndApplyCalibration(false);
            }
            catch { }
        }

        private void InkCanvasAreaEraserTest_Loaded(object sender, RoutedEventArgs e)
        {
            try
            {
                // 清空笔迹，只显示面积擦圆形范围
                InkCanvasAreaEraserTest.Strokes.Clear();
                InkCanvasAreaEraserTest.EraserShape = new EllipseStylusShape(2, 2);
            }
            catch { }
        }

        private void DrawStroke(InkCanvas canvas, params Point[] points)
        {
             // Deprecated, replaced by text geometry generation
        }

        private void InkCanvasAreaEraserTest_PreviewTouchDown(object sender, TouchEventArgs e)
        {
             ProcessAreaEraserInput(e);
        }

        private void InkCanvasAreaEraserTest_PreviewTouchMove(object sender, TouchEventArgs e)
        {
             ProcessAreaEraserInput(e);
        }

        private void ProcessAreaEraserInput(TouchEventArgs e)
        {
            try
            {
                var touchPoint = e.GetTouchPoint(InkCanvasAreaEraserTest);
                var bounds = touchPoint.Bounds;
                
                // 1. 完全模拟软件中的面积擦行为：使用 EraseByPoint 且 EraserShape 等于接触面积
                // 注意：WPF InkCanvas 的 EraseByPoint 模式会擦除 EraserShape 覆盖的任何笔迹部分
                // 这正是“面积擦”的效果 (Spatial Eraser)
                
                // 使用宽高的最大值作为直径，或者直接用椭圆
                double width = bounds.Width;
                double height = bounds.Height;
                
                // 最小限制，避免看不见
                if (width < 2) width = 2;
                if (height < 2) height = 2;

                InkCanvasAreaEraserTest.EraserShape = new EllipseStylusShape(width, height);
                InkCanvasAreaEraserTest.EditingMode = InkCanvasEditingMode.EraseByPoint;

                // 更新视觉反馈圆圈 - 显示面积擦范围
                if (AreaEraserCursor != null)
                {
                    AreaEraserCursor.Width = width;
                    AreaEraserCursor.Height = height;
                    // 使用 Margin 进行定位 (Grid 中 HorizontalAlignment="Left" VerticalAlignment="Top")
                    AreaEraserCursor.Margin = new Thickness(touchPoint.Position.X - width / 2, touchPoint.Position.Y - height / 2, 0, 0);
                    AreaEraserCursor.Visibility = Visibility.Visible;
                }
            }
            catch { }
        }

        private void InkCanvasAreaEraserTest_PreviewTouchUp(object sender, TouchEventArgs e)
        {
            try
            {
                 // 隐藏视觉反馈圆圈
                 if (AreaEraserCursor != null)
                 {
                     AreaEraserCursor.Visibility = Visibility.Collapsed;
                 }

                 double value = GetContactWidth(e);

                 // 过滤明显的误触/无效上报；单次即可参与校准，多划几次则取平均
                 if (value > 5)
                 {
                     AddContactSample(_eraseContactSamples, value);
                     RecomputeAndApplyCalibration(true);

                     // 清空笔迹以便再次测试
                     InkCanvasAreaEraserTest.Strokes.Clear();
                 }
            }
            catch {}
        }

        private void ShowStepGrid(Grid grid, bool isVisible)
        {
            double from = isVisible ? 0 : 1;
            double to = isVisible ? 1 : 0;

            if (isVisible)
            {
                grid.Visibility = Visibility.Visible;
            }

            var anim = new DoubleAnimation
            {
                From = from,
                To = to,
                Duration = TimeSpan.FromMilliseconds(220),
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseInOut }
            };

            anim.Completed += (s, e) =>
            {
                if (!isVisible)
                {
                    grid.Visibility = Visibility.Collapsed;
                }
            };

            grid.BeginAnimation(OpacityProperty, anim);
        }

        private void UpdateEmojiVisual()
        {
            try
            {
                EmojiStep1.Visibility = _currentStep == 1 ? Visibility.Visible : Visibility.Collapsed;
                EmojiStep2.Visibility = _currentStep == 2 ? Visibility.Visible : Visibility.Collapsed;
                EmojiStep3.Visibility = _currentStep == 3 ? Visibility.Visible : Visibility.Collapsed;
                EmojiStep4.Visibility = _currentStep == 4 ? Visibility.Visible : Visibility.Collapsed;
                EmojiStep5.Visibility = _currentStep == 5 ? Visibility.Visible : Visibility.Collapsed;

                TextBlock target;
                switch (_currentStep)
                {
                    case 1:
                        target = EmojiStep1;
                        break;
                    case 2:
                        target = EmojiStep2;
                        break;
                    case 3:
                        target = EmojiStep3;
                        break;
                    case 4:
                        target = EmojiStep4;
                        break;
                    default:
                        target = EmojiStep5;
                        break;
                }

                var anim = new DoubleAnimation
                {
                    From = 0,
                    To = 1,
                    Duration = TimeSpan.FromMilliseconds(220),
                    EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseInOut }
                };
                target.BeginAnimation(OpacityProperty, anim);
            }
            catch { }
        }

        private bool _isInitializing = true;

        private void BtnSetFloatingBarScaleWizard_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (sender is Button btn && btn.Tag != null && double.TryParse(btn.Tag.ToString(), out double scalePercent))
                {
                    SliderFloatingBarScaleWizard.Value = scalePercent;
                }
            }
            catch { }
        }

        private void SliderFloatingBarScaleWizard_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            // 修复：初始化过程中不保存和应用设置，避免立即改变浮动栏大小
            if (_isInitializing) return;
            
            try
            {
                if (MainWindow.Settings?.Appearance != null)
                {
                    MainWindow.Settings.Appearance.FloatingBarScale = e.NewValue;
                    MainWindow.SaveSettingsToFile();
                    _mainWindow?.ReloadSettingsFromSettingsObject();
                }
            }
            catch { }
        }

        #endregion

        #region 按钮事件

        private void BtnPrevious_Click(object sender, RoutedEventArgs e)
        {
            if (_navLocked) return;
            if (_currentStep <= 1) return;
            LockNav();
            _currentStep--;
            UpdateStepVisual();
        }

        private void BtnNext_Click(object sender, RoutedEventArgs e)
        {
            if (_navLocked) return;
            if (_currentStep < 5)
            {
                LockNav();
                _currentStep++;
                UpdateStepVisual();
            }
            else
            {
                ShowCongratsPage();
            }
        }

        private void ShowCongratsPage()
        {
            try
            {
                ShowStepGrid(Step1Grid, false);
                ShowStepGrid(Step2Grid, false);
                ShowStepGrid(Step3Grid, false);
                ShowStepGrid(Step4Grid, false);
                ShowStepGrid(Step5Grid, false);
                ShowStepGrid(Step6Grid, true);
                
                // 隐藏底部导航面板
                if (BottomNavPanel != null) BottomNavPanel.Visibility = Visibility.Collapsed;
                
                // 隐藏步骤指示器
                if (StepIndicatorTextBlock != null) StepIndicatorTextBlock.Visibility = Visibility.Collapsed;
                
                // 1. 隐藏右侧插画区域
                var rightBorder = this.FindName("RightBorder") as Border;
                if (rightBorder == null)
                {
                    // 如果没有找到命名的Border，尝试通过Grid.Column查找
                    var contentHostGrid = this.FindName("ContentHostGrid") as Grid;
                    if (contentHostGrid != null)
                    {
                        var parentGrid = contentHostGrid.Parent as ScrollViewer;
                        if (parentGrid != null && parentGrid.Parent is Grid mainGrid)
                        {
                            rightBorder = mainGrid.Children.OfType<Border>().FirstOrDefault(b => Grid.GetColumn(b) == 1);
                        }
                    }
                }
                if (rightBorder != null)
                {
                    rightBorder.Visibility = Visibility.Collapsed;
                }
                
                // 2. 调整列宽，让左侧内容占据整个宽度
                LeftColumn.Width = new GridLength(1, GridUnitType.Star);
                RightColumn.Width = new GridLength(0);
                
                // 3. 调整ScrollViewer，使其占据整个宽度
                var mainContentGrid = ContentHostGrid.Parent as ScrollViewer;
                if (mainContentGrid != null)
                {
                    mainContentGrid.Margin = new Thickness(0);
                }
                
                StartConfetti();
            }
            catch { }
        }

        private void BtnStartUsing_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                SaveToSettings();
            }
            catch { }
            Close();
        }

        private void StartConfetti()
        {
            try
            {
                if (ConfettiCanvas == null) return;
                ConfettiCanvas.Children.Clear();
                var rand = new Random();
                for (int i = 0; i < 60; i++)
                {
                    var rect = new System.Windows.Shapes.Rectangle
                    {
                        Width = rand.Next(6, 14),
                        Height = rand.Next(10, 24),
                        Fill = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb((byte)rand.Next(50, 255), (byte)rand.Next(50, 255), (byte)rand.Next(50, 255)))
                    };
                    double left = rand.NextDouble() * ConfettiCanvas.ActualWidth;
                    if (double.IsNaN(left) || left <= 0) left = rand.Next(0, 780);
                    System.Windows.Controls.Canvas.SetLeft(rect, left);
                    System.Windows.Controls.Canvas.SetTop(rect, -rand.Next(20, 200));
                    ConfettiCanvas.Children.Add(rect);

                    var transform = new TranslateTransform();
                    rect.RenderTransform = transform;

                    var fall = new DoubleAnimation
                    {
                        From = 0,
                        To = ConfettiCanvas.ActualHeight + 300,
                        Duration = TimeSpan.FromSeconds(rand.NextDouble() * 1.8 + 1.8),
                        RepeatBehavior = RepeatBehavior.Forever
                    };
                    transform.BeginAnimation(TranslateTransform.YProperty, fall);

                    var sway = new DoubleAnimation
                    {
                        From = -12,
                        To = 12,
                        Duration = TimeSpan.FromSeconds(rand.NextDouble() * 1.5 + 0.8),
                        AutoReverse = true,
                        RepeatBehavior = RepeatBehavior.Forever
                    };
                    transform.BeginAnimation(TranslateTransform.XProperty, sway);
                }
            }
            catch { }
        }

        /// <summary>
        /// 停止彩带动画。RepeatBehavior.Forever 的动画时钟会常驻 WPF 计时树并持续引用被动画对象，
        /// 窗口关闭后既回收不掉整棵可视化树，也会一直空转消耗 CPU，因此必须显式清除。
        /// </summary>
        private void StopConfetti()
        {
            try
            {
                if (ConfettiCanvas == null) return;
                foreach (var child in ConfettiCanvas.Children.OfType<UIElement>())
                {
                    if (child.RenderTransform is TranslateTransform transform)
                    {
                        transform.BeginAnimation(TranslateTransform.YProperty, null);
                        transform.BeginAnimation(TranslateTransform.XProperty, null);
                    }
                }
                ConfettiCanvas.Children.Clear();
            }
            catch { }
        }

        /// <summary>
        /// 停止 XAML 中通过 EventTrigger 启动的表情缩放循环动画（同样是 Forever 动画）。
        /// </summary>
        private void StopEmojiAnimations()
        {
            var emojis = new FrameworkElement[] { EmojiStep1, EmojiStep2, EmojiStep3, EmojiStep4, EmojiStep5 };
            foreach (var emoji in emojis)
            {
                try
                {
                    if (emoji?.RenderTransform is ScaleTransform scale)
                    {
                        scale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
                        scale.BeginAnimation(ScaleTransform.ScaleYProperty, null);
                    }
                }
                catch { }
            }
        }

        protected override void OnClosed(EventArgs e)
        {
            StopNavTimer();
            StopConfetti();
            StopEmojiAnimations();
            base.OnClosed(e);
        }

        private System.Windows.Threading.DispatcherTimer _navTimer;

        private void NavTimer_Tick(object sender, EventArgs e)
        {
            StopNavTimer();
            try
            {
                _navLocked = false;
                if (BtnNext != null) BtnNext.IsEnabled = true;
                if (BtnPrevious != null) BtnPrevious.IsEnabled = _currentStep > 1 && Visibility.Visible == BtnPrevious.Visibility;
            }
            catch { }
        }

        /// <summary>
        /// 停止并反注册导航锁定定时器：DispatcherTimer 由 Dispatcher 持有，
        /// 若窗口关闭时仍在运行，Tick 处理函数会继续引用本窗口，阻止其被回收。
        /// </summary>
        private void StopNavTimer()
        {
            if (_navTimer == null) return;
            _navTimer.Stop();
            _navTimer.Tick -= NavTimer_Tick;
            _navTimer = null;
        }

        private void LockNav(int milliseconds = 320)
        {
            try
            {
                _navLocked = true;
                if (BtnNext != null) BtnNext.IsEnabled = false;
                if (BtnPrevious != null) BtnPrevious.IsEnabled = false;
                StopNavTimer();
                _navTimer = new System.Windows.Threading.DispatcherTimer
                {
                    Interval = TimeSpan.FromMilliseconds(milliseconds)
                };
                _navTimer.Tick += NavTimer_Tick;
                _navTimer.Start();
            }
            catch { }
        }

        private void BtnSkip_Click(object sender, RoutedEventArgs e)
        {
            // 跳过也视为已完成，以免反复弹出
            try
            {
                if (MainWindow.Settings?.Startup != null)
                {
                    MainWindow.Settings.Startup.IsInitialSetupCompleted = true;
                    MainWindow.SaveSettingsToFile();
                }
            }
            catch { }

            Close();
        }

        private void CheckBoxIsAutoUpdate_Click(object sender, RoutedEventArgs e)
        {
            try { _mainWindow?.SetAutoUpdateEnabled(CheckBoxIsAutoUpdate.IsChecked == true); } catch { }
            UpdateSilencePeriodVisibility();
        }

        private void CheckBoxRunAtStartup_Click(object sender, RoutedEventArgs e)
        {
            try { _mainWindow?.SetRunAtStartupEnabled(CheckBoxRunAtStartup.IsChecked == true); } catch { }
        }

        private void CheckBoxEnableFloatBarText_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (MainWindow.Settings?.Appearance != null)
                {
                    MainWindow.Settings.Appearance.IsEnableDisPlayFloatBarText = CheckBoxEnableFloatBarText.IsChecked == true;
                    MainWindow.SaveSettingsToFile();
                    _mainWindow?.ReloadSettingsFromSettingsObject();
                }
            }
            catch { }
        }

        private void ComboBoxVideoPresenterSidebarPositionWizard_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            try
            {
                if (MainWindow.Settings?.Appearance != null)
                {
                    var item = ComboBoxVideoPresenterSidebarPositionWizard.SelectedItem as ComboBoxItem;
                    if (item?.Tag != null)
                    {
                        MainWindow.Settings.Appearance.VideoPresenterSidebarPosition = item.Tag.ToString();
                        MainWindow.SaveSettingsToFile();
                        _mainWindow?.ReloadSettingsFromSettingsObject();
                    }
                }
            }
            catch { }
        }

        private void CheckBoxShowPPTNavigationPanelBottom_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (MainWindow.Settings?.PowerPointSettings != null)
                {
                    MainWindow.Settings.PowerPointSettings.IsShowBottomPPTNavigationPanel = CheckBoxShowPPTNavigationPanelBottom.IsChecked == true;
                    MainWindow.SaveSettingsToFile();
                    _mainWindow?.ReloadSettingsFromSettingsObject();
                }
            }
            catch { }
        }

        private void CheckBoxShowButtonPPTNavigationBottom_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (MainWindow.Settings?.PowerPointSettings != null)
                {
                    MainWindow.Settings.PowerPointSettings.IsShowPPTNavigationBottom = CheckBoxShowButtonPPTNavigationBottom.IsChecked == true;
                    MainWindow.SaveSettingsToFile();
                    _mainWindow?.ReloadSettingsFromSettingsObject();
                }
            }
            catch { }
        }

        private void CheckBoxShowPPTNavigationPanelSide_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (MainWindow.Settings?.PowerPointSettings != null)
                {
                    MainWindow.Settings.PowerPointSettings.IsShowSidePPTNavigationPanel = CheckBoxShowPPTNavigationPanelSide.IsChecked == true;
                    MainWindow.SaveSettingsToFile();
                    _mainWindow?.ReloadSettingsFromSettingsObject();
                }
            }
            catch { }
        }

        private void CheckBoxShowButtonPPTNavigationSides_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (MainWindow.Settings?.PowerPointSettings != null)
                {
                    MainWindow.Settings.PowerPointSettings.IsShowPPTNavigationSides = CheckBoxShowButtonPPTNavigationSides.IsChecked == true;
                    MainWindow.SaveSettingsToFile();
                    _mainWindow?.ReloadSettingsFromSettingsObject();
                }
            }
            catch { }
        }

        private void CheckBoxSupportPowerPoint_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (MainWindow.Settings?.PowerPointSettings != null)
                {
                    MainWindow.Settings.PowerPointSettings.PowerPointSupport = CheckBoxSupportPowerPoint.IsChecked == true;
                    MainWindow.SaveSettingsToFile();
                    _mainWindow?.ReloadSettingsFromSettingsObject();
                }
            }
            catch { }
        }

        private void CheckBoxSupportWPS_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (MainWindow.Settings?.PowerPointSettings != null)
                {
                    MainWindow.Settings.PowerPointSettings.IsSupportWPS = CheckBoxSupportWPS.IsChecked == true;
                    MainWindow.SaveSettingsToFile();
                    _mainWindow?.ReloadSettingsFromSettingsObject();
                }
            }
            catch { }
        }

        private void CheckBoxAutoSaveScreenShotInPowerPoint_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (MainWindow.Settings?.PowerPointSettings != null)
                {
                    MainWindow.Settings.PowerPointSettings.IsAutoSaveScreenShotInPowerPoint = CheckBoxAutoSaveScreenShotInPowerPoint.IsChecked == true;
                    MainWindow.SaveSettingsToFile();
                    _mainWindow?.ReloadSettingsFromSettingsObject();
                }
            }
            catch { }
        }

        private void CheckBoxNotifyHiddenPage_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (MainWindow.Settings?.PowerPointSettings != null)
                {
                    MainWindow.Settings.PowerPointSettings.IsNotifyHiddenPage = CheckBoxNotifyHiddenPage.IsChecked == true;
                    MainWindow.SaveSettingsToFile();
                    _mainWindow?.ReloadSettingsFromSettingsObject();
                }
            }
            catch { }
        }

        private void CheckBoxNotifyAutoPlayPresentation_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (MainWindow.Settings?.PowerPointSettings != null)
                {
                    MainWindow.Settings.PowerPointSettings.IsNotifyAutoPlayPresentation = CheckBoxNotifyAutoPlayPresentation.IsChecked == true;
                    MainWindow.SaveSettingsToFile();
                    _mainWindow?.ReloadSettingsFromSettingsObject();
                }
            }
            catch { }
        }

        protected override void OnPreviewKeyDown(KeyEventArgs e)
        {
            base.OnPreviewKeyDown(e);
            if (e.Key == Key.Escape)
            {
                BtnSkip_Click(this, new RoutedEventArgs());
                e.Handled = true;
            }
        }

        private void CheckBoxShowCursorWizard_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (MainWindow.Settings?.Canvas != null)
                {
                    MainWindow.Settings.Canvas.IsShowCursor = CheckBoxShowCursorWizard.IsChecked == true;
                    MainWindow.SaveSettingsToFile();
                    _mainWindow?.ReloadSettingsFromSettingsObject();
                }
            }
            catch { }
        }

        private void CheckBoxAutoClearOnExitWizard_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (MainWindow.Settings?.Canvas != null)
                {
                    MainWindow.Settings.Canvas.HideStrokeWhenSelecting = CheckBoxAutoClearOnExitWizard.IsChecked == true;
                    MainWindow.SaveSettingsToFile();
                    _mainWindow?.ReloadSettingsFromSettingsObject();
                }
            }
            catch { }
        }

        private void CheckBoxEnableInkToShape_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (MainWindow.Settings?.InkToShape != null)
                {
                    MainWindow.Settings.InkToShape.IsInkToShapeEnabled = CheckBoxEnableInkToShape.IsChecked == true;
                    MainWindow.SaveSettingsToFile();
                    _mainWindow?.ReloadSettingsFromSettingsObject();
                }
            }
            catch { }
        }

        private void CheckBoxInkToShapeTriangle_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (MainWindow.Settings?.InkToShape != null)
                {
                    MainWindow.Settings.InkToShape.IsInkToShapeTriangle = CheckBoxInkToShapeTriangle.IsChecked == true;
                    MainWindow.SaveSettingsToFile();
                    _mainWindow?.ReloadSettingsFromSettingsObject();
                }
            }
            catch { }
        }

        private void CheckBoxInkToShapeRectangle_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (MainWindow.Settings?.InkToShape != null)
                {
                    MainWindow.Settings.InkToShape.IsInkToShapeRectangle = CheckBoxInkToShapeRectangle.IsChecked == true;
                    MainWindow.SaveSettingsToFile();
                    _mainWindow?.ReloadSettingsFromSettingsObject();
                }
            }
            catch { }
        }

        private void CheckBoxAutoStraightenLine_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (MainWindow.Settings?.Canvas != null)
                {
                    MainWindow.Settings.Canvas.AutoStraightenLine = CheckBoxAutoStraightenLine.IsChecked == true;
                    MainWindow.SaveSettingsToFile();
                    _mainWindow?.ReloadSettingsFromSettingsObject();
                }
            }
            catch { }
        }

        private void CheckBoxLineEndpointSnapping_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (MainWindow.Settings?.Canvas != null)
                {
                    MainWindow.Settings.Canvas.LineEndpointSnapping = CheckBoxLineEndpointSnapping.IsChecked == true;
                    MainWindow.SaveSettingsToFile();
                    _mainWindow?.ReloadSettingsFromSettingsObject();
                }
            }
            catch { }
        }

        private void CheckBoxIsSpecialScreen_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (MainWindow.Settings?.Advanced != null)
                {
                    MainWindow.Settings.Advanced.IsSpecialScreen = CheckBoxIsSpecialScreen.IsChecked == true;
                    MainWindow.SaveSettingsToFile();
                    _mainWindow?.ReloadSettingsFromSettingsObject();
                }
            }
            catch { }
        }

        private void SliderTouchMultiplier_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            try
            {
                if (MainWindow.Settings?.Advanced != null)
                {
                    MainWindow.Settings.Advanced.TouchMultiplier = e.NewValue;
                    MainWindow.SaveSettingsToFile();
                    _mainWindow?.ReloadSettingsFromSettingsObject();
                }
            }
            catch { }
        }

        private void CheckBoxIsQuadIR_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (MainWindow.Settings?.Advanced != null)
                {
                    MainWindow.Settings.Advanced.IsQuadIR = CheckBoxIsQuadIR.IsChecked == true;
                    MainWindow.SaveSettingsToFile();
                    _mainWindow?.ReloadSettingsFromSettingsObject();
                }
            }
            catch { }
        }

        private void CheckBoxIsSecondConfimeWhenShutdownApp_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (MainWindow.Settings?.Advanced != null)
                {
                    MainWindow.Settings.Advanced.IsSecondConfimeWhenShutdownApp = CheckBoxIsSecondConfimeWhenShutdownApp.IsChecked == true;
                    MainWindow.SaveSettingsToFile();
                    _mainWindow?.ReloadSettingsFromSettingsObject();
                }
            }
            catch { }
        }

        private void CheckBoxIsEnableSilentRestartOnCrash_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (MainWindow.Settings?.Advanced != null)
                {
                    MainWindow.Settings.Advanced.IsEnableSilentRestartOnCrash = CheckBoxIsEnableSilentRestartOnCrash.IsChecked == true;
                    MainWindow.SaveSettingsToFile();
                    _mainWindow?.ReloadSettingsFromSettingsObject();
                }
            }
            catch { }
        }

        private void SCManipulationBoundaryFeedback(object sender, System.Windows.Input.ManipulationBoundaryFeedbackEventArgs e)
        {
            e.Handled = true;
        }

        #endregion
    }
}
