using Ink_Canvas.Helpers;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;

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

            // 就地更新后的用户文件校验/恢复（便携版更新替换了 exe 目录里的程序文件，
            // 这里确认配置类文件都还在；缺失会自动从更新前的备份恢复）。
            try { AutoUpdateHelper.VerifyUserFilesAfterUpdate(); } catch { }

            // 上次"下载成功但安装没生效"的收尾：作废缓存重试，或判定自动更新不可用。
            // 必须在版本检测之前做，避免刚判定失败又立刻弹一次更新提示。
            List<string> gaveUpVersions = null;
            try { gaveUpVersions = AutoUpdateHelper.HandlePendingInstallAttempts(); } catch { }
            ShowAutoUpdateFailureNotice(gaveUpVersions);

            if (Settings.Startup.IsAutoUpdateWithProxy) AvailableLatestVersion = await AutoUpdateHelper.CheckForUpdates(Settings.Startup.AutoUpdateProxy);
            else AvailableLatestVersion = await AutoUpdateHelper.CheckForUpdates();

            if (AvailableLatestVersion != null)
            {
                // 该版本已被判定为"自动更新反复不生效"（安装目录不可写、安装包被反复拦截等）：
                // 不再每次启动都弹窗，否则用户会陷入"每次都弹、点了也没用"的死循环。
                // 设置页的"立即检查更新"仍可手动重试（会清除该标记并重新下载）。
                if (AutoUpdateHelper.IsVersionBlocked(AvailableLatestVersion))
                {
                    LogHelper.WriteLogToFile(
                        $"AutoUpdate | 版本 {AvailableLatestVersion} 已标记为自动更新失败，跳过启动更新提示（可在设置页手动检查更新）",
                        LogHelper.LogType.Warning);
                    return;
                }

                // 用户此前选择了"忽略此版本"：不再每次启动都打扰。
                // 只在"同一个版本号"上生效 —— 出现更新的版本后照常提示；
                // 设置页的「立即检查更新」是明确的用户动作，不受此标记影响。
                if (IsVersionIgnored(AvailableLatestVersion))
                {
                    LogHelper.WriteLogToFile(
                        $"AutoUpdate | 版本 {AvailableLatestVersion} 已被用户忽略，跳过自动更新提示",
                        LogHelper.LogType.Info);
                    return;
                }

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
                    // 非静默：先询问用户，同意后再后台下载并安装（避免用户不更新也白白下载安装包）。
                    // 三个选项：立即更新 / 稍后再说 / 忽略此版本
                    // （旧版只有"是/否"，想不被反复打扰只能去关掉整个自动更新）。
                    var choice = PromptUpdateChoice(AvailableLatestVersion);
                    if (choice == UpdatePromptChoice.Ignore)
                    {
                        IgnoreUpdateVersion(AvailableLatestVersion, showNotification: true);
                    }
                    else if (choice == UpdatePromptChoice.Update)
                    {
                        // 明确选择更新：清掉旧的"已忽略版本"标记，避免设置页一直显示过期的已忽略版本
                        ClearIgnoredUpdateVersion();

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

        /// <summary>更新询问的三种结果</summary>
        private enum UpdatePromptChoice
        {
            /// <summary>稍后再说（本次不更新，下次仍会提示）</summary>
            Later,
            /// <summary>立即更新</summary>
            Update,
            /// <summary>忽略此版本（在该版本上不再自动提示）</summary>
            Ignore
        }

        /// <summary>
        /// 询问是否更新：立即更新 / 稍后再说 / 忽略此版本。
        ///
        /// 用应用统一风格的自绘弹窗（YesOrNoNotificationWindow 的第三按钮），
        /// 而不是系统 MessageBox —— 系统弹窗只有"是/否"，用户"不想更新但也不想每次被问"
        /// 时没有出口，只能去设置里把整个自动更新关掉，从此再也收不到更新提醒。
        ///
        /// 注意：必须带 Owner（自绘弹窗构造时已绑定主窗口）。主窗口会被定时器周期性置顶，
        /// 无主弹窗会被压到主窗口/设置面板之下，用户根本看不到。
        /// 弹窗异常时退回到系统 MessageBox（仅两个选项），保证更新流程不会因此中断。
        /// </summary>
        private UpdatePromptChoice PromptUpdateChoice(string latestVersion)
        {
            var choice = UpdatePromptChoice.Later;
            try
            {
                var window = new YesOrNoNotificationWindow(
                    $"检测到 Ink Canvas Ultra 新版本 v{latestVersion}，是否立即更新？\n\n"
                    + "选择「忽略此版本」后，在这个版本上不会再自动提示（出现更新的版本时仍会提醒；"
                    + "也可随时在「设置」里手动检查更新）。",
                    yesAction: () => choice = UpdatePromptChoice.Update,
                    noAction: () => choice = UpdatePromptChoice.Later,
                    ignoreAction: () => choice = UpdatePromptChoice.Ignore,
                    yesText: "立即更新",
                    noText: "稍后再说",
                    ignoreText: "忽略此版本");

                // 弹窗构造时已把 Owner 绑到主窗口、并由 PopupWindowLayerHelper 统一管理层级，
                // 因此无论从启动流程还是设置面板里发起，都不会被主窗口/设置面板盖住。
                window.ShowDialog();
            }
            catch (Exception ex)
            {
                LogHelper.WriteLogToFile("PromptUpdateChoice failed, fallback to MessageBox | " + ex.Message, LogHelper.LogType.Warning);
                try
                {
                    return MessageBoxHelper.Show(this, "检测到 Ink Canvas Ultra 新版本，是否立即更新？",
                        "Ink Canvas Ultra New Version Available", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes
                        ? UpdatePromptChoice.Update
                        : UpdatePromptChoice.Later;
                }
                catch
                {
                    return UpdatePromptChoice.Later;
                }
            }
            return choice;
        }

        /// <summary>该版本是否已被用户选择"忽略此版本"。</summary>
        private static bool IsVersionIgnored(string version)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(version)) return false;
                return string.Equals(
                    Settings?.Startup?.IgnoredUpdateVersion?.Trim(),
                    version.Trim(),
                    StringComparison.OrdinalIgnoreCase);
            }
            catch { return false; }
        }

        /// <summary>
        /// 记录"忽略此版本"。只在版本号完全相同时抑制自动提示：
        /// 出现更新的版本（更大）会照常提示，不会"忽略一次就永远收不到更新"。
        /// </summary>
        private void IgnoreUpdateVersion(string version, bool showNotification)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(version)) return;

                Settings.Startup.IgnoredUpdateVersion = version.Trim();
                SaveSettingsToFile();

                LogHelper.WriteLogToFile(
                    $"AutoUpdate | 用户选择忽略版本 {version}，该版本不再自动提示（出现更新版本后照常提示）",
                    LogHelper.LogType.Event);

                if (showNotification)
                {
                    ShowNotificationAsync($"已忽略 v{version} 的更新提示，可在「设置 → 立即检查更新」中随时更新");
                }
            }
            catch { }
        }

        /// <summary>用户明确选择更新时，清掉"已忽略版本"标记。</summary>
        private static void ClearIgnoredUpdateVersion()
        {
            try
            {
                if (string.IsNullOrWhiteSpace(Settings?.Startup?.IgnoredUpdateVersion)) return;
                Settings.Startup.IgnoredUpdateVersion = "";
                SaveSettingsToFile();
            }
            catch { }
        }

        /// <summary>
        /// 自动更新最终判定失败的提示：告诉用户自动更新用不了、去哪里手动下载。
        /// 每个版本只会提示一次（失败标记由 AutoUpdateHelper 维护），避免变成新的启动骚扰。
        /// </summary>
        private void ShowAutoUpdateFailureNotice(List<string> gaveUpVersions)
        {
            if (gaveUpVersions == null || gaveUpVersions.Count == 0) return;

            try
            {
                string versions = string.Join("、", gaveUpVersions);
                LogHelper.WriteLogToFile(
                    $"AutoUpdate | 自动更新连续失败，已停止自动重试：{versions}（手动下载：{AutoUpdateHelper.ManualDownloadPageUrl}）",
                    LogHelper.LogType.Error);

                MessageBoxHelper.Show(
                    $"自动更新连续多次未生效（版本 {versions}），已暂时停止自动更新提示。\n\n"
                    + "请手动下载并安装最新版：\n"
                    + AutoUpdateHelper.ManualDownloadPageUrl
                    + "\n\n（若你用的是便携版，请确认程序所在目录可写；也可在「设置 → 关于/更新」里手动重试）",
                    "Ink Canvas Ultra",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }
            catch { }
        }

        /// <summary>
        /// 版本发生变化时（即软件刚更新完成）展示一次「版本更新」窗口。
        ///
        /// 背景：ChangeLogWindow 从项目建立之初就存在，但**全仓没有任何地方创建过它**
        /// （只有窗口自身与 frametoggle 插件的窗口清单引用过），所以"更新完成后有时看不到
        /// 更新日志窗口"的根因是这条链路根本没接上。
        /// 这里用"上次运行版本 vs 当前版本"来判断是否刚更新过：
        ///   - 没有记录（全新安装）→ 只登记，不弹；
        ///   - 记录与当前一致 → 不弹；
        ///   - 不一致（更新/降级）→ 延迟到界面空闲、且启动弹窗都处理完之后显示一次。
        /// 注意：只有在窗口**真正显示出来之后**才写回版本号，这样启动异常、用户提前退出等
        /// 情况下下次启动仍会重试，不会"这次没弹出来就永远看不到"。
        /// </summary>
        private void TryShowChangeLogOnVersionChange()
        {
            try
            {
                if (Settings?.Startup == null) return;
                // 视频展台等无界面启动模式不弹窗
                if (App.CurrentStartupMode == App.StartupMode.Camera) return;

                string current = AutoUpdateHelper.GetDisplayVersion();
                if (string.IsNullOrWhiteSpace(current)) return;

                string last = Settings.Startup.LastRunVersion;
                if (string.IsNullOrWhiteSpace(last))
                {
                    Settings.Startup.LastRunVersion = current;
                    SaveSettingsToFile();
                    return;
                }

                if (string.Equals(last, current, StringComparison.OrdinalIgnoreCase)) return;

                ShowChangeLogDeferred(last, current);
            }
            catch (Exception ex)
            {
                LogHelper.WriteLogToFile("TryShowChangeLogOnVersionChange failed | " + ex.Message, LogHelper.LogType.Warning);
            }
        }

        /// <summary>延迟显示更新日志窗口：避开启动时的初始化与其它弹窗（恢复会话询问、初始向导、更新询问）。</summary>
        private void ShowChangeLogDeferred(string fromVersion, string toVersion)
        {
            Dispatcher.BeginInvoke(new Action(async () =>
            {
                try
                {
                    // 最多等 10 秒：主界面还在忙 / 有启动弹窗时就多等一会儿
                    for (int i = 0; i < 20; i++)
                    {
                        await Task.Delay(500);
                        if (!IsStartupBlockedByDialog()) break;
                    }

                    var window = new ChangeLogWindow();
                    window.Show();
                    try { Helpers.PopupWindowLayerHelper.BringToFront(window); } catch { }

                    // 真正显示成功后才登记版本
                    Settings.Startup.LastRunVersion = toVersion;
                    SaveSettingsToFile();
                    LogHelper.WriteLogToFile($"ChangeLog window shown ({fromVersion} -> {toVersion})", LogHelper.LogType.Event);
                }
                catch (Exception ex)
                {
                    LogHelper.WriteLogToFile("ShowChangeLogDeferred failed | " + ex.Message, LogHelper.LogType.Warning);
                }
            }), DispatcherPriority.ApplicationIdle);
        }

        /// <summary>
        /// 启动阶段是否还有"会挡住更新日志窗口"的弹窗：
        /// 主窗口被模态对话框禁用（如更新询问 MessageBox），或还开着恢复会话询问/初始向导。
        /// </summary>
        private bool IsStartupBlockedByDialog()
        {
            try
            {
                if (!IsEnabled) return true;
                return Application.Current.Windows.OfType<Window>().Any(
                    w => w.IsVisible && (w is YesOrNoNotificationWindow || w is InitialSetupWindow));
            }
            catch
            {
                return false;
            }
        }
    }
}