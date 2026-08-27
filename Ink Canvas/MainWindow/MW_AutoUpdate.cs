using Ink_Canvas.Helpers;
using System.Windows;

namespace Ink_Canvas
{
    public partial class MainWindow : Window
    {
        // 启动阶段是否已触发过自动更新检查（避免与设置窗口触发重复）
        private bool _startupUpdateChecked;

        private async void AutoUpdate()
        {
            if (Settings.Startup.IsAutoUpdateWithProxy) AvailableLatestVersion = await AutoUpdateHelper.CheckForUpdates(Settings.Startup.AutoUpdateProxy);
            else AvailableLatestVersion = await AutoUpdateHelper.CheckForUpdates();

            if (AvailableLatestVersion != null)
            {
                bool IsDownloadSuccessful = false;
                if (Settings.Startup.IsAutoUpdateWithProxy) IsDownloadSuccessful = await AutoUpdateHelper.DownloadSetupFileAndSaveStatus(AvailableLatestVersion, Settings.Startup.AutoUpdateProxy);
                else IsDownloadSuccessful = await AutoUpdateHelper.DownloadSetupFileAndSaveStatus(AvailableLatestVersion);

                if (IsDownloadSuccessful)
                {
                    if (!Settings.Startup.IsAutoUpdateWithSilence)
                    {
                        if (MessageBox.Show("Ink Canvas Ultra 新版本安装包已下载完成，是否立即更新？", "Ink Canvas Ultra New Version Available", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
                        {
                            AutoUpdateHelper.InstallNewVersionApp(AvailableLatestVersion, false);
                        }
                    }
                    else
                    {
                        timerCheckAutoUpdateWithSilence.Start();
                    }
                }
            }
            else
            {
                AutoUpdateHelper.DeleteUpdatesFolder();
            }
        }
    }
}