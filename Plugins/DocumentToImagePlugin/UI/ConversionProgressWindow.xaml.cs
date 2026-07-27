using System.Windows;
using System.Windows.Media;

namespace Ink_Canvas.Plugins.DocumentToImage.UI
{
    /// <summary>
    /// 文档转换进度窗口：显示当前文件、转换阶段与百分比进度。
    /// </summary>
    public partial class ConversionProgressWindow : Window
    {
        public ConversionProgressWindow(Window owner)
        {
            Owner = owner;
            InitializeComponent();
        }

        public void Report(ConversionProgress progress)
        {
            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.Invoke(() => Report(progress));
                return;
            }

            if (!string.IsNullOrEmpty(progress.FileName))
                FileNameTextBlock.Text = progress.FileName;

            if (progress.Total > 0)
            {
                ProgressBar.IsIndeterminate = false;
                ProgressBar.Maximum = progress.Total;
                ProgressBar.Value = progress.Current;
            }
            else
            {
                ProgressBar.IsIndeterminate = true;
            }

            if (!string.IsNullOrEmpty(progress.Message))
                StatusTextBlock.Text = progress.Message;
        }

        public void SetDone(string message)
        {
            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.Invoke(() => SetDone(message));
                return;
            }

            ProgressBar.IsIndeterminate = false;
            ProgressBar.Value = ProgressBar.Maximum;
            StatusTextBlock.Text = message;
            StatusTextBlock.Foreground = Brushes.Green;
        }

        public void SetError(string message)
        {
            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.Invoke(() => SetError(message));
                return;
            }

            ProgressBar.IsIndeterminate = false;
            ProgressBar.Value = ProgressBar.Maximum;
            StatusTextBlock.Text = $"转换失败: {message}";
            StatusTextBlock.Foreground = Brushes.Red;
        }
    }
}
