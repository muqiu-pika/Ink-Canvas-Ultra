using System;
using System.Diagnostics;
using System.Reflection;

namespace Ink_Canvas.Helpers
{
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
