using Ink_Canvas.Helpers;
using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using System.Windows.Threading;

namespace Ink_Canvas
{
    /// <summary>
    /// 激光笔（Laser Pointer）：参考 OneNote 的行为。
    /// 开启后鼠标 / 触控笔 / 手指在画板上按住拖动即画出一条发光轨迹，
    /// 松手后轨迹停留约 1.2 秒再淡出清除。
    /// 关键约束：轨迹只绘制在独立的 LaserPointerCanvas 上（不是 inkCanvas 的子元素），
    /// 不产生 Stroke、不进入笔迹数据、不参与撤销历史与保存/导出，纯视觉引导。
    /// 注意：本项目存在同名的 Ink_Canvas.Canvas 类型，此类中的 Canvas 一律使用完全限定名。
    /// </summary>
    public partial class MainWindow : Window
    {
        #region 激光笔状态

        private bool isLaserPointerEnabled;
        private InkCanvasEditingMode editingModeBeforeLaserPointer = InkCanvasEditingMode.Ink;

        /// <summary>松手后轨迹停留时长（秒），取自设置项「激光笔轨迹保留时间」，下限 0.5 秒</summary>
        private double LaserHoldSeconds => Math.Max(0.5, Settings.Canvas.LaserPointerHoldSeconds);
        /// <summary>整体发光粗细（像素），取自设置项「激光笔粗细调整」，夹在 [2,40] 之间；控制外圈/中层</summary>
        private double LaserHighlightWidth => Math.Max(2, Math.Min(40, Settings.Canvas.LaserPointerHighlightWidth));
        /// <summary>中心白色亮芯宽度（像素），取自设置项「激光笔中心高亮大小」，夹在 [1,20] 之间；独立于整体粗细</summary>
        private double LaserCoreWidth => Math.Max(1, Math.Min(20, Settings.Canvas.LaserPointerCoreWidth));
        /// <summary>停留结束后的淡出时长（秒）</summary>
        private const double LaserFadeSeconds = 0.6;
        /// <summary>轨迹点采样阈值（平方距离），小于该值不新增点，避免点数膨胀</summary>
        private const double LaserSampleThreshold = 6.25;

        private static readonly object LaserMouseKey = new object();

        /// <summary>进行中的一条激光轨迹</summary>
        private sealed class LaserTrail
        {
            public System.Windows.Controls.Canvas Host;
            public PointCollection Points;
            public DispatcherTimer HoldTimer;
            public bool IsFading;
        }

        private readonly Dictionary<object, LaserTrail> _laserTrails = new Dictionary<object, LaserTrail>();

        #endregion

        #region 初始化与开关

        /// <summary>在 MainWindow 构造函数（InitializeComponent 之后）调用一次</summary>
        private void InitializeLaserPointer()
        {
            // 统一在窗口 Preview 阶段处理：既能先于 inkCanvas 自身逻辑拿到输入，
            // 也能把事件标记 Handled，避免同时产生墨迹或触发双指手势。
            PreviewMouseDown += LaserPointer_PreviewMouseDown;
            PreviewMouseMove += LaserPointer_PreviewMouseMove;
            PreviewMouseUp += LaserPointer_PreviewMouseUp;

            PreviewStylusDown += LaserPointer_PreviewStylusDown;
            PreviewStylusMove += LaserPointer_PreviewStylusMove;
            PreviewStylusUp += LaserPointer_PreviewStylusUp;

            PreviewTouchDown += LaserPointer_PreviewTouchDown;
            PreviewTouchMove += LaserPointer_PreviewTouchMove;
            PreviewTouchUp += LaserPointer_PreviewTouchUp;

            // 兜底：光标移出窗口时收不到 MouseUp，需主动结束鼠标轨迹
            MouseLeave += (s, e) => EndLaserTrail(LaserMouseKey);
        }

        private void BtnLaserPointer_Click(object sender, RoutedEventArgs e)
        {
            SetLaserPointerEnabled(!isLaserPointerEnabled);
        }

        /// <summary>切换激光笔开关；enabled 时进入激光态，否则恢复原有墨迹模式</summary>
        public void SetLaserPointerEnabled(bool enabled)
        {
            try
            {
                isLaserPointerEnabled = enabled;
                if (enabled)
                {
                    editingModeBeforeLaserPointer = inkCanvas.EditingMode;
                    // None：inkCanvas 不再收集墨迹，激光期间不会留下任何笔迹数据
                    inkCanvas.EditingMode = InkCanvasEditingMode.None;
                    inkCanvas.Cursor = Cursors.Cross;
                }
                else
                {
                    ClearLaserTrails();
                    inkCanvas.EditingMode = editingModeBeforeLaserPointer;
                    inkCanvas.Cursor = Cursors.Pen;
                }

                UpdateLaserPointerVisual();
            }
            catch (Exception ex)
            {
                LogHelper.WriteLogToFile($"切换激光笔状态失败: {ex.Message}", LogHelper.LogType.Error);
            }
        }

        /// <summary>同步浮动栏上激光笔按钮的显示（文字与指示点颜色）</summary>
        private void UpdateLaserPointerVisual()
        {
            try
            {
                if (TextBlockLaserPointerState != null)
                {
                    TextBlockLaserPointerState.Text = isLaserPointerEnabled ? "激光笔：开" : "激光笔：关";
                }
                if (LaserPointerIndicator != null)
                {
                    LaserPointerIndicator.Fill = new SolidColorBrush(
                        isLaserPointerEnabled ? Color.FromRgb(0xFF, 0x3B, 0x30) : Color.FromRgb(0x5A, 0x5A, 0x5A));
                }
                if (BtnLaserPointer != null)
                {
                    BtnLaserPointer.BorderBrush = isLaserPointerEnabled
                        ? new SolidColorBrush(Color.FromRgb(0xFF, 0x3B, 0x30))
                        : (Brush)TryFindResource("FloatBarBorderBrush");
                }
            }
            catch { }
        }

        #endregion

        #region 轨迹绘制

        /// <summary>判断事件源是否位于画板区域（inkCanvas 或选择覆盖层），排除浮动栏/工具栏等 UI</summary>
        private bool IsInInkCanvasArea(object originalSource)
        {
            DependencyObject d = originalSource as DependencyObject;
            while (d != null)
            {
                if (ReferenceEquals(d, inkCanvas) || ReferenceEquals(d, MWSelectionHost)) return true;
                d = VisualTreeHelper.GetParent(d);
            }
            return false;
        }

        /// <summary>激光颜色：跟随当前画笔颜色；颜色过暗时（如黑色）回退为红色以保证可见</summary>
        private Color GetLaserColor()
        {
            try
            {
                var c = inkCanvas.DefaultDrawingAttributes.Color;
                if (c.R + c.G + c.B < 200) return Color.FromRgb(0xFF, 0x3B, 0x30);
                return c;
            }
            catch
            {
                return Color.FromRgb(0xFF, 0x3B, 0x30);
            }
        }

        /// <summary>
        /// 创建一条轨迹：三层叠加模拟发光（外圈宽而淡 + 中层 + 白色亮芯），
        /// 不使用模糊滤镜，避免长轨迹逐帧重算导致卡顿。
        /// </summary>
        private void StartLaserTrail(object key, System.Windows.Point start)
        {
            try
            {
                EndLaserTrail(key);

                var host = new System.Windows.Controls.Canvas { IsHitTestVisible = false };
                var points = new PointCollection { start };
                var color = GetLaserColor();

                // 三层共享同一个点集合：
                // 外圈与中层随「整体粗细」缩放，白色亮芯由独立的「中心高亮大小」控制（与整体粗细解耦）。
                double thickness = LaserHighlightWidth;
                double core = LaserCoreWidth;
                host.Children.Add(CreateLaserPolyline(points, color, thickness * 2.0, 0.16));
                host.Children.Add(CreateLaserPolyline(points, color, thickness, 0.42));
                host.Children.Add(CreateLaserPolyline(points, Colors.White, core, 0.92));

                LaserPointerCanvas.Children.Add(host);
                _laserTrails[key] = new LaserTrail { Host = host, Points = points };
            }
            catch (Exception ex)
            {
                LogHelper.WriteLogToFile($"创建激光轨迹失败: {ex.Message}", LogHelper.LogType.Error);
            }
        }

        private static Polyline CreateLaserPolyline(PointCollection points, Color color, double thickness, double opacity)
        {
            return new Polyline
            {
                Points = points, // 三层共享同一个点集合，更新一次即同步刷新
                Stroke = new SolidColorBrush(color),
                StrokeThickness = thickness,
                Opacity = opacity,
                StrokeStartLineCap = PenLineCap.Round,
                StrokeEndLineCap = PenLineCap.Round,
                StrokeLineJoin = PenLineJoin.Round,
                IsHitTestVisible = false
            };
        }

        private void UpdateLaserTrail(object key, System.Windows.Point position)
        {
            try
            {
                if (!_laserTrails.TryGetValue(key, out var trail)) return;
                var points = trail.Points;
                if (points.Count > 0)
                {
                    var last = points[points.Count - 1];
                    double dx = position.X - last.X;
                    double dy = position.Y - last.Y;
                    if (dx * dx + dy * dy < LaserSampleThreshold) return;
                }
                points.Add(position);
            }
            catch { }
        }

        /// <summary>结束轨迹：停留 LaserHoldSeconds 后淡出并清除</summary>
        private void EndLaserTrail(object key)
        {
            try
            {
                if (!_laserTrails.TryGetValue(key, out var trail)) return;
                _laserTrails.Remove(key);
                if (trail.IsFading) return;
                trail.IsFading = true;

                var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(LaserHoldSeconds) };
                timer.Tick += (s, e) =>
                {
                    timer.Stop();
                    FadeAndRemoveTrail(trail);
                };
                trail.HoldTimer = timer;
                timer.Start();
            }
            catch { }
        }

        private void FadeAndRemoveTrail(LaserTrail trail)
        {
            try
            {
                var host = trail.Host;
                if (host == null) return;

                var anim = new DoubleAnimation(1, 0, new Duration(TimeSpan.FromSeconds(LaserFadeSeconds)));
                anim.Completed += (s, e) =>
                {
                    host.Children.Clear();
                    try { LaserPointerCanvas?.Children.Remove(host); } catch { }
                };
                host.BeginAnimation(UIElement.OpacityProperty, anim);
            }
            catch { }
        }

        /// <summary>立即清除全部激光轨迹（清屏 / 翻页 / 关闭激光笔时调用）</summary>
        public void ClearLaserTrails()
        {
            try
            {
                foreach (var kv in _laserTrails)
                {
                    var trail = kv.Value;
                    try { trail.HoldTimer?.Stop(); } catch { }
                    var host = trail.Host;
                    if (host != null)
                    {
                        host.Children.Clear();
                        try { LaserPointerCanvas?.Children.Remove(host); } catch { }
                    }
                }
                _laserTrails.Clear();
                LaserPointerCanvas?.Children.Clear();
            }
            catch { }
        }

        #endregion

        #region 鼠标输入

        private void LaserPointer_PreviewMouseDown(object sender, MouseButtonEventArgs e)
        {
            if (!isLaserPointerEnabled) return;
            if (e.StylusDevice != null) return;         // 触控笔/触摸走 Stylus、Touch 分支，避免重复绘制
            if (e.LeftButton != MouseButtonState.Pressed) return;
            if (!IsInInkCanvasArea(e.OriginalSource)) return;

            StartLaserTrail(LaserMouseKey, e.GetPosition(LaserPointerCanvas));
            e.Handled = true;
        }

        private void LaserPointer_PreviewMouseMove(object sender, MouseEventArgs e)
        {
            if (!isLaserPointerEnabled) return;
            if (!_laserTrails.ContainsKey(LaserMouseKey)) return;

            UpdateLaserTrail(LaserMouseKey, e.GetPosition(LaserPointerCanvas));
            e.Handled = true;
        }

        private void LaserPointer_PreviewMouseUp(object sender, MouseButtonEventArgs e)
        {
            if (!_laserTrails.ContainsKey(LaserMouseKey)) return;
            EndLaserTrail(LaserMouseKey);
            e.Handled = true;
        }

        #endregion

        #region 触控笔输入

        private void LaserPointer_PreviewStylusDown(object sender, StylusDownEventArgs e)
        {
            if (!isLaserPointerEnabled) return;
            try
            {
                // 触摸会同时产生 Stylus 与 Touch 事件，触摸交由 Touch 分支处理，避免重复
                if (e.StylusDevice != null && e.StylusDevice.TabletDevice != null &&
                    e.StylusDevice.TabletDevice.Type == TabletDeviceType.Touch) return;
            }
            catch { }
            if (!IsInInkCanvasArea(e.OriginalSource)) return;

            StartLaserTrail("stylus:" + e.StylusDevice.Id, e.GetPosition(LaserPointerCanvas));
            e.Handled = true;
        }

        private void LaserPointer_PreviewStylusMove(object sender, StylusEventArgs e)
        {
            if (!isLaserPointerEnabled) return;
            var key = "stylus:" + e.StylusDevice.Id;
            if (!_laserTrails.ContainsKey(key)) return;

            UpdateLaserTrail(key, e.GetPosition(LaserPointerCanvas));
            e.Handled = true;
        }

        private void LaserPointer_PreviewStylusUp(object sender, StylusEventArgs e)
        {
            if (!isLaserPointerEnabled) return;
            var key = "stylus:" + e.StylusDevice.Id;
            if (!_laserTrails.ContainsKey(key)) return;

            EndLaserTrail(key);
            e.Handled = true;
        }

        #endregion

        #region 触摸输入

        private void LaserPointer_PreviewTouchDown(object sender, TouchEventArgs e)
        {
            if (!isLaserPointerEnabled) return;
            if (!IsInInkCanvasArea(e.OriginalSource)) return;

            StartLaserTrail("touch:" + e.TouchDevice.Id, e.GetTouchPoint(LaserPointerCanvas).Position);
            // 标记已处理：阻止继续冒泡，避免同时触发墨迹收集与双指手势
            e.Handled = true;
        }

        private void LaserPointer_PreviewTouchMove(object sender, TouchEventArgs e)
        {
            if (!isLaserPointerEnabled) return;
            var key = "touch:" + e.TouchDevice.Id;
            if (!_laserTrails.ContainsKey(key)) return;

            UpdateLaserTrail(key, e.GetTouchPoint(LaserPointerCanvas).Position);
            e.Handled = true;
        }

        private void LaserPointer_PreviewTouchUp(object sender, TouchEventArgs e)
        {
            if (!isLaserPointerEnabled) return;
            var key = "touch:" + e.TouchDevice.Id;
            if (!_laserTrails.ContainsKey(key)) return;

            EndLaserTrail(key);
            e.Handled = true;
        }

        #endregion
    }
}
