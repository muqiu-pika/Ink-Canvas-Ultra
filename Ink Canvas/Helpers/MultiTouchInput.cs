using System;
using System.Windows;
using System.Windows.Ink;
using System.Windows.Input;
using System.Windows.Media;

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

        // 维护多段笔迹，避免短时间长距离采样被直线连接
        private Stroke _currentStroke;
        private readonly StrokeCollection _strokes = new StrokeCollection();
        private StylusPoint? _lastPoint = null;
        private int _lastTick = -1;
        private const double MaxGapDistance = 100.0; // 像素阈值：短时间内两点距离过大则断开
        private const int ShortTimeMs = 30; // 时间阈值：认为是“短时间”

        public StrokeCollection StrokeCollection => _strokes;

        /// <summary>
        ///     在笔迹中添加点
        /// </summary>
        /// <param name="point"></param>
        public void Add(StylusPoint point)
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
                return;
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
            else
            {
                _currentStroke.StylusPoints.Add(point);
            }

            _lastPoint = point;
            _lastTick = now;
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

        private readonly DrawingAttributes _drawingAttributes;

        public static implicit operator Stroke(StrokeVisual v)
        {
            throw new NotImplementedException();
        }
    }
}
