using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Ink_Canvas.Helpers;
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
                        if (_instance.WindowState == WindowState.Minimized)
                            _instance.WindowState = WindowState.Normal;
                        _instance.Activate();
                        _instance.Topmost = true;
                        _instance.Topmost = false;
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

        public PluginWorkshopWindow()
        {
            InitializeComponent();
            Loaded += PluginWorkshopWindow_Loaded;
        }

        private void PluginWorkshopWindow_Loaded(object sender, RoutedEventArgs e)
        {
            RefreshPluginList(silent: true);
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
        /// 若已存在同名 plugin 目录，会弹窗询问是否覆盖。
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
        /// 将 .icplugin (ZIP) 解压到目标目录。
        /// </summary>
        private void DoInstallPluginFromFile(string sourceFile, string destDir, bool overwrite)
        {
            try
            {
                if (!Directory.Exists(PluginDirectory))
                    Directory.CreateDirectory(PluginDirectory);

                // 覆盖时先删除旧目录
                if (overwrite && Directory.Exists(destDir))
                {
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
                ShowInlineMessage($"plugin 已安装：{Path.GetFileNameWithoutExtension(fileName)}");

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

        private void RefreshPluginList(bool silent)
        {
            try
            {
                if (!Directory.Exists(PluginDirectory))
                    Directory.CreateDirectory(PluginDirectory);

                // 已安装的 plugin 都是子目录形式（每个子目录包含 plugin.icplugin 清单）
                var pluginDirs = Directory.GetDirectories(PluginDirectory, "*", SearchOption.TopDirectoryOnly)
                                          .Where(d => File.Exists(Path.Combine(d, "plugin.icplugin")))
                                          .ToList();

                int count = pluginDirs.Count;

                if (TextBlockPluginCount != null)
                    TextBlockPluginCount.Text = $"已安装 plugin：{count}";

                RenderInstalledPlugins(pluginDirs);

                if (!silent)
                    ShowInlineMessage("plugin 列表已刷新");
            }
            catch (Exception ex)
            {
                LogHelper.WriteLogToFile($"刷新 plugin 列表失败: {ex.Message}", LogHelper.LogType.Error);
                ShowInlineMessage("刷新 plugin 列表失败：" + ex.Message);
            }
        }

        private void RenderInstalledPlugins(List<string> pluginDirs)
        {
            if (PanelInstalledPlugins == null) return;

            PanelInstalledPlugins.Children.Clear();

            if (pluginDirs.Count == 0)
            {
                PanelInstalledPlugins.Children.Add(new TextBlock
                {
                    Text = "暂无已安装的 plugin",
                    FontSize = 13,
                    Foreground = TryFindResource("SettingsPageAnnotationForeground") as System.Windows.Media.Brush,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    Margin = new Thickness(0, 16, 0, 16)
                });
                return;
            }

            foreach (var dir in pluginDirs)
            {
                // 尝试从 manifest 读取 plugin 名称
                string displayName = Path.GetFileName(dir);
                string pluginId = displayName;
                string version = "";
                try
                {
                    string manifestPath = Path.Combine(dir, "plugin.icplugin");
                    string json = File.ReadAllText(manifestPath, System.Text.Encoding.UTF8);
                    var manifest = Newtonsoft.Json.JsonConvert.DeserializeObject<dynamic>(json);
                    if (manifest != null)
                    {
                        if (manifest.name != null)
                            displayName = (string)manifest.name;
                        if (manifest.id != null)
                            pluginId = (string)manifest.id;
                        if (manifest.version != null)
                            version = (string)manifest.version;
                    }
                }
                catch { /* 读取失败时使用目录名 */ }

                PanelInstalledPlugins.Children.Add(
                    BuildPluginItem(displayName, pluginId, version));
            }
        }

        private UIElement BuildPluginItem(string displayName, string pluginId, string version)
        {
            var border = new Border
            {
                Margin = new Thickness(0, 0, 0, 6),
                Padding = new Thickness(10, 8, 10, 8),
                BorderBrush = TryFindResource("PopupWindowBorderBrush") as System.Windows.Media.Brush,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(6)
            };

            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
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
                Foreground = TryFindResource("PopupWindowForeground") as System.Windows.Media.Brush
            };
            titlePanel.Children.Add(title);

            // 显示 plugin ID 和版本
            var subtitle = new TextBlock
            {
                Text = string.IsNullOrEmpty(version) ? pluginId : $"{pluginId}  v{version}",
                FontSize = 11,
                Foreground = TryFindResource("SettingsPageAnnotationForeground") as System.Windows.Media.Brush
            };
            titlePanel.Children.Add(subtitle);

            Grid.SetColumn(titlePanel, 0);
            grid.Children.Add(titlePanel);

            var versionTag = new Border
            {
                Padding = new Thickness(8, 2, 8, 2),
                Background = TryFindResource("PopupWindowDarkBlueBorderBackground") as System.Windows.Media.Brush,
                CornerRadius = new CornerRadius(4),
                VerticalAlignment = VerticalAlignment.Center
            };
            var versionText = new TextBlock
            {
                Text = string.IsNullOrEmpty(version) ? "已安装" : $"v{version}",
                FontSize = 11,
                Foreground = TryFindResource("PopupWindowDarkBlueBorderForeground") as System.Windows.Media.Brush
            };
            versionTag.Child = versionText;
            Grid.SetColumn(versionTag, 1);
            grid.Children.Add(versionTag);

            border.Child = grid;
            return border;
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
