using iNKORE.UI.WPF.Modern;
using Ink_Canvas.Helpers;
using System;
using System.Windows;

namespace Ink_Canvas
{
    public partial class YesOrNoNotificationWindow : Window
    {
        private readonly Action _yesAction;
        private readonly Action _noAction;

        public YesOrNoNotificationWindow(string text, Action yesAction = null, Action noAction = null)
        {
            _yesAction = yesAction;
            _noAction = noAction;
            InitializeComponent();
            Label.Text = text;
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

                // 设置所有者窗口，确保对话框始终显示在主窗口之上
                this.Owner = mainWindow;
            }

            // 纳入统一弹出层：Owner 已指向主窗口，这里只补充置顶与层级一致性处理
            Helpers.PopupWindowLayerHelper.Register(this);
        }

        private void ButtonYes_Click(object sender, RoutedEventArgs e)
        {
            if (_yesAction == null)
            {
                Close();
                return;
            }
            try
            {
                _yesAction.Invoke();
            }
            catch (Exception ex)
            {
                // 回调多为 COM/文件操作（如 PPT 跳页），失败不应击穿 UI 事件层导致崩溃
                LogHelper.WriteLogToFile($"YesOrNoNotificationWindow 确认回调执行失败: {ex}", LogHelper.LogType.Error);
            }
            finally
            {
                Close();
            }
        }

        private void ButtonNo_Click(object sender, RoutedEventArgs e)
        {
            if (_noAction == null)
            {
                Close();
                return;
            }
            try
            {
                _noAction.Invoke();
            }
            catch (Exception ex)
            {
                LogHelper.WriteLogToFile($"YesOrNoNotificationWindow 取消回调执行失败: {ex}", LogHelper.LogType.Error);
            }
            finally
            {
                Close();
            }
        }

        private void Window_Closed(object sender, EventArgs e)
        {
            MainWindow.IsShowingRestoreHiddenSlidesWindow = false;
        }
    }
}