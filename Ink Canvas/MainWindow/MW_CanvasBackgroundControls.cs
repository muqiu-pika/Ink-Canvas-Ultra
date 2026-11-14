using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace Ink_Canvas
{
    public partial class MainWindow
    {
        /// <summary>
        /// 画布底色预设，按相对亮度由浅到深排列：
        /// 暖纸白 → 雾蓝 → 雾绿 → 浅紫灰 → 暖杏米 → 深灰黑。
        /// 全部为低饱和中性色，久看不刺眼；网格线颜色、界面亮暗、默认笔色都由所选底色的亮度自动推导。
        /// </summary>
        private static readonly Color[] CanvasColorPresets =
        {
            Color.FromRgb(0xF7, 0xF6, 0xF2), // 暖纸白
            Color.FromRgb(0xE4, 0xEE, 0xF6), // 雾蓝
            Color.FromRgb(0xE3, 0xEF, 0xE6), // 雾绿
            Color.FromRgb(0xEC, 0xE8, 0xF2), // 浅紫灰
            Color.FromRgb(0xF2, 0xE8, 0xDC), // 暖杏米
            Color.FromRgb(0x1F, 0x23, 0x28)  // 深灰黑
        };

        /// <summary>与 CanvasColorPresets 一一对应的中文名（用于 ToolTip）</summary>
        private static readonly string[] CanvasColorPresetNames =
        {
            "暖纸白",
            "雾蓝",
            "雾绿",
            "浅紫灰",
            "暖杏米",
            "深灰黑"
        };

        private readonly List<Border> _canvasColorSwatchBorders = new List<Border>();
        private bool _canvasColorSwatchesBuilt;

        /// <summary>
        /// 色板只在首次展开弹窗时构建一次。控件在 XAML 里是空 StackPanel，
        /// 由代码填充，避免在 XAML 里重复写 6 段同构色块。
        /// </summary>
        private void EnsureCanvasColorSwatches()
        {
            if (_canvasColorSwatchesBuilt) return;
            if (CanvasColorPresetPanel == null) return;

            for (int i = 0; i < CanvasColorPresets.Length; i++)
            {
                Color color = CanvasColorPresets[i];
                string name = i < CanvasColorPresetNames.Length ? CanvasColorPresetNames[i] : null;

                var swatch = new Border
                {
                    Width = 24,
                    Height = 24,
                    Margin = new Thickness(3, 0, 3, 0),
                    CornerRadius = new CornerRadius(6),
                    BorderThickness = new Thickness(1),
                    BorderBrush = new SolidColorBrush(Color.FromArgb(0x40, 0x80, 0x80, 0x80)),
                    Background = FreezeBrush(color),
                    Cursor = Cursors.Hand,
                    Tag = color,
                    ToolTip = string.IsNullOrEmpty(name)
                        ? null
                        : name + "  " + string.Format("#{0:X2}{1:X2}{2:X2}", color.R, color.G, color.B)
                };
                // 触摸设备上 TouchUp 若不吞掉事件会再提升为 MouseLeftButtonUp，导致选色执行两次
                swatch.TouchUp += (s, e) =>
                {
                    e.Handled = true;
                    ApplyPresetCanvasColor((Color)((Border)s).Tag);
                };
                swatch.MouseLeftButtonUp += (s, e) =>
                {
                    e.Handled = true;
                    ApplyPresetCanvasColor((Color)((Border)s).Tag);
                };
                _canvasColorSwatchBorders.Add(swatch);
                CanvasColorPresetPanel.Children.Add(swatch);
            }

            _canvasColorSwatchesBuilt = true;
        }

        /// <summary>刷新色板选中态（当前底色等于某个预设时描边高亮）</summary>
        private void UpdateCanvasBackgroundSwatches()
        {
            try
            {
                EnsureCanvasColorSwatches();

                Color current = CurrentCanvasBackground;
                Brush accent;
                try { accent = ResolveThemeAccentBrush(); }
                catch { accent = FreezeBrush(Color.FromRgb(0x00, 0x78, 0xD4)); }

                var normal = new SolidColorBrush(Color.FromArgb(0x40, 0x80, 0x80, 0x80));
                if (normal.CanFreeze) normal.Freeze();

                foreach (var swatch in _canvasColorSwatchBorders)
                {
                    var color = (Color)swatch.Tag;
                    bool selected = color.R == current.R && color.G == current.G && color.B == current.B;
                    swatch.BorderThickness = new Thickness(selected ? 2 : 1);
                    swatch.BorderBrush = selected ? accent : normal;
                }

                if (BtnCanvasColorReset != null)
                {
                    // 未自定义时「恢复默认」无意义，直接置灰
                    BtnCanvasColorReset.IsEnabled = HasCustomCanvasBackground;
                    BtnCanvasColorReset.Opacity = HasCustomCanvasBackground ? 1.0 : 0.5;
                }
            }
            catch { }
        }

        /// <summary>
        /// 预设色块点击：直接应用，不依赖「自定义颜色」插件。
        /// 底色由主程序自己持久化到 Settings，因此没装插件时这些颜色同样可用、同样能保持。
        /// </summary>
        private void ApplyPresetCanvasColor(Color color)
        {
            SetCanvasBackgroundColorCore(color);
        }

        private void BtnCanvasColorCustom_Click(object sender, RoutedEventArgs e)
        {
            // 取色窗口属于主程序（插件复用同一实现），因此这里也不依赖插件
            Color? picked = ColorPickerWindow.Pick(
                CurrentCanvasBackground,
                "画布颜色",
                "选择画布的底色。网格线、界面明暗与默认笔色会跟随底色亮度自动适配。",
                this);

            if (picked.HasValue) SetCanvasBackgroundColorCore(picked.Value);
        }

        private void BtnCanvasColorReset_Click(object sender, RoutedEventArgs e)
        {
            ResetCanvasBackgroundColorCore();
        }

        private static Brush FreezeBrush(Color color)
        {
            var brush = new SolidColorBrush(color);
            if (brush.CanFreeze) brush.Freeze();
            return brush;
        }
    }
}
