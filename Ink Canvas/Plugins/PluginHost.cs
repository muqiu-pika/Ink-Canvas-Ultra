using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows;
using Ink_Canvas.Helpers;
using Newtonsoft.Json;

namespace Ink_Canvas.Plugins
{
    /// <summary>
    /// plugin 宿主管理器：负责扫描、加载、初始化、卸载所有 plugin，
    /// 并实现 IPluginHost 接口向 plugin 提供主程序能力。
    /// </summary>
    public class PluginHost : IPluginHost
    {
        // ===== 单例 =====
        public static PluginHost Instance { get; private set; }

        // ===== 状态 =====
        private readonly Window _mainWindow;
        private readonly string _pluginsRoot;           // Plugins 根目录
        private readonly List<LoadedPlugin> _loaded = new List<LoadedPlugin>();
        private readonly Dictionary<string, PluginEntryPoint> _routeTable = new Dictionary<string, PluginEntryPoint>(StringComparer.OrdinalIgnoreCase);

        // ===== 事件 =====
        public event EventHandler ApplicationExiting;
        public event EventHandler<BoardModeChangedEventArgs> BoardModeChanged;

        // ===== 路由处理委托（主程序注册，用于响应声明式入口点） =====
        private readonly Dictionary<string, Func<PluginEntryPoint, object, bool>> _routeHandlers =
            new Dictionary<string, Func<PluginEntryPoint, object, bool>>(StringComparer.OrdinalIgnoreCase);

        private PluginHost(Window mainWindow)
        {
            _mainWindow = mainWindow;
            _pluginsRoot = App.RootPath + "Plugins\\";
        }

        /// <summary>初始化全局唯一 PluginHost 实例。仅应调用一次。</summary>
        public static PluginHost Initialize(Window mainWindow)
        {
            if (Instance != null) return Instance;
            Instance = new PluginHost(mainWindow);
            return Instance;
        }

        // ===== IPluginHost 实现 =====

        public string PluginDirectory => _pluginsRoot;
        public string HostRootPath => App.RootPath;
        public Window MainWindow => _mainWindow;

        public void Log(string message, PluginLogLevel logLevel = PluginLogLevel.Info)
        {
            LogHelper.WriteLogToFile(message, ToLogHelperType(logLevel));
        }

        private static LogHelper.LogType ToLogHelperType(PluginLogLevel level)
        {
            switch (level)
            {
                case PluginLogLevel.Trace: return LogHelper.LogType.Trace;
                case PluginLogLevel.Warning: return LogHelper.LogType.Warning;
                case PluginLogLevel.Error: return LogHelper.LogType.Error;
                case PluginLogLevel.Event: return LogHelper.LogType.Event;
                default: return LogHelper.LogType.Info;
            }
        }

        public void ShowNotification(string message)
        {
            try
            {
                (_mainWindow as MainWindow)?.ShowNotificationAsync(message);
            }
            catch { }
        }

        public bool TriggerRoute(string route, object parameter = null)
        {
            if (string.IsNullOrEmpty(route)) return false;
            if (_routeTable.TryGetValue(route, out var ep))
            {
                if (_routeHandlers.TryGetValue(route, out var handler))
                {
                    return handler(ep, parameter);
                }
            }
            return false;
        }

        // ===== 加载 / 卸载 =====

        /// <summary>扫描 Plugins 根目录下所有子目录，加载其中合法的 plugin</summary>
        public void LoadAll()
        {
            try
            {
                if (!Directory.Exists(_pluginsRoot))
                {
                    Directory.CreateDirectory(_pluginsRoot);
                    return;
                }

                foreach (var dir in Directory.GetDirectories(_pluginsRoot, "*", SearchOption.TopDirectoryOnly))
                {
                    LoadPluginFromDirectory(dir);
                }
            }
            catch (Exception ex)
            {
                LogHelper.WriteLogToFile($"PluginHost.LoadAll 失败: {ex.Message}", LogHelper.LogType.Error);
            }
        }

        private void LoadPluginFromDirectory(string pluginDir)
        {
            try
            {
                // 1) manifest 文件：plugin.icplugin
                string manifestFile = Path.Combine(pluginDir, "plugin.icplugin");
                if (!File.Exists(manifestFile))
                {
                    LogHelper.WriteLogToFile($"跳过插件目录（缺少 plugin.icplugin）: {pluginDir}", LogHelper.LogType.Warning);
                    return;
                }

                PluginManifest manifest;
                try
                {
                    string json = File.ReadAllText(manifestFile, System.Text.Encoding.UTF8);
                    manifest = JsonConvert.DeserializeObject<PluginManifest>(json);
                }
                catch (Exception ex)
                {
                    LogHelper.WriteLogToFile($"plugin manifest 解析失败 [{pluginDir}]: {ex.Message}", LogHelper.LogType.Error);
                    return;
                }

                if (manifest == null || string.IsNullOrWhiteSpace(manifest.Id))
                {
                    LogHelper.WriteLogToFile($"plugin manifest 无效 [{pluginDir}]: Id 为空", LogHelper.LogType.Warning);
                    return;
                }

                // 2) 重复加载检查
                if (_loaded.Any(p => string.Equals(p.Manifest.Id, manifest.Id, StringComparison.OrdinalIgnoreCase)))
                {
                    LogHelper.WriteLogToFile($"plugin 已加载，跳过: {manifest.Id}", LogHelper.LogType.Warning);
                    return;
                }

                // 3) 注册声明式入口点
                if (manifest.EntryPoints != null)
                {
                    foreach (var ep in manifest.EntryPoints)
                    {
                        if (string.IsNullOrWhiteSpace(ep?.Route)) continue;
                        _routeTable[ep.Route] = ep;
                    }
                }

                // 4) 加载程序集入口（若有）
                IPlugin pluginInstance = null;
                if (!string.IsNullOrWhiteSpace(manifest.EntryAssembly) && !string.IsNullOrWhiteSpace(manifest.EntryClass))
                {
                    string asmPath = Path.Combine(pluginDir, manifest.EntryAssembly);
                    if (!File.Exists(asmPath))
                    {
                        LogHelper.WriteLogToFile($"plugin 入口程序集不存在: {asmPath}", LogHelper.LogType.Warning);
                    }
                    else
                    {
                        try
                        {
                            var asm = Assembly.LoadFrom(asmPath);
                            var type = asm.GetType(manifest.EntryClass, throwOnError: false);
                            if (type == null || !typeof(IPlugin).IsAssignableFrom(type))
                            {
                                LogHelper.WriteLogToFile($"plugin 入口类无效: {manifest.EntryClass}", LogHelper.LogType.Warning);
                            }
                            else
                            {
                                pluginInstance = (IPlugin)Activator.CreateInstance(type);
                                pluginInstance.Initialize(this);
                            }
                        }
                        catch (Exception ex)
                        {
                            LogHelper.WriteLogToFile($"plugin 初始化失败 [{manifest.Id}]: {ex.Message}", LogHelper.LogType.Error);
                        }
                    }
                }

                _loaded.Add(new LoadedPlugin
                {
                    Manifest = manifest,
                    Directory = pluginDir,
                    Instance = pluginInstance
                });

                LogHelper.WriteLogToFile($"plugin 已加载: {manifest.Id} v{manifest.Version}", LogHelper.LogType.Event);
            }
            catch (Exception ex)
            {
                LogHelper.WriteLogToFile($"LoadPluginFromDirectory 失败 [{pluginDir}]: {ex.Message}", LogHelper.LogType.Error);
            }
        }

        /// <summary>关闭所有 plugin 并触发 ApplicationExiting 事件</summary>
        public void ShutdownAll()
        {
            try
            {
                ApplicationExiting?.Invoke(this, EventArgs.Empty);

                foreach (var p in _loaded)
                {
                    try { p.Instance?.Shutdown(); }
                    catch (Exception ex) { LogHelper.WriteLogToFile($"plugin 关闭异常 [{p.Manifest.Id}]: {ex.Message}", LogHelper.LogType.Error); }
                }
                _loaded.Clear();
                _routeTable.Clear();
            }
            catch (Exception ex)
            {
                LogHelper.WriteLogToFile($"PluginHost.ShutdownAll 失败: {ex.Message}", LogHelper.LogType.Error);
            }
        }

        // ===== 主程序向 PluginHost 注册路由处理器 =====

        /// <summary>
        /// 主程序注册对声明式入口点的处理逻辑。
        /// 例如注册 "video-presenter" → 显示视频展台侧栏。
        /// </summary>
        public void RegisterRouteHandler(string route, Func<PluginEntryPoint, object, bool> handler)
        {
            if (string.IsNullOrEmpty(route) || handler == null) return;
            _routeHandlers[route] = handler;
        }

        // ===== 查询接口 =====

        /// <summary>查询某个 plugin 是否已安装（仅判断 manifest 存在，不要求程序集入口）</summary>
        public bool IsPluginInstalled(string pluginId)
        {
            if (string.IsNullOrEmpty(pluginId)) return false;
            return _loaded.Any(p => string.Equals(p.Manifest.Id, pluginId, StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>查询某个声明式入口点是否已注册（即对应 plugin 已安装）</summary>
        public bool IsRouteAvailable(string route)
        {
            if (string.IsNullOrEmpty(route)) return false;
            return _routeTable.ContainsKey(route);
        }

        /// <summary>获取所有已加载 plugin 的 manifest（用于插件工坊展示）</summary>
        public IReadOnlyList<PluginManifest> GetAllManifests()
        {
            return _loaded.Select(p => p.Manifest).ToList();
        }

        /// <summary>触发白板模式变化事件（主程序在切换模式时调用）</summary>
        public void RaiseBoardModeChanged(int newMode)
        {
            BoardModeChanged?.Invoke(this, new BoardModeChangedEventArgs { NewMode = newMode });
        }

        private class LoadedPlugin
        {
            public PluginManifest Manifest { get; set; }
            public string Directory { get; set; }
            public IPlugin Instance { get; set; }
        }
    }
}
