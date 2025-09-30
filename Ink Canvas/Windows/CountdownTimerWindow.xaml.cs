﻿using Ink_Canvas.Helpers;
using iNKORE.UI.WPF.Modern;
using System;
using System.Media;
using System.Timers;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;

namespace Ink_Canvas
{
    /// <summary>
    /// Interaction logic for StopwatchWindow.xaml
    /// </summary>
    public partial class CountdownTimerWindow : Window
    {
        public CountdownTimerWindow()
        {
            InitializeComponent();
            AnimationsHelper.ShowWithSlideFromBottomAndFade(this, 0.25);
            MainWindow mainWindow = Application.Current.MainWindow as MainWindow;
            if (mainWindow != null)
            {
                if (mainWindow.GetMainWindowTheme() == "Light")
                {
                    ThemeManager.SetRequestedTheme(this, ElementTheme.Light);
                    ResourceDictionary rd = new ResourceDictionary() { Source = new Uri("Resources/Styles/Light-PopupWindow.xaml", UriKind.Relative) };
                    Application.Current.Resources.MergedDictionaries.Add(rd);
                }
                else
                {
                    ThemeManager.SetRequestedTheme(this, ElementTheme.Dark);
                    ResourceDictionary rd = new ResourceDictionary() { Source = new Uri("Resources/Styles/Dark-PopupWindow.xaml", UriKind.Relative) };
                    Application.Current.Resources.MergedDictionaries.Add(rd);
                }
            }

            timer.Elapsed += Timer_Elapsed;
            timer.Interval = 50;
        }

        private void Timer_Elapsed(object sender, ElapsedEventArgs e)
        {
            if (!isTimerRunning || isPaused)
            {
                timer.Stop();
                return;
            }

            TimeSpan timeSpan = DateTime.Now - startTime;
            TimeSpan totalTimeSpan = new TimeSpan(hour, minute, second);
            TimeSpan leftTimeSpan = totalTimeSpan - timeSpan;
            if (leftTimeSpan.Milliseconds > 0) leftTimeSpan += new TimeSpan(0, 0, 1);
            double spentTimePercent = timeSpan.TotalMilliseconds / (totalSeconds * 1000.0);
            Application.Current.Dispatcher.Invoke(() =>
            {
                ProcessBarTime.CurrentValue = 1 - spentTimePercent;
                TextBlockHour.Text = leftTimeSpan.Hours.ToString("00");
                TextBlockMinute.Text = leftTimeSpan.Minutes.ToString("00");
                TextBlockSecond.Text = leftTimeSpan.Seconds.ToString("00");
                TbCurrentTime.Text = leftTimeSpan.ToString(@"hh\:mm\:ss");
                if (spentTimePercent >= 1)
                {
                    ProcessBarTime.CurrentValue = 0;
                    TextBlockHour.Text = "00";
                    TextBlockMinute.Text = "00";
                    TextBlockSecond.Text = "00";
                    timer.Stop();
                    isTimerRunning = false;
                    SymbolIconStart.Glyph = "\uEdb5";
                    SymbolIconStartCover.Glyph = "\uEdb5";
                    BtnStartCover.Visibility = Visibility.Visible;
                    BorderStopTime.Visibility = Visibility.Collapsed;
                }
            });
            if (spentTimePercent >= 1)
            {
                Application.Current.Dispatcher.Invoke(() =>
                {
                    //Play sound
                    player.Stream = Properties.Resources.TimerDownNotice;
                    player.Play();
                });
            }
        }

        SoundPlayer player = new SoundPlayer();

        int hour = 0;
        int minute = 1;
        int second = 0;
        int totalSeconds = 60;

        DateTime startTime = DateTime.Now;
        DateTime pauseTime = DateTime.Now;

        bool isTimerRunning = false;
        bool isPaused = false;

        Timer timer = new Timer();

        private void Grid_MouseUp(object sender, MouseButtonEventArgs e)
        {
            if (isTimerRunning) return;
            if (ProcessBarTime.Visibility == Visibility.Visible && isTimerRunning == false)
            {
                ProcessBarTime.Visibility = Visibility.Collapsed;
                GridAdjustHour.Visibility = Visibility.Visible;
            }
            else
            {
                ProcessBarTime.Visibility = Visibility.Visible;
                GridAdjustHour.Visibility = Visibility.Collapsed;

                if (hour == 0 && minute == 0 && second == 0)
                {
                    second = 1;
                    TextBlockSecond.Text = second.ToString("00");
                }
            }
        }

        private void Button_Click(object sender, RoutedEventArgs e)
        {
            hour++;
            if (hour >= 100) hour = 0;
            TextBlockHour.Text = hour.ToString("00");
        }

        private void Button_Click_1(object sender, RoutedEventArgs e)
        {
            hour += 5;
            if (hour >= 100) hour = 0;
            TextBlockHour.Text = hour.ToString("00");
        }

        private void Button_Click_2(object sender, RoutedEventArgs e)
        {
            hour--;
            if (hour < 0) hour = 99;
            TextBlockHour.Text = hour.ToString("00");
        }

        private void Button_Click_3(object sender, RoutedEventArgs e)
        {
            hour -= 5;
            if (hour < 0) hour = 99;
            TextBlockHour.Text = hour.ToString("00");
        }

        private void Button_Click_4(object sender, RoutedEventArgs e)
        {
            minute++;
            if (minute >= 60) minute = 0;
            TextBlockMinute.Text = minute.ToString("00");
        }

        private void Button_Click_5(object sender, RoutedEventArgs e)
        {
            minute += 5;
            if (minute >= 60) minute = 0;
            TextBlockMinute.Text = minute.ToString("00");
        }

        private void Button_Click_6(object sender, RoutedEventArgs e)
        {
            minute--;
            if (minute < 0) minute = 59;
            TextBlockMinute.Text = minute.ToString("00");
        }

        private void Button_Click_7(object sender, RoutedEventArgs e)
        {
            minute -= 5;
            if (minute < 0) minute = 59;
            TextBlockMinute.Text = minute.ToString("00");
        }

        private void Button_Click_8(object sender, RoutedEventArgs e)
        {
            second += 5;
            if (second >= 60) second = 0;
            TextBlockSecond.Text = second.ToString("00");
        }

        private void Button_Click_9(object sender, RoutedEventArgs e)
        {
            second++;
            if (second >= 60) second = 0;
            TextBlockSecond.Text = second.ToString("00");
        }

        private void Button_Click_10(object sender, RoutedEventArgs e)
        {
            second--;
            if (second < 0) second = 59;
            TextBlockSecond.Text = second.ToString("00");
        }

        private void Button_Click_11(object sender, RoutedEventArgs e)
        {
            second -= 5;
            if (second < 0) second = 59;
            TextBlockSecond.Text = second.ToString("00");
        }

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            ProcessBarTime.Visibility = Visibility.Visible;
            GridAdjustHour.Visibility = Visibility.Collapsed;
            BorderStopTime.Visibility = Visibility.Collapsed;
        }

        private void BtnFullscreen_MouseUp(object sender, MouseButtonEventArgs e)
        {
            if (WindowState == WindowState.Normal)
            {
                WindowState = WindowState.Maximized;
                SymbolIconFullscreen.Glyph = "\uE73f";
            }
            else
            {
                WindowState = WindowState.Normal;
                SymbolIconFullscreen.Glyph = "\uE740";
            }
        }

        private void BtnReset_MouseUp(object sender, MouseButtonEventArgs e)
        {
            if (!isTimerRunning)
            {
                TextBlockHour.Text = hour.ToString("00");
                TextBlockMinute.Text = minute.ToString("00");
                TextBlockSecond.Text = second.ToString("00");
                BtnResetCover.Visibility = Visibility.Visible;
                BtnStartCover.Visibility = Visibility.Collapsed;
                BorderStopTime.Visibility = Visibility.Collapsed;
                return;
            }
            else if (isTimerRunning && isPaused)
            {
                TextBlockHour.Text = hour.ToString("00");
                TextBlockMinute.Text = minute.ToString("00");
                TextBlockSecond.Text = second.ToString("00");
                BtnResetCover.Visibility = Visibility.Visible;
                BtnStartCover.Visibility = Visibility.Collapsed;
                BorderStopTime.Visibility = Visibility.Collapsed;
                SymbolIconStart.Glyph = "\uEdb5";
                SymbolIconStartCover.Glyph = "\uEdb5";
                isTimerRunning = false;
                timer.Stop();
                isPaused = false;
                ProcessBarTime.CurrentValue = 0;
                ProcessBarTime.IsPaused = false;
            }
            else
            {
                UpdateStopTime();
                startTime = DateTime.Now;
                Timer_Elapsed(timer, null);
            }
        }

        void UpdateStopTime()
        {
            TimeSpan totalTimeSpan = new TimeSpan(hour, minute, second);
            TextBlockStopTime.Text = (startTime + totalTimeSpan).ToString("t");
        }

        private void BtnStart_MouseUp(object sender, MouseButtonEventArgs e)
        {
            if (isPaused && isTimerRunning)
            {
                //继续
                startTime += DateTime.Now - pauseTime;
                ProcessBarTime.IsPaused = false;
                SymbolIconStart.Glyph = "\uEdb4";
                SymbolIconStartCover.Glyph = "\uEdb4";
                isPaused = false;
                timer.Start();
                UpdateStopTime();
                BorderStopTime.Visibility = Visibility.Visible;
            }
            else if (isTimerRunning)
            {
                //暂停
                pauseTime = DateTime.Now;
                ProcessBarTime.IsPaused = true;
                SymbolIconStart.Glyph = "\uEdb5";
                SymbolIconStartCover.Glyph = "\uEdb5";
                BorderStopTime.Visibility = Visibility.Collapsed;
                isPaused = true;
                timer.Stop();
            }
            else
            {
                //从头开始
                startTime = DateTime.Now;
                totalSeconds = ((hour * 60) + minute) * 60 + second;
                ProcessBarTime.IsPaused = false;
                SymbolIconStart.Glyph = "\uEdb4";
                SymbolIconStartCover.Glyph = "\uEdb4";
                BtnResetCover.Visibility = Visibility.Collapsed;

                if (totalSeconds <= 10)
                {
                    timer.Interval = 20;
                }
                else if (totalSeconds <= 60)
                {
                    timer.Interval = 30;
                }
                else if (totalSeconds <= 120)
                {
                    timer.Interval = 50;
                }
                else
                {
                    timer.Interval = 100;
                }

                isPaused = false;
                isTimerRunning = true;
                timer.Start();
                UpdateStopTime();
                BorderStopTime.Visibility = Visibility.Visible;
            }
        }

        private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            isTimerRunning = false;
        }

        private void BtnClose_MouseUp(object sender, MouseButtonEventArgs e)
        {
            Close();
        }

        private bool _isInCompact = false;

        private void BtnMinimal_OnMouseUp(object sender, MouseButtonEventArgs e)
        {
            if (_isInCompact)
            {
                Width = 1100;
                Height = 700;
                BigViewController.Visibility = Visibility.Visible;
                TbCurrentTime.Visibility = Visibility.Collapsed;

                // Set to center
                double dpiScaleX = 1, dpiScaleY = 1;
                PresentationSource source = PresentationSource.FromVisual(this);
                if (source != null)
                {
                    dpiScaleX = source.CompositionTarget.TransformToDevice.M11;
                    dpiScaleY = source.CompositionTarget.TransformToDevice.M22;
                }
                IntPtr windowHandle = new WindowInteropHelper(this).Handle;
                System.Windows.Forms.Screen screen = System.Windows.Forms.Screen.FromHandle(windowHandle);
                double screenWidth = screen.Bounds.Width / dpiScaleX, screenHeight = screen.Bounds.Height / dpiScaleY;
                Left = (screenWidth / 2) - (Width / 2);
                Top = (screenHeight / 2) - (Height / 2);
                Left = (screenWidth / 2) - (Width / 2);
                Top = (screenHeight / 2) - (Height / 2);
            }
            else
            {
                if (WindowState == WindowState.Maximized)
                {
                    WindowState = WindowState.Normal;
                    SymbolIconFullscreen.Glyph = "\uE740";
                }
                Width = 400;
                Height = 250;
                BigViewController.Visibility = Visibility.Collapsed;
                TbCurrentTime.Visibility = Visibility.Visible;
            }

            _isInCompact = !_isInCompact;
        }

        private void WindowDragMove(object sender, MouseEventArgs e)
        {
            if (e.LeftButton == MouseButtonState.Pressed)
                DragMove();
        }
    }
}