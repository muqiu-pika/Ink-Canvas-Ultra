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
                // 关闭后延时后台 GC，回收插件工坊窗口可视化树内存
                Helpers.WindowMemoryHelper.ReleaseOnClose(_instance);
                _instance.Closed += (s, e) =>
                {
                    // 事件反订阅统一在实例的 OnClosed 中完成（更可靠，不依赖静态字段状态）
                    lock (_instanceLock)
                    {
                        if (ReferenceEquals(_instance, s)) _instance = null;
                    }
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
            // 订阅 PluginHost 的列表变化事件，自动刷新（先反订阅，避免 Loaded 多次触发导致重复订阅）
            try
            {
                var host = PluginHost.Instance;
                if (host != null)
                {
                    host.PluginListChanged -= OnPluginListChanged;
                    host.PluginListChanged += OnPluginListChanged;
                }
            }
            catch { }

            RefreshPluginList(silent: true);
        }

        protected override void OnClosed(EventArgs e)
        {
            // PluginHost 是与应用同生命周期的单例，若不反订阅其事件，
            // 本窗口会被单例事件链永久引用，关闭后无法回收
            try
            {
                var host = PluginHost.Instance;
                if (host != null) host.PluginListChanged -= OnPluginListChanged;
            }
            catch { }

            Loaded -= PluginWorkshopWindow_Loaded;
            base.OnClosed(e);
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
                    // 设置窗口可能只是被隐藏（复用实例），需重新显示并刷新最新设置值
                    if (!existing.IsVisible)
                    {
                        existing.Owner = mw;
                        existing.ShowInTaskbar = true;
                        // 预构建阶段被挪到了屏幕外，重新显示前恢复到主窗口居中位置
                        try
                        {
                            existing.Left = mw.Left + (mw.ActualWidth - existing.Width) / 2;
                            existing.Top = mw.Top + (mw.ActualHeight - existing.Height) / 2;
                        }
                        catch { }
                        existing.Show();
                        existing.ReloadContents();
                    }
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

            // 安装前校验：主程序版本是否满足插件要求的最低版本（不满足则直接拒绝，不解压、不留残留）
            var pkgManifest = TryReadManifestFromPackage(sourceFile);
            if (pkgManifest != null && !PluginHost.IsHostVersionCompatible(pkgManifest))
            {
                ShowInlineMessage($"无法安装：插件「{pkgManifest.Name}」需要主程序 ≥ {pkgManifest.MinHostVersion}，请先升级软件至该版本及以上。");
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
                    yesAction: () => DoInstallPluginFromFile(sourceFile, destDir, overwrite: true, autoEnable: true),
                    noAction: () => ShowInlineMessage("已取消安装"));
                confirm.Owner = this;
                Helpers.WindowMemoryHelper.ReleaseOnClose(confirm);
                confirm.ShowDialog();
                return;
            }

            DoInstallPluginFromFile(sourceFile, destDir, overwrite: false, autoEnable: true);
        }

        /// <summary>
        /// 将 .icplugin (ZIP) 解压到目标目录，并立即加载到 PluginHost。
        /// </summary>
        private void DoInstallPluginFromFile(string sourceFile, string destDir, bool overwrite, bool autoEnable)
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
                    if (autoEnable)
                    {
                        // 默认启用新装的 plugin
                        host.SetPluginEnabled(manifest.Id, true);

                        // 若是视频展台 plugin，在桌面创建指向软件安装位置的快捷方式
                        TryCreateVideoPresenterDesktopShortcut(manifest);

                        ShowInlineMessage($"plugin 已安装并启用：{manifest.Name}");
                    }
                    else
                    {
                        ShowInlineMessage($"plugin 已安装：{manifest.Name}（请手动启用）");
                    }
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

            // 该插件是否注册了「设置面板」工厂（如自定义快捷键插件）
            Func<UIElement> settingsFactory = null;
            try { settingsFactory = PluginHost.Instance?.GetSettingsPanelFactory(pluginId); } catch { }

            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }); // 0 标题
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });                     // 1 状态标签
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });                     // 2 启用/禁用开关
            // 4、5 列固定宽度且始终存在：即便某行没有「设置」或「更新」按钮，
            // 也保留同样宽度，使第 2 列开关在每行处于同一纵向直线。
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(30) });                 // 3 设置按钮占位
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(64) });                 // 4 更新按钮占位

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
                Text = !info.IsCompatible
                    ? "版本不兼容"
                    : (info.IsLoaded ? "运行中" : (info.IsEnabled ? "已启用" : "已禁用")),
                FontSize = 11,
                Foreground = TryFindResource("PopupWindowDarkBlueBorderForeground") as Brush
            };
            statusTag.Child = statusText;
            Grid.SetColumn(statusTag, 1);
            grid.Children.Add(statusTag);

            // 启用/禁用开关（版本不兼容时禁用开关，需先升级软件）
            var toggle = new CheckBox
            {
                IsChecked = info.IsCompatible ? (bool?)info.IsEnabled : (bool?)false,
                IsEnabled = info.IsCompatible,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(12, 0, 0, 0),
                ToolTip = info.IsCompatible
                    ? "启用 / 禁用此 plugin（立即生效）"
                    : "该插件需要更高版本软件，请先升级后才能启用"
            };
            toggle.Checked += (s, e) => OnPluginToggleChanged(pluginId, true);
            toggle.Unchecked += (s, e) => OnPluginToggleChanged(pluginId, false);
            Grid.SetColumn(toggle, 2);
            grid.Children.Add(toggle);

            // 设置图标：有设置面板的插件显示「设置」图标，点击展开/折叠其下方面板（同一位置两个图标状态）
            Border settingsPanelHost = null;
            if (settingsFactory != null)
            {
                var settingsBtn = new Button
                {
                    Width = 30,
                    Height = 30,
                    Padding = new Thickness(0),
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(8, 0, 0, 0),
                    ToolTip = "设置"
                };
                settingsBtn.Content = BuildSettingsGlyph("\uE713");
                Grid.SetColumn(settingsBtn, 3);
                grid.Children.Add(settingsBtn);

                // 折叠在插件条目下方的设置面板（默认收起）
                settingsPanelHost = new Border
                {
                    Margin = new Thickness(0, 0, 0, 6),
                    Padding = new Thickness(12, 10, 12, 10),
                    BorderBrush = TryFindResource("PopupWindowBorderBrush") as Brush,
                    BorderThickness = new Thickness(1),
                    CornerRadius = new CornerRadius(6),
                    Visibility = Visibility.Collapsed
                };
                // 记录工厂，展开时用其重建 UI 以获得最新状态
                settingsPanelHost.Tag = settingsFactory;

                setupSettingsPanelToggle(settingsBtn, settingsPanelHost);
            }

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
                updateBtn.Click += async (s, e) => await UpdatePluginAsync(update, info);
                Grid.SetColumn(updateBtn, 4);
                grid.Children.Add(updateBtn);
            }

            border.Child = grid;

            // 若存在设置面板，将卡片与可折叠面板打包为一个条目整体
            if (settingsPanelHost != null)
            {
                var item = new StackPanel { Margin = new Thickness(0) };
                item.Children.Add(border);
                item.Children.Add(settingsPanelHost);
                return item;
            }
            return border;
        }

        /// <summary>构建一个 Segoe Fluent Icons 字体的图标文本块。</summary>
        private TextBlock BuildSettingsGlyph(string glyph)
        {
            var icon = new TextBlock
            {
                Text = glyph,
                FontSize = 15,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };
            try { icon.FontFamily = TryFindResource("FluentIconFontFamily") as FontFamily ?? new FontFamily("Segoe Fluent Icons"); } catch { icon.FontFamily = new FontFamily("Segoe Fluent Icons"); }
            return icon;
        }

        /// <summary>设置图标的点击切换：展开/折叠插件设置面板，并在同一位置切换图标。</summary>
        private void setupSettingsPanelToggle(Button settingsBtn, Border panelHost)
        {
            if (settingsBtn == null || panelHost == null) return;
            settingsBtn.Click += (s, e) =>
            {
                if (panelHost.Visibility == Visibility.Visible)
                {
                    // 折叠：图标切回「设置」
                    panelHost.Visibility = Visibility.Collapsed;
                    settingsBtn.Content = BuildSettingsGlyph("\uE713");
                    settingsBtn.ToolTip = "设置";
                }
                else
                {
                    // 展开：用工厂重建 UI（刷新最新绑定），图标切换为「折叠」
                    panelHost.Child = null;
                    try
                    {
                        var factory = panelHost.Tag as Func<UIElement>;
                        if (factory != null) panelHost.Child = factory();
                    }
                    catch { }
                    panelHost.Visibility = Visibility.Visible;
                    settingsBtn.Content = BuildSettingsGlyph("\uE70D");
                    settingsBtn.ToolTip = "收起设置";
                }
            };
        }

        /// <summary>插件开关切换：立即加载或软卸载，启用前校验版本兼容性。</summary>
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

                if (enabled)
                {
                    // 启用前校验：主程序版本不足则拦截并提示，而不是“假启用”
                    var info = host.GetAllInstalledPlugins()
                        .FirstOrDefault(p => string.Equals(p.Manifest?.Id, pluginId, StringComparison.OrdinalIgnoreCase));
                    string required = PluginHost.GetRequiredHostVersionIfIncompatible(info?.Manifest?.MinHostVersion);
                    if (required != null)
                    {
                        ShowInlineMessage($"无法启用「{info?.Manifest?.Name ?? pluginId}」：需要主程序 ≥ {required}，请先升级软件。");
                        RefreshPluginList(silent: true); // 复位开关为实际状态
                        return;
                    }
                }

                bool ok = host.SetPluginEnabled(pluginId, enabled);
                if (enabled && !ok)
                {
                    ShowInlineMessage($"启用失败：plugin「{pluginId}」未能加载（可能版本不兼容或入口无效）。");
                }
                else
                {
                    ShowInlineMessage(enabled ? $"已启用 plugin：{pluginId}" : $"已禁用 plugin：{pluginId}");
                }
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

            var required = PluginHost.GetRequiredHostVersionIfIncompatible(plugin.MinHostVersion);
            var installBtn = new Button
            {
                Content = "安装",
                Width = 80,
                Height = 32,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(12, 0, 0, 0)
            };
            if (required != null)
            {
                // 主程序版本低于插件要求：安装按钮置灰，并提示需要升级软件
                installBtn.IsEnabled = false;
                installBtn.Opacity = 0.5;
                installBtn.ToolTip = $"需要升级软件至 {required} 及以上";
            }
            else
            {
                installBtn.Click += async (s, e) => await InstallOnlinePluginAsync(plugin);
            }
            Grid.SetColumn(installBtn, 1);
            grid.Children.Add(installBtn);

            if (required != null)
            {
                var hint = new TextBlock
                {
                    Text = $"需升级软件至 {required} 及以上",
                    FontSize = 11,
                    Foreground = TryFindResource("PopupWindowAnnotationForeground") as Brush,
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(8, 0, 0, 0),
                    TextWrapping = TextWrapping.Wrap
                };
                Grid.SetColumn(hint, 2);
                grid.Children.Add(hint);
            }

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

        /// <summary>从网络下载 .icplugin（支持主下载源 + fallback）并安装，安装前校验 SHA256。</summary>
        private async Task InstallOnlinePluginAsync(OnlinePluginInfo plugin)
        {
            if (string.IsNullOrWhiteSpace(plugin.DownloadUrl) && string.IsNullOrWhiteSpace(plugin.FallbackUrl))
            {
                ShowInlineMessage("该插件未提供下载地址");
                return;
            }

            // 主程序版本不满足插件最低要求时拒绝下载安装（按钮已置灰，此处为二次防护）
            if (!PluginHost.IsHostVersionCompatible(plugin.MinHostVersion))
            {
                ShowInlineMessage($"无法安装：该插件需要主程序 ≥ {plugin.MinHostVersion}，请先升级软件。");
                return;
            }

            ShowInstallProgress($"准备安装 {plugin.Name}", "正在连接下载源...");
            string tempFile = null;
            try
            {
                var download = await DownloadAndVerifyPluginAsync(plugin);
                tempFile = download.TempFile;
                if (string.IsNullOrEmpty(tempFile))
                {
                    UpdateInstallProgress(-1, $"安装 {plugin.Name} 失败", download.FailureReason);
                    await Task.Delay(1500);
                    return;
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
                TryDeleteTempFile(tempFile);
            }
        }

        /// <summary>更新已安装的 plugin：先禁用，再下载安装，最后按原状态启用。</summary>
        private async Task UpdatePluginAsync(OnlinePluginInfo plugin, InstalledPluginInfo installed)
        {
            if (string.IsNullOrWhiteSpace(plugin.DownloadUrl) && string.IsNullOrWhiteSpace(plugin.FallbackUrl))
            {
                ShowInlineMessage("该插件未提供下载地址");
                return;
            }

            var host = PluginHost.Instance;
            string pluginId = installed.Manifest?.Id;
            bool wasEnabled = installed.IsEnabled;

            ShowInstallProgress($"准备更新 {plugin.Name}", "正在禁用旧版本...");
            try
            {
                // 1. 禁用插件（卸载并持久化禁用状态）
                if (host != null && !string.IsNullOrEmpty(pluginId))
                {
                    host.SetPluginEnabled(pluginId, false);
                }

                // 2. 下载最新版本
                UpdateInstallProgress(0, $"正在下载 {plugin.Name}", "正在连接下载源...");
                string tempFile = null;
                try
                {
                    var download = await DownloadAndVerifyPluginAsync(plugin);
                    tempFile = download.TempFile;
                    if (string.IsNullOrEmpty(tempFile))
                    {
                        UpdateInstallProgress(-1, $"更新 {plugin.Name} 失败", download.FailureReason);
                        await Task.Delay(1500);
                        return;
                    }

                    // 主程序版本不满足插件最低要求时拒绝更新（避免覆盖安装后无法加载）
                    var updateManifest = TryReadManifestFromPackage(tempFile);
                    if (updateManifest != null && !PluginHost.IsHostVersionCompatible(updateManifest))
                    {
                        UpdateInstallProgress(-1, $"更新 {plugin.Name} 失败", $"需要主程序 ≥ {updateManifest.MinHostVersion}");
                        await Task.Delay(1500);
                        return;
                    }

                    // 3. 安装新版本（直接覆盖原目录，不弹确认框）
                    SetInstallProgressIndeterminate($"正在安装 {plugin.Name}", "解压并替换插件文件...");
                    string destDir = installed.Directory;
                    if (string.IsNullOrEmpty(destDir) || !Directory.Exists(destDir))
                    {
                        destDir = Path.Combine(PluginDirectory, plugin.Id);
                        if (!Directory.Exists(destDir)) Directory.CreateDirectory(destDir);
                    }
                    DoInstallPluginFromFile(tempFile, destDir, overwrite: true, autoEnable: false);

                    // 4. 如果原来处于启用状态，则重新启用
                    if (host != null && !string.IsNullOrEmpty(pluginId) && wasEnabled)
                    {
                        UpdateInstallProgress(100, $"正在启用 {plugin.Name}", "加载新版本...");
                        host.SetPluginEnabled(pluginId, true);
                    }

                    UpdateInstallProgress(100, $"更新 {plugin.Name} 完成", wasEnabled ? "插件已更新并启用" : "插件已更新（保持禁用）");
                    await Task.Delay(800);
                }
                finally
                {
                    TryDeleteTempFile(tempFile);
                }
            }
            catch (Exception ex)
            {
                LogHelper.WriteLogToFile($"更新 plugin 失败 {plugin.Id}: {ex.Message}", LogHelper.LogType.Error);
                UpdateInstallProgress(-1, $"更新 {plugin.Name} 失败", ex.Message);
                await Task.Delay(1500);
            }
            finally
            {
                HideInstallProgress();
            }
        }

        /// <summary>下载并校验插件包的结果。</summary>
        private sealed class PluginDownloadResult
        {
            /// <summary>通过校验的临时文件路径；失败时为 null。</summary>
            public string TempFile { get; set; }

            /// <summary>失败原因；成功时为 null。</summary>
            public string FailureReason { get; set; }
        }

        /// <summary>
        /// 依次尝试 downloadUrl 与 fallbackUrl 下载插件包，并在下载后就地校验。
        /// 目录记录的 size 仅作参考（插件重新打包后 size 可能未同步），不一致时只记警告并继续；
        /// 完整性以 SHA256 为准，某一源校验不通过会自动尝试下一个源。
        /// </summary>
        /// <param name="plugin">在线插件信息</param>
        /// <returns>成功时 TempFile 为临时文件路径；全部源均失败时 TempFile 为 null 并给出 FailureReason</returns>
        private async Task<PluginDownloadResult> DownloadAndVerifyPluginAsync(OnlinePluginInfo plugin)
        {
            var urls = new List<string>();
            if (!string.IsNullOrWhiteSpace(plugin.DownloadUrl)) urls.Add(plugin.DownloadUrl);
            if (!string.IsNullOrWhiteSpace(plugin.FallbackUrl) && !urls.Contains(plugin.FallbackUrl, StringComparer.OrdinalIgnoreCase))
                urls.Add(plugin.FallbackUrl);

            bool anyDownloaded = false;

            for (int i = 0; i < urls.Count; i++)
            {
                var url = urls[i];
                string tempFile = Path.Combine(Path.GetTempPath(), $"{plugin.Id}-{plugin.Version}{PluginFileExtension}");

                // 下载
                try
                {
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
                    anyDownloaded = true;
                }
                catch (Exception ex)
                {
                    LogHelper.WriteLogToFile($"下载 plugin 源失败 [{plugin.Id}] {url}: {ex.Message}", LogHelper.LogType.Warning);
                    TryDeleteTempFile(tempFile);
                    continue;
                }

                // 校验文件大小：仅警告，不中断安装。
                // 目录里的 size 可能在插件重新打包后未同步，此时以 SHA256 为唯一判据。
                long actualSize = new FileInfo(tempFile).Length;
                if (plugin.Size > 0 && actualSize != plugin.Size)
                {
                    LogHelper.WriteLogToFile(
                        $"插件 [{plugin.Id}] 文件大小与目录记录不一致：实际 {actualSize} 字节，记录 {plugin.Size} 字节（以 SHA256 为准）",
                        LogHelper.LogType.Warning);
                }

                // 校验 SHA256：不通过则丢弃本源，继续尝试下一个源
                if (plugin.Checksum != null &&
                    !string.IsNullOrWhiteSpace(plugin.Checksum.Value) &&
                    string.Equals(plugin.Checksum.Algorithm, "SHA256", StringComparison.OrdinalIgnoreCase))
                {
                    UpdateInstallProgress(-1, $"正在校验 {plugin.Name}", "校验 SHA256...");
                    string fileHash = CalculateSHA256(tempFile);
                    if (!string.Equals(fileHash, plugin.Checksum.Value, StringComparison.OrdinalIgnoreCase))
                    {
                        LogHelper.WriteLogToFile(
                            $"插件 [{plugin.Id}] 源 {url} 的 SHA256 不匹配：实际 {fileHash}，期望 {plugin.Checksum.Value}",
                            LogHelper.LogType.Warning);
                        TryDeleteTempFile(tempFile);
                        continue;
                    }
                }

                return new PluginDownloadResult { TempFile = tempFile };
            }

            return new PluginDownloadResult
            {
                FailureReason = anyDownloaded ? "所有下载源的文件校验均未通过" : "所有下载源均不可用"
            };
        }

        /// <summary>删除下载过程中的临时文件，删除失败时静默忽略。</summary>
        private static void TryDeleteTempFile(string path)
        {
            try { if (!string.IsNullOrEmpty(path) && File.Exists(path)) File.Delete(path); } catch { }
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

        /// <summary>直接从 .icplugin 安装包（ZIP）内读取 plugin.icplugin 清单，用于安装前版本校验（避免先解压再发现不兼容）。</summary>
        private static PluginManifest TryReadManifestFromPackage(string packageFile)
        {
            try
            {
                using (var archive = System.IO.Compression.ZipFile.OpenRead(packageFile))
                {
                    var entry = archive.GetEntry("plugin.icplugin");
                    if (entry == null) return null;
                    using (var sr = new StreamReader(entry.Open()))
                    {
                        return Newtonsoft.Json.JsonConvert.DeserializeObject<PluginManifest>(sr.ReadToEnd());
                    }
                }
            }
            catch { return null; }
        }

        /// <summary>
        /// 若安装的 plugin 是视频展台，则在桌面创建快捷方式，目标指向当前软件安装位置。
        /// </summary>
        private static void TryCreateVideoPresenterDesktopShortcut(PluginManifest manifest)
        {
            if (manifest == null) return;

            bool isVideoPresenter =
                string.Equals(manifest.Id, "ink-canvas.visualpresenter", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(manifest.Name, "视频展台", StringComparison.OrdinalIgnoreCase) ||
                (manifest.EntryPoints != null && manifest.EntryPoints.Any(ep =>
                    string.Equals(ep?.Route, "video-presenter", StringComparison.OrdinalIgnoreCase)));

            if (!isVideoPresenter) return;

            try
            {
                string exePath = Path.Combine(App.RootPath, "Ink Canvas Ultra.exe");
                if (!File.Exists(exePath))
                {
                    LogHelper.WriteLogToFile($"创建视频展台快捷方式失败：未找到主程序 {exePath}", LogHelper.LogType.Warning);
                    return;
                }

                string desktopPath = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
                string shortcutPath = Path.Combine(desktopPath, "视频展台.lnk");

                var shell = new IWshRuntimeLibrary.WshShell();
                var shortcut = (IWshRuntimeLibrary.IWshShortcut)shell.CreateShortcut(shortcutPath);
                shortcut.TargetPath = exePath;
                shortcut.Arguments = App.VideoPresenterLaunchArgument;
                shortcut.WorkingDirectory = App.RootPath;
                shortcut.IconLocation = $"{exePath},0";
                shortcut.Description = "Ink Canvas Ultra - 视频展台";
                shortcut.Save();

                LogHelper.WriteLogToFile($"视频展台桌面快捷方式已创建: {shortcutPath}", LogHelper.LogType.Event);
            }
            catch (Exception ex)
            {
                LogHelper.WriteLogToFile($"创建视频展台桌面快捷方式失败: {ex.Message}", LogHelper.LogType.Error);
            }
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
