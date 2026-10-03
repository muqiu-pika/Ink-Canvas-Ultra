using System;
using System.Diagnostics;
using System.Reflection;

namespace Ink_Canvas.Helpers
{
    /// <summary>
    /// 关闭调试输出中的 WPF 噪音：
    /// "System.Windows.ResourceDictionary Warning: 9 : Resource not found; ResourceKey='xxx'"
    ///
    /// 依据（微软官方答复 + 对 WPF 内部源码的反编译分析）：
    /// - 该警告只在使用 DynamicResource、且"附加了调试器 / 注册表开启 ManagedTracing /
    ///   调用过 PresentationTraceSources.Refresh()"的情况下产生；
    ///   生产环境（无调试器）不会输出，也不会对界面产生任何影响。
    /// - 警告由内部类 MS.Internal.TraceResourceDictionary 经 AvTrace 发出。
    ///
    /// 【重要】AvTrace 内部有 **两个互不相干** 的开关，必须同时处理，不能只把 _traceSource 置空：
    ///
    ///   IsEnabledOverride => (_traceSource != null)
    ///       使用者只有 FrameworkElement.FindResourceInternal —— 也就是 "Resource not found" 警告本身；
    ///   IsEnabled         => (_isEnabled 这个被缓存的 bool)
    ///       使用者是 ResourceDictionary.SetKeys 等一批内部路径，
    ///       这些路径最终走到 AvTrace.Trace(...) → _traceSource.TraceEvent(...) 写日志。
    ///
    /// 只把 _traceSource 置为 null 的后果（曾真实引发 v26.10.2 的启动崩溃 + 崩溃重启死循环）：
    ///   IsEnabledOverride 变成 false（警告确实消失），但 IsEnabled 仍为 true，
    ///   于是 SetKeys → TraceActivityItem → AvTrace.Trace → _traceSource.TraceEvent
    ///   对 null 解引用抛 NullReferenceException。
    ///   该异常发生在 XAML 加载 ResourceDictionary.Source（MainWindow.xaml 的合并字典）的过程中，
    ///   会把 MainWindow 的 InitializeComponent 直接打断，导致主窗口构造失败；
    ///   而崩溃处理器又会执行"保存快照 + 静默重启"，于是形成"启动 → 崩溃 → 重启"的死循环。
    ///
    /// 现在的做法：**绝不动 _traceSource 的引用**
    ///   1) 把该 TraceSource 的开关打到 Critical（WPF 自身不产生 Critical 级跟踪）并清空监听器
    ///      —— IsEnabledOverride 路径即便走到 Trace 也写不出任何内容；
    ///   2) 若目标版本上存在 _isEnabled 字段，则一并置 false —— IsEnabled 路径直接短路，连 Trace 都不会进。
    /// 两个开关都关掉、且不存在 null 解引用，因此既没有噪音也不会抛异常。
    ///
    /// 注意：开关用 Critical 而不是 Off —— 当跟踪是在 .config 中开启时，
    /// WPF 在调试器下会把 Off 提升为 Warning，而 Critical 不会被提升。
    ///
    /// 代价是同一条跟踪里的其它资源告警也会一并静音，因此仅作噪音治理；
    /// 资源键是否真的缺失仍应从根因排查（本项目涉及的 FloatBar*/BoardBar* 键
    /// 均已确认在 Resources/Styles/Light.xaml、Light-Board.xaml、Dark.xaml、Dark-Board.xaml 中定义）。
    /// </summary>
    internal static class SkipResourceNotFound
    {
        private static bool _installed;

        /// <summary>
        /// 在 App_Startup 最早处调用：此时 WPF 已初始化 TraceResourceDictionary
        /// （_traceSource 已因调试器附加 / ManagedTracing 而被创建），关掉后即不再输出。
        /// </summary>
        public static void Install()
        {
            if (_installed) return;
            _installed = true;

            try
            {
                var traceResDictType = FindType("MS.Internal.TraceResourceDictionary");
                if (traceResDictType == null) return;

                var avTraceField = traceResDictType.GetField("_avTrace", BindingFlags.NonPublic | BindingFlags.Static);
                if (avTraceField == null) return;

                var avTrace = avTraceField.GetValue(null);
                if (avTrace == null) return;

                var avTraceType = avTrace.GetType();

                // 1) 保留 _traceSource 引用，只把开关打到 Critical 并清空监听器。
                var traceSourceField = avTraceType.GetField("_traceSource", BindingFlags.NonPublic | BindingFlags.Instance);
                var traceSource = traceSourceField == null
                    ? null
                    : traceSourceField.GetValue(avTrace) as TraceSource;

                if (traceSource != null)
                {
                    try
                    {
                        if (traceSource.Switch != null) traceSource.Switch.Level = SourceLevels.Critical;
                    }
                    catch
                    {
                        // 个别 WPF 版本上 Switch 可能为 null 或不支持写入，忽略即可。
                    }

                    try { traceSource.Listeners.Clear(); } catch { }
                }

                // 2) 把 AvTrace 的"是否启用"缓存字段置 false，让 IsEnabled 路径直接短路。
                //    字段名随 WPF 版本可能变化，找不到就跳过 —— 第 1 步已足够消除噪音与异常。
                var isEnabledField = avTraceType.GetField("_isEnabled", BindingFlags.NonPublic | BindingFlags.Instance);
                if (isEnabledField != null && isEnabledField.FieldType == typeof(bool))
                {
                    try { isEnabledField.SetValue(avTrace, false); } catch { }
                }
            }
            catch
            {
                // 仅为消除调试输出噪音；反射失败（如未来 WPF 内部实现变化）不应影响应用启动。
            }
        }

        /// <summary>
        /// WPF 内部类型需在 PresentationFramework 等程序集中查找；
        /// 逐个尝试 AppDomain 已加载的程序集（App_Startup 时均已加载）。
        /// </summary>
        private static Type FindType(string typeName)
        {
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                try
                {
                    var type = assembly.GetType(typeName, false);
                    if (type != null) return type;
                }
                catch
                {
                    // 单个程序集反射失败不影响继续查找。
                }
            }
            return null;
        }
    }
}
