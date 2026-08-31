using Ink_Canvas.Helpers;
using System;
using System.Windows;

namespace Ink_Canvas
{
    public partial class MainWindow : Window
    {
        // 启动阶段是否已触发过自动更新检查（避免与设置窗口触发重复）
        private bool _startupUpdateChecked;

        private async void AutoUpdate()
        {
            // 记录本次检测时间（供设置窗口"立即检查更新"旁的提示展示）
            try
            {
                Settings.Startup.LastUpdateCheckTime = DateTime.Now.ToString("yyyy/M/d");
                SaveSettingsToFile();
            }
            catch { }

            if (Settings.Startup.IsAutoUpdateWithProxy) AvailableLatestVersion = await AutoUpdateHelper.CheckForUpdates(Settings.Startup.AutoUpdateProxy);
            else AvailableLatestVersion = await AutoUpdateHelper.CheckForUpdates();

            if (AvailableLatestVersion != null)
            {
                if (Settings.Startup.IsAutoUpdateWithSilence)
                {
                    // 静默更新开启：此时不下载安装包，仅记录待静默安装的版本并启动定时器，
                    // 待静默时段到点后由定时器负责下载并静默安装。
                    // 使用独立字段 _silentInstallVersion，而非共享字段 AvailableLatestVersion：
                    // 后者会被后续检查无条件覆盖（网络异常时为 null），导致定时器拼不出正确安装包路径。
                    _silentInstallVersion = AvailableLatestVersion;
                    timerCheckAutoUpdateWithSilence.Start();
                }
                else
                {
                    // 非静默：先询问用户，同意后再后台下载并安装（避免用户不更新也白白下载安装包）
                    if (MessageBox.Show("检测到 Ink Canvas Ultra 新版本，是否立即更新？", "Ink Canvas Ultra New Version Available", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
                    {
                        bool IsDownloadSuccessful = false;
                        if (Settings.Startup.IsAutoUpdateWithProxy) IsDownloadSuccessful = await AutoUpdateHelper.DownloadSetupFileAndSaveStatus(AvailableLatestVersion, Settings.Startup.AutoUpdateProxy);
                        else IsDownloadSuccessful = await AutoUpdateHelper.DownloadSetupFileAndSaveStatus(AvailableLatestVersion);

                        if (IsDownloadSuccessful)
                        {
                            AutoUpdateHelper.InstallNewVersionApp(AvailableLatestVersion, false);
                        }
                    }
                }
            }
            else
            {
                // 检查返回 null 可能是"无新版本"也可能是"网络异常"。
                // 若存在已下载待静默安装的安装包，不能删除更新目录，否则会破坏排期的静默更新。
                if (!AutoUpdateHelper.HasPendingDownload())
                {
                    AutoUpdateHelper.DeleteUpdatesFolder();
                }
            }
        }
    }
}