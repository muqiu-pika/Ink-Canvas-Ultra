using System;
using System.Windows;

namespace Ink_Canvas.Helpers
{
    /// <summary>
    /// 应用级资源字典（Application.Current.Resources.MergedDictionaries）的合并工具。
    ///
    /// 背景：各弹窗窗口过去都在构造函数里直接
    ///     Application.Current.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = ... });
    /// 而窗口关闭时从不移除，于是每打开一次窗口就往应用级资源里堆一份字典 —— 内存只增不减，
    /// 同时资源查找链越来越长（样式命中变慢，间接拖慢窗口打开速度）。
    ///
    /// 这里按 Source 去重：已存在则复用（必要时挪到列表末尾，保持“后加者优先”的覆盖语义），
    /// 不存在才新建，从而把字典数量收敛为常数。
    /// </summary>
    internal static class ResourceDictionaryHelper
    {
        /// <summary>浅色弹窗样式字典</summary>
        public static readonly Uri LightPopupWindow = new Uri("Resources/Styles/Light-PopupWindow.xaml", UriKind.Relative);

        /// <summary>深色弹窗样式字典</summary>
        public static readonly Uri DarkPopupWindow = new Uri("Resources/Styles/Dark-PopupWindow.xaml", UriKind.Relative);

        /// <summary>浅色主主题字典（FloatBar*、SettingsPage* 等）</summary>
        public static readonly Uri LightTheme = new Uri("Resources/Styles/Light.xaml", UriKind.Relative);

        /// <summary>
        /// 浅色白板栏字典（BoardBar* 等）。
        /// 注意：BoardBar* 与 FloatBar* 并不同源 —— 前者只定义在 Light-Board.xaml / Dark-Board.xaml，
        /// 后者只定义在 Light.xaml / Dark.xaml，是两个彼此独立的文件，必须成对合并才不会漏键。
        /// </summary>
        public static readonly Uri LightBoard = new Uri("Resources/Styles/Light-Board.xaml", UriKind.Relative);

        /// <summary>
        /// 在构建任何 UI 之前把"基础主题键"补进应用级资源（幂等）。
        ///
        /// 目的仅是让 DynamicResource 在首次解析时一定能命中，消除成百上千条
        /// "System.Windows.ResourceDictionary Warning: 9 : Resource not found"。
        /// 起因：MainWindow 的 InitializeComponent() 会立刻构建浮动栏 / 白板栏并解析其中的
        /// DynamicResource（FloatBar* / BoardBar*），而此时主题字典尚未合并；
        /// 且 Release 下 iNKORE 的 XamlControlsResources / ThemeManager 会重建应用级资源字典，
        /// 把更早合并进去的字典冲刷掉。
        ///
        /// 这里默认用浅色仅作"占位"：随后 MainWindow 构造里的 SetTheme/SetBoardTheme
        /// 会按设置移除不需要的那一支并合并正确主题，二者之间不存在渲染帧，因此不会闪色。
        /// </summary>
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
