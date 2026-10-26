using System;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace Ink_Canvas
{
    /// <summary>
    /// 「墨迹选项」色块长按的环形进度反馈：在色块外围画一圈从 12 点方向开始逐渐闭合的圆弧，
    /// 走到一整圈时触发取色器。
    /// 用 Adorner 而不是往布局里插元素：色块位于 UniformGrid / SimpleStackPanel 中，
    /// 直接加子元素会挤占布局；Adorner 不参与布局、不被裁剪到元素边界内，
    /// 正好用来做"外围一圈"这种临时装饰。
    /// 进度由 WPF 动画（<see cref="DoubleAnimation"/>）驱动而非定时器逐帧 setter：
    /// 动画跟随渲染时钟插值，不会因为按住期间 UI 线程忙于处理 TouchMove 而掉帧。
    /// </summary>
    internal sealed class PenSlotLongPressRingAdorner : Adorner
    {
        /// <summary>环与色块边缘的间距（设备无关像素）</summary>
        private const double RingGap = 5;

        /// <summary>环的粗细</summary>
        private const double RingThickness = 3;

        /// <summary>长按进度 0..1；AffectsRender 让动画每帧自动触发重绘，无需手动 InvalidateVisual。</summary>
        public static readonly DependencyProperty ProgressProperty =
            DependencyProperty.Register(
                nameof(Progress),
                typeof(double),
                typeof(PenSlotLongPressRingAdorner),
                new FrameworkPropertyMetadata(
                    0.0,
                    FrameworkPropertyMetadataOptions.AffectsRender,
                    null,
                    CoerceProgress));

        private Brush _ringBrush = Brushes.White;

        // 画笔缓存：环的颜色只在挂载时确定，没必要每帧 clone/新建
        private Pen _trackPen;
        private Pen _progressPen;
        private Brush _penSourceBrush;

        public PenSlotLongPressRingAdorner(UIElement adornedElement) : base(adornedElement)
        {
            // 纯装饰：不能挡住色块自身的点击/触摸
            IsHitTestVisible = false;
        }

        /// <summary>长按进度 0..1</summary>
        public double Progress
        {
            get { return (double)GetValue(ProgressProperty); }
            set { SetValue(ProgressProperty, value); }
        }

        private static object CoerceProgress(DependencyObject d, object baseValue)
        {
            double value = (double)baseValue;
            if (value < 0) return 0.0;
            if (value > 1) return 1.0;
            return value;
        }

        /// <summary>环的颜色（白圈 / 主题蓝圈由外部按画布明暗决定）</summary>
        public Brush RingBrush
        {
            get { return _ringBrush; }
            set
            {
                var next = value ?? Brushes.White;
                if (ReferenceEquals(_ringBrush, next)) return;
                _ringBrush = next;
                InvalidatePenCache();
                InvalidateVisual();
            }
        }

        /// <summary>
        /// 开始长按进度动画：从当前进度线性走到 1，完成后回调。
        /// 用线性（不加缓动）——进度条必须匀速，否则"围满"的节奏会失真。
        /// </summary>
        public void StartProgress(double durationMs, Action onCompleted)
        {
            if (durationMs <= 0) durationMs = 1;
            var animation = new DoubleAnimation(0.0, 1.0, TimeSpan.FromMilliseconds(durationMs))
            {
                EasingFunction = null,
                FillBehavior = FillBehavior.HoldEnd
            };
            if (onCompleted != null)
            {
                EventHandler handler = null;
                handler = (s, e) =>
                {
                    animation.Completed -= handler;
                    onCompleted();
                };
                animation.Completed += handler;
            }
            BeginAnimation(ProgressProperty, animation);
        }

        /// <summary>停止进度动画并把进度归零（抬手/位移超限时调用）。</summary>
        public void StopProgress()
        {
            BeginAnimation(ProgressProperty, null);
            SetCurrentValue(ProgressProperty, 0.0);
        }

        private void InvalidatePenCache()
        {
            _penSourceBrush = null;
            _trackPen = null;
            _progressPen = null;
        }

        private void EnsurePens()
        {
            if (_trackPen != null && _progressPen != null && ReferenceEquals(_penSourceBrush, _ringBrush)) return;

            _penSourceBrush = _ringBrush;

            var trackBrush = _ringBrush.Clone();
            trackBrush.Opacity = 0.22;
            if (trackBrush.CanFreeze) trackBrush.Freeze();

            var trackPen = new Pen(trackBrush, RingThickness);
            if (trackPen.CanFreeze) trackPen.Freeze();
            _trackPen = trackPen;

            var progressPen = new Pen(_ringBrush, RingThickness)
            {
                StartLineCap = PenLineCap.Round,
                EndLineCap = PenLineCap.Round
            };
            if (progressPen.CanFreeze) progressPen.Freeze();
            _progressPen = progressPen;
        }

        protected override void OnRender(DrawingContext dc)
        {
            var element = AdornedElement as FrameworkElement;
            if (element == null || dc == null) return;

            double width = element.ActualWidth;
            double height = element.ActualHeight;
            if (width <= 1 || height <= 1) return;

            var center = new Point(width / 2, height / 2);
            double radius = Math.Min(width, height) / 2 + RingGap;
            if (radius <= RingThickness) return;

            EnsurePens();

            // 底圈：淡淡的完整圆，让用户看得出"这是一圈进度"，也能看到还差多少
            dc.DrawEllipse(null, _trackPen, center, radius, radius);

            double progress = Progress;
            double sweep = 360.0 * progress;
            if (sweep < 0.3) return;

            // 满圈用整圆绘制：ArcTo 起点终点重合时不产生可见弧
            if (sweep >= 359.5)
            {
                dc.DrawEllipse(null, _progressPen, center, radius, radius);
                return;
            }

            var geometry = new StreamGeometry();
            using (var ctx = geometry.Open())
            {
                var start = PointOnCircle(center, radius, -90);
                var end = PointOnCircle(center, radius, -90 + sweep);
                ctx.BeginFigure(start, false, false);
                ctx.ArcTo(end, new Size(radius, radius), 0, sweep > 180, SweepDirection.Clockwise, true, false);
            }
            geometry.Freeze();
            dc.DrawGeometry(null, _progressPen, geometry);
        }

        private static Point PointOnCircle(Point center, double radius, double angleDegrees)
        {
            double radians = angleDegrees * Math.PI / 180.0;
            return new Point(center.X + radius * Math.Cos(radians), center.Y + radius * Math.Sin(radians));
        }
    }

    /// <summary>把 <see cref="PenSlotLongPressRingAdorner"/> 挂到色块上 / 摘下来。</summary>
    internal static class PenSlotLongPressRing
    {
        /// <summary>返回 null 表示当前没有可用 AdornerLayer（极少见），调用方按"无反馈"降级处理。</summary>
        public static PenSlotLongPressRingAdorner Attach(UIElement element, Brush ringBrush)
        {
            if (element == null) return null;
            try
            {
                var layer = AdornerLayer.GetAdornerLayer(element);
                if (layer == null) return null;
                var adorner = new PenSlotLongPressRingAdorner(element) { RingBrush = ringBrush };
                layer.Add(adorner);
                return adorner;
            }
            catch
            {
                return null;
            }
        }

        public static void Detach(PenSlotLongPressRingAdorner adorner)
        {
            if (adorner == null) return;
            try
            {
                adorner.StopProgress();
                var layer = AdornerLayer.GetAdornerLayer(adorner.AdornedElement);
                if (layer != null) layer.Remove(adorner);
            }
            catch { }
        }
    }
}
