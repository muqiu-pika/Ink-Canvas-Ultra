using System;
using System.Windows;
using Ink_Canvas.Plugins;

namespace Ink_Canvas
{
    /// <summary>
    /// 插件所需 Windows 系统组件缺失时的引导安装页：
    /// 一键提权安装（UAC + DISM）→ 复检；成功则关闭并放行插件安装；
    /// 失败（用户取消授权 / DISM 失败）→ 显示「设备不支持」，插件安装中止。
    /// </summary>
    public partial class PluginSystemRequirementWindow : Window
    {
        /// <summary>引导安装成功（组件已就绪）时为 true，插件工坊据此放行安装。</summary>
        public bool Installed { get; private set; }

        private readonly string _component;
        private readonly string _installCommand;
        private bool _busy;

        public PluginSystemRequirementWindow(PluginSystemCheckResult check, string pluginName)
        {
            InitializeComponent();

            _component = check.Component ?? "";
            _installCommand = check.InstallCommand ?? "";

            TitleText.Text = "插件「" + (pluginName ?? "") + "」需要 Windows 系统组件";
            DetailText.Text = "该插件的手写识别功能依赖 " + (check.DisplayName ?? _component)
                + "。\n当前状态：" + (string.IsNullOrEmpty(check.Summary) ? "未安装" : check.Summary)
                + "。\n\n点击下方按钮授权安装（需管理员权限，将从 Windows 更新下载组件），"
                + "安装完成后将继续安装插件。";
            MethodText.Text = "也可手动安装：以管理员身份运行\n    " + _installCommand
                + "\n或在「设置 → 应用 → 可选功能」中搜索并安装。";
        }

        private void BtnElevatedInstall_Click(object sender, RoutedEventArgs e)
        {
            if (_busy) return;
            _busy = true;
            BtnElevatedInstall.IsEnabled = false;
            ProgressText.Text = "正在安装（请在弹出的授权窗口中确认）…";

            string component = _component;
            var worker = new System.ComponentModel.BackgroundWorker();
            worker.DoWork += (s, we) =>
            {
                // 提权 DISM 在新进程中完成，阻塞等待放在后台线程，避免卡住 UI
                bool ok = PluginSystemRequirements.TryElevatedInstall(component, out var error);
                we.Result = new Tuple<bool, string>(ok, error);
            };
            worker.RunWorkerCompleted += (s, args) =>
            {
                _busy = false;
                BtnElevatedInstall.IsEnabled = true;

                if (args.Error != null)
                {
                    ProgressText.Text = "安装过程出错：" + args.Error.Message;
                    return;
                }
                var result = (Tuple<bool, string>)args.Result;
                if (result.Item1)
                {
                    Installed = true;
                    Close();
                    return;
                }

                // C. 存在缺失且无法安装：提示设备不支持，插件安装将被中止
                ProgressText.Text = "安装未能完成：" + result.Item2
                    + "\n该设备当前不支持本插件的系统识别功能，插件安装已取消。";
            };
            worker.RunWorkerAsync();
        }

        private void BtnOpenSettings_Click(object sender, RoutedEventArgs e)
        {
            PluginSystemRequirements.OpenWindowsSettings();
        }

        private void BtnCopyCommand_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                Clipboard.SetText(_installCommand);
                ProgressText.Text = "安装命令已复制到剪贴板";
            }
            catch { }
        }

        private void BtnCancel_Click(object sender, RoutedEventArgs e)
        {
            Installed = false;
            Close();
        }
    }
}
