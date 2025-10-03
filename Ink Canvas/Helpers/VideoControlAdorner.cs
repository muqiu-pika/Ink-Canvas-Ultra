using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Threading;

namespace Ink_Canvas.Helpers
{
    public class VideoControlAdorner : Adorner
    {
        private readonly VisualCollection visuals;
        private readonly Grid root;
        private readonly StackPanel bar;
        private readonly Button playPauseBtn;
        private readonly Slider progressSlider;
        private readonly Slider volumeSlider;
        private readonly DispatcherTimer progressTimer;
        private readonly MediaElement media;
        private InkCanvas ownerInkCanvas;
        private bool isDraggingProgress;

        public VideoControlAdorner(MediaElement adornedMedia)
            : base(adornedMedia)
        {
            media = adornedMedia;
            visuals = new VisualCollection(this);

            root = new Grid
            {
                Background = new SolidColorBrush(Color.FromArgb(96, 0, 0, 0)),
                Height = 40,
                VerticalAlignment = VerticalAlignment.Bottom,
                Margin = new Thickness(0),
                Visibility = Visibility.Collapsed
            };

            bar = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(8, 0, 8, 0)
            };

            playPauseBtn = new Button
            {
                Content = "暂停",
                MinWidth = 60,
                Margin = new Thickness(0, 0, 8, 0)
            };
            playPauseBtn.Click += (s, e) => TogglePlayPause();

            progressSlider = new Slider
            {
                Minimum = 0,
                Maximum = 100,
                Width = 200,
                Margin = new Thickness(0, 0, 8, 0)
            };
            progressSlider.PreviewMouseDown += (s, e) => isDraggingProgress = true;
            progressSlider.PreviewMouseUp += (s, e) =>
            {
                isDraggingProgress = false;
                SeekTo(progressSlider.Value);
            };
            progressSlider.ValueChanged += (s, e) =>
            {
                if (isDraggingProgress)
                {
                    SeekTo(progressSlider.Value);
                }
            };

            volumeSlider = new Slider
            {
                Minimum = 0,
                Maximum = 1,
                Width = 100,
                Value = double.IsNaN(media.Volume) ? 0.5 : media.Volume
            };
            volumeSlider.ValueChanged += (s, e) =>
            {
                media.Volume = volumeSlider.Value;
            };

            bar.Children.Add(playPauseBtn);
            bar.Children.Add(progressSlider);
            bar.Children.Add(volumeSlider);
            root.Children.Add(bar);
            visuals.Add(root);

            media.Loaded += Media_Loaded;
            media.MediaOpened += Media_MediaOpened;
            media.MediaEnded += (s, e) => playPauseBtn.Content = "播放";

            progressTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(200) };
            progressTimer.Tick += ProgressTimer_Tick;
        }

        private void Media_Loaded(object sender, RoutedEventArgs e)
        {
            ownerInkCanvas = FindOwnerInkCanvas(media);
            if (ownerInkCanvas != null)
            {
                ownerInkCanvas.SelectionChanged += OwnerInkCanvas_SelectionChanged;
                UpdateVisibilityBySelection();
            }
            UpdatePlayPauseButtonText();
        }

        private void Media_MediaOpened(object sender, RoutedEventArgs e)
        {
            if (media.NaturalDuration.HasTimeSpan)
            {
                var ts = media.NaturalDuration.TimeSpan;
                progressSlider.Maximum = ts.TotalMilliseconds;
                progressTimer.Start();
            }
        }

        private void ProgressTimer_Tick(object sender, EventArgs e)
        {
            if (isDraggingProgress) return;
            if (media.NaturalDuration.HasTimeSpan)
            {
                progressSlider.Value = media.Position.TotalMilliseconds;
            }
            UpdatePlayPauseButtonText();
        }

        private void OwnerInkCanvas_SelectionChanged(object sender, EventArgs e)
        {
            UpdateVisibilityBySelection();
        }

        private void UpdateVisibilityBySelection()
        {
            if (ownerInkCanvas == null)
            {
                root.Visibility = Visibility.Collapsed;
                return;
            }
            var selected = ownerInkCanvas.GetSelectedElements();
            bool isSelected = selected != null && selected.Contains(AdornedElement);
            root.Visibility = isSelected ? Visibility.Visible : Visibility.Collapsed;
        }

        private void TogglePlayPause()
        {
            try
            {
                // media.Tag stores playing state in existing code; keep consistent if possible
                bool isPlaying = media.Tag is bool b && b;
                if (isPlaying)
                {
                    media.Pause();
                    media.Tag = false;
                    playPauseBtn.Content = "播放";
                }
                else
                {
                    media.Play();
                    media.Tag = true;
                    playPauseBtn.Content = "暂停";
                }
            }
            catch { }
        }

        private void SeekTo(double milliseconds)
        {
            if (media.NaturalDuration.HasTimeSpan)
            {
                media.Position = TimeSpan.FromMilliseconds(milliseconds);
            }
        }

        private void UpdatePlayPauseButtonText()
        {
            bool isPlaying = media.Tag is bool b && b;
            playPauseBtn.Content = isPlaying ? "暂停" : "播放";
        }

        protected override int VisualChildrenCount => visuals.Count;

        protected override Visual GetVisualChild(int index) => visuals[index];

        protected override Size ArrangeOverride(Size finalSize)
        {
            double width = AdornedElement.RenderSize.Width;
            root.Width = width;
            root.Arrange(new Rect(new Point(0, AdornedElement.RenderSize.Height - root.Height), new Size(width, root.Height)));
            return finalSize;
        }

        private static InkCanvas FindOwnerInkCanvas(DependencyObject start)
        {
            var current = start;
            while (current != null)
            {
                if (current is InkCanvas ic) return ic;
                current = VisualTreeHelper.GetParent(current);
            }
            return null;
        }
    }
}