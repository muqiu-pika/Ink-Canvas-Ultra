using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;
using System.Reflection;
using System.Windows;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows.Controls;
using Microsoft.Win32;

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

        /// <summary>手动下载页（自动更新连续失败时提示用户去这里自取）</summary>
        public const string ManualDownloadPageUrl = "https://github.com/muqiu-pika/Ink-Canvas-Ultra/releases/latest";

        // ===== 安装目录判定（便携版就地更新的关键） =====
        // Inno 安装包的 AppId（见 Ink Canvas Ultra-AnyCPU.iss），_is1 后缀由 Inno 自动追加。
        private const string UninstallKeySubPath =
            @"Software\Microsoft\Windows\CurrentVersion\Uninstall\{69D2B507-FAB8-4C81-A597-F36263C6A25E}_is1";

        /// <summary>当前程序（exe）所在目录，取不到返回 null。</summary>
        private static string GetAppDirectory()
        {
            try
            {
                string location = Assembly.GetExecutingAssembly().Location;
                if (!string.IsNullOrEmpty(location)) return Path.GetDirectoryName(location);
            }
            catch { }

            try
            {
                string baseDir = AppDomain.CurrentDomain.SetupInformation.ApplicationBase;
                if (!string.IsNullOrEmpty(baseDir)) return baseDir.TrimEnd('\\');
            }
            catch { }

            return null;
        }

        /// <summary>读取 Inno 记录的安装目录（InstallLocation）；没装过返回 null。用户级安装写 HKCU。</summary>
        private static string ReadInstalledLocation()
        {
            try
            {
                using (var key = Registry.CurrentUser.OpenSubKey(UninstallKeySubPath))
                {
                    if (key?.GetValue("InstallLocation") is string userLoc && !string.IsNullOrWhiteSpace(userLoc))
                        return userLoc;
                }
            }
            catch { }

            try
            {
                // 管理模式下 Inno 写 HKLM（32 位安装包 → 由 WOW 重定向到 32 位视图，直接按普通路径读即可）
                using (var key = Registry.LocalMachine.OpenSubKey(UninstallKeySubPath))
                {
                    if (key?.GetValue("InstallLocation") is string machineLoc && !string.IsNullOrWhiteSpace(machineLoc))
                        return machineLoc;
                }
            }
            catch { }

            return null;
        }

        private static bool PathEquals(string a, string b)
        {
            if (string.IsNullOrEmpty(a) || string.IsNullOrEmpty(b)) return false;
            try
            {
                return string.Equals(
                    Path.GetFullPath(a).TrimEnd('\\'),
                    Path.GetFullPath(b).TrimEnd('\\'),
                    StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                return string.Equals(a.TrimEnd('\\'), b.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase);
            }
        }

        /// <summary>
        /// 是否需要把安装包的目标目录强制指向"当前程序所在目录"。
        ///
        /// 背景（便携版用户"点了更新、也提示安装了，但每次启动仍弹窗、版本也没变"的根因）：
        /// 安装包的 DefaultDirName 是 %LOCALAPPDATA%\Programs\Ink Canvas Ultra。
        /// 便携版（从 zip 解压运行、从未安装过）静默安装时会装进那个默认目录，
        /// **用户自己那份便携目录里的文件一个都不会被替换**，于是本地版本永远小于远端版本。
        ///
        /// 判定：只有当"Inno 记录的安装目录"就是"当前 exe 所在目录"时，才按普通安装版处理
        /// （此时 UsePreviousAppDir 会自然回到同一个目录）；其余情况一律加 /DIR 指过来，
        /// 这样便携目录会被就地替换，之后 Inno 记录的安装目录也变成该目录，后续更新同样落在原处。
        /// </summary>
        private static bool NeedInstallDirOverride(out string appDir)
        {
            appDir = GetAppDirectory();
            if (string.IsNullOrEmpty(appDir)) return false;
            return !PathEquals(ReadInstalledLocation(), appDir);
        }

        /// <summary>目标目录是否可写（便携目录可能被解压到 Program Files 之类不可写位置）。</summary>
        private static bool CanWriteToDirectory(string dir)
        {
            if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir)) return false;
            string probe = Path.Combine(dir, ".icu_update_write_test_" + Guid.NewGuid().ToString("N") + ".tmp");
            try
            {
                using (var fs = new FileStream(probe, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                {
                    fs.WriteByte(0);
                }
                return true;
            }
            catch
            {
                return false;
            }
            finally
            {
                try { if (File.Exists(probe)) File.Delete(probe); } catch { }
            }
        }

        // ===== 安装包完整性校验 =====

        private const long MinSetupFileSize = 1024 * 1024;   // 安装包约 6.5 MB，1 MB 以下必然是坏的

        /// <summary>
        /// 校验下载到的安装包（作为 MultiSourceDownloader 的 validate 委托）。
        /// 返回 null = 通过，返回字符串 = 失败原因（会换下一个源）。
        /// </summary>
        private static string ValidateSetupFile(string path, string expectedVersion)
        {
            try
            {
                var info = new FileInfo(path);
                if (!info.Exists) return "文件不存在";
                if (info.Length < MinSetupFileSize) return $"文件不完整（仅 {info.Length} 字节）";

                // PE 头：MZ + PE\0\0
                using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
                {
                    if (fs.ReadByte() != 0x4D || fs.ReadByte() != 0x5A) return "不是有效的可执行文件（缺少 MZ 头）";
                }

                // 有版本资源时顺带核对版本号（旧安装包未设 VersionInfoVersion，FileVersion 为空 → 跳过，兼容历史版本）
                try
                {
                    var vi = FileVersionInfo.GetVersionInfo(path);
                    var actual = NormalizeToThreeParts(ParseVersionLoose(vi?.FileVersion));
                    var expected = NormalizeToThreeParts(ParseVersionLoose(expectedVersion));
                    if (actual != null && expected != null && actual != expected)
                    {
                        return $"安装包版本不匹配（安装包 {vi.FileVersion}，期望 {expectedVersion}）";
                    }
                }
                catch { }

                return null;
            }
            catch (Exception ex)
            {
                return ex.Message;
            }
        }

        private static Version ParseVersionLoose(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return null;
            Version v;
            return Version.TryParse(SanitizeRemoteVersion(text), out v) ? v : null;
        }

        // ===== 安装尝试标记 / 自愈 =====
        // 失败形态：安装包被启动过但版本没变化（安装目录不对、文件被占用、安装失败……）。
        // 旧行为对此毫无感知：缓存状态一直是 true → 每次启动都照着弹、照着"装"，用户被卡在循环里。
        // 现在：启动时发现"尝试安装的版本 > 当前版本"即判定未生效 →
        //   第 1 次：作废已下载的安装包与状态文件，强制重新下载（可能换到另一个源）；
        //   第 2 次：判定为自动更新不可用，写标记并提示用户手动下载；此后启动不再重复弹窗。

        private const int MaxInstallAttempts = 2;

        private static string InstallAttemptFile(string version)
            => Path.Combine(updatesFolderPath, $"InstallAttemptV{version}.txt");

        private static string InstallFailedFile(string version)
            => Path.Combine(updatesFolderPath, $"InstallFailedV{version}.txt");

        private static string DownloadStatusFile(string version)
            => Path.Combine(updatesFolderPath, $"DownloadV{version}Status.txt");

        private static string DownloadedSetupFile(string version)
            => Path.Combine(updatesFolderPath, $"Ink.Canvas.Ultra.V{version}.Setup.exe");

        /// <summary>启动安装包前登记一次"安装尝试"（失败自愈靠它判断上一次是否生效）。</summary>
        private static void MarkInstallAttempt(string version)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(version)) return;
                if (!Directory.Exists(updatesFolderPath)) Directory.CreateDirectory(updatesFolderPath);

                int attempts = 0;
                string file = InstallAttemptFile(version);
                try
                {
                    if (File.Exists(file)) int.TryParse(File.ReadAllText(file).Trim(), out attempts);
                }
                catch { }

                File.WriteAllText(file, (attempts + 1).ToString(System.Globalization.CultureInfo.InvariantCulture));
            }
            catch { }
        }

        /// <summary>
        /// 启动时收尾"上次安装尝试"。返回值 = 本次需要提示用户"自动更新失败、请手动下载"的版本列表。
        /// 必须在版本检测之前调用。
        /// </summary>
        public static List<string> HandlePendingInstallAttempts()
        {
            var gaveUp = new List<string>();
            try
            {
                if (!Directory.Exists(updatesFolderPath)) return gaveUp;

                Version local = NormalizeToThreeParts(Assembly.GetExecutingAssembly().GetName().Version);

                // 1) 已经装上去了：清理该版本的尝试/失败标记与安装包缓存
                foreach (string file in Directory.GetFiles(updatesFolderPath, "InstallAttemptV*.txt")
                                             .Concat(Directory.GetFiles(updatesFolderPath, "InstallFailedV*.txt")))
                {
                    try
                    {
                        string version = ExtractVersionFromMarkerFile(file);
                        Version parsed = ParseVersionLoose(version);
                        if (parsed == null || (local != null && NormalizeToThreeParts(parsed) <= local))
                        {
                            // 版本号读不出来，或已经升级到该版本（>= 它）→ 标记与缓存都没用了
                            TryDeleteFileQuietly(file);
                            if (parsed != null)
                            {
                                TryDeleteFileQuietly(DownloadStatusFile(version));
                                TryDeleteFileQuietly(DownloadedSetupFile(version));
                            }
                        }
                    }
                    catch { }
                }

                // 2) 安装了但没生效：作废缓存重试，或判定失败并提示手动下载
                foreach (string file in Directory.GetFiles(updatesFolderPath, "InstallAttemptV*.txt"))
                {
                    string version = ExtractVersionFromMarkerFile(file);
                    Version parsed = ParseVersionLoose(version);
                    if (parsed == null)
                    {
                        TryDeleteFileQuietly(file);
                        continue;
                    }
                    if (local != null && NormalizeToThreeParts(parsed) <= local) continue;   // 已生效（上面已清理）

                    int attempts = 0;
                    try { int.TryParse(File.ReadAllText(file).Trim(), out attempts); } catch { }

                    if (attempts >= MaxInstallAttempts)
                    {
                        // 判定自动更新对该用户不可用：写失败标记 → 之后启动不再自动弹窗
                        try
                        {
                            if (!File.Exists(InstallFailedFile(version)))
                            {
                                File.WriteAllText(InstallFailedFile(version), DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
                            }
                        }
                        catch { }
                        TryDeleteFileQuietly(file);
                        LogHelper.WriteLogToFile(
                            $"AutoUpdate | 版本 {version} 已尝试自动安装 {attempts} 次仍未生效，停止自动重试（请手动下载：{ManualDownloadPageUrl}）",
                            LogHelper.LogType.Error);
                        gaveUp.Add(version);
                    }
                    else
                    {
                        // 第一次没生效：先怀疑"下载到的安装包有问题"（代理/网络半截文件），
                        // 作废缓存重新下载，下次安装会重新走一遍下载 + 校验。
                        TryDeleteFileQuietly(DownloadStatusFile(version));
                        TryDeleteFileQuietly(DownloadedSetupFile(version));
                        LogHelper.WriteLogToFile(
                            $"AutoUpdate | 上次安装版本 {version} 未生效，已作废安装包缓存并将在下次启动重试",
                            LogHelper.LogType.Warning);
                    }
                }
            }
            catch (Exception ex)
            {
                LogHelper.WriteLogToFile($"AutoUpdate | 处理安装尝试标记失败：{ex.Message}", LogHelper.LogType.Warning);
            }
            return gaveUp;
        }

        /// <summary>该版本是否已被判定为"自动更新反复不生效"（此时不再自动弹更新提示）。</summary>
        public static bool IsVersionBlocked(string version)
        {
            try
            {
                return !string.IsNullOrWhiteSpace(version) && File.Exists(InstallFailedFile(version));
            }
            catch { return false; }
        }

        /// <summary>
        /// 用户主动在设置页点"立即检查更新"时调用：清除该版本的失败/尝试标记与安装包缓存，
        /// 让手动重试能真正重新下载安装包（而不是复用上次那份可能已损坏的缓存）。
        /// </summary>
        public static void PrepareManualRetry(string version)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(version)) return;
                TryDeleteFileQuietly(InstallFailedFile(version));
                TryDeleteFileQuietly(InstallAttemptFile(version));
                TryDeleteFileQuietly(DownloadStatusFile(version));
                TryDeleteFileQuietly(DownloadedSetupFile(version));
            }
            catch { }
        }

        private static string ExtractVersionFromMarkerFile(string file)
        {
            try
            {
                string name = Path.GetFileNameWithoutExtension(file);
                int idx = name.LastIndexOf('V');
                return idx >= 0 ? name.Substring(idx + 1) : null;
            }
            catch { return null; }
        }

        private static void TryDeleteFileQuietly(string path)
        {
            try
            {
                if (!string.IsNullOrEmpty(path) && File.Exists(path)) File.Delete(path);
            }
            catch { }
        }

        // ===== 就地更新时的用户文件保护 =====
        // 就地更新 = 用安装包替换 exe 目录里的程序文件。安装包只会"新增/覆盖它自己带的文件"，
        // 不会删除目录里的其它内容（iss 没有 [InstallDelete]），Plugins 目录也在打包时被排除，
        // 所以正常情况下用户的配置、插件、笔迹都不会丢。
        // 这里再加一道保险：更新前把 exe 目录里的用户文件备份一份，更新后启动时校验内容、
        // 缺失就自动恢复 —— 万一日后打包误把 Log.txt / Settings.json 打进安装包，也不会覆盖掉用户配置。

        private static string UpdateBackupRoot
        {
            get
            {
                return Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "Ink Canvas Ultra",
                    "UpdateBackup");
            }
        }

        /// <summary>exe 目录里需要保护的用户文件（顶层，不递归子目录）。</summary>
        private static readonly string[] PortableUserFilePatterns =
        {
            "*.json",          // Settings.json 及同类配置文件（旧版本/便携模式会把设置写在 exe 目录）
            "Names.txt",       // 点名名单
            "Replace.txt",     // 点名替换名单
            "*.exe.config"     // 运行配置（有人会在这里加 runtime/proxy 设置，更新会覆盖它，先留一份副本）
        };

        private static string UpdateBackupDir(string version) => Path.Combine(UpdateBackupRoot, "V" + version);
        private static string UpdateBackupManifest(string version) => Path.Combine(UpdateBackupDir(version), "manifest.txt");

        private static string FileHashOrNull(string path)
        {
            try
            {
                using (var sha = System.Security.Cryptography.SHA256.Create())
                using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                {
                    return BitConverter.ToString(sha.ComputeHash(fs)).Replace("-", string.Empty);
                }
            }
            catch { return null; }
        }

        private static void BackupUserFilesBeforeInPlaceUpdate(string version, string appDir)
        {
            try
            {
                if (string.IsNullOrEmpty(version) || string.IsNullOrEmpty(appDir)) return;

                // 只保留最近一次就地更新的备份：清掉其它版本的备份目录，避免越攒越多
                try
                {
                    if (Directory.Exists(UpdateBackupRoot))
                    {
                        foreach (string old in Directory.GetDirectories(UpdateBackupRoot))
                        {
                            if (!PathEquals(old, UpdateBackupDir(version)))
                            {
                                try { Directory.Delete(old, true); } catch { }
                            }
                        }
                    }
                }
                catch { }

                string dir = UpdateBackupDir(version);
                if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);

                var manifest = new List<string>();
                foreach (string pattern in PortableUserFilePatterns)
                {
                    foreach (string src in Directory.GetFiles(appDir, pattern, SearchOption.TopDirectoryOnly))
                    {
                        try
                        {
                            string name = Path.GetFileName(src);
                            string dst = Path.Combine(dir, name);
                            File.Copy(src, dst, true);
                            string hash = FileHashOrNull(dst);
                            if (hash != null)
                            {
                                // 第三个字段 = 是否参与"更新后内容比对"：
                                // *.exe.config 是安装包自带的运行配置，更新后本来就会被换掉，
                                // 只保留一份旧副本供回溯，不参与比对（避免每次更新都误报"内容不一致"）。
                                bool compare = !name.EndsWith(".exe.config", StringComparison.OrdinalIgnoreCase);
                                manifest.Add(name + "|" + hash + "|" + (compare ? "1" : "0"));
                            }
                        }
                        catch { }
                    }
                }

                if (manifest.Count == 0)
                {
                    // 没有需要保护的用户文件 → 不留下空备份目录
                    try { if (Directory.Exists(dir)) Directory.Delete(dir, true); } catch { }
                    return;
                }

                File.WriteAllLines(UpdateBackupManifest(version), manifest);
                LogHelper.WriteLogToFile($"AutoUpdate | 已备份就地更新前的用户文件 {manifest.Count} 个 → {dir}", LogHelper.LogType.Info);
            }
            catch (Exception ex)
            {
                LogHelper.WriteLogToFile($"AutoUpdate | 备份用户文件失败：{ex.Message}", LogHelper.LogType.Warning);
            }
        }

        /// <summary>
        /// 就地更新后（新版首次启动时）校验 exe 目录里的用户文件：
        /// 缺失 → 从备份恢复；内容被替换 → 保留用户当前文件并把备份留着供排查（不静默回滚用户的较新设置）。
        /// 备份目录保留（下次就地更新时清理旧版本目录），便于用户/技术支持取回更新前的副本。
        /// </summary>
        public static void VerifyUserFilesAfterUpdate()
        {
            try
            {
                if (!Directory.Exists(UpdateBackupRoot)) return;

                string appDir = GetAppDirectory();
                if (string.IsNullOrEmpty(appDir)) return;

                foreach (string manifest in Directory.GetFiles(UpdateBackupRoot, "manifest.txt", SearchOption.AllDirectories))
                {
                    string dir = Path.GetDirectoryName(manifest);
                    bool allVerified = true;

                    try
                    {
                        foreach (string line in File.ReadAllLines(manifest))
                        {
                            string[] parts = line.Split('|');
                            if (parts.Length < 2 || string.IsNullOrWhiteSpace(parts[0])) continue;

                            string name = parts[0];
                            string expectedHash = parts[1];
                            bool compareContent = parts.Length < 3 || parts[2] != "0";
                            string target = Path.Combine(appDir, name);
                            string backupCopy = Path.Combine(dir, name);

                            if (!File.Exists(target))
                            {
                                try
                                {
                                    File.Copy(backupCopy, target, true);
                                    LogHelper.WriteLogToFile($"AutoUpdate | 就地更新后用户文件缺失，已从备份恢复：{target}", LogHelper.LogType.Warning);
                                }
                                catch
                                {
                                    allVerified = false;
                                }
                            }
                            else if (compareContent && !string.Equals(FileHashOrNull(target), expectedHash, StringComparison.OrdinalIgnoreCase))
                            {
                                // 更新后重新写过该文件（或被打包文件覆盖）：保留当前文件，不覆盖，
                                // 备份继续留着并记录路径，便于排查/人工取回。
                                allVerified = false;
                                LogHelper.WriteLogToFile(
                                    $"AutoUpdate | 用户文件内容与更新前备份不一致，已保留当前文件；更新前副本保留在 {backupCopy}",
                                    LogHelper.LogType.Warning);
                            }
                        }
                    }
                    catch { allVerified = false; }

                    // 处理过就不再重复处理（避免每次启动反复记日志）；
                    // 备份目录本身保留下来：万一用户此前自定义过 exe.config / 发现配置不对，
                    // 可以从这里取回更新前的副本。下次就地更新时会自动清掉旧版本的备份目录。
                    TryDeleteFileQuietly(manifest);
                    if (allVerified)
                    {
                        LogHelper.WriteLogToFile($"AutoUpdate | 就地更新后用户文件校验通过（备份保留在 {dir}）", LogHelper.LogType.Info);
                    }
                }
            }
            catch (Exception ex)
            {
                LogHelper.WriteLogToFile($"AutoUpdate | 校验用户文件失败：{ex.Message}", LogHelper.LogType.Warning);
            }
        }

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
                    // 状态是 true 也要确认安装包真的还在磁盘上：
                    // 状态文件残留（文件被清理/被杀软删除/换过版本目录）时必须重新下载，
                    // 否则会出现"提示已下载、安装却什么也没发生"的假成功。
                    if (File.Exists(DownloadedSetupFile(version)))
                    {
                        LogHelper.WriteLogToFile("AutoUpdate | Setup file already downloaded.");
                        progressCallback?.Invoke(100);
                        return true;
                    }

                    LogHelper.WriteLogToFile("AutoUpdate | 下载状态为已完成但安装包缺失，重新下载", LogHelper.LogType.Warning);
                    SaveDownloadStatus(statusFile, false);
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
                    largeFile: true,
                    // 完整性校验：服务器给了 Content-Length 时长度不符、或不是可执行文件、或版本号对不上，
                    // 都算这次源失败（下载器会自动换下一个源）。
                    // 不校验的话，代理/网络半途截断但没抛异常会被当成"下载成功"并写状态 true，
                    // 之后每次更新都会跳过重新下载、去跑一个损坏的安装包 —— 用户就永远卡在"装了也没用"。
                    validate: path => ValidateSetupFile(path, version));

                if (!result.Success)
                {
                    LogHelper.WriteLogToFile($"AutoUpdate | 安装包下载失败：{result.FailureReason}", LogHelper.LogType.Error);
                    SaveDownloadStatus(statusFile, false);
                    return false;
                }

                // 状态文件写入前再复核一次磁盘上的文件（防"状态 true 但文件已被破坏/删除"的残留状态）
                string downloadedReason = ValidateSetupFile(DownloadedSetupFile(version), version);
                if (downloadedReason != null)
                {
                    LogHelper.WriteLogToFile($"AutoUpdate | 安装包校验未通过：{downloadedReason}", LogHelper.LogType.Error);
                    TryDeleteFileQuietly(DownloadedSetupFile(version));
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

                // 启动安装包前先复核一次（防止缓存里躺着损坏的安装包）
                string invalidReason = ValidateSetupFile(setupFilePath, version);
                if (invalidReason != null)
                {
                    LogHelper.WriteLogToFile($"AutoUpdate | 缓存中的安装包不可用（{invalidReason}），已删除并将于下次重新下载", LogHelper.LogType.Warning);
                    TryDeleteFileQuietly(setupFilePath);
                    TryDeleteFileQuietly(DownloadStatusFile(version));
                    return;
                }

                // /SILENT、/VERYSILENT：无界面安装。
                // /SUPPRESSMSGBOXES：压制询问/提示框，避免静默安装卡死在确认框。
                // /CLOSEAPPLICATIONS：关闭正在运行的应用（配合 iss 的 CloseApplications），
                //   避免主程序文件被占用导致安装失败。
                // /NORESTART：安装后不自动重启。
                string InstallCommand = $"\"{setupFilePath}\" /SILENT /SUPPRESSMSGBOXES /CLOSEAPPLICATIONS /NORESTART";
                if (isInSilence) InstallCommand += " /VERYSILENT";

                // ===== 便携版就地更新 =====
                // 安装包的 DefaultDirName 是 %LOCALAPPDATA%\Programs\Ink Canvas Ultra。
                // 便携版（zip 解压即用、从未安装过）如果不指定 /DIR，安装包会装到那个默认目录，
                // 用户自己那份便携目录里的文件一个都不会被替换 —— 现象就是"点了更新、也提示安装了，
                // 但每次启动照样弹、版本号也没变"。这里把目标目录强制指到"当前 exe 所在目录"，
                // 实现真正的就地更新；更新后 Inno 记录的安装目录也变成该目录，之后更新自然落在原处。
                string appDir;
                bool inPlace = NeedInstallDirOverride(out appDir);
                if (inPlace && !string.IsNullOrEmpty(appDir))
                {
                    if (!CanWriteToDirectory(appDir))
                    {
                        LogHelper.WriteLogToFile(
                            $"AutoUpdate | 程序目录不可写，已跳过就地自动更新：{appDir}",
                            LogHelper.LogType.Warning);
                        if (!isInSilence)
                        {
                            MessageBoxHelper.Show(
                                $"当前程序所在目录不可写，无法就地自动更新：\n{appDir}\n\n"
                                + "请把程序移动到可写目录（例如 D 盘的自建文件夹）后重试，"
                                + $"或手动下载最新版：\n{ManualDownloadPageUrl}",
                                "Ink Canvas Ultra",
                                MessageBoxButton.OK,
                                MessageBoxImage.Warning);
                        }
                        return;
                    }

                    // 就地更新前备份 exe 目录里的用户文件（配置等），更新后启动时校验/恢复：
                    // 安装包本身只覆盖"它自己带的程序文件"、不会删除其它文件，
                    // 这一步是兜底，防止某次打包误把 Log.txt / Settings.json 打进去而覆盖用户配置。
                    BackupUserFilesBeforeInPlaceUpdate(version, appDir);

                    // /DIR 指定安装目录；/NOICONS 与取消 desktopicon 任务避免给便携版用户
                    // 额外塞进开始菜单/桌面快捷方式。
                    InstallCommand += $" /DIR=\"{appDir}\" /NOICONS /MERGETASKS=\"!desktopicon\"";
                    LogHelper.WriteLogToFile($"AutoUpdate | 就地更新模式：目标目录 {appDir}", LogHelper.LogType.Info);
                }

                // 登记一次"安装尝试"：下次启动若本地版本仍低于该版本，就判定这次安装没生效，
                // 由 HandlePendingInstallAttempts() 作废缓存重试 / 最终提示手动下载。
                MarkInstallAttempt(version);

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
