using Ink_Canvas.Helpers;
using System;
using System.Collections.Generic;
using System.Linq;
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
            /// <summary>最近一次收到该轨迹事件（按下/移动）的时间，用于回收「等不到 Up」的残留轨迹</summary>
            public DateTime LastActivity = DateTime.UtcNow;
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

            // 兜底：指针离开窗口 / 窗口失焦 / 捕获丢失时收不到对应的 Up，需主动收尾。
            // 副屏手写时驱动常把「按下」报成鼠标、「抬起」报成手写笔（或反之），Up 会丢失，
            // 残留轨迹会让后续 Up 持续被吞掉（详见 LaserPointer_PreviewMouseUp 注释）。
            MouseLeave += (s, e) => EndAllLaserTrails();
            StylusLeave += (s, e) => EndAllLaserTrails();
            TouchLeave += (s, e) => EndAllLaserTrails();
            LostMouseCapture += (s, e) => EndAllLaserTrails();
            LostStylusCapture += (s, e) => EndAllLaserTrails();
            Deactivated += (s, e) => EndAllLaserTrails();

            // 初始同步一次按钮视觉（浮动栏 + 白板工具栏）
            UpdateLaserPointerVisual();
        }

        private void BtnLaserPointer_Click(object sender, RoutedEventArgs e)
        {
            SetLaserPointerEnabled(!isLaserPointerEnabled);
        }

        /// <summary>白板工具栏上的激光笔按钮：与浮动栏按钮共用同一开关状态（白板模式下浮动栏是隐藏的）</summary>
        private void BoardBtnLaserPointer_Click(object sender, RoutedEventArgs e)
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

        /// <summary>同步浮动栏与白板「墨迹选项」面板中激光笔按钮的显示（文字、指示点、描边）</summary>
        private void UpdateLaserPointerVisual()
        {
            try
            {
                bool on = isLaserPointerEnabled;
                // 开启时用当前激光笔颜色（与轨迹实际颜色一致，选色后能立刻看到反馈）
                var activeBrush = new SolidColorBrush(GetLaserColor());
                var idleBrush = new SolidColorBrush(Color.FromRgb(0x5A, 0x5A, 0x5A));

                // 浮动栏「墨迹选项」
                if (TextBlockLaserPointerState != null)
                {
                    TextBlockLaserPointerState.Text = on ? "激光笔：开" : "激光笔：关";
                }
                if (LaserPointerIndicator != null)
                {
                    LaserPointerIndicator.Fill = on ? activeBrush : idleBrush;
                }
                if (BtnLaserPointer != null)
                {
                    if (on) BtnLaserPointer.BorderBrush = activeBrush;
                    else BtnLaserPointer.SetResourceReference(Control.BorderBrushProperty, "FloatBarBorderBrush");
                }

                // 白板「墨迹选项」（BoardPenPalette）内的同一个开关
                if (BoardTextBlockLaserPointerState != null)
                {
                    BoardTextBlockLaserPointerState.Text = on ? "激光笔：开" : "激光笔：关";
                }
                if (BoardLaserPointerIndicator != null)
                {
                    BoardLaserPointerIndicator.Fill = on ? activeBrush : idleBrush;
                }
                if (BoardBtnLaserPointer != null)
                {
                    if (on) BoardBtnLaserPointer.BorderBrush = activeBrush;
                    else BoardBtnLaserPointer.SetResourceReference(Control.BorderBrushProperty, "BoardBarBorderBrush");
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

        /// <summary>
        /// 当前生效的激光笔颜色（已含可见性回退处理）。
        /// 激光笔与画笔共用同一份选色：按模式取「该模式最后一次选中的颜色」，
        /// 浮动栏（桌面/批注）记 lastDesktopInkColor、白板记 lastBoardInkColor，两种模式分开记忆、互不干扰。
        /// </summary>
        private Color GetLaserColor()
        {
            try
            {
                int index = currentMode == 1 ? lastBoardInkColor : lastDesktopInkColor;
                var c = GetInkColorByIndex(index);
                // 颜色过暗（如黑色）时回退为红色，保证在任何背景下都能看见
                if (c.R + c.G + c.B < 200) return Color.FromRgb(0xFF, 0x3B, 0x30);
                // 白板（浅色板面）上近白的激光几乎不可见，同样回退为红色；黑板/桌面不受影响
                if (currentMode == 1 && Settings.Canvas.UsingWhiteboard && c.R + c.G + c.B > 700)
                    return Color.FromRgb(0xFF, 0x3B, 0x30);
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
                // 开新轨迹前先回收残留（设备上报类型不一致时旧轨迹可能永远等不到自己的 Up）
                PruneStaleLaserTrails();

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
                trail.LastActivity = DateTime.UtcNow; // 刷新活跃时间，供残留轨迹回收判断
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

        /// <summary>
        /// 结束鼠标轨迹。
        /// 注意：这里**绝不能** e.Handled = true —— Up 属于「抬起」语义，标记 Handled 会吞掉冒泡的
        /// MouseUp，而 ButtonBase 的 Click 正是靠 MouseUp 触发的。只要有一条轨迹残留（见
        /// PruneStaleLaserTrails），此后所有鼠标点击都会变成「按下有效、抬起被吞」：按钮永远不响应，
        /// 只有再到画布上完整走一次按下+抬起（StartLaserTrail 会先结束旧轨迹）才能解开 ——
        /// 这正是「激光笔开启时副屏手写后工具栏点不动，先用鼠标点一下主屏才恢复」的成因。
        /// 拦截墨迹只需处理 Down/Move 即可（EditingMode 已置 None，抬起无需再拦）。
        /// </summary>
        private void LaserPointer_PreviewMouseUp(object sender, MouseButtonEventArgs e)
        {
            if (!_laserTrails.ContainsKey(LaserMouseKey)) return;
            EndLaserTrail(LaserMouseKey);
            PruneStaleLaserTrails();
        }

        /// <summary>轨迹失活回收阈值：超过该时长没有任何事件（且无鼠标键按下）即视为残留</summary>
        private static readonly TimeSpan LaserStaleThreshold = TimeSpan.FromSeconds(10);

        /// <summary>
        /// 清理「设备已松开但轨迹仍在」的残留：
        /// ① 鼠标轨迹存在，却没有任何鼠标键处于按下状态（副屏手写时部分驱动把「按下」报成鼠标、
        ///    而「抬起」报成手写笔/触摸，鼠标轨迹永远等不到自己的 Up）；
        /// ② 任意轨迹超过 LaserStaleThreshold 没有任何事件（触摸/触笔的 Up 丢失或被上报成其他设备类型）。
        /// 不清理的后果见 LaserPointer_PreviewMouseUp：残留会让后续 Up 的处理逻辑持续空转，且轨迹永远不淡出。
        /// </summary>
        private void PruneStaleLaserTrails()
        {
            try
            {
                // ① 鼠标轨迹：只要所有鼠标键都已松开，轨迹必然是残留，立即回收
                if (_laserTrails.ContainsKey(LaserMouseKey)
                    && Mouse.LeftButton != MouseButtonState.Pressed
                    && Mouse.RightButton != MouseButtonState.Pressed
                    && Mouse.MiddleButton != MouseButtonState.Pressed
                    && Mouse.XButton1 != MouseButtonState.Pressed
                    && Mouse.XButton2 != MouseButtonState.Pressed)
                {
                    EndLaserTrail(LaserMouseKey);
                }

                // ② 通用兜底：长时间无任何事件的轨迹（触摸/触笔 Up 丢失）按失活时间回收
                var now = DateTime.UtcNow;
                foreach (var key in _laserTrails.Keys.ToList())
                {
                    if (now - _laserTrails[key].LastActivity >= LaserStaleThreshold)
                    {
                        EndLaserTrail(key);
                    }
                }
            }
            catch { }
        }

        /// <summary>结束全部进行中的轨迹（保留淡出动画）；用于指针离开窗口、失焦、捕获丢失等兜底场景。</summary>
        private void EndAllLaserTrails()
        {
            try
            {
                if (_laserTrails.Count == 0) return;
                foreach (var key in _laserTrails.Keys.ToList()) EndLaserTrail(key);
            }
            catch { }
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
            if (!_laserTrails.ContainsKey(key)) { PruneStaleLaserTrails(); return; }

            EndLaserTrail(key);
            // 手写笔抬起时顺带回收可能残留的鼠标轨迹（副屏驱动上报类型不一致）
            PruneStaleLaserTrails();
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
            if (!_laserTrails.ContainsKey(key)) { PruneStaleLaserTrails(); return; }

            EndLaserTrail(key);
            // 触摸抬起时顺带回收可能残留的鼠标轨迹（副屏驱动上报类型不一致）
            PruneStaleLaserTrails();
        }

        #endregion
    }
}
