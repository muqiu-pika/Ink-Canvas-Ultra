using Ink_Canvas.Helpers;
using iNKORE.UI.WPF.Modern;
using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Animation;
using System.Windows.Media;

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
                    CheckBoxNoFakePressureTriangle.IsChecked = inkToShape.IsInkToShapeNoFakePressureTriangle;
                    CheckBoxNoFakePressureRectangle.IsChecked = inkToShape.IsInkToShapeNoFakePressureRectangle;
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
                        if (v > 0 && v <= 3)
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
                    MainWindow.Settings.InkToShape.IsInkToShapeNoFakePressureTriangle = CheckBoxNoFakePressureTriangle.IsChecked == true;
                    MainWindow.Settings.InkToShape.IsInkToShapeNoFakePressureRectangle = CheckBoxNoFakePressureRectangle.IsChecked == true;
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

        private bool _isCongratsShown = false;

        private void ShowCongratsPage()
        {
            try
            {
                _isCongratsShown = true;
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

        private void LockNav(int milliseconds = 320)
        {
            try
            {
                _navLocked = true;
                if (BtnNext != null) BtnNext.IsEnabled = false;
                if (BtnPrevious != null) BtnPrevious.IsEnabled = false;
                var t = new System.Windows.Threading.DispatcherTimer
                {
                    Interval = TimeSpan.FromMilliseconds(milliseconds)
                };
                t.Tick += (s, e) =>
                {
                    try
                    {
                        _navLocked = false;
                        if (BtnNext != null) BtnNext.IsEnabled = true;
                        if (BtnPrevious != null) BtnPrevious.IsEnabled = _currentStep > 1 ? Visibility.Visible == BtnPrevious.Visibility : false;
                    }
                    catch { }
                    if (s is System.Windows.Threading.DispatcherTimer dt) dt.Stop();
                };
                t.Start();
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

        private void CheckBoxNoFakePressureTriangle_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (MainWindow.Settings?.InkToShape != null)
                {
                    MainWindow.Settings.InkToShape.IsInkToShapeNoFakePressureTriangle = CheckBoxNoFakePressureTriangle.IsChecked == true;
                    MainWindow.SaveSettingsToFile();
                    _mainWindow?.ReloadSettingsFromSettingsObject();
                }
            }
            catch { }
        }

        private void CheckBoxNoFakePressureRectangle_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (MainWindow.Settings?.InkToShape != null)
                {
                    MainWindow.Settings.InkToShape.IsInkToShapeNoFakePressureRectangle = CheckBoxNoFakePressureRectangle.IsChecked == true;
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

        #endregion
    }
}
