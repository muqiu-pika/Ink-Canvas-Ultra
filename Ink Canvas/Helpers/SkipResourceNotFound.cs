using System;
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
    /// - 警告由内部类 MS.Internal.TraceResourceDictionary 经 AvTrace 发出；
    ///   AvTrace.IsEnabledOverride 直接取决于其私有字段 _traceSource 是否为 null。
    /// - 通过反射把 _traceSource 置空即可整体关闭这条跟踪。
    ///
    /// 重要：本开关只影响"调试器输出"，不改变任何资源查找、主题切换与外观行为。
    /// 代价是同一条跟踪里的其它资源告警也会一并静音，因此仅作噪音治理；
    /// 资源键是否真的缺失仍应从根因排查（本项目涉及的 FloatBar*/BoardBar* 键
    /// 均已确认在 Resources/Styles/Light.xaml、Light-Board.xaml、Dark.xaml、Dark-Board.xaml 中定义）。
    /// </summary>
    internal static class SkipResourceNotFound
    {
        private static bool _installed;

        /// <summary>
        /// 在 App_Startup 最早处调用：此时 WPF 已初始化 TraceResourceDictionary
        /// （_traceSource 已因调试器附加而被创建），置空后即不再输出。
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

                var traceSourceField = avTrace.GetType().GetField("_traceSource", BindingFlags.NonPublic | BindingFlags.Instance);
                if (traceSourceField == null) return;

                traceSourceField.SetValue(avTrace, null);
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
