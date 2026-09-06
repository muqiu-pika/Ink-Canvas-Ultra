using Ink_Canvas.Helpers;
using iNKORE.UI.WPF.Modern;
using System;
using System.Windows;
using System.Windows.Input;

namespace Ink_Canvas
{
    /// <summary>
    /// Interaction logic for StopwatchWindow.xaml
    /// </summary>
    public partial class OperatingGuideWindow : Window
    {
        public OperatingGuideWindow()
        {
            InitializeComponent();
            AnimationsHelper.ShowWithSlideFromBottomAndFade(this, 0.25);
            MainWindow mainWindow = Application.Current.MainWindow as MainWindow;
            if (mainWindow != null)
            {
                bool isLight = mainWindow.GetMainWindowTheme() == "Light";
                ThemeManager.SetRequestedTheme(this, isLight ? ElementTheme.Light : ElementTheme.Dark);
                // 去重合并，避免每次打开窗口都往应用级资源里堆一份字典
                ResourceDictionaryHelper.ApplyPopupWindowTheme(isLight);
            }

            // 纳入统一弹出层（Owner 绑定主窗口 + 按激活顺序排层）
            Helpers.PopupWindowLayerHelper.Register(this);
        }

        private void BtnClose_MouseUp(object sender, MouseButtonEventArgs e)
        {
            Close();
        }

        private void WindowDragMove(object sender, MouseEventArgs e)
        {
            if (e.LeftButton == MouseButtonState.Pressed) DragMove();
        }

        private void SCManipulationBoundaryFeedback(object sender, ManipulationBoundaryFeedbackEventArgs e) {
            e.Handled = true;
        }
    }
}