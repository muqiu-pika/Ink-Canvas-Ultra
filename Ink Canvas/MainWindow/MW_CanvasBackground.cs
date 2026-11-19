using System;
using System.Globalization;
using System.Windows;
using System.Windows.Media;

namespace Ink_Canvas
{
    public partial class MainWindow
    {
        /// <summary>默认浅色画布底色（暖纸白，比纯白柔和）</summary>
        internal static readonly Color DefaultCanvasBackgroundLight = Color.FromRgb(0xF7, 0xF6, 0xF2);

        /// <summary>默认深色画布底色（深灰黑，不用纯黑）</summary>
        internal static readonly Color DefaultCanvasBackgroundDark = Color.FromRgb(0x1F, 0x23, 0x28);

        /// <summary>
        /// 亮/暗分界阈值（相对亮度）。取 0.55 而非 0.5：
        /// 中灰 #808080 的亮度恰好是 0.502，视觉上属于"深底"，用白笔才看得清。
        /// </summary>
        private const double LightBackgroundThreshold = 0.55;

        /// <summary>
        /// 用户选定的画布底色。null = 使用内置默认（暖纸白）。
        /// 与 Settings.Canvas.CanvasBackground 同步，持久化由主程序自己负责，
        /// 不依赖「自定义颜色」插件——没装插件时预设色与自定义取色同样可用。
        /// </summary>
        private Color? _customCanvasBackground;

        /// <summary>当前生效的画布底色</summary>
        internal Color CurrentCanvasBackground
        {
            get
            {
                if (_customCanvasBackground.HasValue) return _customCanvasBackground.Value;
                return Settings?.Canvas?.UsingWhiteboard == false
                    ? DefaultCanvasBackgroundDark
                    : DefaultCanvasBackgroundLight;
            }
        }

        /// <summary>画布底色是否由用户自定义（false = 内置默认暖纸白）</summary>
        internal bool HasCustomCanvasBackground => _customCanvasBackground.HasValue;

        /// <summary>底色是否属于"亮底"（决定界面亮暗、默认笔色黑/白）</summary>
        internal static bool IsLightBackground(Color color)
        {
            return GetRelativeLuminance(color) >= LightBackgroundThreshold;
        }

        /// <summary>
        /// 启动时从设置恢复画布底色。必须在 Settings 加载完成之后、首次 CheckColorTheme 之前调用。
        /// 老配置文件里没有 canvasBackground 字段时保持 null，跟随 UsingWhiteboard 的内置默认。
        /// </summary>
        internal void LoadCanvasBackgroundFromSettings()
        {
            try
            {
                string saved = Settings?.Canvas?.CanvasBackground;
                Color color;
                if (!string.IsNullOrWhiteSpace(saved) && TryParseArgbColor(saved, out color))
                {
                    _customCanvasBackground = color;
                }
                else
                {
                    _customCanvasBackground = null;
                }
            }
            catch
            {
                _customCanvasBackground = null;
            }
        }

        /// <summary>
        /// 应用画布底色。
        /// 底色是唯一事实来源：UsingWhiteboard 由底色亮度派生，界面主题、笔色亮暗映射、
        /// 长按进度环颜色、白板栏样式字典都读这个字段，因此只需维护这一处。
        /// </summary>
        internal bool SetCanvasBackgroundColorCore(Color color)
        {
            try
            {
                _customCanvasBackground = color;

                // 持久化到主设置：不依赖插件，重启后仍保持用户选的底色
                if (Settings?.Canvas != null)
                {
                    Settings.Canvas.CanvasBackground = ColorToArgbHex(color);
                }

                RefreshCanvasBackgroundVisuals(color);
                try { SaveSettingsToFile(); } catch { }
                try { Plugins.PluginHost.Instance?.RaiseCanvasBackgroundChanged(); } catch { }
                return true;
            }
            catch (Exception ex)
            {
                Helpers.LogHelper.WriteLogToFile("应用画布底色失败 | " + ex.Message, Helpers.LogHelper.LogType.Warning);
                return false;
            }
        }

        /// <summary>恢复内置默认底色（暖纸白）</summary>
        internal void ResetCanvasBackgroundColorCore()
        {
            try
            {
                _customCanvasBackground = null;
                if (Settings?.Canvas != null) Settings.Canvas.CanvasBackground = null;

                RefreshCanvasBackgroundVisuals(DefaultCanvasBackgroundLight);
                try { SaveSettingsToFile(); } catch { }
                try { Plugins.PluginHost.Instance?.RaiseCanvasBackgroundChanged(); } catch { }
            }
            catch { }
        }

        /// <summary>
        /// 底色变化后的统一刷新：背景层、派生白板模式、默认笔色、界面主题、方格纸。
        /// 顺序有依赖：先落背景与派生字段，再走 CheckColorTheme 刷笔色/调色板，
        /// 最后刷主题字典与网格（网格色依赖底色亮度）。
        /// </summary>
        private void RefreshCanvasBackgroundVisuals(Color color)
        {
            bool isLight = IsLightBackground(color);

            // 1. 派生白板模式：所有读 UsingWhiteboard 的地方（界面 Light/Dark、笔色映射、
            //    白板栏样式字典、长按进度环）自动跟随，无需逐处改动
            if (Settings?.Canvas != null) Settings.Canvas.UsingWhiteboard = isLight;

            // 2. 背景层直接落色：批注模式下 CheckColorTheme 不会碰背景，
            //    但用户仍可能在批注模式里改底色，这里统一写入
            if (GridBackgroundCover != null)
            {
                var brush = new SolidColorBrush(color);
                if (brush.CanFreeze) brush.Freeze();
                GridBackgroundCover.Background = brush;
            }

            // 3. 默认笔色对比度修正：亮底不保留白笔、暗底不保留黑笔
            //    （与原「切白板/切黑板」逻辑一致，只是判据从 UsingWhiteboard 变成底色亮度；
            //    纠正目标为「自定义颜色」插件配置的对应亮/暗底默认色，未配置时仍是内置黑/白）
            if (currentMode != 0)
            {
                if (isLight)
                {
                    if (inkColor == 5) lastBoardInkColor = GetConfiguredBoardDefaultPenIndex(isLight: true);
                }
                else
                {
                    if (inkColor == 0) lastBoardInkColor = GetConfiguredBoardDefaultPenIndex(isLight: false);
                }
            }

            // 4. 笔色与调色板
            CheckColorTheme(true);

            // 5. 界面主题字典（白板栏 BoardBar* 配色随亮暗切换）。
            // 主窗口尚未 Loaded 时跳过切换应用级主题，避免启动早期时序问题；
            // Loaded 之后的任何一次改色都会正常刷新
            if (IsLoaded)
            {
                try { SystemEvents_UserPreferenceChanged(null, null); } catch { }
            }

            // 6. 方格纸：网格线颜色按底色亮度重算
            try { RefreshGridPaper(); } catch { }

            // 7. 弹窗内的底色选中态
            try { UpdateCanvasBackgroundSwatches(); } catch { }
        }

        #region 颜色文本互转（#AARRGGBB，与 Settings 其它颜色字段一致）

        private static string ColorToArgbHex(Color color)
        {
            return string.Format(CultureInfo.InvariantCulture, "#{0:X2}{1:X2}{2:X2}{3:X2}",
                color.A, color.R, color.G, color.B);
        }

        /// <summary>解析 #AARRGGBB / #RRGGBB / #RGB</summary>
        private static bool TryParseArgbColor(string text, out Color color)
        {
            color = Colors.White;
            if (string.IsNullOrWhiteSpace(text)) return false;

            string value = text.Trim().TrimStart('#');
            try
            {
                if (value.Length == 8)
                {
                    color = Color.FromArgb(
                        byte.Parse(value.Substring(0, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture),
                        byte.Parse(value.Substring(2, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture),
                        byte.Parse(value.Substring(4, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture),
                        byte.Parse(value.Substring(6, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture));
                    return true;
                }
                if (value.Length == 6)
                {
                    color = Color.FromRgb(
                        byte.Parse(value.Substring(0, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture),
                        byte.Parse(value.Substring(2, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture),
                        byte.Parse(value.Substring(4, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture));
                    return true;
                }
                if (value.Length == 3)
                {
                    byte r = byte.Parse(value.Substring(0, 1), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
                    byte g = byte.Parse(value.Substring(1, 1), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
                    byte b = byte.Parse(value.Substring(2, 1), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
                    color = Color.FromRgb((byte)(r * 17), (byte)(g * 17), (byte)(b * 17));
                    return true;
                }
            }
            catch { }
            return false;
        }

        #endregion
    }
}
