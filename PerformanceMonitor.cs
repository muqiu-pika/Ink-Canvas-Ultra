using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;

namespace Ink_Canvas.Helpers
{
    public class PerformanceMonitor
    {
        private static PerformanceCounter _cpuCounter;
        private static PerformanceCounter _ramCounter;
        private static Timer _monitorTimer;
        private static Action<string> _logCallback;

        public static void Initialize(Action<string> logCallback)
        {
            _logCallback = logCallback;
            
            try
            {
                _cpuCounter = new PerformanceCounter("Processor", "% Processor Time", "_Total");
                _ramCounter = new PerformanceCounter("Memory", "Available MBytes");
                
                // 每5秒监控一次
                _monitorTimer = new Timer(CollectMetrics, null, TimeSpan.Zero, TimeSpan.FromSeconds(5));
            }
            catch (Exception ex)
            {
                _logCallback?.Invoke($"性能监控初始化失败: {ex.Message}");
            }
        }

        private static void CollectMetrics(object state)
        {
            try
            {
                var cpuUsage = _cpuCounter.NextValue();
                var availableRAM = _ramCounter.NextValue();
                var process = Process.GetCurrentProcess();
                var workingSet = process.WorkingSet64 / 1024 / 1024; // MB
                var handleCount = process.HandleCount;
                var threadCount = process.Threads.Count;

                var message = $"CPU: {cpuUsage:F1}% | 内存: {workingSet}MB | 可用内存: {availableRAM}MB | 句柄: {handleCount} | 线程: {threadCount}";
                
                // 高占用警告
                if (cpuUsage > 80 || workingSet > 500)
                {
                    message = $"⚠️ 高占用: {message}";
                }
                
                _logCallback?.Invoke(message);
            }
            catch (Exception ex)
            {
                _logCallback?.Invoke($"监控数据收集失败: {ex.Message}");
            }
        }

        public static void Stop()
        {
            _monitorTimer?.Dispose();
            _cpuCounter?.Dispose();
            _ramCounter?.Dispose();
        }

        public static long MeasureMemoryUsage()
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            
            return GC.GetTotalMemory(false);
        }

        public static void LogMemoryLeakCheck()
        {
            var before = GC.GetTotalMemory(false);
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            var after = GC.GetTotalMemory(false);
            
            _logCallback?.Invoke($"内存检查: {before / 1024 / 1024}MB -> {after / 1024 / 1024}MB (差异: {(after - before) / 1024 / 1024}MB)");
        }
    }
}