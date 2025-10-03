using Ink_Canvas.Helpers;
using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace Ink_Canvas
{
    public partial class MainWindow : Window
    {
        MediaElement selectedMediaElement = null;
        DispatcherTimer videoTimer;
        bool isVideoSeeking = false;
        // 使用 Fluent 字体标准 Play/Pause 图标编码
        readonly string PlayGlyph = "\ue768";  // Play
        readonly string PauseGlyph = "\ue769"; // Pause

        private void InkCanvas_VideoSelectionChanged(object sender, EventArgs e)
        {
            // 查找当前选择中的第一个视频元素
            selectedMediaElement = null;
            try
            {
                List<UIElement> selected = InkCanvasElementsHelper.GetSelectedElements(inkCanvas);
                foreach (UIElement el in selected)
                {
                    if (el is MediaElement me)
                    {
                        selectedMediaElement = me;
                        break;
                    }
                }
            }
            catch { }

            if (selectedMediaElement != null)
            {
                // 初始化滑块值
                try
                {
                    SliderVideoVolume.Value = selectedMediaElement.Volume * 100;
                    if (selectedMediaElement.NaturalDuration.HasTimeSpan)
                    {
                        SliderVideoProgress.Maximum = selectedMediaElement.NaturalDuration.TimeSpan.TotalSeconds;
                        SliderVideoProgress.Value = selectedMediaElement.Position.TotalSeconds;
                        UpdateVideoProgressTuning();
                    }
                    else
                    {
                        SliderVideoProgress.Maximum = 100;
                    }
                }
                catch { }

                // 事件绑定用于适配时长与结束逻辑
                try
                {
                    selectedMediaElement.MediaOpened -= SelectedMediaElement_MediaOpened;
                    selectedMediaElement.MediaOpened += SelectedMediaElement_MediaOpened;
                    selectedMediaElement.MediaEnded -= SelectedMediaElement_MediaEnded;
                    selectedMediaElement.MediaEnded += SelectedMediaElement_MediaEnded;
                }
                catch { }

                // 初始图标：尽量判断当前是否在播放，否则默认显示播放图标
                try
                {
                    if (selectedMediaElement.CanPause &&
                        selectedMediaElement.NaturalDuration.HasTimeSpan &&
                        selectedMediaElement.Position > TimeSpan.Zero &&
                        selectedMediaElement.Position < selectedMediaElement.NaturalDuration.TimeSpan)
                    {
                        IconVideoPlayPause.Glyph = PauseGlyph;
                    }
                    else
                    {
                        IconVideoPlayPause.Glyph = PlayGlyph;
                    }
                }
                catch { IconVideoPlayPause.Glyph = PlayGlyph; }

                BorderVideoSelectionControl.Visibility = Visibility.Visible;
                updateBorderVideoSelectionControlLocation();

                if (videoTimer == null)
                {
                    videoTimer = new DispatcherTimer();
                    videoTimer.Interval = TimeSpan.FromMilliseconds(250);
                    videoTimer.Tick += VideoTimer_Tick;
                }
                videoTimer.Start();
            }
            else
            {
                HideVideoSelectionControl();
            }
        }

        private void updateBorderVideoSelectionControlLocation()
        {
            // 已嵌入到 BorderStrokeSelectionControl 内部，位置随父容器自动变化
            try { BorderVideoSelectionControl.Margin = new Thickness(0, 4, 0, 0); } catch { }
        }

        private void HideVideoSelectionControl()
        {
            try
            {
                BorderVideoSelectionControl.Visibility = Visibility.Collapsed;
                if (videoTimer != null) videoTimer.Stop();

                if (selectedMediaElement != null)
                {
                    try
                    {
                        selectedMediaElement.MediaOpened -= SelectedMediaElement_MediaOpened;
                        selectedMediaElement.MediaEnded -= SelectedMediaElement_MediaEnded;
                    }
                    catch { }
                    selectedMediaElement = null;
                }
                IconVideoPlayPause.Glyph = PlayGlyph;
            }
            catch { }
        }

        private void VideoTimer_Tick(object sender, EventArgs e)
        {
            if (selectedMediaElement == null || isVideoSeeking) return;
            try
            {
                if (selectedMediaElement.NaturalDuration.HasTimeSpan)
                {
                    SliderVideoProgress.Maximum = selectedMediaElement.NaturalDuration.TimeSpan.TotalSeconds;
                    SliderVideoProgress.Value = selectedMediaElement.Position.TotalSeconds;
                }
            }
            catch { }
        }

        private void BtnVideoPlayPause_Click(object sender, RoutedEventArgs e)
        {
            if (selectedMediaElement == null) return;
            try
            {
                // 根据当前图标来切换状态，保证一致的行为
                if (IconVideoPlayPause.Glyph == PlayGlyph)
                {
                    selectedMediaElement.Play();
                    IconVideoPlayPause.Glyph = PauseGlyph;
                }
                else
                {
                    if (selectedMediaElement.CanPause)
                    {
                        selectedMediaElement.Pause();
                    }
                    else
                    {
                        selectedMediaElement.Stop();
                    }
                    IconVideoPlayPause.Glyph = PlayGlyph;
                }
            }
            catch { }
        }

        private void SliderVideoVolume_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (selectedMediaElement == null) return;
            try { selectedMediaElement.Volume = SliderVideoVolume.Value / 100.0; } catch { }
        }

        private void SliderVideoProgress_PreviewMouseDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            isVideoSeeking = true;
        }

        private void SliderVideoProgress_PreviewMouseUp(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            isVideoSeeking = false;
            SliderVideoProgress_ValueChanged(sender, null);
        }

        private void SliderVideoProgress_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (selectedMediaElement == null) return;
            if (!selectedMediaElement.NaturalDuration.HasTimeSpan) return;
            try
            {
                selectedMediaElement.Position = TimeSpan.FromSeconds(SliderVideoProgress.Value);
            }
            catch { }
        }

        private void SelectedMediaElement_MediaOpened(object sender, RoutedEventArgs e)
        {
            try
            {
                if (selectedMediaElement == null) return;
                if (selectedMediaElement.NaturalDuration.HasTimeSpan)
                {
                    SliderVideoProgress.Maximum = selectedMediaElement.NaturalDuration.TimeSpan.TotalSeconds;
                    UpdateVideoProgressTuning();
                }
            }
            catch { }
        }

        private void SelectedMediaElement_MediaEnded(object sender, RoutedEventArgs e)
        {
            try
            {
                IconVideoPlayPause.Glyph = PlayGlyph;
                if (selectedMediaElement.NaturalDuration.HasTimeSpan)
                {
                    SliderVideoProgress.Value = SliderVideoProgress.Maximum;
                }
            }
            catch { }
        }

        private void UpdateVideoProgressTuning()
        {
            try
            {
                // 根据时长优化交互：小步进为 1s，大步进为总时长的 5%（至少 5s）
                SliderVideoProgress.SmallChange = 1;
                SliderVideoProgress.LargeChange = Math.Max(5, SliderVideoProgress.Maximum * 0.05);
                SliderVideoProgress.TickFrequency = Math.Max(1, SliderVideoProgress.Maximum / 100);
            }
            catch { }
        }
    }
}