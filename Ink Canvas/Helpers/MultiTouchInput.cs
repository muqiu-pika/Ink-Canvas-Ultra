using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Ink;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace Ink_Canvas.Helpers
{
    public class VisualCanvas : FrameworkElement
    {
        // 预览层可承载多个视觉对象：活动笔段（构造传入）+ 已封存的分段笔迹。
        // 每段只渲染一次，长笔画书写时不再每帧全量重绘。
        private readonly List<Visual> _visuals = new List<Visual>();

        protected override Visual GetVisualChild(int index)
        {
            return _visuals[index];
        }

        protected override int VisualChildrenCount => _visuals.Count;

        public VisualCanvas(DrawingVisual visual)
        {
            AddVisual(visual);
            IsHitTestVisible = false;
            Focusable = false;
        }

        public void AddVisual(Visual visual)
        {
            if (visual == null) return;
            _visuals.Add(visual);
            AddVisualChild(visual);
        }
    }

    public class StrokeVisual : DrawingVisual
    {
        public StrokeVisual() : this(new DrawingAttributes()
        {
            Color = Colors.Red,
            //FitToCurve = true,
            Width = 3,
            Height = 3
        })
        {
        }

        public StrokeVisual(DrawingAttributes drawingAttributes)
        {
            _drawingAttributes = drawingAttributes;
        }

        public Stroke Stroke { set; get; }

        private Stroke _currentStroke;
        private readonly StrokeCollection _strokes = new StrokeCollection();
        private StylusPoint? _lastPoint = null;
        private int _lastTick = -1;
        private const double MaxGapDistance = 100.0;
        private const int ShortTimeMs = 30;
        private const double MinPointDistance = 0.6;
        private bool _redrawScheduled = false;

        // 增量渲染：活动笔段的点数达到阈值即"封存"为独立 DrawingVisual（只渲染一次），
        // 之后每帧节流重绘只处理活动笔段，避免长笔画时 O(n²) 的全量重绘造成墨迹延迟。
        private const int ChunkPointThreshold = 64;
        private readonly List<DrawingVisual> _sealedChunks = new List<DrawingVisual>();

        /// <summary>承载本视觉的预览容器；封存的分段笔迹会追加到这里（可为 null，仅测试场景）。</summary>
        public VisualCanvas HostCanvas { get; set; }

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
                // 旧笔段不会再增长：立即封存，仅活动笔段参与后续重绘
                SealCurrentStroke(false);

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

                // 达到分段阈值：封存当前笔段，并以最后一点为起点延续新笔段（视觉上无缝衔接）
                if (_currentStroke.StylusPoints.Count >= ChunkPointThreshold)
                {
                    SealCurrentStroke(true);
                }
            }

            _lastPoint = point;
            _lastTick = now;
            return true;
        }

        /// <summary>
        ///     把当前活动笔段渲染为独立 DrawingVisual 并封存；seedWithLastPoint 时以最后一点延续新笔段。
        /// </summary>
        private void SealCurrentStroke(bool seedWithLastPoint)
        {
            if (_currentStroke == null) return;

            try
            {
                var chunk = new DrawingVisual();
                using (var dc = chunk.RenderOpen())
                {
                    _currentStroke.Draw(dc);
                }
                _sealedChunks.Add(chunk);
                HostCanvas?.AddVisual(chunk);
            }
            catch { }

            if (seedWithLastPoint && _lastPoint.HasValue)
            {
                var collection = new StylusPointCollection { _lastPoint.Value };
                _currentStroke = new Stroke(collection) { DrawingAttributes = _drawingAttributes };
                _strokes.Add(_currentStroke);
            }
            else
            {
                _currentStroke = null;
            }
        }

        public void Redraw()
        {
            try
            {
                using (var dc = RenderOpen())
                {
                    // 已封存的笔段由各自的 DrawingVisual 呈现（只渲染一次），
                    // 这里只需重绘尚未封存的活动笔段
                    if (_currentStroke != null)
                    {
                        _currentStroke.Draw(dc);
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
