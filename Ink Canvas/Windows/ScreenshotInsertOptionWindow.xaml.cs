using System.Windows;

namespace Ink_Canvas
{
    /// <summary>
    /// ScreenshotInsertOptionWindow.xaml 的交互逻辑
    /// </summary>
    public partial class ScreenshotInsertOptionWindow : Window
    {
        public enum InsertOption
        {
            Cancel,
            InsertToCanvas,
            InsertToBoard,
            /// <summary>
            /// 复制到剪贴板（同时生成图片文件，便于粘贴/复制到其他位置）
            /// </summary>
            CopyToClipboard
        }

        public InsertOption SelectedOption { get; private set; } = InsertOption.Cancel;

        public ScreenshotInsertOptionWindow()
        {
            InitializeComponent();
            // 纳入统一弹出层：跟随主窗口置顶状态，与其它功能窗口表现一致。
            Helpers.PopupWindowLayerHelper.Register(this);
        }

        private void BtnInsertToCanvas_Click(object sender, RoutedEventArgs e)
        {
            SelectedOption = InsertOption.InsertToCanvas;
            DialogResult = true;
            Close();
        }

        private void BtnInsertToBoard_Click(object sender, RoutedEventArgs e)
        {
            SelectedOption = InsertOption.InsertToBoard;
            DialogResult = true;
            Close();
        }

        private void BtnCopyToClipboard_Click(object sender, RoutedEventArgs e)
        {
            SelectedOption = InsertOption.CopyToClipboard;
            DialogResult = true;
            Close();
        }

        private void BtnCancel_Click(object sender, RoutedEventArgs e)
        {
            SelectedOption = InsertOption.Cancel;
            DialogResult = false;
            Close();
        }
    }
}
