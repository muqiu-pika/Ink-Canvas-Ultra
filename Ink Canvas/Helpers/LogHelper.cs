using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;

namespace Ink_Canvas.Helpers
{
    class LogHelper
    {
        public static string LogFile = "Log.txt";

        // 跨线程/跨进程写日志时的同步锁：保证同一进程内多线程串行写，
        // 配合 FileShare.ReadWrite 使用，从根因消除“文件正由另一进程使用”的 IOException。
        private static readonly object _logLock = new object();

        // 日志文件大小封顶：超过该值时截断，只保留尾部若干字节，避免长期运行日志无限增长占用磁盘。
        // 与 MainWindow 启动期“超过 512KB 整删”的清理互补：运行中长会话期间日志也能被压住。
        private const long LogFileTrimThresholdBytes = 1024 * 1024;   // 1MB 触发截断
        private const int LogFileTrimTailBytes = 512 * 1024;          // 保留最后 512KB

        public static void NewLog(string str)
        {
            WriteLogToFile(str, LogType.Info);
        }

        public static void NewLog(Exception ex)
        {
            WriteLogToFile(ex.ToString(), LogType.Error);
        }

        public static void WriteLogToFile(string str, LogType logType = LogType.Info)
        {
            string strLogType = GetLogTypeLabel(logType);
            try
            {
                var file = App.RootPath + LogFile;
                if (!Directory.Exists(App.RootPath))
                {
                    Directory.CreateDirectory(App.RootPath);
                }
                lock (_logLock)
                {
                    TryTrimLogFile(file);
                    // FileShare.ReadWrite 允许其他进程/线程同时读写该文件，
                    // 从根因消除“文件正由另一进程使用”的 IOException。
                    using (var fs = new FileStream(file, FileMode.Append, FileAccess.Write, FileShare.ReadWrite))
                    using (var sw = new StreamWriter(fs))
                    {
                        sw.WriteLine(string.Format("{0} [{1}] {2}", DateTime.Now.ToString("O"), strLogType, str));
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"LogHelper | WriteLogToFile failed: {ex}");
            }
        }

        /// <summary>日志超过阈值时截断为尾部内容；失败静默，不影响本次写入。</summary>
        private static void TryTrimLogFile(string file)
        {
            try
            {
                var fi = new FileInfo(file);
                if (!fi.Exists || fi.Length <= LogFileTrimThresholdBytes) return;

                string tail;
                using (var src = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                {
                    long start = Math.Max(0, src.Length - LogFileTrimTailBytes);
                    src.Seek(start, SeekOrigin.Begin);
                    // 对齐到下一行行首，避免截出半行
                    if (start > 0)
                    {
                        int b;
                        while ((b = src.ReadByte()) != -1 && b != '\n') { }
                    }
                    using (var sr = new StreamReader(src))
                    {
                        tail = sr.ReadToEnd();
                    }
                }

                File.WriteAllText(file, tail);
            }
            catch
            {
                // 截断失败不影响本次日志写入
            }
        }

        public static void WriteObjectLogToFile(object obj, LogType logType = LogType.Info)
        {
            string strLogType = GetLogTypeLabel(logType);
            try
            {
                var file = App.RootPath + LogFile;
                if (!Directory.Exists(App.RootPath))
                {
                    Directory.CreateDirectory(App.RootPath);
                }
                lock (_logLock)
                {
                    using (var fs = new FileStream(file, FileMode.Append, FileAccess.Write, FileShare.ReadWrite))
                    using (var sw = new StreamWriter(fs))
                    {
                        sw.WriteLine($"{DateTime.Now:O} [{strLogType}] Object Log:");
                        if (obj != null)
                        {
                            Type type = obj.GetType();
                            PropertyInfo[] properties = type.GetProperties();
                            foreach (PropertyInfo property in properties)
                            {
                                object value = property.GetValue(obj, null);
                                sw.WriteLine($"{property.Name}: {value}");
                            }
                        }
                        else
                        {
                            sw.WriteLine("null");
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"LogHelper | WriteObjectLogToFile failed: {ex}");
            }
        }

        private static string GetLogTypeLabel(LogType logType)
        {
            switch (logType)
            {
                case LogType.Event:
                    return "Event";
                case LogType.Trace:
                    return "Trace";
                case LogType.Warning:
                    return "Warning";
                case LogType.Error:
                    return "Error";
                default:
                    return "Info";
            }
        }

        public enum LogType
        {
            Info,
            Trace,
            Warning,
            Error,
            Event
        }
    }
}
