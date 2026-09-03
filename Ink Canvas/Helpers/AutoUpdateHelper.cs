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
        /// </summary>
        public static async Task<UpdateCheckResult> CheckForUpdatesDetailed(string proxy = null)
        {
            var result = new UpdateCheckResult();
            try
            {
                Version local = NormalizeToThreeParts(Assembly.GetExecutingAssembly().GetName().Version);
                string remoteAddress = proxy;
                remoteAddress += "https://raw.githubusercontent.com/muqiu-pika/Ink-Canvas-Ultra/master/AutomaticUpdateVersionControl.txt";
                string remoteVersion = SanitizeRemoteVersion(await GetRemoteVersion(remoteAddress));

                // 拉取失败/返回空 → 网络异常（而非"无更新"）
                if (string.IsNullOrEmpty(remoteVersion))
                {
                    LogHelper.WriteLogToFile("Failed to retrieve remote version.", LogHelper.LogType.Error);
                    result.IsNetworkError = true;
                    return result;
                }

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

        public static async Task<string> GetRemoteVersion(string fileUrl)
        {
            using (HttpClient client = new HttpClient())
            {
                try
                {
                    client.Timeout = TimeSpan.FromSeconds(15);
                    HttpResponseMessage response = await client.GetAsync(fileUrl);
                    response.EnsureSuccessStatusCode();

                    return await response.Content.ReadAsStringAsync();
                }
                catch (HttpRequestException ex)
                {
                    LogHelper.WriteLogToFile($"AutoUpdate | HTTP request error: {ex.Message}", LogHelper.LogType.Error);
                }
                catch (Exception ex)
                {
                    LogHelper.WriteLogToFile($"AutoUpdate | Error: {ex.Message}", LogHelper.LogType.Error);
                }

                return null;
            }
        }

        private static string updatesFolderPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Ink Canvas Ultra", "AutoUpdate");

        public static async Task<bool> DownloadSetupFileAndSaveStatus(string version, string proxy = "", Action<double> progressCallback = null)
        {
            // 状态文件路径按本次调用计算（局部变量），避免与其它并发的下载调用（如静默定时器）
            // 通过共享的 static 字段互相覆盖，导致把成功/失败状态写到对方的文件。
            string statusFile = Path.Combine(updatesFolderPath, $"DownloadV{version}Status.txt");
            try
            {
                if (File.Exists(statusFile) && File.ReadAllText(statusFile).Trim().ToLower() == "true")
                {
                    LogHelper.WriteLogToFile("AutoUpdate | Setup file already downloaded.");
                    progressCallback?.Invoke(100);
                    return true;
                }

                string downloadUrl = $"{proxy}https://github.com/muqiu-pika/Ink-Canvas-Ultra/releases/download/v{version}/Ink.Canvas.Ultra.V{version}.Setup.exe";

                SaveDownloadStatus(statusFile, false);
                await DownloadFile(downloadUrl, $"{updatesFolderPath}\\Ink.Canvas.Ultra.V{version}.Setup.exe", progressCallback);
                SaveDownloadStatus(statusFile, true);

                LogHelper.WriteLogToFile("AutoUpdate | Setup file successfully downloaded.");
                return true;
            }
            catch (Exception ex)
            {
                LogHelper.WriteLogToFile($"AutoUpdate | Error downloading and installing update: {ex.Message}", LogHelper.LogType.Error);

                SaveDownloadStatus(statusFile, false);
                return false;
            }
        }

        private static async Task DownloadFile(string fileUrl, string destinationPath, Action<double> progressCallback = null)
        {
            using (HttpClient client = new HttpClient())
            {
                try
                {
                    // 安装包可达几十 MB：下载统一放宽到 30 分钟，不再与"是否传进度回调"绑定。
                    // 此前静默/自动更新不传进度回调、只有 15 秒超时，大包几乎必然 TaskCanceledException。
                    client.Timeout = TimeSpan.FromMinutes(30);

                    using (HttpResponseMessage response = await client.GetAsync(fileUrl, HttpCompletionOption.ResponseHeadersRead))
                    {
                        response.EnsureSuccessStatusCode();
                        long totalBytes = response.Content.Headers.ContentLength ?? -1;

                        using (FileStream fileStream = File.Create(destinationPath))
                        using (Stream contentStream = await response.Content.ReadAsStreamAsync())
                        {
                            byte[] buffer = new byte[81920];
                            long receivedBytes = 0;
                            int read;
                            while ((read = await contentStream.ReadAsync(buffer, 0, buffer.Length)) > 0)
                            {
                                await fileStream.WriteAsync(buffer, 0, read);
                                receivedBytes += read;
                                if (progressCallback != null)
                                {
                                    // 服务器未给出总长度时回调 -1（由调用方决定如何展示）
                                    double pct = totalBytes > 0 ? (double)receivedBytes / totalBytes * 100.0 : -1;
                                    progressCallback(pct);
                                }
                            }
                            fileStream.Close();
                        }
                    }
                }
                catch (HttpRequestException ex)
                {
                    LogHelper.WriteLogToFile($"AutoUpdate | HTTP request error: {ex.Message}", LogHelper.LogType.Error);
                    throw;
                }
                catch (Exception ex)
                {
                    LogHelper.WriteLogToFile($"AutoUpdate | Error: {ex.Message}", LogHelper.LogType.Error);
                    throw;
                }
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
