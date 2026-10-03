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

        /// <summary>
        /// 通用确认弹窗。
        /// </summary>
        /// <param name="text">提示正文</param>
        /// <param name="yesAction">"是"的回调</param>
        /// <param name="noAction">"否"的回调</param>
        /// <param name="ignoreAction">可选的第三选项回调；传入时才显示「忽略」按钮（默认不显示，既有调用行为不变）</param>
        /// <param name="yesText">自定义"是"的按钮文案</param>
        /// <param name="noText">自定义"否"的按钮文案</param>
        /// <param name="ignoreText">自定义"忽略"的按钮文案</param>
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

                // 设置所有者窗口，确保对话框始终显示在主窗口之上
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