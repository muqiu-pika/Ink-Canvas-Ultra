using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using Ink_Canvas.Helpers;
using Newtonsoft.Json;

namespace Ink_Canvas.Plugins
{
    /// <summary>
    /// plugin 宿主管理器：负责扫描、加载、初始化、卸载所有 plugin，
    /// 并实现 IPluginHost 接口向 plugin 提供主程序能力。
    /// 支持运行时加载/卸载（软卸载，已加载程序集不释放）。
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

        // ===== 主程序能力委托（由 MainWindow 注入，避免硬耦合） =====
        private readonly PluginHostOptions _opts;
        private readonly string _pluginsStateFile;      // plugins.json 启用状态持久化
        private readonly Dictionary<string, bool> _enabledState = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);

        // ===== 插件注册跟踪（用于 UnloadPlugin 时自动清理） =====
        // 当前正在加载的 pluginId（在 LoadPluginFromDirectory 期间设置，RegisterRouteHandler/RegisterSelectionControlBar 时关联）
        private string _currentLoadingPluginId;
        // route -> pluginId（插件通过 RegisterRouteHandler 注册的路由，按 pluginId 跟踪）
        private readonly Dictionary<string, string> _routeOwners = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        // pluginId -> 插件注册的选择控制条 UI 列表
        private readonly Dictionary<string, List<UIElement>> _pluginControlBars = new Dictionary<string, List<UIElement>>(StringComparer.OrdinalIgnoreCase);

        // ===== 事件 =====
        public event EventHandler ApplicationExiting;
        public event EventHandler<BoardModeChangedEventArgs> BoardModeChanged;
        public event EventHandler<PluginElementSelectionChangedEventArgs> ElementSelectionChanged;
        public event EventHandler<PluginElementEventArgs> ElementRemoved;
        public event EventHandler<PluginElementEventArgs> ElementTransformed;

        /// <summary>已安装 plugin 列表或启用状态发生变化时触发，供插件工坊 UI 刷新</summary>
        public event EventHandler PluginListChanged;

        // ===== 路由处理委托 =====
        // 主程序内建处理器
        private readonly Dictionary<string, Func<PluginEntryPoint, object, bool>> _builtinRouteHandlers =
            new Dictionary<string, Func<PluginEntryPoint, object, bool>>(StringComparer.OrdinalIgnoreCase);
        // plugin 注册的处理器（优先级高于内建）
        private readonly Dictionary<string, Func<object, bool>> _pluginRouteHandlers =
            new Dictionary<string, Func<object, bool>>(StringComparer.OrdinalIgnoreCase);

        private PluginHost(Window mainWindow, PluginHostOptions options)
        {
            _mainWindow = mainWindow;
            _opts = options ?? new PluginHostOptions();
            _pluginsRoot = App.RootPath + "Plugins\\";
            _pluginsStateFile = Path.Combine(_pluginsRoot, "plugins.json");
            LoadEnabledState();
        }

        /// <summary>初始化全局唯一 PluginHost 实例。仅应调用一次。</summary>
        public static PluginHost Initialize(Window mainWindow, PluginHostOptions options = null)
        {
            if (Instance != null) return Instance;
            Instance = new PluginHost(mainWindow, options);
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

            // 优先 plugin 注册的处理器
            if (_pluginRouteHandlers.TryGetValue(route, out var pluginHandler))
            {
                try { return pluginHandler(parameter); } catch (Exception ex)
                {
                    LogHelper.WriteLogToFile($"plugin 路由处理器异常 [{route}]: {ex.Message}", LogHelper.LogType.Error);
                }
            }

            // 其次主程序内建处理器
            if (_routeTable.TryGetValue(route, out var ep))
            {
                if (_builtinRouteHandlers.TryGetValue(route, out var handler))
                {
                    return handler(ep, parameter);
                }
            }
            return false;
        }

        public void RegisterRouteHandler(string route, Func<object, bool> handler)
        {
            if (string.IsNullOrEmpty(route) || handler == null) return;
            _pluginRouteHandlers[route] = handler;
            // 关联到当前正在加载的 pluginId，供 UnloadPlugin 自动清理
            if (!string.IsNullOrEmpty(_currentLoadingPluginId))
            {
                _routeOwners[route] = _currentLoadingPluginId;
            }
        }

        public void UnregisterRouteHandler(string route)
        {
            if (string.IsNullOrEmpty(route)) return;
            _pluginRouteHandlers.Remove(route);
            _routeOwners.Remove(route);
        }

        // ===== 画布与元素 API =====

        public InkCanvas GetInkCanvas()
        {
            try { return _opts.GetInkCanvas?.Invoke(); } catch { }
            return null;
        }

        public IReadOnlyList<UIElement> GetSelectedElements()
        {
            try { return _opts.GetSelectedElements?.Invoke() ?? new List<UIElement>(); } catch { }
            return new List<UIElement>();
        }

        public void CommitElementInsertHistory(UIElement element)
        {
            try { _opts.CommitElementInsertHistory?.Invoke(element); } catch { }
        }

        public string AutoSavedStrokesLocation
        {
            get
            {
                try { return _opts.GetAutoSavedStrokesLocation?.Invoke() ?? string.Empty; } catch { }
                return string.Empty;
            }
        }

        // ===== 选择控制条插槽 =====

        public void RegisterSelectionControlBar(UIElement controlBar)
        {
            try
            {
                _opts.RegisterSelectionControlBar?.Invoke(controlBar);
                // 关联到当前正在加载的 pluginId，供 UnloadPlugin 自动清理
                if (!string.IsNullOrEmpty(_currentLoadingPluginId))
                {
                    if (!_pluginControlBars.TryGetValue(_currentLoadingPluginId, out var list))
                    {
                        list = new List<UIElement>();
                        _pluginControlBars[_currentLoadingPluginId] = list;
                    }
                    if (!list.Contains(controlBar)) list.Add(controlBar);
                }
            }
            catch { }
        }

        public void UnregisterSelectionControlBar(UIElement controlBar)
        {
            try
            {
                _opts.UnregisterSelectionControlBar?.Invoke(controlBar);
                // 从所有 pluginId 的列表中移除
                foreach (var kv in _pluginControlBars)
                {
                    kv.Value.Remove(controlBar);
                }
            }
            catch { }
        }

        // ===== 事件触发（主程序调用） =====

        public void RaiseBoardModeChanged(int newMode)
        {
            BoardModeChanged?.Invoke(this, new BoardModeChangedEventArgs { NewMode = newMode });
        }

        public void RaiseElementSelectionChanged(IReadOnlyList<UIElement> selected)
        {
            ElementSelectionChanged?.Invoke(this, new PluginElementSelectionChangedEventArgs { SelectedElements = selected });
        }

        public void RaiseElementRemoved(UIElement element)
        {
            ElementRemoved?.Invoke(this, new PluginElementEventArgs { Element = element });
        }

        public void RaiseElementTransformed(UIElement element)
        {
            ElementTransformed?.Invoke(this, new PluginElementEventArgs { Element = element });
        }

        private void RaisePluginListChanged()
        {
            try { PluginListChanged?.Invoke(this, EventArgs.Empty); } catch { }
        }

        // ===== 启动时加载 =====

        /// <summary>扫描 Plugins 根目录下所有子目录，加载其中已启用的 plugin</summary>
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
                    var manifest = TryReadManifest(dir);
                    if (manifest == null) continue;

                    // 启动时默认全部启用，除非 plugins.json 显式禁用
                    bool enabled;
                    if (!_enabledState.TryGetValue(manifest.Id, out enabled))
                    {
                        enabled = true;
                        _enabledState[manifest.Id] = true;
                    }
                    if (enabled)
                    {
                        LoadPluginFromDirectory(dir);
                    }
                }
            }
            catch (Exception ex)
            {
                LogHelper.WriteLogToFile($"PluginHost.LoadAll 失败: {ex.Message}", LogHelper.LogType.Error);
            }
        }

        // ===== 运行时加载 / 卸载（供插件工坊调用） =====

        /// <summary>运行时加载指定 id 的 plugin（若已加载则返回 false）</summary>
        public bool LoadPlugin(string pluginId)
        {
            if (string.IsNullOrEmpty(pluginId)) return false;

            if (_loaded.Any(p => string.Equals(p.Manifest.Id, pluginId, StringComparison.OrdinalIgnoreCase)))
            {
                return false;
            }

            var dir = FindPluginDirectory(pluginId);
            if (dir == null) return false;

            LoadPluginFromDirectory(dir);
            RaisePluginListChanged();
            return _loaded.Any(p => string.Equals(p.Manifest.Id, pluginId, StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>软卸载指定 id 的 plugin：调用 Shutdown、解绑事件、移除路由/UI、从已加载列表移除</summary>
        public bool UnloadPlugin(string pluginId)
        {
            if (string.IsNullOrEmpty(pluginId)) return false;

            var idx = _loaded.FindIndex(x => string.Equals(x.Manifest.Id, pluginId, StringComparison.OrdinalIgnoreCase));
            if (idx < 0) return false;

            var p = _loaded[idx];
            try
            {
                p.Instance?.Shutdown();
            }
            catch (Exception ex)
            {
                LogHelper.WriteLogToFile($"plugin Shutdown 异常 [{p.Manifest.Id}]: {ex.Message}", LogHelper.LogType.Error);
            }

            // 移除该 plugin 声明式入口点对应的路由
            if (p.Manifest.EntryPoints != null)
            {
                foreach (var ep in p.Manifest.EntryPoints)
                {
                    if (string.IsNullOrWhiteSpace(ep?.Route)) continue;
                    _routeTable.Remove(ep.Route);
                    _pluginRouteHandlers.Remove(ep.Route);
                    _routeOwners.Remove(ep.Route);
                }
            }

            // 自动清理：插件通过 RegisterRouteHandler 注册但未在 manifest 中声明的路由
            var extraRoutes = _routeOwners
                .Where(kv => string.Equals(kv.Value, pluginId, StringComparison.OrdinalIgnoreCase))
                .Select(kv => kv.Key)
                .ToList();
            foreach (var route in extraRoutes)
            {
                _routeTable.Remove(route);
                _pluginRouteHandlers.Remove(route);
                _routeOwners.Remove(route);
            }

            // 自动清理：插件通过 RegisterSelectionControlBar 注册但未在 Shutdown 中反注册的 UI
            if (_pluginControlBars.TryGetValue(pluginId, out var bars))
            {
                foreach (var bar in bars.ToList())
                {
                    try { _opts.UnregisterSelectionControlBar?.Invoke(bar); }
                    catch (Exception ex) { LogHelper.WriteLogToFile($"自动移除插件 UI 失败 [{pluginId}]: {ex.Message}", LogHelper.LogType.Warning); }
                }
                _pluginControlBars.Remove(pluginId);
            }

            _loaded.RemoveAt(idx);
            LogHelper.WriteLogToFile($"plugin 已软卸载: {p.Manifest.Id}", LogHelper.LogType.Event);
            RaisePluginListChanged();
            return true;
        }

        /// <summary>查询某个 plugin 的启用状态（默认启用）</summary>
        public bool IsPluginEnabled(string pluginId)
        {
            if (string.IsNullOrEmpty(pluginId)) return false;
            return _enabledState.TryGetValue(pluginId, out var enabled) ? enabled : true;
        }

        /// <summary>设置 plugin 启用状态并持久化；true → LoadPlugin，false → UnloadPlugin</summary>
        public void SetPluginEnabled(string pluginId, bool enabled)
        {
            if (string.IsNullOrEmpty(pluginId)) return;
            _enabledState[pluginId] = enabled;
            SaveEnabledState();

            if (enabled)
            {
                LoadPlugin(pluginId);
            }
            else
            {
                UnloadPlugin(pluginId);
            }
        }

        /// <summary>查询某个 plugin 是否已加载（运行时活动）</summary>
        public bool IsPluginLoaded(string pluginId)
        {
            if (string.IsNullOrEmpty(pluginId)) return false;
            return _loaded.Any(p => string.Equals(p.Manifest.Id, pluginId, StringComparison.OrdinalIgnoreCase));
        }

        // ===== 主程序向 PluginHost 注册内建路由处理器 =====

        public void RegisterRouteHandler(string route, Func<PluginEntryPoint, object, bool> handler)
        {
            if (string.IsNullOrEmpty(route) || handler == null) return;
            _builtinRouteHandlers[route] = handler;
        }

        // ===== 查询接口 =====

        /// <summary>查询某个声明式入口点是否已注册（即对应 plugin 已安装且启用）</summary>
        public bool IsRouteAvailable(string route)
        {
            if (string.IsNullOrEmpty(route)) return false;
            return _routeTable.ContainsKey(route);
        }

        /// <summary>获取所有已加载 plugin 的 manifest</summary>
        public IReadOnlyList<PluginManifest> GetLoadedManifests()
        {
            return _loaded.Select(p => p.Manifest).ToList();
        }

        /// <summary>获取所有已安装 plugin 的 manifest（含禁用状态），用于插件工坊展示</summary>
        public IReadOnlyList<InstalledPluginInfo> GetAllInstalledPlugins()
        {
            var result = new List<InstalledPluginInfo>();
            if (!Directory.Exists(_pluginsRoot)) return result;

            foreach (var dir in Directory.GetDirectories(_pluginsRoot, "*", SearchOption.TopDirectoryOnly))
            {
                var manifest = TryReadManifest(dir);
                if (manifest == null) continue;

                result.Add(new InstalledPluginInfo
                {
                    Manifest = manifest,
                    Directory = dir,
                    IsEnabled = IsPluginEnabled(manifest.Id),
                    IsLoaded = IsPluginLoaded(manifest.Id)
                });
            }
            return result;
        }

        // ===== 内部辅助 =====

        private PluginManifest TryReadManifest(string pluginDir)
        {
            try
            {
                string manifestFile = Path.Combine(pluginDir, "plugin.icplugin");
                if (!File.Exists(manifestFile)) return null;
                string json = File.ReadAllText(manifestFile, System.Text.Encoding.UTF8);
                return JsonConvert.DeserializeObject<PluginManifest>(json);
            }
            catch (Exception ex)
            {
                LogHelper.WriteLogToFile($"plugin manifest 读取失败 [{pluginDir}]: {ex.Message}", LogHelper.LogType.Warning);
                return null;
            }
        }

        private string FindPluginDirectory(string pluginId)
        {
            if (!Directory.Exists(_pluginsRoot)) return null;
            foreach (var dir in Directory.GetDirectories(_pluginsRoot, "*", SearchOption.TopDirectoryOnly))
            {
                var manifest = TryReadManifest(dir);
                if (manifest != null && string.Equals(manifest.Id, pluginId, StringComparison.OrdinalIgnoreCase))
                {
                    return dir;
                }
            }
            return null;
        }

        private void LoadPluginFromDirectory(string pluginDir)
        {
            try
            {
                var manifest = TryReadManifest(pluginDir);
                if (manifest == null || string.IsNullOrWhiteSpace(manifest.Id))
                {
                    LogHelper.WriteLogToFile($"plugin manifest 无效 [{pluginDir}]", LogHelper.LogType.Warning);
                    return;
                }

                // 重复加载检查
                if (_loaded.Any(p => string.Equals(p.Manifest.Id, manifest.Id, StringComparison.OrdinalIgnoreCase)))
                {
                    LogHelper.WriteLogToFile($"plugin 已加载，跳过: {manifest.Id}", LogHelper.LogType.Trace);
                    return;
                }

                // 注册声明式入口点
                if (manifest.EntryPoints != null)
                {
                    foreach (var ep in manifest.EntryPoints)
                    {
                        if (string.IsNullOrWhiteSpace(ep?.Route)) continue;
                        _routeTable[ep.Route] = ep;
                    }
                }

                // 加载程序集入口（若有）
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
                            // 使用 Assembly.Load(byte[]) 代替 Assembly.LoadFrom(string)
                            // 原因：.NET Framework 下 Assembly.LoadFrom 会锁定 DLL 文件，
                            // 导致覆盖安装时 Directory.Delete 失败（需要重启才能释放文件锁）。
                            // Assembly.Load(byte[]) 从字节数组加载，不锁定文件，
                            // 覆盖安装时可以自由删除/替换 DLL，实现真正的热加载。
                            byte[] asmBytes = File.ReadAllBytes(asmPath);
                            var asm = Assembly.Load(asmBytes);
                            var type = asm.GetType(manifest.EntryClass, throwOnError: false);
                            if (type == null || !typeof(IPlugin).IsAssignableFrom(type))
                            {
                                LogHelper.WriteLogToFile($"plugin 入口类无效: {manifest.EntryClass}", LogHelper.LogType.Warning);
                            }
                            else
                            {
                                pluginInstance = (IPlugin)Activator.CreateInstance(type);
                                // 设置当前加载上下文，让 RegisterRouteHandler/RegisterSelectionControlBar
                                // 能自动关联到该 pluginId
                                _currentLoadingPluginId = manifest.Id;
                                try
                                {
                                    pluginInstance.Initialize(this);
                                }
                                finally
                                {
                                    _currentLoadingPluginId = null;
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            LogHelper.WriteLogToFile($"plugin 初始化失败 [{manifest.Id}]: {ex.Message}", LogHelper.LogType.Error);
                        }
                    }
                }

                // Initialize 失败（pluginInstance == null）时不添加到 _loaded，
                // 否则后续 LoadPlugin 会误判为"已加载"而拒绝重新加载
                if (pluginInstance == null && !string.IsNullOrWhiteSpace(manifest.EntryAssembly))
                {
                    // 清理已注册的声明式入口点（因为插件实际未成功加载）
                    if (manifest.EntryPoints != null)
                    {
                        foreach (var ep in manifest.EntryPoints)
                        {
                            if (string.IsNullOrWhiteSpace(ep?.Route)) continue;
                            _routeTable.Remove(ep.Route);
                        }
                    }
                    LogHelper.WriteLogToFile($"plugin 入口实例创建失败，未添加到已加载列表 [{manifest.Id}]", LogHelper.LogType.Warning);
                    return;
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
                _pluginRouteHandlers.Clear();
                _routeOwners.Clear();
                _pluginControlBars.Clear();
            }
            catch (Exception ex)
            {
                LogHelper.WriteLogToFile($"PluginHost.ShutdownAll 失败: {ex.Message}", LogHelper.LogType.Error);
            }
        }

        // ===== 启用状态持久化 =====

        private void LoadEnabledState()
        {
            try
            {
                if (!File.Exists(_pluginsStateFile)) return;
                string json = File.ReadAllText(_pluginsStateFile, System.Text.Encoding.UTF8);
                var dict = JsonConvert.DeserializeObject<Dictionary<string, bool>>(json);
                if (dict != null)
                {
                    foreach (var kv in dict) _enabledState[kv.Key] = kv.Value;
                }
            }
            catch (Exception ex)
            {
                LogHelper.WriteLogToFile($"读取 plugins.json 失败: {ex.Message}", LogHelper.LogType.Warning);
            }
        }

        private void SaveEnabledState()
        {
            try
            {
                if (!Directory.Exists(_pluginsRoot))
                    Directory.CreateDirectory(_pluginsRoot);
                string json = JsonConvert.SerializeObject(_enabledState, Formatting.Indented);
                File.WriteAllText(_pluginsStateFile, json, System.Text.Encoding.UTF8);
            }
            catch (Exception ex)
            {
                LogHelper.WriteLogToFile($"写入 plugins.json 失败: {ex.Message}", LogHelper.LogType.Warning);
            }
        }

        // ===== 内部类型 =====

        private class LoadedPlugin
        {
            public PluginManifest Manifest { get; set; }
            public string Directory { get; set; }
            public IPlugin Instance { get; set; }
        }
    }

    /// <summary>主程序注入 PluginHost 的能力委托集合</summary>
    public class PluginHostOptions
    {
        public Func<InkCanvas> GetInkCanvas { get; set; }
        public Func<IReadOnlyList<UIElement>> GetSelectedElements { get; set; }
        public Action<UIElement> CommitElementInsertHistory { get; set; }
        public Func<string> GetAutoSavedStrokesLocation { get; set; }
        public Action<UIElement> RegisterSelectionControlBar { get; set; }
        public Action<UIElement> UnregisterSelectionControlBar { get; set; }
    }

    /// <summary>已安装 plugin 信息（含启用/加载状态），用于插件工坊展示</summary>
    public class InstalledPluginInfo
    {
        public PluginManifest Manifest { get; set; }
        public string Directory { get; set; }
        public bool IsEnabled { get; set; }
        public bool IsLoaded { get; set; }
    }
}
