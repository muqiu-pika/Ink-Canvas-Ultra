using System;
using System.Windows;
using System.Windows.Ink;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace Ink_Canvas.Helpers
{
    public class VisualCanvas : FrameworkElement
    {
        protected override Visual GetVisualChild(int index)
        {
            return Visual;
        }

        protected override int VisualChildrenCount => 1;

        public VisualCanvas(DrawingVisual visual)
        {
            Visual = visual;
            AddVisualChild(visual);
            IsHitTestVisible = false;
            Focusable = false;
        }

        public DrawingVisual Visual { get; }
    }

    /// <summary>
    ///     用于显示笔迹的类
    /// </summary>
    public class StrokeVisual : DrawingVisual
    {
        /// <summary>
        ///     创建显示笔迹的类
        /// </summary>
        public StrokeVisual() : this(new DrawingAttributes()
        {
            Color = Colors.Red,
            //FitToCurve = true,
            Width = 3,
            Height = 3
        })
        {
        }

        /// <summary>
        ///     创建显示笔迹的类
        /// </summary>
        /// <param name="drawingAttributes"></param>
        public StrokeVisual(DrawingAttributes drawingAttributes)
        {
            _drawingAttributes = drawingAttributes;
        }

        /// <summary>
        ///     设置或获取显示的笔迹
        /// </summary>
        public Stroke Stroke { set; get; }

        private Stroke _currentStroke;
        private readonly StrokeCollection _strokes = new StrokeCollection();
        private StylusPoint? _lastPoint = null;
        private int _lastTick = -1;
        private const double MaxGapDistance = 100.0;
        private const int ShortTimeMs = 30;
        // 相邻点最小间距（像素）：触摸屏的原始采样往往极密（0.1px 级），
        // 过滤过近的冗余点可大幅减少累积点数，从而降低每帧全量重绘（Redraw）的
        // Draw 成本与内存/GC 占用；0.6px 对笔迹视觉影响可忽略。
        private const double MinPointDistance = 0.6;
        private bool _redrawScheduled = false;

        public StrokeCollection StrokeCollection => _strokes;

        /// <summary>
        ///     在笔迹中添加点。
        /// </summary>
        /// <returns>true 表示本批输入中确实加入了新点，调用方据此决定是否重绘，避免空帧重绘。</returns>
        public bool Add(StylusPoint point)
        {
            var now = Environment.TickCount;

            if (_currentStroke == null)
            {
                var collection = new StylusPointCollection { point };
                _currentStroke = new Stroke(collection) { DrawingAttributes = _drawingAttributes };
                _strokes.Add(_currentStroke);
                if (Stroke == null) Stroke = _currentStroke; // 保持兼容：首段作为 Stroke 属性
                _lastPoint = point;
                _lastTick = now;
                return true;
            }

            double dist = 0.0;
            int dt = 0;
            if (_lastPoint.HasValue)
            {
                var lp = _lastPoint.Value;
                var dx = point.X - lp.X;
                var dy = point.Y - lp.Y;
                dist = Math.Sqrt(dx * dx + dy * dy);
                dt = (_lastTick < 0) ? 0 : (now - _lastTick);
            }

            // 若在极短时间内跨越了较远距离，则视为断线，开启新笔段
            if (dt >= 0 && dt <= ShortTimeMs && dist >= MaxGapDistance)
            {
                var collection = new StylusPointCollection { point };
                _currentStroke = new Stroke(collection) { DrawingAttributes = _drawingAttributes };
                _strokes.Add(_currentStroke);
            }
            else if (_lastPoint.HasValue && dist < MinPointDistance)
            {
                // 去冗余：与上一实际点距离过近的采样点直接丢弃。
                // 仍刷新 _lastPoint/_lastTick（保持最新），避免误触断线判定，
                // 同时让后续点重新计算距离。分叉开的点不受影响（其 dist 已足够大）。
                _lastPoint = point;
                _lastTick = now;
                return false;
            }
            else
            {
                _currentStroke.StylusPoints.Add(point);
            }

            _lastPoint = point;
            _lastTick = now;
            return true;
        }

        /// <summary>
        ///     重新画出笔迹
        /// </summary>
        public void Redraw()
        {
            try
            {
                using (var dc = RenderOpen())
                {
                    foreach (var s in _strokes)
                    {
                        s.Draw(dc);
                    }
                }
            }
            catch { }
        }

        /// <summary>
        ///     节流重绘：在渲染优先级安排一次重绘，避免每次移动都立即重绘
        /// </summary>
        public void RedrawThrottled()
        {
            if (_redrawScheduled) return;
            _redrawScheduled = true;
            Dispatcher.BeginInvoke(DispatcherPriority.Render, new Action(() =>
            {
                _redrawScheduled = false;
                Redraw();
            }));
        }

        private readonly DrawingAttributes _drawingAttributes;

        public static implicit operator Stroke(StrokeVisual v)
        {
            throw new NotImplementedException();
        }
    }
}
