using System;
using System.Windows;

namespace Ink_Canvas.Helpers
{
    internal static class ResourceDictionaryHelper
    {
        public static readonly Uri LightPopupWindow = new Uri("Resources/Styles/Light-PopupWindow.xaml", UriKind.Relative);

        public static readonly Uri DarkPopupWindow = new Uri("Resources/Styles/Dark-PopupWindow.xaml", UriKind.Relative);

        /// <summary>浅色主主题字典（FloatBar*、SettingsPage* 等）</summary>
        public static readonly Uri LightTheme = new Uri("Resources/Styles/Light.xaml", UriKind.Relative);

        /// <summary>
        /// 浅色白板栏字典（BoardBar* 等）。
        /// 注意：BoardBar* 与 FloatBar* 并不同源 —— 前者只定义在 Light-Board.xaml / Dark-Board.xaml，
        /// 后者只定义在 Light.xaml / Dark.xaml，是两个彼此独立的文件，必须成对合并才不会漏键。
        /// </summary>
        public static readonly Uri LightBoard = new Uri("Resources/Styles/Light-Board.xaml", UriKind.Relative);

        public static void EnsureStartupThemeKeys()
        {
            MergeOnce(LightTheme);
            MergeOnce(LightBoard);
        }

        /// <summary>
        /// 按主题应用弹窗样式字典（幂等，不会重复堆积）。
        /// </summary>
        public static void ApplyPopupWindowTheme(bool isLight)
        {
            MergeOnce(isLight ? LightPopupWindow : DarkPopupWindow);
        }

        /// <summary>
        /// 按主题应用弹窗样式字典（幂等），theme 传入 "Light" / "Dark"。
        /// </summary>
        public static void ApplyPopupWindowTheme(string theme)
        {
            ApplyPopupWindowTheme(string.Equals(theme, "Light", StringComparison.OrdinalIgnoreCase));
        }

        public static ResourceDictionary MergeOnce(string relativeSource)
        {
            if (string.IsNullOrEmpty(relativeSource)) return null;
            return MergeOnce(new Uri(relativeSource, UriKind.Relative));
        }

        /// <summary>
        /// 把指定资源字典合并到应用级资源中；若已存在同一 Source，则复用已有实例，
        /// 仅在其不处于末尾时挪到末尾（保证覆盖优先级与原先“直接 Add”一致）。
        /// </summary>
        public static ResourceDictionary MergeOnce(Uri source)
        {
            if (source == null) return null;
            var app = Application.Current;
            if (app == null) return null;

            try
            {
                var dictionaries = app.Resources.MergedDictionaries;
                for (int i = 0; i < dictionaries.Count; i++)
                {
                    var dictionary = dictionaries[i];
                    if (dictionary == null || !IsSameSource(dictionary.Source, source)) continue;

                    if (i != dictionaries.Count - 1)
                    {
                        dictionaries.RemoveAt(i);
                        dictionaries.Add(dictionary);
                    }
                    return dictionary;
                }

                var created = new ResourceDictionary { Source = source };
                dictionaries.Add(created);
                return created;
            }
            catch (Exception ex)
            {
                LogHelper.WriteLogToFile($"合并资源字典失败({source}): {ex.Message}", LogHelper.LogType.Error);
                return null;
            }
        }

        /// <summary>
        /// 从应用级资源中移除指定 Source 的所有字典（含历史遗留的重复项）。
        /// </summary>
        public static int Remove(Uri source)
        {
            if (source == null) return 0;
            var app = Application.Current;
            if (app == null) return 0;

            int removed = 0;
            try
            {
                var dictionaries = app.Resources.MergedDictionaries;
                for (int i = dictionaries.Count - 1; i >= 0; i--)
                {
                    var dictionary = dictionaries[i];
                    if (dictionary == null || !IsSameSource(dictionary.Source, source)) continue;
                    dictionaries.RemoveAt(i);
                    removed++;
                }
            }
            catch (Exception ex)
            {
                LogHelper.WriteLogToFile($"移除资源字典失败({source}): {ex.Message}", LogHelper.LogType.Error);
            }
            return removed;
        }

        /// <summary>
        /// 兼容相对 URI 与 pack:// 绝对 URI 的 Source 比较。
        /// </summary>
        private static bool IsSameSource(Uri left, Uri right)
        {
            if (left == null || right == null) return false;
            return string.Equals(Normalize(left), Normalize(right), StringComparison.OrdinalIgnoreCase);
        }

        private static string Normalize(Uri uri)
        {
            var text = uri.IsAbsoluteUri ? uri.ToString() : uri.OriginalString;
            if (string.IsNullOrEmpty(text)) return string.Empty;

            text = text.Replace('\\', '/');

            const string packPrefix = "pack://application:,,,";
            if (text.StartsWith(packPrefix, StringComparison.OrdinalIgnoreCase))
            {
                text = text.Substring(packPrefix.Length);
            }

            // 去掉 "/程序集名;component" 这类前缀，让 pack URI 与相对 URI 能互相匹配
            int componentIndex = text.IndexOf(";component", StringComparison.OrdinalIgnoreCase);
            if (componentIndex >= 0)
            {
                text = text.Substring(componentIndex + ";component".Length);
            }

            return text.TrimStart('/');
        }
    }
}
