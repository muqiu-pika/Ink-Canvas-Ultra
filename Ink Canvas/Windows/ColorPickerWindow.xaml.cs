using iNKORE.UI.WPF.Modern;
using Ink_Canvas.Helpers;
using System;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using WpfCanvas = System.Windows.Controls.Canvas;

namespace Ink_Canvas
{
    /// <summary>
    /// 统一取色窗口。风格对齐截图插入选项框（Mica + 圆角卡片 + Fluent 图标），
    /// 供主程序（画布底色）与插件（自定义画笔颜色）共用，避免两套取色器风格漂移。
    ///
    /// 交互：左侧预设色板 / HEX / RGB，右侧 HSV 饱和度-明度面板 + 色相条，
    /// 鼠标与触摸都可用（白板设备以触摸为主，手势必须一并支持）。
    /// </summary>
    public partial class ColorPickerWindow : Window
    {
        private const double SvSize = 184.0;
        private const double HueWidth = 184.0;
        private const double MarkerSize = 14.0;
        private const double HueMarkerWidth = 6.0;

        /// <summary>预设色板：兼顾墨迹色与画布底色（深浅各半）</summary>
        private static readonly string[] PresetColors =
        {
            "#000000", "#404040", "#808080", "#C0C0C0", "#FFFFFF", "#F2F2F2", "#FAF3E0", "#E8F5E9",
            "#E3F2FD", "#FFF3E0", "#FCE4EC", "#F3E5F5", "#1F1F1F", "#0F172A", "#1E293B", "#334155",
            "#DC2626", "#EA580C", "#EAB308", "#16A34A", "#0D9488", "#2563EB", "#7C3AED", "#DB2777"
        };

        private readonly Color _originalColor;
        private readonly System.Collections.Generic.List<Border> _presetBorders =
            new System.Collections.Generic.List<Border>();

        private double _hue;
        private double _saturation;
        private double _value;

        /// <summary>UI 回填期间置位，避免 TextChanged 与选色互相触发成环</summary>
        private bool _syncing;

        private bool _draggingSv;
        private bool _draggingHue;

        /// <summary>用户确认的颜色（取消时等于打开时的颜色）</summary>
        public Color SelectedColor { get; private set; }

        public ColorPickerWindow() : this(Colors.White, null, null)
        {
        }

        public ColorPickerWindow(Color initial, string title = null, string subtitle = null)
        {
            InitializeComponent();

            _originalColor = initial;
            SelectedColor = initial;

            if (!string.IsNullOrWhiteSpace(title)) TitleText.Text = title;
            if (!string.IsNullOrWhiteSpace(subtitle))
            {
                SubtitleText.Text = subtitle;
                SubtitleText.Visibility = Visibility.Visible;
            }

            ApplyHostTheme();
            BuildPresetSwatches();
            SetColor(initial, true);

            OriginalSwatch.Background = ToBrush(initial);

            try { Helpers.PopupWindowLayerHelper.Register(this); } catch { }
        }

        /// <summary>
        /// 以模态方式取色。owner 为 null 时回落到主窗口；返回 null 表示用户取消。
        /// </summary>
        public static Color? Pick(Color initial, string title = null, string subtitle = null, Window owner = null)
        {
            try
            {
                var picker = new ColorPickerWindow(initial, title, subtitle);

                Window targetOwner = owner;
                if (targetOwner == null) targetOwner = Application.Current?.MainWindow;
                if (targetOwner == null && Application.Current != null && Application.Current.Windows.Count > 0)
                {
                    targetOwner = Application.Current.Windows[0];
                }
                picker.Owner = targetOwner;

                bool? result = picker.ShowDialog();
                if (result == true) return picker.SelectedColor;
            }
            catch (Exception ex)
            {
                LogHelper.WriteLogToFile("取色窗口打开失败 | " + ex.Message, LogHelper.LogType.Warning);
            }
            return null;
        }

        private void ApplyHostTheme()
        {
            try
            {
                bool isLight = true;
                if (Application.Current?.MainWindow is MainWindow mainWindow)
                {
                    isLight = string.Equals(mainWindow.GetMainWindowTheme(), "Light", StringComparison.OrdinalIgnoreCase);
                }
                ThemeManager.SetRequestedTheme(this, isLight ? ElementTheme.Light : ElementTheme.Dark);
                ResourceDictionaryHelper.ApplyPopupWindowTheme(isLight);
            }
            catch { }
        }

        private void BuildPresetSwatches()
        {
            foreach (var hex in PresetColors)
            {
                Color color;
                if (!TryParseHex(hex, out color)) continue;

                var border = new Border
                {
                    Width = 24,
                    Height = 24,
                    Margin = new Thickness(3),
                    CornerRadius = new CornerRadius(5),
                    Background = ToBrush(color),
                    BorderThickness = new Thickness(1),
                    BorderBrush = new SolidColorBrush(Color.FromArgb(0x26, 0x80, 0x80, 0x80)),
                    Cursor = Cursors.Hand,
                    Tag = color
                };
                border.MouseLeftButtonUp += PresetSwatch_Click;
                border.TouchUp += PresetSwatch_TouchUp;
                _presetBorders.Add(border);
                PresetPanel.Children.Add(border);
            }
        }

        private void PresetSwatch_Click(object sender, MouseButtonEventArgs e)
        {
            var border = sender as Border;
            if (border == null) return;
            if (border.Tag is Color color) SetColor(color, true);
            e.Handled = true;
        }

        private void PresetSwatch_TouchUp(object sender, TouchEventArgs e)
        {
            var border = sender as Border;
            if (border == null) return;
            if (border.Tag is Color color) SetColor(color, true);
            e.Handled = true;
        }

        #region 颜色 <-> HSV

        private static void ColorToHsv(Color color, out double hue, out double saturation, out double value)
        {
            double r = color.R / 255.0, g = color.G / 255.0, b = color.B / 255.0;
            double max = Math.Max(r, Math.Max(g, b));
            double min = Math.Min(r, Math.Min(g, b));
            double delta = max - min;

            value = max;
            saturation = max <= 0 ? 0 : delta / max;

            if (delta <= 0)
            {
                hue = 0;
                return;
            }

            if (max == r) hue = 60 * (((g - b) / delta) % 6);
            else if (max == g) hue = 60 * (((b - r) / delta) + 2);
            else hue = 60 * (((r - g) / delta) + 4);

            if (hue < 0) hue += 360;
        }

        private static Color HsvToColor(double hue, double saturation, double value)
        {
            hue = ((hue % 360) + 360) % 360;
            saturation = Clamp01(saturation);
            value = Clamp01(value);

            double c = value * saturation;
            double x = c * (1 - Math.Abs(((hue / 60.0) % 2) - 1));
            double m = value - c;

            double r = 0, g = 0, b = 0;
            if (hue < 60) { r = c; g = x; }
            else if (hue < 120) { r = x; g = c; }
            else if (hue < 180) { g = c; b = x; }
            else if (hue < 240) { g = x; b = c; }
            else if (hue < 300) { r = x; b = c; }
            else { r = c; b = x; }

            return Color.FromRgb(
                (byte)Math.Round((r + m) * 255),
                (byte)Math.Round((g + m) * 255),
                (byte)Math.Round((b + m) * 255));
        }

        private static double Clamp01(double value)
        {
            if (value < 0) return 0;
            if (value > 1) return 1;
            return value;
        }

        #endregion

        /// <summary>
        /// 应用一个颜色并刷新全部 UI。syncFields=true 时回填 HEX/RGB 文本（用户拖动时只更新预览）。
        /// </summary>
        private void SetColor(Color color, bool syncFields)
        {
            ColorToHsv(color, out _hue, out _saturation, out _value);
            SelectedColor = color;

            UpdateHueGradient();
            UpdateMarkers();
            UpdatePreview(color, syncFields);
        }

        private void UpdateHueGradient()
        {
            try
            {
                if (SvColorRect.Fill is LinearGradientBrush brush && brush.GradientStops.Count > 1)
                {
                    // 饱和度面板的右上角必须是当前纯色相，"白色→黑"的叠加由上层矩形负责
                    brush.GradientStops[brush.GradientStops.Count - 1].Color = HsvToColor(_hue, 1, 1);
                }
            }
            catch { }
        }

        private void UpdateMarkers()
        {
            double x = _saturation * SvSize;
            double y = (1 - _value) * SvSize;
            WpfCanvas.SetLeft(SvMarker, x - MarkerSize / 2);
            WpfCanvas.SetTop(SvMarker, y - MarkerSize / 2);

            // 光标描边跟随背景明暗：亮区用深色描边，暗区用白描边，保证始终可见
            double luminance = (0.2126 * SelectedColor.R + 0.7152 * SelectedColor.G + 0.0722 * SelectedColor.B) / 255.0;
            SvMarker.Stroke = luminance > 0.55 ? new SolidColorBrush(Color.FromRgb(0x33, 0x33, 0x33)) : Brushes.White;

            double hueX = (_hue / 360.0) * HueWidth;
            WpfCanvas.SetLeft(HueMarker, hueX - HueMarkerWidth / 2);
            WpfCanvas.SetTop(HueMarker, -2);
        }

        private void UpdatePreview(Color color, bool syncFields)
        {
            PreviewSwatch.Background = ToBrush(color);

            _syncing = true;
            try
            {
                if (syncFields)
                {
                    HexBox.Text = string.Format(CultureInfo.InvariantCulture, "#{0:X2}{1:X2}{2:X2}", color.R, color.G, color.B);
                    RBox.Text = color.R.ToString(CultureInfo.InvariantCulture);
                    GBox.Text = color.G.ToString(CultureInfo.InvariantCulture);
                    BBox.Text = color.B.ToString(CultureInfo.InvariantCulture);
                }
            }
            finally
            {
                _syncing = false;
            }
        }

        private static Brush ToBrush(Color color)
        {
            var brush = new SolidColorBrush(color);
            if (brush.CanFreeze) brush.Freeze();
            return brush;
        }

        #region HSV 面板交互

        private void SvCanvas_MouseDown(object sender, MouseButtonEventArgs e)
        {
            _draggingSv = true;
            SvCanvas.CaptureMouse();
            ApplySvPoint(e.GetPosition(SvCanvas));
            e.Handled = true;
        }

        private void SvCanvas_MouseMove(object sender, MouseEventArgs e)
        {
            if (!_draggingSv) return;
            ApplySvPoint(e.GetPosition(SvCanvas));
            e.Handled = true;
        }

        private void SvCanvas_MouseUp(object sender, MouseButtonEventArgs e)
        {
            EndSvDrag();
            SvCanvas.ReleaseMouseCapture();
            e.Handled = true;
        }

        private void SvCanvas_TouchDown(object sender, TouchEventArgs e)
        {
            _draggingSv = true;
            SvCanvas.CaptureTouch(e.TouchDevice);
            ApplySvPoint(e.GetTouchPoint(SvCanvas).Position);
            e.Handled = true;
        }

        private void SvCanvas_TouchMove(object sender, TouchEventArgs e)
        {
            if (!_draggingSv) return;
            ApplySvPoint(e.GetTouchPoint(SvCanvas).Position);
            e.Handled = true;
        }

        private void SvCanvas_TouchUp(object sender, TouchEventArgs e)
        {
            EndSvDrag();
            SvCanvas.ReleaseTouchCapture(e.TouchDevice);
            e.Handled = true;
        }

        private void EndSvDrag()
        {
            _draggingSv = false;
            // 拖动结束时把最终色值回填到 HEX/RGB，避免用户看不到精确值
            UpdatePreview(SelectedColor, true);
        }

        private void ApplySvPoint(Point point)
        {
            _saturation = Clamp01(point.X / SvSize);
            _value = 1 - Clamp01(point.Y / SvSize);
            SelectedColor = HsvToColor(_hue, _saturation, _value);
            UpdateMarkers();
            UpdatePreview(SelectedColor, false);
        }

        #endregion

        #region 色相条交互

        private void HueCanvas_MouseDown(object sender, MouseButtonEventArgs e)
        {
            _draggingHue = true;
            HueCanvas.CaptureMouse();
            ApplyHuePoint(e.GetPosition(HueCanvas));
            e.Handled = true;
        }

        private void HueCanvas_MouseMove(object sender, MouseEventArgs e)
        {
            if (!_draggingHue) return;
            ApplyHuePoint(e.GetPosition(HueCanvas));
            e.Handled = true;
        }

        private void HueCanvas_MouseUp(object sender, MouseButtonEventArgs e)
        {
            EndHueDrag();
            HueCanvas.ReleaseMouseCapture();
            e.Handled = true;
        }

        private void HueCanvas_TouchDown(object sender, TouchEventArgs e)
        {
            _draggingHue = true;
            HueCanvas.CaptureTouch(e.TouchDevice);
            ApplyHuePoint(e.GetTouchPoint(HueCanvas).Position);
            e.Handled = true;
        }

        private void HueCanvas_TouchMove(object sender, TouchEventArgs e)
        {
            if (!_draggingHue) return;
            ApplyHuePoint(e.GetTouchPoint(HueCanvas).Position);
            e.Handled = true;
        }

        private void HueCanvas_TouchUp(object sender, TouchEventArgs e)
        {
            EndHueDrag();
            HueCanvas.ReleaseTouchCapture(e.TouchDevice);
            e.Handled = true;
        }

        private void EndHueDrag()
        {
            _draggingHue = false;
            UpdatePreview(SelectedColor, true);
        }

        private void ApplyHuePoint(Point point)
        {
            _hue = Clamp01(point.X / HueWidth) * 360.0;
            SelectedColor = HsvToColor(_hue, _saturation, _value);
            UpdateHueGradient();
            UpdateMarkers();
            UpdatePreview(SelectedColor, false);
        }

        #endregion

        #region 文本输入

        private void HexBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (_syncing) return;

            Color color;
            if (!TryParseHex(HexBox.Text, out color)) return;

            SetColor(color, false);
            // 只回填 R/G/B，六进制框保持用户正在输入的形态（否则会把 "#ff" 补全成完整值打断输入）
            _syncing = true;
            try
            {
                RBox.Text = color.R.ToString(CultureInfo.InvariantCulture);
                GBox.Text = color.G.ToString(CultureInfo.InvariantCulture);
                BBox.Text = color.B.ToString(CultureInfo.InvariantCulture);
            }
            finally
            {
                _syncing = false;
            }
        }

        private void RgbBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (_syncing) return;

            byte r, g, b;
            if (!TryParseByte(RBox.Text, out r)) return;
            if (!TryParseByte(GBox.Text, out g)) return;
            if (!TryParseByte(BBox.Text, out b)) return;

            SetColor(Color.FromRgb(r, g, b), false);
            _syncing = true;
            try
            {
                HexBox.Text = string.Format(CultureInfo.InvariantCulture, "#{0:X2}{1:X2}{2:X2}", r, g, b);
            }
            finally
            {
                _syncing = false;
            }
        }

        private static bool TryParseByte(string text, out byte value)
        {
            value = 0;
            if (string.IsNullOrWhiteSpace(text)) return false;
            int parsed;
            if (!int.TryParse(text.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out parsed)) return false;
            if (parsed < 0 || parsed > 255) return false;
            value = (byte)parsed;
            return true;
        }

        /// <summary>支持 #RRGGBB / RRGGBB / #RGB / RGB</summary>
        private static bool TryParseHex(string text, out Color color)
        {
            color = Colors.Black;
            if (string.IsNullOrWhiteSpace(text)) return false;

            string value = text.Trim().TrimStart('#');
            try
            {
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

        private void BtnOk_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = true;
            Close();
        }

        private void BtnCancel_Click(object sender, RoutedEventArgs e)
        {
            SelectedColor = _originalColor;
            DialogResult = false;
            Close();
        }
    }
}
