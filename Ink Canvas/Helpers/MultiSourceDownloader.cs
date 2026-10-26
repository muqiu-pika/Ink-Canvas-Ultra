using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Ink_Canvas.Helpers
{
    internal static class MultiSourceDownloader
    {
        public const int FirstByteTimeoutSeconds = 10;

        /// <summary>小文件（版本文件 / 市场目录 / 插件包）整体超时，秒。</summary>
        public const int SmallFileTotalTimeoutSeconds = 15;

        /// <summary>传输停滞超时，秒：连续这么久没收到任何数据就放弃当前源。</summary>
        public const int StallTimeoutSeconds = 30;

        public const int LargeFileTotalTimeoutMinutes = 30;

        /// <summary>
        /// 共享 HttpClient。每次请求 new 一个 HttpClient 会堆积 TIME_WAIT 套接字，
        /// 更新检查 / 插件刷新反复触发时尤其明显，因此这里全局复用一个实例。
        /// </summary>
        private static readonly HttpClient SharedClient = CreateSharedClient();

        private static HttpClient CreateSharedClient()
        {
            var client = new HttpClient();
            // 超时由每个请求的 CancellationToken 分阶段控制，这里必须设为无限，
            // 否则 HttpClient.Timeout 会先一步把大文件下载掐断。
            client.Timeout = Timeout.InfiniteTimeSpan;
            try
            {
                client.DefaultRequestHeaders.Add("User-Agent", "InkCanvasUltra");
            }
            catch { }
            return client;
        }

        /// <summary>需要走代理的 GitHub 自有域名。</summary>
        private static readonly string[] GitHubHosts =
        {
            "github.com",
            "www.github.com",
            "raw.githubusercontent.com",
            "objects.githubusercontent.com",
            "codeload.github.com"
        };

        public static string WithProxy(string url, string proxy)
        {
            if (string.IsNullOrWhiteSpace(url) || string.IsNullOrWhiteSpace(proxy)) return url;
            try
            {
                var uri = new Uri(url);
                for (int i = 0; i < GitHubHosts.Length; i++)
                {
                    if (string.Equals(uri.Host, GitHubHosts[i], StringComparison.OrdinalIgnoreCase))
                    {
                        return proxy + url;
                    }
                }
            }
            catch
            {
                // URL 解析失败就原样返回，由后续下载阶段报错
            }
            return url;
        }

        internal sealed class TextDownloadResult
        {
            public bool Success { get; set; }
            public string Content { get; set; }
            public string UsedUrl { get; set; }
            public string FailureReason { get; set; }
        }

        internal sealed class FileDownloadResult
        {
            public bool Success { get; set; }
            public string UsedUrl { get; set; }
            public string FailureReason { get; set; }
            public int AttemptedSources { get; set; }

            /// <summary>服务器给出的总长度（-1 = 未知）。</summary>
            public long ExpectedBytes { get; set; } = -1;

            public long ReceivedBytes { get; set; }
        }

        /// <summary>
        /// 依次尝试各源下载文本。返回首个成功的源的内容；全部失败时 Success=false。
        /// </summary>
        public static async Task<TextDownloadResult> DownloadTextAsync(IEnumerable<string> urls)
        {
            var lastReason = "没有可用的下载源";
            int attempted = 0;

            foreach (string url in urls)
            {
                if (string.IsNullOrWhiteSpace(url)) continue;
                attempted++;

                var sw = Stopwatch.StartNew();
                try
                {
                    string content;
                    // ① 首字节：10 秒内拿不到响应头就换源
                    using (var headCts = new CancellationTokenSource(TimeSpan.FromSeconds(FirstByteTimeoutSeconds)))
                    using (HttpResponseMessage response = await SharedClient
                        .GetAsync(url, HttpCompletionOption.ResponseHeadersRead, headCts.Token)
                        .ConfigureAwait(false))
                    {
                        response.EnsureSuccessStatusCode();

                        // ② 整体：剩余预算 = 15 秒 - 已用时间
                        long remainMs = SmallFileTotalTimeoutSeconds * 1000 - sw.ElapsedMilliseconds;
                        if (remainMs <= 0) throw new TimeoutException("整体超时（首字节阶段已耗尽预算）");

                        using (var bodyCts = new CancellationTokenSource(TimeSpan.FromMilliseconds(remainMs)))
                        using (Stream stream = await response.Content.ReadAsStreamAsync().ConfigureAwait(false))
                        using (var buffer = new MemoryStream())
                        {
                            byte[] chunk = new byte[8192];
                            int read;
                            while ((read = await stream.ReadAsync(chunk, 0, chunk.Length, bodyCts.Token).ConfigureAwait(false)) > 0)
                            {
                                buffer.Write(chunk, 0, read);
                            }
                            // 远端文本文件可能带 BOM，统一去掉，避免拼 URL / 解析 JSON 时踩坑
                            content = Encoding.UTF8.GetString(buffer.ToArray()).Trim('\uFEFF');
                        }
                    }

                    return new TextDownloadResult { Success = true, Content = content, UsedUrl = url };
                }
                catch (OperationCanceledException)
                {
                    lastReason = "超时（10 秒未响应或 15 秒内未读完）";
                }
                catch (TimeoutException ex)
                {
                    lastReason = ex.Message;
                }
                catch (HttpRequestException ex)
                {
                    lastReason = "HTTP 错误：" + ex.Message;
                }
                catch (Exception ex)
                {
                    lastReason = ex.Message;
                }

                LogHelper.WriteLogToFile($"下载源失败 [{url}]：{lastReason}", LogHelper.LogType.Warning);
            }

            return new TextDownloadResult
            {
                Success = false,
                FailureReason = attempted == 0 ? "没有可用的下载源" : $"全部 {attempted} 个源均失败：{lastReason}"
            };
        }

        public static async Task<FileDownloadResult> DownloadFileAsync(
            IEnumerable<string> urls,
            string destinationPath,
            Action<double> progressCallback = null,
            bool largeFile = false,
            Func<string, string> validate = null,
            Action<int, int> onSourceChanged = null)
        {
            var sources = new List<string>();
            foreach (string u in urls)
            {
                if (!string.IsNullOrWhiteSpace(u) && !sources.Contains(u)) sources.Add(u);
            }
            if (sources.Count == 0)
            {
                return new FileDownloadResult { Success = false, FailureReason = "没有可用的下载源" };
            }

            var lastReason = "未知错误";

            for (int i = 0; i < sources.Count; i++)
            {
                string url = sources[i];
                if (onSourceChanged != null) onSourceChanged(i + 1, sources.Count);

                var sw = Stopwatch.StartNew();
                bool wroteAnything = false;
                long totalBytes = -1;
                long receivedBytes = 0;
                try
                {
                    using (var headCts = new CancellationTokenSource(TimeSpan.FromSeconds(FirstByteTimeoutSeconds)))
                    using (HttpResponseMessage response = await SharedClient
                        .GetAsync(url, HttpCompletionOption.ResponseHeadersRead, headCts.Token)
                        .ConfigureAwait(false))
                    {
                        response.EnsureSuccessStatusCode();
                        totalBytes = response.Content.Headers.ContentLength ?? -1;

                        // ② 整体：大文件 30 分钟；小文件 15 秒（扣除已用时间）
                        long budgetMs = largeFile
                            ? LargeFileTotalTimeoutMinutes * 60 * 1000
                            : SmallFileTotalTimeoutSeconds * 1000;
                        long remainMs = budgetMs - sw.ElapsedMilliseconds;
                        if (remainMs <= 0) throw new TimeoutException("整体超时");

                        using (var overallCts = new CancellationTokenSource(TimeSpan.FromMilliseconds(remainMs)))
                        // ③ 停滞：连续 30 秒没有新数据就放弃本源
                        using (var stallCts = new CancellationTokenSource())
                        using (Stream stream = await response.Content.ReadAsStreamAsync().ConfigureAwait(false))
                        using (FileStream fileStream = File.Create(destinationPath))
                        {
                            stallCts.CancelAfter(TimeSpan.FromSeconds(StallTimeoutSeconds));

                            byte[] buffer = new byte[81920];
                            int read;
                            while ((read = await stream.ReadAsync(buffer, 0, buffer.Length, stallCts.Token).ConfigureAwait(false)) > 0)
                            {
                                await fileStream.WriteAsync(buffer, 0, read).ConfigureAwait(false);
                                receivedBytes += read;
                                wroteAnything = true;

                                // 只要还在进数据就重置停滞计时（CancelAfter 重复调用会重新计时）
                                stallCts.CancelAfter(TimeSpan.FromSeconds(StallTimeoutSeconds));

                                if (progressCallback != null && totalBytes > 0)
                                {
                                    progressCallback((double)receivedBytes / totalBytes * 100.0);
                                }
                            }
                        }
                    }

                    // 完整性：服务器给了 Content-Length 时，实际收到的字节数必须完全一致。
                    // 代理/网络半途断开、源端提前结束流（都不抛异常）都表现为"读完了但字节不够"，
                    // 这种情况绝不能算成功 —— 否则会缓存一个损坏的文件、还把状态写成"已下载"，
                    // 之后每次更新都跳过重新下载去跑这个坏文件（用户表现为"装了也没用、还一直弹"）。
                    if (totalBytes > 0 && receivedBytes != totalBytes)
                    {
                        lastReason = $"文件不完整：收到 {receivedBytes}/{totalBytes} 字节";
                        LogHelper.WriteLogToFile($"下载源失败 [{url}]：{lastReason}", LogHelper.LogType.Warning);
                        TryDeleteFile(destinationPath);
                        continue;
                    }

                    // 下载完成后的自定义校验（如 SHA256）；不通过则换源
                    if (validate != null)
                    {
                        string reason = validate(destinationPath);
                        if (reason != null)
                        {
                            lastReason = reason;
                            LogHelper.WriteLogToFile($"下载源校验未通过 [{url}]：{reason}", LogHelper.LogType.Warning);
                            TryDeleteFile(destinationPath);
                            continue;
                        }
                    }

                    return new FileDownloadResult
                    {
                        Success = true,
                        UsedUrl = url,
                        AttemptedSources = i + 1,
                        ExpectedBytes = totalBytes,
                        ReceivedBytes = receivedBytes
                    };
                }
                catch (OperationCanceledException)
                {
                    lastReason = "超时（10 秒未响应 / 30 秒无数据 / 超出整体时限）";
                }
                catch (TimeoutException ex)
                {
                    lastReason = ex.Message;
                }
                catch (HttpRequestException ex)
                {
                    lastReason = "HTTP 错误：" + ex.Message;
                }
                catch (IOException ex)
                {
                    lastReason = "写入文件失败：" + ex.Message;
                }
                catch (Exception ex)
                {
                    lastReason = ex.Message;
                }

                LogHelper.WriteLogToFile($"下载源失败 [{url}]：{lastReason}", LogHelper.LogType.Warning);
                // 只清理"我们写到一半"的文件，避免误删无关文件
                if (wroteAnything || File.Exists(destinationPath)) TryDeleteFile(destinationPath);
            }

            return new FileDownloadResult
            {
                Success = false,
                FailureReason = $"全部 {sources.Count} 个源均失败：{lastReason}",
                AttemptedSources = sources.Count
            };
        }

        private static void TryDeleteFile(string path)
        {
            try
            {
                if (!string.IsNullOrEmpty(path) && File.Exists(path)) File.Delete(path);
            }
            catch { }
        }
    }
}
