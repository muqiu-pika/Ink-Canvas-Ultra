using System;
using System.IO;
using System.Linq;
using Ink_Canvas.Plugins;

namespace Ink_Canvas.Helpers
{
    /// <summary>
    /// 桌面「视频展台」快捷方式的生命周期管理。
    ///
    /// 约定：
    ///   安装并启用 / 手动启用 → 创建；
    ///   手动禁用 / 卸载插件 / 版本不兼容 → 删除；
    ///   卸载软件 → 由 Inno Setup 的 [UninstallDelete] 删除（路径须与 ShortcutPath 使用的桌面一致）。
    ///
    /// 快捷方式直接指向主程序 + --video-presenter 参数，不依赖任何 .vbs 启动脚本。
    /// 单独成类而非放在插件工坊窗口内，是因为启动流程（MainWindow.InitializePluginSystem）
    /// 也需要在未打开插件工坊的情况下同步快捷方式状态。
    /// </summary>
    public static class VideoPresenterShortcutHelper
    {
        /// <summary>桌面快捷方式文件名。改动此值须同步修改 .iss 的 [UninstallDelete] 段。</summary>
        public const string ShortcutFileName = "视频展台.lnk";

        /// <summary>
        /// 判断 plugin 是否为视频展台。
        /// 按 Id / 名称 / 入口路由三种方式识别，避免单一字段变动导致漏判。
        /// </summary>
        public static bool IsVideoPresenterPlugin(PluginManifest manifest)
        {
            if (manifest == null) return false;

            return string.Equals(manifest.Id, "ink-canvas.visualpresenter", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(manifest.Name, "视频展台", StringComparison.OrdinalIgnoreCase) ||
                   (manifest.EntryPoints != null && manifest.EntryPoints.Any(ep =>
                       string.Equals(ep?.Route, "video-presenter", StringComparison.OrdinalIgnoreCase)));
        }

        /// <summary>获取视频展台桌面快捷方式的完整路径。</summary>
        public static string GetShortcutPath()
        {
            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.Desktop),
                ShortcutFileName);
        }

        /// <summary>
        /// 在桌面创建视频展台快捷方式，目标指向当前软件安装位置。
        /// 幂等：已存在则直接覆盖。
        /// </summary>
        public static void Create()
        {
            try
            {
                string exePath = Path.Combine(App.RootPath, "Ink Canvas Ultra.exe");
                if (!File.Exists(exePath))
                {
                    LogHelper.WriteLogToFile($"创建视频展台快捷方式失败：未找到主程序 {exePath}", LogHelper.LogType.Warning);
                    return;
                }

                string shortcutPath = GetShortcutPath();

                var shell = new IWshRuntimeLibrary.WshShell();
                var shortcut = (IWshRuntimeLibrary.IWshShortcut)shell.CreateShortcut(shortcutPath);
                shortcut.TargetPath = exePath;
                shortcut.Arguments = App.VideoPresenterLaunchArgument;
                shortcut.WorkingDirectory = App.RootPath;
                shortcut.IconLocation = $"{exePath},0";
                shortcut.WindowStyle = 1;
                shortcut.Description = "Ink Canvas Ultra - 视频展台";
                shortcut.Save();

                LogHelper.WriteLogToFile($"视频展台桌面快捷方式已创建: {shortcutPath}", LogHelper.LogType.Event);
            }
            catch (Exception ex)
            {
                LogHelper.WriteLogToFile($"创建视频展台桌面快捷方式失败: {ex.Message}", LogHelper.LogType.Error);
            }
        }

        /// <summary>
        /// 删除桌面的视频展台快捷方式。不存在时静默跳过，不抛异常。
        /// </summary>
        public static void Delete()
        {
            try
            {
                string shortcutPath = GetShortcutPath();
                if (!File.Exists(shortcutPath)) return;

                File.Delete(shortcutPath);
                LogHelper.WriteLogToFile($"视频展台桌面快捷方式已删除: {shortcutPath}", LogHelper.LogType.Event);
            }
            catch (Exception ex)
            {
                LogHelper.WriteLogToFile($"删除视频展台桌面快捷方式失败: {ex.Message}", LogHelper.LogType.Error);
            }
        }

        /// <summary>
        /// 启动时单向同步：仅清理，不重建。
        ///
        /// 插件未安装 / 已禁用 / 版本不兼容时，移除可能残留的快捷方式，
        /// 避免出现点了没反应的无效入口。
        /// 插件已启用但快捷方式缺失（用户手动删除）时不做重建，尊重用户的手动删除。
        /// </summary>
        public static void SyncOnStartup()
        {
            try
            {
                var host = PluginHost.Instance;
                if (host == null) return;

                var vp = host.GetAllInstalledPlugins()
                             .FirstOrDefault(p => IsVideoPresenterPlugin(p.Manifest));

                // 插件不存在，或存在但未启用 / 不兼容 → 清理残留
                if (vp == null || !vp.IsEnabled || !vp.IsCompatible)
                {
                    Delete();
                }
            }
            catch (Exception ex)
            {
                LogHelper.WriteLogToFile($"启动时同步视频展台快捷方式失败: {ex.Message}", LogHelper.LogType.Error);
            }
        }
    }
}
