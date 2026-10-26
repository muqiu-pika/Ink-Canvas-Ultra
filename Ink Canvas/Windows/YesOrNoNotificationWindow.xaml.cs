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
        private readonly Action _ignoreAction;

        public YesOrNoNotificationWindow(string text, Action yesAction = null, Action noAction = null,
            Action ignoreAction = null, string yesText = null, string noText = null, string ignoreText = null)
        {
            _yesAction = yesAction;
            _noAction = noAction;
            _ignoreAction = ignoreAction;
            InitializeComponent();
            Label.Text = text;

            if (!string.IsNullOrWhiteSpace(yesText)) ButtonYes.Content = yesText;
            if (!string.IsNullOrWhiteSpace(noText)) ButtonNo.Content = noText;
            if (ignoreAction != null)
            {
                ButtonIgnore.Visibility = Visibility.Visible;
                if (!string.IsNullOrWhiteSpace(ignoreText)) ButtonIgnore.Content = ignoreText;
            }

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

                this.Owner = mainWindow;
            }

            // 纳入统一弹出层：Owner 已指向主窗口，这里只补充置顶与层级一致性处理
            Helpers.PopupWindowLayerHelper.Register(this);
        }

        private void ButtonIgnore_Click(object sender, RoutedEventArgs e)
        {
            if (_ignoreAction == null)
            {
                Close();
                return;
            }
            try
            {
                _ignoreAction.Invoke();
            }
            catch (Exception ex)
            {
                LogHelper.WriteLogToFile($"YesOrNoNotificationWindow 忽略回调执行失败: {ex}", LogHelper.LogType.Error);
            }
            finally
            {
                Close();
            }
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