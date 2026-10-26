using Ink_Canvas.Helpers;
using iNKORE.UI.WPF.Modern;
using System;
using System.Diagnostics;
using System.Windows;

namespace Ink_Canvas
{
    /// <summary>
    /// Interaction logic for ChangeLogWindow.xaml
    /// </summary>
    public partial class ChangeLogWindow : Window
    {
        public ChangeLogWindow()
        {
            InitializeComponent();
            if (Application.Current.MainWindow is MainWindow mainWindow)
            {
                bool isLight = mainWindow.GetMainWindowTheme() == "Light";
                ThemeManager.SetRequestedTheme(this, isLight ? ElementTheme.Light : ElementTheme.Dark);
                // 去重合并，避免每次打开窗口都往应用级资源里堆一份字典
                ResourceDictionaryHelper.ApplyPopupWindowTheme(isLight);
            }
            // 关闭后清理可视化树（位图/媒体引用）并触发后台 GC，避免内存只增不减
            Helpers.WindowMemoryHelper.ReleaseOnClose(this);
            // 纳入统一弹出层（Owner 绑定主窗口 + 按激活顺序排层）
            Helpers.PopupWindowLayerHelper.Register(this);
        }

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            // 版本号统一按三段式展示（与设置页/自动更新一致），不要直接显示四段的程序集版本
            LabelVersion.Text = "Version: " + Helpers.AutoUpdateHelper.GetDisplayVersion();
        }

        private void Window_Closed(object sender, EventArgs e)
        {

        }

        private void Button_IgnoreUpdate(object sender, RoutedEventArgs e)
        {
            this.Close();
        }

        private void Button_Click(object sender, RoutedEventArgs e)
        {
            if (MessageBoxHelper.Show("是否跳转到官网查看完整更新日志？", "查看更新日志", MessageBoxButton.YesNo, MessageBoxImage.Information) == MessageBoxResult.Yes)
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = "https://doc.muqiu.eu.org/article/changelog/",
                    UseShellExecute = true
                });
            }
            Close();
        }
    }
}
