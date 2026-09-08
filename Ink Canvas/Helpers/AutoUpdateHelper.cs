using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;
using System.Reflection;
using System.Windows;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows.Controls;

namespace Ink_Canvas.Helpers
{
    internal class AutoUpdateHelper
    {
        /// <summary>
        /// 程序集版本是四段（末段固定为 0），而版本号命名是三段（年份 + 月份 + 当月第几个版本）。
        /// 比较前统一归一化为三段，避免末段 0 影响比较结果。
        /// </summary>
        private static Version NormalizeToThreeParts(Version version)
        {
            if (version == null) return null;
            return new Version(version.Major, version.Minor, Math.Max(version.Build, 0));
        }

        /// <summary>
        /// 供界面展示的版本号（三段式，如 26.9.1）。
        /// </summary>
        public static string GetDisplayVersion()
        {
            return GetDisplayVersion(Assembly.GetExecutingAssembly().GetName().Version);
        }

        private static string GetDisplayVersion(Version version)
        {
            if (version == null) return "0.0.0";
            return string.Format("{0}.{1}.{2}", version.Major, version.Minor, Math.Max(version.Build, 0));
        }

        /// <summary>
        /// 清理远端版本号文本：它是纯文本文件，可能带 BOM / 首尾空白 / 换行 / v 前缀。
        /// 不清理的话，拼出来的下载地址与状态文件名都会带上脏字符导致更新失败。
        /// </summary>
        private static string SanitizeRemoteVersion(string rawVersion)
        {
            if (string.IsNullOrEmpty(rawVersion)) return null;
            return rawVersion.Trim().Trim('\uFEFF').TrimStart('v', 'V').Trim();
        }

        // 版本文件三源（按优先级）：
        //   1. 自建代理 gh.muqiu.eu.org（国内直连最快）
        //   2. jsDelivr CDN 镜像
        //   3. GitHub RAW 直连（最后兜底；若用户在设置里填了代理前缀，则走代理）
        // 顺序尝试，任一源拿到内容即用；全部失败才算网络异常。
        private const string VersionFileSelfProxy = "https://gh.muqiu.eu.org/gh/raw/muqiu-pika/Ink-Canvas-Ultra/master/AutomaticUpdateVersionControl.txt";
        private const string VersionFileJsDelivr = "https://cdn.jsdelivr.net/gh/muqiu-pika/Ink-Canvas-Ultra@master/AutomaticUpdateVersionControl.txt";
        private const string VersionFileGitHubRaw = "https://raw.githubusercontent.com/muqiu-pika/Ink-Canvas-Ultra/master/AutomaticUpdateVersionControl.txt";

        /// <summary>
        /// 当前生效的 GitHub 代理前缀（设置 → 自动更新 → 代理）。
        /// 开关未开启、或地址为空时返回空串。
        /// 所有"直连 GitHub"的源（Releases 直连 / GitHub RAW）都会套上它。
        /// </summary>
        public static string GetEffectiveProxy()
        {
            try
            {
                var startup = MainWindow.Settings?.Startup;
                if (startup != null &&
                    startup.IsAutoUpdateWithProxy &&
                    !string.IsNullOrWhiteSpace(startup.AutoUpdateProxy))
                {
                    return startup.AutoUpdateProxy.Trim();
                }
            }
            catch { }
            return string.Empty;
        }

        /// <summary>构造版本文件候选源（按优先级）。proxy 只作用于最后的 GitHub RAW 源。</summary>
        private static string[] BuildVersionSources(string proxy)
        {
            return new[]
            {
                VersionFileSelfProxy,
                VersionFileJsDelivr,
                MultiSourceDownloader.WithProxy(VersionFileGitHubRaw, proxy)
            };
        }

        public static async Task<string> CheckForUpdates(string proxy = null)
        {
            var result = await CheckForUpdatesDetailed(proxy);
            return result.HasNewVersion ? result.LatestVersion : null;
        }

        /// <summary>手动检查更新结果：区分"有新版本 / 已是最新 / 网络异常"。</summary>
        public class UpdateCheckResult
        {
            public bool HasNewVersion { get; set; }
            public bool IsNetworkError { get; set; }
            public string LatestVersion { get; set; }
        }

        /// <summary>
        /// 详细版本检测：能区分"确实没有新版本"与"网络异常/远端内容无效"，
        /// 供手动检查使用——避免断网时误报"您已安装最新版"。
        /// 并发合并：启动时可能有多个入口同时触发检测（启动检测 / 设置页 / 定时器），
        /// 在"超时"场景下会并发挂起多个请求、超时后一次性刷出多条 TaskCanceledException。
        /// 这里让同一 proxy 的在途检测共享同一个 Task，真正只发出一次网络请求。
        /// </summary>
        private static readonly object CheckGate = new object();
        private static Task<UpdateCheckResult> _inFlightCheck;
        private static string _inFlightProxy = string.Empty;

        public static Task<UpdateCheckResult> CheckForUpdatesDetailed(string proxy = null)
        {
            string key = proxy ?? string.Empty;
            lock (CheckGate)
            {
                if (_inFlightCheck != null && !_inFlightCheck.IsCompleted && _inFlightProxy == key)
                    return _inFlightCheck;

                _inFlightProxy = key;
                _inFlightCheck = CheckForUpdatesCore(proxy);
                return _inFlightCheck;
            }
        }

        private static async Task<UpdateCheckResult> CheckForUpdatesCore(string proxy = null)
        {
            var result = new UpdateCheckResult();
            try
            {
                // 兜底：调用方没传代理时，仍然沿用设置里的代理，避免漏掉某个入口
                if (string.IsNullOrWhiteSpace(proxy)) proxy = GetEffectiveProxy();

                Version local = NormalizeToThreeParts(Assembly.GetExecutingAssembly().GetName().Version);

                // 三源顺序尝试：自建代理 → jsDelivr → GitHub RAW（可带代理前缀）。
                // 每个源首字节 10 秒、整体 15 秒，超时自动换下一个源。
                var fetched = await MultiSourceDownloader.DownloadTextAsync(BuildVersionSources(proxy));
                string remoteVersion = fetched.Success ? SanitizeRemoteVersion(fetched.Content) : null;

                // 所有源都拿不到 → 网络异常（而非"无更新"）
                if (string.IsNullOrEmpty(remoteVersion))
                {
                    LogHelper.WriteLogToFile($"AutoUpdate | 版本检测失败：{fetched.FailureReason}", LogHelper.LogType.Warning);
                    result.IsNetworkError = true;
                    return result;
                }
                LogHelper.WriteLogToFile($"AutoUpdate | 版本检测成功，使用源：{fetched.UsedUrl}", LogHelper.LogType.Info);

                Version remote;
                if (!Version.TryParse(remoteVersion, out remote))
                {
                    LogHelper.WriteLogToFile($"AutoUpdate | 远端版本号无法解析：{remoteVersion}", LogHelper.LogType.Error);
                    result.IsNetworkError = true;
                    return result;
                }
                remote = NormalizeToThreeParts(remote);

                // 必须按 Version（逐段数值）比较，不能按字符串比较：
                // 字符串比较下 "26.9.1" < "8.0.2"，会把新版本误判成旧版本。
                if (remote > local)
                {
                    LogHelper.WriteLogToFile("AutoUpdate | New version Available: " + remoteVersion);
                    result.HasNewVersion = true;
                    result.LatestVersion = remoteVersion;
                }
                else
                {
                    // 已是最新（HasNewVersion 保持 false）
                }
                return result;
            }
            catch (Exception ex)
            {
                LogHelper.WriteLogToFile($"AutoUpdate | Error: {ex.Message}", LogHelper.LogType.Error);
                result.IsNetworkError = true;
                return result;
            }
        }

        /// <summary>拉取远端版本文件内容，失败返回 null。</summary>
        public static Task<string> GetRemoteVersion(string fileUrl)
        {
            return GetRemoteVersion(fileUrl, false);
        }

        /// <summary>
        /// 拉取远端版本文件内容，失败返回 null。
        /// 设置页"检查代理返回数据"按钮走这里：只测用户填的那一个地址，不做多源回退。
        /// 超时与其它下载保持一致（首字节 10 秒 / 整体 15 秒）。
        /// </summary>
        /// <param name="suppressLog">保留参数，兼容既有调用；失败原因已由下载器统一记录。</param>
        public static async Task<string> GetRemoteVersion(string fileUrl, bool suppressLog)
        {
            if (string.IsNullOrWhiteSpace(fileUrl)) return null;
            var result = await MultiSourceDownloader.DownloadTextAsync(new[] { fileUrl });
            return result.Success ? result.Content : null;
        }

        private static string updatesFolderPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Ink Canvas Ultra", "AutoUpdate");

        public static async Task<bool> DownloadSetupFileAndSaveStatus(string version, string proxy = "", Action<double> progressCallback = null)
        {
            // 状态文件路径按本次调用计算（局部变量），避免与其它并发的下载调用（如静默定时器）
            // 通过共享的 static 字段互相覆盖，导致把成功/失败状态写到对方的文件。
            string statusFile = Path.Combine(updatesFolderPath, $"DownloadV{version}Status.txt");
            try
            {
                // 兜底：调用方没传代理时，仍然沿用设置里的代理
                if (string.IsNullOrWhiteSpace(proxy)) proxy = GetEffectiveProxy();

                if (File.Exists(statusFile) && File.ReadAllText(statusFile).Trim().ToLower() == "true")
                {
                    LogHelper.WriteLogToFile("AutoUpdate | Setup file already downloaded.");
                    progressCallback?.Invoke(100);
                    return true;
                }

                // 安装包多源：自建代理优先，GitHub Releases 直连兜底（若设置了代理则走代理）。
                // 安装包没有 jsDelivr 形式 —— jsDelivr 的 /gh/ 只镜像仓库文件，不镜像 release 资产。
                var sources = new[]
                {
                    $"https://gh.muqiu.eu.org/gh/releases/muqiu-pika/Ink-Canvas-Ultra/v{version}/Ink.Canvas.Ultra.V{version}.Setup.exe",
                    MultiSourceDownloader.WithProxy(
                        $"https://github.com/muqiu-pika/Ink-Canvas-Ultra/releases/download/v{version}/Ink.Canvas.Ultra.V{version}.Setup.exe",
                        proxy)
                };

                SaveDownloadStatus(statusFile, false);
                var result = await MultiSourceDownloader.DownloadFileAsync(
                    sources,
                    Path.Combine(updatesFolderPath, $"Ink.Canvas.Ultra.V{version}.Setup.exe"),
                    progressCallback,
                    largeFile: true);

                if (!result.Success)
                {
                    LogHelper.WriteLogToFile($"AutoUpdate | 安装包下载失败：{result.FailureReason}", LogHelper.LogType.Error);
                    SaveDownloadStatus(statusFile, false);
                    return false;
                }

                SaveDownloadStatus(statusFile, true);
                LogHelper.WriteLogToFile($"AutoUpdate | Setup file successfully downloaded from {result.UsedUrl}");
                return true;
            }
            catch (Exception ex)
            {
                LogHelper.WriteLogToFile($"AutoUpdate | Error downloading and installing update: {ex.Message}", LogHelper.LogType.Error);

                SaveDownloadStatus(statusFile, false);
                return false;
            }
        }

        private static void SaveDownloadStatus(string statusFilePath, bool isSuccess)
        {
            try
            {
                if (string.IsNullOrEmpty(statusFilePath)) return;

                string directory = Path.GetDirectoryName(statusFilePath);
                if (!Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                File.WriteAllText(statusFilePath, isSuccess.ToString());
            }
            catch (Exception ex)
            {
                LogHelper.WriteLogToFile($"AutoUpdate | Error saving download status: {ex.Message}", LogHelper.LogType.Error);
            }
        }

        public static void InstallNewVersionApp(string version, bool isInSilence)
        {
            try
            {
                string setupFilePath = Path.Combine(updatesFolderPath, $"Ink.Canvas.Ultra.V{version}.Setup.exe");

                if (!File.Exists(setupFilePath))
                {
                    LogHelper.WriteLogToFile($"AutoUpdate | Setup file not found: {setupFilePath}", LogHelper.LogType.Error);
                    return;
                }

                // /SILENT、/VERYSILENT：无界面安装。
                // /SUPPRESSMSGBOXES：压制询问/提示框，避免静默安装卡死在确认框。
                // /CLOSEAPPLICATIONS：关闭正在运行的应用（配合 iss 的 CloseApplications），
                //   避免主程序文件被占用导致安装失败。
                // /NORESTART：安装后不自动重启。
                string InstallCommand = $"\"{setupFilePath}\" /SILENT /SUPPRESSMSGBOXES /CLOSEAPPLICATIONS /NORESTART";
                if (isInSilence) InstallCommand += " /VERYSILENT";
                ExecuteCommandLine(InstallCommand);
            }
            catch (Exception ex)
            {
                LogHelper.WriteLogToFile($"AutoUpdate | Error installing update: {ex.Message}", LogHelper.LogType.Error);
            }
        }


        private static void ExecuteCommandLine(string command)
        {
            try
            {
                ProcessStartInfo processStartInfo = new ProcessStartInfo
                {
                    FileName = "cmd.exe",
                    Arguments = $"/c {command}",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                using (Process process = new Process { StartInfo = processStartInfo })
                {
                    process.Start();
                    Application.Current.Shutdown();
                    /*process.WaitForExit();
                    int exitCode = process.ExitCode;*/
                }
            }
            catch { }
        }

        public static void DeleteUpdatesFolder()
        {
            try
            {
                if (Directory.Exists(updatesFolderPath))
                {
                    Directory.Delete(updatesFolderPath, true);
                }
            }
            catch (Exception ex)
            {
                LogHelper.WriteLogToFile($"AutoUpdate clearing| Error deleting updates folder: {ex.Message}", LogHelper.LogType.Error);
            }
        }

        /// <summary>
        /// 是否存在已下载完成、待（静默）安装的安装包（任一 DownloadV*Status.txt 内容为 true）。
        /// 供清理目录前判断：检查返回 null 可能是网络异常，不能据此删除待安装的安装包，
        /// 否则会破坏已排期的静默更新。
        /// </summary>
        public static bool HasPendingDownload()
        {
            try
            {
                if (!Directory.Exists(updatesFolderPath)) return false;
                foreach (string file in Directory.GetFiles(updatesFolderPath, "DownloadV*Status.txt"))
                {
                    try
                    {
                        if (File.ReadAllText(file).Trim().ToLower() == "true") return true;
                    }
                    catch { }
                }
            }
            catch { }
            return false;
        }
    }

    internal class AutoUpdateWithSilenceTimeComboBox
    {
        public static ObservableCollection<string> Hours { get; set; } = new ObservableCollection<string>();
        public static ObservableCollection<string> Minutes { get; set; } = new ObservableCollection<string>();

        public static void InitializeAutoUpdateWithSilenceTimeComboBoxOptions(ComboBox startTimeComboBox, ComboBox endTimeComboBox)
        {
            for (int hour = 0; hour <= 23; ++hour)
            {
                Hours.Add(hour.ToString("00"));
            }
            for (int minute = 0; minute <= 59; minute += 20)
            {
                Minutes.Add(minute.ToString("00"));
            }
            startTimeComboBox.ItemsSource = Hours.SelectMany(h => Minutes.Select(m => $"{h}:{m}"));
            endTimeComboBox.ItemsSource = Hours.SelectMany(h => Minutes.Select(m => $"{h}:{m}"));
        }

        public static bool CheckIsInSilencePeriod(string startTime, string endTime)
        {
            if (startTime == endTime) return true;
            DateTime currentTime = DateTime.Now;

            DateTime StartTime = DateTime.ParseExact(startTime, "HH:mm", null);
            DateTime EndTime = DateTime.ParseExact(endTime, "HH:mm", null);
            if (StartTime <= EndTime)
            { // 单日时间段
                return currentTime >= StartTime && currentTime <= EndTime;
            }
            else
            { // 跨越两天的时间段
                return currentTime >= StartTime || currentTime <= EndTime;
            }
        }
    }
}
