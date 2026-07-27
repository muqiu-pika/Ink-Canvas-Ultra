using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Security.Cryptography;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Ink_Canvas.Helpers;
using Ink_Canvas.Plugins;
using Microsoft.Win32;

namespace Ink_Canvas
{
    public partial class PluginWorkshopWindow : Window
    {
        // ===== 单例：避免重复打开多个插件工坊窗口 =====
        private static PluginWorkshopWindow _instance;
        private static readonly object _instanceLock = new object();

        /// <summary>获取或创建当前唯一的插件工坊窗口实例。若已存在则激活已有窗口。</summary>
        public static PluginWorkshopWindow GetOrCreate(Window owner)
        {
            lock (_instanceLock)
            {
                if (_instance != null)
                {
                    try
                    {
                        // 已存在实例，激活并前置
                        // 窗口本身已设置 Topmost=True（与 MainWindow/MW_Settings 一致），
                        // 因此 Activate() 能让它在 Topmost 窗口堆栈中浮到 MainWindow 之上
                        if (_instance.WindowState == WindowState.Minimized)
                            _instance.WindowState = WindowState.Normal;
                        _instance.Activate();
                        _instance.Focus();
                        return _instance;
                    }
                    catch
                    {
                        // 实例可能已被关闭但未触发 Closed 清理，重建
                        _instance = null;
                    }
                }

                _instance = new PluginWorkshopWindow();
                if (owner != null)
                {
                    _instance.Owner = owner;
                }
                _instance.Closed += (s, e) =>
                {
                    // 解除 PluginListChanged 订阅
                    try
                    {
                        var host = PluginHost.Instance;
                        if (host != null) host.PluginListChanged -= _instance.OnPluginListChanged;
                    }
                    catch { }
                    lock (_instanceLock) { _instance = null; }
                };
                return _instance;
            }
        }

        /// <summary>当前是否已有插件工坊窗口实例</summary>
        public static bool HasInstance => _instance != null;

        // plugin 存放目录（位于程序运行目录下的 Plugins 文件夹）
        private static string PluginDirectory => App.RootPath + "Plugins\\";

        // .icplugin 安装包扩展名
        private const string PluginFileExtension = ".icplugin";

        // 在线插件商店目录地址（按优先级尝试）
        private static readonly string[] MarketSources = new[]
        {
            "https://plugin.muqiu.eu.org/v1/market.json",          // EdgeOne Pages
            "https://cdn.jsdelivr.net/gh/muqiu-pika/Ink-Canvas-Ultra-Plugin@main/market/v1/market.json" // jsDelivr 回退
        };

        // 最近一次获取到的在线插件列表
        private List<OnlinePluginInfo> _availablePlugins = new List<OnlinePluginInfo>();

        public PluginWorkshopWindow()
        {
            InitializeComponent();
            Loaded += PluginWorkshopWindow_Loaded;
        }

        private void PluginWorkshopWindow_Loaded(object sender, RoutedEventArgs e)
        {
            // 订阅 PluginHost 的列表变化事件，自动刷新
            try
            {
                var host = PluginHost.Instance;
                if (host != null)
                {
                    host.PluginListChanged += OnPluginListChanged;
                }
            }
            catch { }

            RefreshPluginList(silent: true);
        }

        private void OnPluginListChanged(object sender, EventArgs e)
        {
            // 在 UI 线程刷新列表
            Dispatcher.Invoke(() => RefreshPluginList(silent: true));
        }

        private void BtnClose_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        /// <summary>返回设置：关闭插件工坊后重新打开设置窗口</summary>
        private void BtnBackToSettings_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                Close();

                // 通过 MainWindow 重新打开设置窗口（与浮动栏齿轮按钮逻辑一致）
                if (Application.Current.MainWindow is MainWindow mw)
                {
                    mw.Dispatcher.Invoke(() =>
                    {
                        var existing = Application.Current.Windows.OfType<MW_Settings>().FirstOrDefault();
                        if (existing != null)
                        {
                            existing.Activate();
                            return;
                        }
                        var settingsWindow = new MW_Settings
                        {
                            Owner = mw
                        };
                        settingsWindow.Show();
                    });
                }
            }
            catch (Exception ex)
            {
                LogHelper.WriteLogToFile($"返回设置失败: {ex.Message}", LogHelper.LogType.Error);
            }
        }

        private void SCManipulationBoundaryFeedback(object sender, ManipulationBoundaryFeedbackEventArgs e)
        {
            // 抑制边界反馈，避免窗口整体位移
            e.Handled = true;
        }

        // ===== 从本地安装 .icplugin =====

        private void BtnPluginInstallFromLocal_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var dialog = new OpenFileDialog
                {
                    Title = "选择 plugin 安装包",
                    Filter = $"Ink Canvas plugin (*{PluginFileExtension})|*{PluginFileExtension}|所有文件 (*.*)|*.*",
                    Multiselect = false,
                    CheckFileExists = true,
                    CheckPathExists = true
                };

                if (dialog.ShowDialog() != true)
                {
                    return;
                }

                BeginInstallPluginFromFile(dialog.FileName);
            }
            catch (Exception ex)
            {
                LogHelper.WriteLogToFile($"从本地安装 plugin 失败: {ex.Message}", LogHelper.LogType.Error);
                ShowInlineMessage("安装失败：" + ex.Message);
            }
        }

        /// <summary>
        /// 开始安装指定的 .icplugin 文件。
        /// .icplugin 是一个 ZIP 格式的安装包，安装时会解压到 Plugins\&lt;包名&gt;\ 子目录。
        /// 若已存在同名 plugin 目录，会弹窗询问是否覆盖（先卸载旧版再删除目录）。
        /// </summary>
        private void BeginInstallPluginFromFile(string sourceFile)
        {
            if (!File.Exists(sourceFile))
            {
                ShowInlineMessage("所选文件不存在");
                return;
            }

            string ext = Path.GetExtension(sourceFile);
            if (!string.Equals(ext, PluginFileExtension, StringComparison.OrdinalIgnoreCase))
            {
                ShowInlineMessage($"仅支持 *{PluginFileExtension} 格式的 plugin 安装包");
                return;
            }

            string fileNameWithoutExt = Path.GetFileNameWithoutExtension(sourceFile);
            string destDir = Path.Combine(PluginDirectory, fileNameWithoutExt);
            bool exists = Directory.Exists(destDir);

            if (exists)
            {
                // 询问是否覆盖
                var confirm = new YesOrNoNotificationWindow(
                    $"已存在同名 plugin \"{fileNameWithoutExt}\"，是否覆盖安装？",
                    yesAction: () => DoInstallPluginFromFile(sourceFile, destDir, overwrite: true),
                    noAction: () => ShowInlineMessage("已取消安装"));
                confirm.Owner = this;
                confirm.ShowDialog();
                return;
            }

            DoInstallPluginFromFile(sourceFile, destDir, overwrite: false);
        }

        /// <summary>
        /// 将 .icplugin (ZIP) 解压到目标目录，并立即加载到 PluginHost。
        /// </summary>
        private void DoInstallPluginFromFile(string sourceFile, string destDir, bool overwrite)
        {
            try
            {
                var host = PluginHost.Instance;

                if (!Directory.Exists(PluginDirectory))
                    Directory.CreateDirectory(PluginDirectory);

                // 覆盖时：先软卸载旧版（解绑事件），再删除旧目录
                if (overwrite && Directory.Exists(destDir))
                {
                    // 尝试读取旧 manifest 的 id，软卸载
                    var oldManifest = TryReadManifest(destDir);
                    if (oldManifest != null && host != null)
                    {
                        host.UnloadPlugin(oldManifest.Id);
                    }
                    Directory.Delete(destDir, recursive: true);
                }

                // .icplugin 是 ZIP 格式，使用 System.IO.Compression 解压
                // 注意：.NET Framework 4.7.2 的 ExtractToDirectory 不支持 overwriteFiles 参数，
                // 因此覆盖安装时已在上方删除旧目录，这里目标目录必然为空，可直接解压。
                if (!Directory.Exists(destDir))
                    Directory.CreateDirectory(destDir);

                System.IO.Compression.ZipFile.ExtractToDirectory(sourceFile, destDir);

                // 校验解压后是否包含 plugin.icplugin 清单
                string manifestPath = Path.Combine(destDir, "plugin.icplugin");
                if (!File.Exists(manifestPath))
                {
                    // 清理无效安装
                    try { Directory.Delete(destDir, recursive: true); } catch { }
                    ShowInlineMessage("安装包无效：缺少 plugin.icplugin 清单文件");
                    return;
                }

                string fileName = Path.GetFileName(sourceFile);
                LogHelper.WriteLogToFile($"plugin 已安装: {fileName} -> {destDir}", LogHelper.LogType.Event);

                // 立即加载到 PluginHost（无需重启）
                var manifest = TryReadManifest(destDir);
                if (manifest != null && host != null)
                {
                    // 默认启用新装的 plugin
                    host.SetPluginEnabled(manifest.Id, true);
                    ShowInlineMessage($"plugin 已安装并启用：{manifest.Name}");
                }
                else
                {
                    ShowInlineMessage($"plugin 已安装：{Path.GetFileNameWithoutExtension(fileName)}（请手动启用）");
                }

                RefreshPluginList(silent: true);
            }
            catch (Exception ex)
            {
                LogHelper.WriteLogToFile($"解压 plugin 安装包失败: {ex.Message}", LogHelper.LogType.Error);
                ShowInlineMessage("安装失败：" + ex.Message);
            }
        }

        // ===== 刷新 plugin 列表 =====

        private void BtnPluginRefresh_Click(object sender, RoutedEventArgs e)
        {
            RefreshPluginList(silent: false);
        }

        private async void RefreshPluginList(bool silent)
        {
            try
            {
                if (!Directory.Exists(PluginDirectory))
                    Directory.CreateDirectory(PluginDirectory);

                var host = PluginHost.Instance;
                IReadOnlyList<InstalledPluginInfo> installed;
                if (host != null)
                {
                    installed = host.GetAllInstalledPlugins();
                }
                else
                {
                    // PluginHost 未初始化时回退到目录扫描
                    var dirs = Directory.GetDirectories(PluginDirectory, "*", SearchOption.TopDirectoryOnly)
                                         .Where(d => File.Exists(Path.Combine(d, "plugin.icplugin")))
                                         .ToList();
                    installed = dirs.Select(d => new InstalledPluginInfo
                    {
                        Manifest = TryReadManifest(d),
                        Directory = d,
                        IsEnabled = true,
                        IsLoaded = false
                    }).Where(x => x.Manifest != null).ToList();
                }

                int count = installed.Count;
                if (TextBlockPluginCount != null)
                    TextBlockPluginCount.Text = $"已安装 plugin：{count}";

                RenderInstalledPlugins(installed);

                // 同步加载在线插件商店目录
                if (!silent)
                    ShowInlineMessage("正在加载在线插件列表...");
                await RefreshAvailablePluginsAsync(installed);

                if (!silent)
                    ShowInlineMessage("plugin 列表已刷新");
            }
            catch (Exception ex)
            {
                LogHelper.WriteLogToFile($"刷新 plugin 列表失败: {ex.Message}", LogHelper.LogType.Error);
                ShowInlineMessage("刷新 plugin 列表失败：" + ex.Message);
            }
        }

        /// <summary>从多个市场源获取在线插件目录，并渲染「可安装」区域。</summary>
        private async Task RefreshAvailablePluginsAsync(IReadOnlyList<InstalledPluginInfo> installed)
        {
            _availablePlugins = new List<OnlinePluginInfo>();
            Exception lastError = null;

            foreach (var url in MarketSources)
            {
                try
                {
                    using (var client = new WebClient())
                    {
                        client.Encoding = System.Text.Encoding.UTF8;
                        string json = await client.DownloadStringTaskAsync(new Uri(url));
                        var catalog = Newtonsoft.Json.JsonConvert.DeserializeObject<OnlinePluginCatalog>(json);
                        _availablePlugins = catalog?.Plugins ?? new List<OnlinePluginInfo>();
                        if (_availablePlugins.Count > 0)
                        {
                            LogHelper.WriteLogToFile($"在线插件目录加载成功: {url}", LogHelper.LogType.Info);
                            break;
                        }
                    }
                }
                catch (Exception ex)
                {
                    lastError = ex;
                    LogHelper.WriteLogToFile($"在线插件目录源失败 [{url}]: {ex.Message}", LogHelper.LogType.Warning);
                }
            }

            if (_availablePlugins.Count == 0 && lastError != null)
            {
                LogHelper.WriteLogToFile($"获取在线 plugin 目录失败: {lastError.Message}", LogHelper.LogType.Error);
                ShowInlineMessage("获取在线插件列表失败，请检查网络连接。");
            }

            Dispatcher.Invoke(() => RenderAvailablePlugins(installed));
        }

        private void RenderInstalledPlugins(IReadOnlyList<InstalledPluginInfo> installed)
        {
            if (PanelInstalledPlugins == null) return;

            PanelInstalledPlugins.Children.Clear();

            if (installed.Count == 0)
            {
                PanelInstalledPlugins.Children.Add(new TextBlock
                {
                    Text = "暂无已安装的 plugin",
                    FontSize = 13,
                    Foreground = TryFindResource("SettingsPageAnnotationForeground") as Brush,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    Margin = new Thickness(0, 16, 0, 16)
                });
                return;
            }

            foreach (var info in installed)
            {
                PanelInstalledPlugins.Children.Add(
                    BuildPluginItem(info));
            }
        }

        private UIElement BuildPluginItem(InstalledPluginInfo info)
        {
            var manifest = info.Manifest;
            string displayName = manifest?.Name ?? Path.GetFileName(info.Directory);
            string pluginId = manifest?.Id ?? Path.GetFileName(info.Directory);
            string version = manifest?.Version ?? "";

            var border = new Border
            {
                Margin = new Thickness(0, 0, 0, 6),
                Padding = new Thickness(10, 8, 10, 8),
                BorderBrush = TryFindResource("PopupWindowBorderBrush") as Brush,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(6)
            };

            var update = FindOnlineUpdate(info);

            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            if (update != null)
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var titlePanel = new StackPanel
            {
                Orientation = Orientation.Vertical
            };

            var title = new TextBlock
            {
                Text = displayName,
                FontSize = 14,
                FontWeight = FontWeights.Bold,
                Foreground = TryFindResource("PopupWindowForeground") as Brush
            };
            titlePanel.Children.Add(title);

            // 显示 plugin ID 和版本
            var subtitle = new TextBlock
            {
                Text = string.IsNullOrEmpty(version) ? pluginId : $"{pluginId}  v{version}",
                FontSize = 11,
                Foreground = TryFindResource("SettingsPageAnnotationForeground") as Brush
            };
            titlePanel.Children.Add(subtitle);

            Grid.SetColumn(titlePanel, 0);
            grid.Children.Add(titlePanel);

            // 状态标签
            var statusTag = new Border
            {
                Padding = new Thickness(8, 2, 8, 2),
                Background = TryFindResource("PopupWindowDarkBlueBorderBackground") as Brush,
                CornerRadius = new CornerRadius(4),
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(8, 0, 0, 0)
            };
            var statusText = new TextBlock
            {
                Text = info.IsLoaded ? "运行中" : (info.IsEnabled ? "已启用" : "已禁用"),
                FontSize = 11,
                Foreground = TryFindResource("PopupWindowDarkBlueBorderForeground") as Brush
            };
            statusTag.Child = statusText;
            Grid.SetColumn(statusTag, 1);
            grid.Children.Add(statusTag);

            // 启用/禁用开关
            var toggle = new CheckBox
            {
                IsChecked = info.IsEnabled,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(12, 0, 0, 0),
                ToolTip = "启用 / 禁用此 plugin（立即生效）"
            };
            toggle.Checked += (s, e) => OnPluginToggleChanged(pluginId, true);
            toggle.Unchecked += (s, e) => OnPluginToggleChanged(pluginId, false);
            Grid.SetColumn(toggle, 2);
            grid.Children.Add(toggle);

            // 在线有更新时显示「更新」按钮
            if (update != null)
            {
                var updateBtn = new Button
                {
                    Content = "更新",
                    Width = 60,
                    Height = 32,
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(12, 0, 0, 0),
                    ToolTip = $"更新到 v{update.Version}"
                };
                updateBtn.Click += async (s, e) => await InstallOnlinePluginAsync(update);
                Grid.SetColumn(updateBtn, 3);
                grid.Children.Add(updateBtn);
            }

            border.Child = grid;
            return border;
        }

        /// <summary>插件开关切换：立即加载或软卸载</summary>
        private void OnPluginToggleChanged(string pluginId, bool enabled)
        {
            try
            {
                var host = PluginHost.Instance;
                if (host == null)
                {
                    ShowInlineMessage("PluginHost 未初始化");
                    return;
                }
                host.SetPluginEnabled(pluginId, enabled);
                ShowInlineMessage(enabled ? $"已启用 plugin：{pluginId}" : $"已禁用 plugin：{pluginId}");
                RefreshPluginList(silent: true);
            }
            catch (Exception ex)
            {
                LogHelper.WriteLogToFile($"切换 plugin 状态失败: {ex.Message}", LogHelper.LogType.Error);
                ShowInlineMessage("切换失败：" + ex.Message);
            }
        }

        // ===== 在线插件商店 =====

        /// <summary>查找指定已安装插件是否有可用在线更新。</summary>
        private OnlinePluginInfo FindOnlineUpdate(InstalledPluginInfo installed)
        {
            if (installed?.Manifest == null) return null;
            var online = _availablePlugins.FirstOrDefault(p =>
                string.Equals(p.Id, installed.Manifest.Id, StringComparison.OrdinalIgnoreCase));
            if (online == null) return null;
            return IsNewerVersion(online.Version, installed.Manifest.Version) ? online : null;
        }

        /// <summary>比较版本号，判断 onlineVersion 是否比 installedVersion 新。</summary>
        private static bool IsNewerVersion(string onlineVersion, string installedVersion)
        {
            if (string.IsNullOrWhiteSpace(onlineVersion)) return false;
            if (string.IsNullOrWhiteSpace(installedVersion)) return true;
            if (Version.TryParse(onlineVersion, out var onlineV) && Version.TryParse(installedVersion, out var installedV))
            {
                return onlineV > installedV;
            }
            return !string.Equals(onlineVersion, installedVersion, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>渲染「可安装」区域：过滤掉已安装且为最新版本的插件。</summary>
        private void RenderAvailablePlugins(IReadOnlyList<InstalledPluginInfo> installed)
        {
            if (PanelAvailablePlugins == null) return;

            PanelAvailablePlugins.Children.Clear();

            var installedIds = new HashSet<string>(installed.Select(i => i.Manifest?.Id ?? Path.GetFileName(i.Directory)), StringComparer.OrdinalIgnoreCase);
            var available = _availablePlugins.Where(p => !installedIds.Contains(p.Id)).ToList();

            if (available.Count == 0)
            {
                PanelAvailablePlugins.Children.Add(new TextBlock
                {
                    Text = "暂无可安装的在线 plugin（或已全部安装）",
                    FontSize = 13,
                    Foreground = TryFindResource("SettingsPageAnnotationForeground") as Brush,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    Margin = new Thickness(0, 20, 0, 20)
                });
                return;
            }

            foreach (var plugin in available)
            {
                PanelAvailablePlugins.Children.Add(BuildAvailablePluginItem(plugin));
            }
        }

        private UIElement BuildAvailablePluginItem(OnlinePluginInfo plugin)
        {
            var border = new Border
            {
                Margin = new Thickness(0, 0, 0, 6),
                Padding = new Thickness(10, 8, 10, 8),
                BorderBrush = TryFindResource("PopupWindowBorderBrush") as Brush,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(6)
            };

            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var titlePanel = new StackPanel { Orientation = Orientation.Vertical };

            var title = new TextBlock
            {
                Text = $"{plugin.Icon} {plugin.Name}",
                FontSize = 14,
                FontWeight = FontWeights.Bold,
                Foreground = TryFindResource("PopupWindowForeground") as Brush
            };
            titlePanel.Children.Add(title);

            var subtitle = new TextBlock
            {
                Text = $"{plugin.Id}  v{plugin.Version}",
                FontSize = 11,
                Foreground = TryFindResource("SettingsPageAnnotationForeground") as Brush
            };
            titlePanel.Children.Add(subtitle);

            if (!string.IsNullOrWhiteSpace(plugin.Description))
            {
                var desc = new TextBlock
                {
                    Text = plugin.Description,
                    FontSize = 12,
                    Foreground = TryFindResource("SettingsPageAnnotationForeground") as Brush,
                    TextWrapping = TextWrapping.Wrap,
                    Margin = new Thickness(0, 4, 0, 0)
                };
                titlePanel.Children.Add(desc);
            }

            Grid.SetColumn(titlePanel, 0);
            grid.Children.Add(titlePanel);

            var installBtn = new Button
            {
                Content = "安装",
                Width = 80,
                Height = 32,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(12, 0, 0, 0)
            };
            installBtn.Click += async (s, e) => await InstallOnlinePluginAsync(plugin);
            Grid.SetColumn(installBtn, 1);
            grid.Children.Add(installBtn);

            border.Child = grid;
            return border;
        }

        // ===== 安装进度 UI =====

        private void ShowInstallProgress(string status, string detail = "")
        {
            Dispatcher.Invoke(() =>
            {
                TextBlockInstallStatus.Text = status;
                TextBlockInstallDetail.Text = detail;
                ProgressBarInstall.Value = 0;
                ProgressBarInstall.IsIndeterminate = false;
                GridInstallProgress.Visibility = Visibility.Visible;
            });
        }

        private void UpdateInstallProgress(double value, string status = null, string detail = null)
        {
            Dispatcher.Invoke(() =>
            {
                if (value >= 0) ProgressBarInstall.Value = Math.Min(100, Math.Max(0, value));
                if (status != null) TextBlockInstallStatus.Text = status;
                if (detail != null) TextBlockInstallDetail.Text = detail;
            });
        }

        private void SetInstallProgressIndeterminate(string status, string detail = "")
        {
            Dispatcher.Invoke(() =>
            {
                TextBlockInstallStatus.Text = status;
                TextBlockInstallDetail.Text = detail;
                ProgressBarInstall.IsIndeterminate = true;
            });
        }

        private void HideInstallProgress()
        {
            Dispatcher.Invoke(() =>
            {
                GridInstallProgress.Visibility = Visibility.Collapsed;
                ProgressBarInstall.IsIndeterminate = false;
                ProgressBarInstall.Value = 0;
            });
        }

        /// <summary>从网络下载 .icplugin（支持主下载源 + fallback）并安装，安装前校验大小与 SHA256。</summary>
        private async Task InstallOnlinePluginAsync(OnlinePluginInfo plugin)
        {
            if (string.IsNullOrWhiteSpace(plugin.DownloadUrl) && string.IsNullOrWhiteSpace(plugin.FallbackUrl))
            {
                ShowInlineMessage("该插件未提供下载地址");
                return;
            }

            ShowInstallProgress($"准备安装 {plugin.Name}", "正在连接下载源...");
            string tempFile = null;
            try
            {
                tempFile = await DownloadPluginWithFallbackAsync(plugin);
                if (string.IsNullOrEmpty(tempFile))
                {
                    UpdateInstallProgress(-1, $"下载 {plugin.Name} 失败", "所有下载源均不可用");
                    await Task.Delay(1500);
                    return;
                }

                // 校验文件大小
                SetInstallProgressIndeterminate($"正在校验 {plugin.Name}", "校验文件大小...");
                var fileInfo = new FileInfo(tempFile);
                if (plugin.Size > 0 && fileInfo.Length != plugin.Size)
                {
                    UpdateInstallProgress(-1, $"安装 {plugin.Name} 失败", "文件大小校验失败");
                    await Task.Delay(1500);
                    return;
                }

                // 校验 SHA256
                UpdateInstallProgress(-1, $"正在校验 {plugin.Name}", "校验 SHA256...");
                if (plugin.Checksum != null &&
                    !string.IsNullOrWhiteSpace(plugin.Checksum.Value) &&
                    string.Equals(plugin.Checksum.Algorithm, "SHA256", StringComparison.OrdinalIgnoreCase))
                {
                    string fileHash = CalculateSHA256(tempFile);
                    if (!string.Equals(fileHash, plugin.Checksum.Value, StringComparison.OrdinalIgnoreCase))
                    {
                        UpdateInstallProgress(-1, $"安装 {plugin.Name} 失败", "SHA256 校验失败");
                        await Task.Delay(1500);
                        return;
                    }
                }

                // 安装
                SetInstallProgressIndeterminate($"正在安装 {plugin.Name}", "解压并加载插件...");
                BeginInstallPluginFromFile(tempFile);

                UpdateInstallProgress(100, $"安装 {plugin.Name} 完成", "插件已启用");
                await Task.Delay(800);
            }
            catch (Exception ex)
            {
                LogHelper.WriteLogToFile($"下载 plugin 失败 {plugin.Id}: {ex.Message}", LogHelper.LogType.Error);
                UpdateInstallProgress(-1, $"安装 {plugin.Name} 失败", ex.Message);
                await Task.Delay(1500);
            }
            finally
            {
                HideInstallProgress();
                try { if (tempFile != null && File.Exists(tempFile)) File.Delete(tempFile); } catch { }
            }
        }

        /// <summary>依次尝试 downloadUrl 与 fallbackUrl 下载插件包，返回临时文件路径。</summary>
        private async Task<string> DownloadPluginWithFallbackAsync(OnlinePluginInfo plugin)
        {
            var urls = new List<string>();
            if (!string.IsNullOrWhiteSpace(plugin.DownloadUrl)) urls.Add(plugin.DownloadUrl);
            if (!string.IsNullOrWhiteSpace(plugin.FallbackUrl) && !urls.Contains(plugin.FallbackUrl, StringComparer.OrdinalIgnoreCase))
                urls.Add(plugin.FallbackUrl);

            for (int i = 0; i < urls.Count; i++)
            {
                var url = urls[i];
                try
                {
                    string tempFile = Path.Combine(Path.GetTempPath(), $"{plugin.Id}-{plugin.Version}{PluginFileExtension}");
                    using (var client = new WebClient())
                    {
                        client.DownloadProgressChanged += (s, e) =>
                        {
                            UpdateInstallProgress(e.ProgressPercentage,
                                $"正在下载 {plugin.Name}",
                                $"源 {i + 1}/{urls.Count}：{e.BytesReceived / 1024} KB / {e.TotalBytesToReceive / 1024} KB");
                        };
                        await client.DownloadFileTaskAsync(new Uri(url), tempFile);
                    }
                    return tempFile;
                }
                catch (Exception ex)
                {
                    LogHelper.WriteLogToFile($"下载 plugin 源失败 [{plugin.Id}] {url}: {ex.Message}", LogHelper.LogType.Warning);
                }
            }
            return null;
        }

        /// <summary>计算文件 SHA256 校验值（大写十六进制）。</summary>
        private static string CalculateSHA256(string filePath)
        {
            using (var sha = SHA256.Create())
            using (var stream = File.OpenRead(filePath))
            {
                var hash = sha.ComputeHash(stream);
                return BitConverter.ToString(hash).Replace("-", "").ToUpperInvariant();
            }
        }

        // ===== 打开 plugin 目录 =====

        private void BtnPluginOpenFolder_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (!Directory.Exists(PluginDirectory))
                    Directory.CreateDirectory(PluginDirectory);

                System.Diagnostics.Process.Start(PluginDirectory);
            }
            catch (Exception ex)
            {
                LogHelper.WriteLogToFile($"打开 plugin 目录失败: {ex.Message}", LogHelper.LogType.Error);
                ShowInlineMessage("打开 plugin 目录失败：" + ex.Message);
            }
        }

        // ===== 辅助 =====

        private static PluginManifest TryReadManifest(string pluginDir)
        {
            try
            {
                string manifestFile = Path.Combine(pluginDir, "plugin.icplugin");
                if (!File.Exists(manifestFile)) return null;
                string json = File.ReadAllText(manifestFile, System.Text.Encoding.UTF8);
                return Newtonsoft.Json.JsonConvert.DeserializeObject<PluginManifest>(json);
            }
            catch { return null; }
        }

        // ===== 简易内联提示 =====

        private void ShowInlineMessage(string message)
        {
            try
            {
                if (Application.Current.MainWindow is MainWindow mw)
                {
                    mw.ShowNotificationAsync(message);
                }
            }
            catch { }
        }
    }
}
