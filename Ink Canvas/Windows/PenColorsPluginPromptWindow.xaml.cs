using iNKORE.UI.WPF.Modern;
using Ink_Canvas.Helpers;
using System;
using System.Windows;

namespace Ink_Canvas
{
    public partial class PenColorsPluginPromptWindow : Window
    {
        private readonly Action _yesAction;

        public PenColorsPluginPromptWindow(Action yesAction = null)
        {
            _yesAction = yesAction;
            InitializeComponent();

            MainWindow mainWindow = Application.Current.MainWindow as MainWindow;
            if (mainWindow != null)
            {
                if (mainWindow.GetMainWindowTheme() == "Light")
                {
                    ThemeManager.SetRequestedTheme(this, ElementTheme.Light);
                }
                else
                {
                    ThemeManager.SetRequestedTheme(this, ElementTheme.Dark);
                }

                // 所有者指向主窗口，确保提示显示在主窗口（含浮动栏）之上
                this.Owner = mainWindow;
            }

            // 纳入统一弹出层：Owner 已指向主窗口，这里补充置顶与层级一致性处理
            Helpers.PopupWindowLayerHelper.Register(this);
        }

        private void ButtonYes_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                _yesAction?.Invoke();
            }
            catch (Exception ex)
            {
                LogHelper.WriteLogToFile($"PenColorsPluginPromptWindow 确认回调执行失败: {ex}", LogHelper.LogType.Error);
            }
            finally
            {
                Close();
            }
        }

        private void ButtonNo_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }
    }
}
