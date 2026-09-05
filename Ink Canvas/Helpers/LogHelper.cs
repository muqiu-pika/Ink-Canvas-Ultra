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
