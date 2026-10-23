using iNKORE.UI.WPF.Modern;
using Ink_Canvas.Helpers;
using System;
using System.Windows;

namespace Ink_Canvas
{
    /// <summary>
    /// 「设置颜色」功能缺失提示窗：用户点击「设置颜色」入口（或右键/长按色块）时，
    /// 若未安装「自定义画笔颜色」插件，弹此窗口引导安装。
    /// 「是」→ 前往插件工坊；「否」→ 仅关闭提示。
    /// 该窗口带 Windows 原生边框（Mica 背景 + UseModernWindowStyle=False），
    /// 由「窗口边框开关」插件纳入可管理列表（仅当自定义画笔颜色插件已安装时才显示该项）。
    /// </summary>
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
            // 否：不跳转，仅关闭提示
            Close();
        }
    }
}
