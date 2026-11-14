using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;

namespace Ink_Canvas.Plugins
{
    /// <summary>系统组件检查结果。</summary>
    public sealed class PluginSystemCheckResult
    {
        public string Component;
        public bool Available;
        public string DisplayName;
        public string InstallCommand;
        public string SettingsUri;
        public string Summary;
    }

    /// <summary>
    /// 插件声明的 Windows 系统组件需求检查（plugin.icplugin 的 systemComponent 字段）。
    /// 只做文件级只读检查，不实例化 COM、不弹窗；安装引导（提权 DISM / 打开系统设置）
    /// 由插件工坊的引导页调用 <see cref="TryElevatedInstall"/> 与 <see cref="OpenWindowsSettings"/>。
    /// </summary>
    internal static class PluginSystemRequirements
    {
        /// <summary>已知的系统组件。未在表中的组件名一律视为可用（不做拦截）。</summary>
        private static readonly Dictionary<string, PluginSystemCheckResult> Known =
            new Dictionary<string, PluginSystemCheckResult>(StringComparer.OrdinalIgnoreCase)
            {
                ["MathRecognizer"] = new PluginSystemCheckResult
                {
                    Component = "MathRecognizer",
                    DisplayName = "Windows 数学识别器（墨迹公式引擎）",
                    InstallCommand = "Dism /online /add-capability /capabilityname:MathRecognizer~~~~0.0.1.0",
                    SettingsUri = "ms-settings:optionalfeatures"
                }
            };

        /// <summary>检查组件是否可用；未知组件名返回可用（不做拦截）。</summary>
        public static PluginSystemCheckResult Check(string component)
        {
            if (string.IsNullOrWhiteSpace(component)) return new PluginSystemCheckResult { Available = true };
            if (!Known.TryGetValue(component, out var result))
                return new PluginSystemCheckResult { Available = true, Component = component };

            result.Available = FilesPresent();
            result.Summary = result.Available
                ? "已安装"
                : "未安装（缺少 " + (Is64BitProcess ? "mraut.dll" : "mraut.dll")
                  + " / micaut.dll 数学识别组件）";
            return result;
        }

        private static bool Is64BitProcess => IntPtr.Size == 8;

        /// <summary>
        /// 同时覆盖 64/32 位进程的 Common Files 路径：32 位进程里 CommonProgramFiles
        /// 会指向 Program Files (x86)，而系统 Ink 组件可能只在 64 位目录下，两条都查。
        /// </summary>
        public static bool FilesPresent()
        {
            foreach (var directory in EnumerateInkDirectories())
            {
                bool hasControl = File.Exists(Path.Combine(directory, "micaut.dll"));
                bool hasRecognizer = File.Exists(Path.Combine(directory, "mraut.dll"));
                if (hasControl && hasRecognizer) return true;
            }
            return false;
        }

        private static IEnumerable<string> EnumerateInkDirectories()
        {
            var roots = new List<string>();
            AddRoot(roots, Environment.GetEnvironmentVariable("CommonProgramW6432"));
            AddRoot(roots, Environment.GetEnvironmentVariable("CommonProgramFiles"));
            AddRoot(roots, Environment.GetFolderPath(Environment.SpecialFolder.CommonProgramFiles));
            AddRoot(roots, Environment.GetFolderPath(Environment.SpecialFolder.CommonProgramFilesX86));

            foreach (string root in roots)
            {
                string directory = Path.Combine(root, "Microsoft Shared", "Ink");
                if (Directory.Exists(directory)) yield return directory;
            }
        }

        private static void AddRoot(List<string> roots, string root)
        {
            if (string.IsNullOrEmpty(root)) return;
            foreach (string existing in roots)
                if (string.Equals(existing, root, StringComparison.OrdinalIgnoreCase)) return;
            roots.Add(root);
        }

        /// <summary>
        /// 以管理员身份运行 DISM 安装可选功能（触发 UAC），阻塞等待完成后复检。
        /// 返回是否安装成功；失败时 error 给出原因（用户拒绝 UAC / DISM 失败等）。
        /// </summary>
        public static bool TryElevatedInstall(string component, out string error)
        {
            error = null;
            if (!Known.TryGetValue(component, out var info) || string.IsNullOrEmpty(info.InstallCommand))
            {
                error = "该组件不支持自动安装";
                return false;
            }

            try
            {
                var startInfo = new ProcessStartInfo("dism.exe", info.InstallCommand)
                {
                    Verb = "runas",                    // 触发 UAC 提权
                    UseShellExecute = true,
                    CreateNoWindow = true,
                    WindowStyle = ProcessWindowStyle.Minimized
                };
                using (var process = Process.Start(startInfo))
                {
                    if (process == null)
                    {
                        error = "无法启动安装程序";
                        return false;
                    }
                    process.WaitForExit();

                    // DISM 成功码：0 / 3010（重启才生效——数学组件不需要重启，也按成功处理）
                    if (process.ExitCode != 0 && process.ExitCode != 3010)
                    {
                        error = "DISM 安装命令退出码 " + process.ExitCode;
                        return false;
                    }
                }

                if (FilesPresent()) return true;
                error = "安装命令已执行，但组件文件仍未就绪";
                return false;
            }
            catch (System.ComponentModel.Win32Exception wex)
            {
                // 1223 = 用户取消了 UAC 提权
                error = wex.NativeErrorCode == 1223 ? "用户取消了管理员授权" : wex.Message;
                return false;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }
        }

        /// <summary>打开 Windows「可选功能」设置页。</summary>
        public static void OpenWindowsSettings()
        {
            try
            {
                var info = Known.TryGetValue("MathRecognizer", out var recognized)
                    ? recognized
                    : new PluginSystemCheckResult();
                var uri = string.IsNullOrEmpty(recognized.SettingsUri)
                    ? "ms-settings:optionalfeatures"
                    : recognized.SettingsUri;
                Process.Start(new ProcessStartInfo(uri) { UseShellExecute = true });
            }
            catch { }
        }
    }
}
