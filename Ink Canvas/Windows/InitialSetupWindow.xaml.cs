using Ink_Canvas.Helpers;
using iNKORE.UI.WPF.Modern;
using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Animation;

namespace Ink_Canvas
{
    public partial class InitialSetupWindow : Window
    {
        private int _currentStep = 1;
        private readonly MainWindow _mainWindow;

        public InitialSetupWindow()
        {
            InitializeComponent();

            _mainWindow = Application.Current.MainWindow as MainWindow;

            // 进入动画（仿 Windows OOBE 轻微上滑+淡入）
            AnimationsHelper.ShowWithSlideFromBottomAndFade(this, 0.25);

            // 根据主窗口主题应用弹窗样式
            try
            {
                if (_mainWindow != null)
                {
                    if (_mainWindow.GetMainWindowTheme() == "Light")
                    {
                        ThemeManager.SetRequestedTheme(this, ElementTheme.Light);
                        var rd = new ResourceDictionary { Source = new Uri("Resources/Styles/Light-PopupWindow.xaml", UriKind.Relative) };
                        Application.Current.Resources.MergedDictionaries.Add(rd);
                    }
                    else
                    {
                        ThemeManager.SetRequestedTheme(this, ElementTheme.Dark);
                        var rd = new ResourceDictionary { Source = new Uri("Resources/Styles/Dark-PopupWindow.xaml", UriKind.Relative) };
                        Application.Current.Resources.MergedDictionaries.Add(rd);
                    }
                }
            }
            catch { }

            LoadFromSettings();
            UpdateStepVisual();
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
                        if (System.IO.File.Exists(Environment.GetFolderPath(Environment.SpecialFolder.Startup) + "\\Ink Canvas Ultra.lnk"))
                        {
                            CheckBoxRunAtStartup.IsChecked = true;
                        }
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
                    CheckBoxNoFakePressureTriangle.IsChecked = inkToShape.IsInkToShapeNoFakePressureTriangle;
                    CheckBoxNoFakePressureRectangle.IsChecked = inkToShape.IsInkToShapeNoFakePressureRectangle;
                }
                if (canvas != null)
                {
                    CheckBoxAutoStraightenLine.IsChecked = canvas.AutoStraightenLine;
                    CheckBoxLineEndpointSnapping.IsChecked = canvas.LineEndpointSnapping;
                }

                // Step3 - 外观选项
                var appearance = MainWindow.Settings.Appearance;
                if (appearance != null)
                {
                    try { SliderFloatingBarScaleWizard.Value = appearance.FloatingBarScale; } catch { }
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
                    MainWindow.Settings.InkToShape.IsInkToShapeNoFakePressureTriangle = CheckBoxNoFakePressureTriangle.IsChecked == true;
                    MainWindow.Settings.InkToShape.IsInkToShapeNoFakePressureRectangle = CheckBoxNoFakePressureRectangle.IsChecked == true;
                }
                if (MainWindow.Settings.Canvas != null)
                {
                    MainWindow.Settings.Canvas.AutoStraightenLine = CheckBoxAutoStraightenLine.IsChecked == true;
                    MainWindow.Settings.Canvas.LineEndpointSnapping = CheckBoxLineEndpointSnapping.IsChecked == true;
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
            try { SilencePeriodPanel.Visibility = (CheckBoxIsAutoUpdateWithSilence.IsChecked == true) ? Visibility.Visible : Visibility.Collapsed; } catch { }
        }

        private void CheckBoxIsAutoUpdateWithSilence_Click(object sender, RoutedEventArgs e)
        {
            UpdateSilencePeriodVisibility();
        }

        private void BorderCalculateMultiplier_TouchDown(object sender, TouchEventArgs e)
        {
            try
            {
                var args = e.GetTouchPoint(null).Bounds;
                double value = (MainWindow.Settings?.Advanced?.IsQuadIR == true) ? Math.Sqrt(args.Width * args.Height) : args.Width;
                double recommended = 5 / (value * 1.1);
                double min = SliderTouchMultiplier.Minimum, max = SliderTouchMultiplier.Maximum;
                double recommendedClamped = Math.Max(min, Math.Min(max, recommended));

                TextBlockShowCalculatedMultiplierWizard.Text = recommended.ToString("F2");

                var result = MessageBox.Show($"检测到推荐触摸倍数为 {recommended:F2}。\n\n是否启用特殊屏幕并自动调整相关参数？", "应用推荐设置", MessageBoxButton.YesNo, MessageBoxImage.Question);
                if (result == MessageBoxResult.Yes)
                {
                    if (MainWindow.Settings?.Advanced != null)
                    {
                        MainWindow.Settings.Advanced.IsSpecialScreen = true;
                        MainWindow.Settings.Advanced.TouchMultiplier = recommendedClamped;
                        MainWindow.Settings.Advanced.NibModeBoundsWidthThresholdValue = 2.5;
                        MainWindow.Settings.Advanced.FingerModeBoundsWidthThresholdValue = 2.5;
                        MainWindow.Settings.Advanced.NibModeBoundsWidthEraserSize = 0.8;
                        MainWindow.Settings.Advanced.FingerModeBoundsWidthEraserSize = 0.8;
                    }
                    CheckBoxIsSpecialScreen.IsChecked = true;
                    SliderTouchMultiplier.Value = recommendedClamped;
                    MainWindow.SaveSettingsToFile();
                    MessageBox.Show("已应用推荐设置并调整相关参数。", "完成", MessageBoxButton.OK, MessageBoxImage.Information);
                }
            }
            catch { }
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

        private void SliderFloatingBarScaleWizard_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
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

        #endregion

        #region 按钮事件

        private void BtnPrevious_Click(object sender, RoutedEventArgs e)
        {
            if (_currentStep <= 1) return;
            _currentStep--;
            UpdateStepVisual();
        }

        private void BtnNext_Click(object sender, RoutedEventArgs e)
        {
            if (_currentStep < 5)
            {
                _currentStep++;
                UpdateStepVisual();
            }
            else
            {
                // 完成
                SaveToSettings();
                Close();
            }
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

        protected override void OnPreviewKeyDown(KeyEventArgs e)
        {
            base.OnPreviewKeyDown(e);
            if (e.Key == Key.Escape)
            {
                BtnSkip_Click(this, new RoutedEventArgs());
                e.Handled = true;
            }
        }

        #endregion
    }
}
