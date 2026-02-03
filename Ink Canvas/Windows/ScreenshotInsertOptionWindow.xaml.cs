using System.Windows;

namespace Ink_Canvas.Windows
{
    /// <summary>
    /// ScreenshotInsertOptionWindow.xaml 的交互逻辑
    /// </summary>
    public partial class ScreenshotInsertOptionWindow : Window
    {
        /// <summary>
        /// 用户选择的插入选项
        /// </summary>
        public enum InsertOption
        {
            /// <summary>
            /// 取消操作
            /// </summary>
            Cancel,
            /// <summary>
            /// 插入到画板
            /// </summary>
            InsertToCanvas,
            /// <summary>
            /// 插入到白板照片列表
            /// </summary>
            InsertToBoard
        }

        /// <summary>
        /// 获取用户选择的选项
        /// </summary>
        public InsertOption SelectedOption { get; private set; } = InsertOption.Cancel;

        public ScreenshotInsertOptionWindow()
        {
            InitializeComponent();
        }

        /// <summary>
        /// 插入到画板
        /// </summary>
        private void BtnInsertToCanvas_Click(object sender, RoutedEventArgs e)
        {
            SelectedOption = InsertOption.InsertToCanvas;
            DialogResult = true;
            Close();
        }

        /// <summary>
        /// 插入到白板照片列表
        /// </summary>
        private void BtnInsertToBoard_Click(object sender, RoutedEventArgs e)
        {
            SelectedOption = InsertOption.InsertToBoard;
            DialogResult = true;
            Close();
        }

        /// <summary>
        /// 取消操作
        /// </summary>
        private void BtnCancel_Click(object sender, RoutedEventArgs e)
        {
            SelectedOption = InsertOption.Cancel;
            DialogResult = false;
            Close();
        }
    }
}
