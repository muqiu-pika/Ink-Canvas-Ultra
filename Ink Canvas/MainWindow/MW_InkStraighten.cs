using Ink_Canvas.Helpers;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Ink;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using Point = System.Windows.Point;

namespace Ink_Canvas
{
    public partial class MainWindow : Window
    {
        private const int InkStraightenMousePointerId = -1;

        /// <summary>低速判定阈值（px/ms）：低于该速度视为停顿，沿用 Artistry 默认值</summary>
        private const double InkStraightenSpeedThresholdPxPerMs = 0.03;

        private sealed class InkStraightenSession
        {
            public Point StartPoint;
            public Point LastPoint;
            public DateTime LastTimestamp;
            public bool IsLowSpeedCandidate;
            public DateTime LowSpeedStartTimestamp;
            public Point LowSpeedAnchorPoint;
            public double LowSpeedDisplacement;
            public bool IsTriggered;
            public bool IsCommitted;
            public bool IsInputSuppressed;
            public Line PreviewLine;
            public Point LatestPoint;
        }

        /// <summary>当前活动的顿笔拉直会话集合（按指针 id 管理；鼠标固定为 -1）</summary>
        private readonly Dictionary<int, InkStraightenSession> _inkStraightenSessions =
            new Dictionary<int, InkStraightenSession>();

        /// <summary>会话残留回收阈值：输入链路异常中断（收不到 Up）时按最后活跃时间回收</summary>
        private static readonly TimeSpan InkStraightenSessionStaleTimeout = TimeSpan.FromSeconds(30);

        private static bool IsPromotedFromStylus(MouseEventArgs e)
        {
            return e?.StylusDevice != null;
        }

        /// <summary>
        /// 顿笔拉直当前是否可用：开关开启 + 墨迹输入态（普通墨迹，或多指书写模式的触摸墨迹态）+
        /// 非图形绘制、非橡皮擦、非激光笔、非单指拖动模式。
        /// </summary>
        private bool IsInkStraightenAvailable()
        {
            bool isInkInputMode = inkCanvas.EditingMode == InkCanvasEditingMode.Ink;
            bool isMultiTouchInkInputMode = isInMultiTouchMode && inkCanvas.EditingMode == InkCanvasEditingMode.None;

            return Settings.Canvas.StopTimingStraighten
                   && (isInkInputMode || isMultiTouchInkInputMode)
                   && drawingShapeMode == 0
                   && !forceEraser
                   && !isLaserPointerEnabled
                   && !isSingleFingerDragMode;
        }

        private static double InkStraightenDistance(Point a, Point b)
        {
            double dx = a.X - b.X;
            double dy = a.Y - b.Y;
            return Math.Sqrt(dx * dx + dy * dy);
        }

        private double GetInputScaleFactor()
        {
            try
            {
                var dpi = VisualTreeHelper.GetDpi(this);
                return Math.Max(1.0, dpi.DpiScaleX);
            }
            catch
            {
                return 1.0;
            }
        }

        /// <summary>停顿时长阈值（ms），夹在设置滑条范围 [100, 2000] 内</summary>
        private int ClampStopTimingThresholdMs()
        {
            return Math.Max(100, Math.Min(2000, Settings.Canvas.StopTimingThresholdMs));
        }

        /// <summary>
        /// 在构造函数中调用一次：挂接 inkCanvas 的 Stylus 事件（不依赖鼠标提升，对手写笔/触摸都可靠）
        /// 与窗口级 Preview 抬笔兜底（指针离开画布/窗口时也能结束会话）。
        /// </summary>
        private void InitializeInkStraighten()
        {
            try
            {
                inkCanvas.StylusDown += inkCanvas_StylusDownForStraighten;
                inkCanvas.StylusMove += inkCanvas_StylusMoveForStraighten;
                inkCanvas.StylusUp += inkCanvas_StylusUpForStraighten;

                // 兜底：Up 在画布外/被其它逻辑吞掉时，窗口 Preview 阶段仍能结束会话
                PreviewMouseUp += Window_PreviewMouseUpForStraighten;
                PreviewStylusUp += Window_PreviewStylusUpForStraighten;
            }
            catch (Exception ex)
            {
                LogHelper.WriteLogToFile("InitializeInkStraighten failed | " + ex.Message, LogHelper.LogType.Warning);
            }
        }

        /// <summary>清理长时间未更新的会话，避免输入链路异常中断时会话与预览线残留。</summary>
        private void CleanupStaleInkStraightenSessions()
        {
            if (_inkStraightenSessions.Count == 0) return;

            DateTime now = DateTime.UtcNow;
            var stalePointerIds = _inkStraightenSessions
                .Where(item => (now - item.Value.LastTimestamp) > InkStraightenSessionStaleTimeout)
                .Select(item => item.Key)
                .ToList();

            foreach (var pointerId in stalePointerIds)
            {
                if (!_inkStraightenSessions.TryGetValue(pointerId, out var session)) continue;

                RemoveInkStraightenPreview(session);
                if (session.IsInputSuppressed && !isLaserPointerEnabled &&
                    inkCanvas.EditingMode == InkCanvasEditingMode.None)
                {
                    inkCanvas.EditingMode = InkCanvasEditingMode.Ink;
                }
                _inkStraightenSessions.Remove(pointerId);
            }
        }

        /// <summary>启动会话（按下时）。多指手势进行中（dec.Count &gt; 1）不启动。</summary>
        private void StartInkStraightenSession(int pointerId, Point startPoint)
        {
            if (!IsInkStraightenAvailable()) return;
            if (dec != null && dec.Count > 1) return;

            CleanupStaleInkStraightenSessions();
            var now = DateTime.UtcNow;
            _inkStraightenSessions[pointerId] = new InkStraightenSession
            {
                StartPoint = startPoint,
                LastPoint = startPoint,
                LatestPoint = startPoint,
                LastTimestamp = now,
                IsLowSpeedCandidate = true,
                LowSpeedStartTimestamp = now,
                LowSpeedAnchorPoint = startPoint,
                LowSpeedDisplacement = 0
            };
        }

        private void UpdateInkStraightenSession(int pointerId, Point currentPoint)
        {
            if (!_inkStraightenSessions.TryGetValue(pointerId, out var session)) return;
            if (!session.IsTriggered && !IsInkStraightenAvailable()) return;

            DateTime now = DateTime.UtcNow;
            double deltaMs = Math.Max(1, (now - session.LastTimestamp).TotalMilliseconds);
            double deltaDistance = InkStraightenDistance(currentPoint, session.LastPoint) * GetInputScaleFactor();
            double speed = deltaDistance / deltaMs;

            session.LatestPoint = currentPoint;

            if (!session.IsTriggered)
            {
                if (speed < InkStraightenSpeedThresholdPxPerMs)
                {
                    if (!session.IsLowSpeedCandidate)
                    {
                        session.IsLowSpeedCandidate = true;
                        session.LowSpeedStartTimestamp = now;
                        session.LowSpeedAnchorPoint = currentPoint;
                        session.LowSpeedDisplacement = 0;
                    }
                    else
                    {
                        session.LowSpeedDisplacement =
                            InkStraightenDistance(currentPoint, session.LowSpeedAnchorPoint) * GetInputScaleFactor();
                    }

                    // 触发条件：低速段位移不超过容差 + 停顿达到设定时长 + 已画线段达到最小长度
                    double drawnLength = InkStraightenDistance(session.StartPoint, currentPoint) * GetInputScaleFactor();
                    double minLength = Settings.Canvas.AutoStraightenLineThreshold * GetResolutionScale();
                    if (session.LowSpeedDisplacement <= Settings.Canvas.StopTimingError
                        && (now - session.LowSpeedStartTimestamp).TotalMilliseconds >= ClampStopTimingThresholdMs()
                        && drawnLength >= minLength)
                    {
                        session.IsTriggered = true;
                        HideMultiTouchRawStrokePreview(pointerId);
                        if (!session.IsInputSuppressed && inkCanvas.EditingMode == InkCanvasEditingMode.Ink)
                        {
                            // 抑制原生墨迹收集：进行中的原生笔画会被放弃（不会产生 StrokeCollected），
                            // 后续轨迹只更新预览直线
                            inkCanvas.EditingMode = InkCanvasEditingMode.None;
                            session.IsInputSuppressed = true;
                        }
                        UpdateInkStraightenPreview(session, currentPoint);
                    }
                    else if (session.LowSpeedDisplacement > Settings.Canvas.StopTimingError)
                    {
                        session.IsLowSpeedCandidate = false;
                    }
                }
                else
                {
                    session.IsLowSpeedCandidate = false;
                }
            }
            else
            {
                UpdateInkStraightenPreview(session, currentPoint);
            }

            session.LastPoint = currentPoint;
            session.LastTimestamp = now;
        }

        /// <summary>多指书写模式下触发拉直后，立即移除该指针的原始自由曲线预览层。</summary>
        private void HideMultiTouchRawStrokePreview(int pointerId)
        {
            if (!isInMultiTouchMode) return;

            var visualCanvas = GetVisualCanvas(pointerId);
            if (visualCanvas != null && inkCanvas.Children.Contains(visualCanvas))
            {
                inkCanvas.Children.Remove(visualCanvas);
            }
        }

        /// <summary>更新实时预览直线（首帧创建，样式取自当前画笔属性）。</summary>
        private void UpdateInkStraightenPreview(InkStraightenSession session, Point currentPoint)
        {
            if (session.PreviewLine == null)
            {
                var drawingAttributes = inkCanvas.DefaultDrawingAttributes;
                session.PreviewLine = new Line
                {
                    IsHitTestVisible = false,
                    Stroke = new SolidColorBrush(drawingAttributes.Color),
                    StrokeThickness = Math.Max(1, drawingAttributes.Width),
                    StrokeStartLineCap = PenLineCap.Round,
                    StrokeEndLineCap = PenLineCap.Round
                };
                System.Windows.Controls.Panel.SetZIndex(session.PreviewLine, 9999);
                Main_Grid.Children.Add(session.PreviewLine);
            }
            session.PreviewLine.X1 = session.StartPoint.X;
            session.PreviewLine.Y1 = session.StartPoint.Y;
            session.PreviewLine.X2 = currentPoint.X;
            session.PreviewLine.Y2 = currentPoint.Y;
        }

        private void RemoveInkStraightenPreview(InkStraightenSession session)
        {
            if (session.PreviewLine != null && Main_Grid.Children.Contains(session.PreviewLine))
            {
                Main_Grid.Children.Remove(session.PreviewLine);
                session.PreviewLine = null;
            }
        }

        /// <summary>
        /// 结束指定会话（抬笔/兜底）。
        /// 已触发且未提交时：多指书写预览路径（未抑制原生收集）延迟到 MainWindow_StylusUp 的
        /// 手动提交处替换原始笔迹（避免与预览层交接竞态）；其余情况直接提交直线。
        /// </summary>
        private void EndInkStraightenSession(int pointerId)
        {
            if (!_inkStraightenSessions.TryGetValue(pointerId, out var session)) return;

            if (session.IsCommitted)
            {
                if (session.IsInputSuppressed && !isLaserPointerEnabled &&
                    inkCanvas.EditingMode == InkCanvasEditingMode.None)
                {
                    inkCanvas.EditingMode = InkCanvasEditingMode.Ink;
                }
                _inkStraightenSessions.Remove(pointerId);
                return;
            }

            RemoveInkStraightenPreview(session);

            bool shouldDeferCommitToStrokeCollected = session.IsTriggered
                                                      && isInMultiTouchMode
                                                      && !session.IsInputSuppressed;

            if (session.IsTriggered && !shouldDeferCommitToStrokeCollected)
            {
                CommitInkStraightenStroke(
                    session.StartPoint, session.LatestPoint,
                    0.5f, 0.5f, inkCanvas.DefaultDrawingAttributes.Clone());
                session.IsCommitted = true;
            }

            if (session.IsInputSuppressed && !isLaserPointerEnabled &&
                inkCanvas.EditingMode == InkCanvasEditingMode.None)
            {
                inkCanvas.EditingMode = InkCanvasEditingMode.Ink;
            }

            if (!shouldDeferCommitToStrokeCollected)
            {
                _inkStraightenSessions.Remove(pointerId);
            }
        }

        /// <summary>按两点提交拉直直线（继承起止压感与画笔属性，计入撤销历史）。</summary>
        private void CommitInkStraightenStroke(Point start, Point end, float startPressure, float endPressure,
            DrawingAttributes drawingAttributes)
        {
            var straightStroke = new Stroke(new StylusPointCollection
            {
                new StylusPoint(start.X, start.Y, startPressure),
                new StylusPoint(end.X, end.Y, endPressure)
            })
            {
                DrawingAttributes = drawingAttributes
            };

            var previousCommitType = _currentCommitType;
            _currentCommitType = CommitReason.UserInput;
            try
            {
                inkCanvas.Strokes.Add(straightStroke);
            }
            finally
            {
                _currentCommitType = previousCommitType;
            }
        }

        /// <summary>
        /// StrokeCollected 阶段的替换钩子：已触发但原生笔画仍被提交时（时序边缘），把原始笔画替换为直线。
        /// 返回 true 表示原始笔画已被替换，调用方应跳过后续处理。
        /// </summary>
        private bool TryApplyPendingInkStraighten(Stroke rawStroke)
        {
            if (rawStroke == null || rawStroke.StylusPoints.Count == 0) return false;

            Point rawStartPoint = rawStroke.StylusPoints.First().ToPoint();
            InkStraightenSession matchedSession = null;
            double minDistance = double.MaxValue;
            foreach (var session in _inkStraightenSessions.Values)
            {
                if (!session.IsTriggered || session.IsCommitted) continue;

                double distance = InkStraightenDistance(rawStartPoint, session.StartPoint);
                if (distance < minDistance)
                {
                    minDistance = distance;
                    matchedSession = session;
                }
            }

            if (matchedSession == null || minDistance > 48) return false;

            RemoveInkStraightenPreview(matchedSession);

            var straightStroke = new Stroke(new StylusPointCollection
            {
                new StylusPoint(matchedSession.StartPoint.X, matchedSession.StartPoint.Y,
                    rawStroke.StylusPoints.First().PressureFactor),
                new StylusPoint(matchedSession.LatestPoint.X, matchedSession.LatestPoint.Y,
                    rawStroke.StylusPoints.Last().PressureFactor)
            })
            {
                DrawingAttributes = rawStroke.DrawingAttributes.Clone()
            };

            SetNewBackupOfStroke();
            var previousReplaceCommitType = _currentCommitType;
            try
            {
                _currentCommitType = CommitReason.ShapeRecognition;
                inkCanvas.Strokes.Remove(rawStroke);
                inkCanvas.Strokes.Add(straightStroke);
            }
            finally
            {
                _currentCommitType = previousReplaceCommitType;
            }

            matchedSession.IsCommitted = true;
            var matchedSessionPair = _inkStraightenSessions.FirstOrDefault(item => ReferenceEquals(item.Value, matchedSession));
            if (matchedSessionPair.Value != null)
            {
                _inkStraightenSessions.Remove(matchedSessionPair.Key);
            }
            return true;
        }

        /// <summary>
        /// 多指书写延迟提交钩子（MainWindow_StylusUp 调用）：会话已触发时直接提交直线并丢弃原始预览笔迹。
        /// 返回 true 表示已由拉直提交，调用方应跳过原始笔迹的入库与其 StrokeCollected 处理。
        /// </summary>
        private bool TryCommitDeferredInkStraightenByPointer(int pointerId, Stroke rawStroke)
        {
            if (!_inkStraightenSessions.TryGetValue(pointerId, out var session)
                || !session.IsTriggered
                || session.IsCommitted)
            {
                return false;
            }

            RemoveInkStraightenPreview(session);

            float startPressure = rawStroke?.StylusPoints?.Count > 0 ? rawStroke.StylusPoints.First().PressureFactor : 0.5f;
            float endPressure = rawStroke?.StylusPoints?.Count > 0 ? rawStroke.StylusPoints.Last().PressureFactor : 0.5f;
            CommitInkStraightenStroke(
                session.StartPoint, session.LatestPoint, startPressure, endPressure,
                rawStroke?.DrawingAttributes?.Clone() ?? inkCanvas.DefaultDrawingAttributes.Clone());

            session.IsCommitted = true;
            if (session.IsInputSuppressed && !isLaserPointerEnabled &&
                inkCanvas.EditingMode == InkCanvasEditingMode.None)
            {
                inkCanvas.EditingMode = InkCanvasEditingMode.Ink;
            }
            _inkStraightenSessions.Remove(pointerId);
            return true;
        }

        #region 输入事件入口

        private void inkCanvas_StylusDownForStraighten(object sender, StylusDownEventArgs e)
        {
            try { StartInkStraightenSession(e.StylusDevice.Id, e.GetPosition(inkCanvas)); } catch { }
        }

        private void inkCanvas_StylusMoveForStraighten(object sender, StylusEventArgs e)
        {
            try { UpdateInkStraightenSession(e.StylusDevice.Id, e.GetPosition(inkCanvas)); } catch { }
        }

        private void inkCanvas_StylusUpForStraighten(object sender, StylusEventArgs e)
        {
            try { EndInkStraightenSession(e.StylusDevice.Id); } catch { }
        }

        /// <summary>鼠标路径（由 inkCanvas_MouseDown/Move/Up 调用）；笔提升的鼠标事件跳过，避免与 Stylus 会话重复。</summary>
        private void HandleMouseStraightenDown(MouseButtonEventArgs e)
        {
            if (IsPromotedFromStylus(e)) return;
            if (e?.ChangedButton == MouseButton.Left)
            {
                StartInkStraightenSession(InkStraightenMousePointerId, e.GetPosition(inkCanvas));
            }
        }

        private void HandleMouseStraightenMove(MouseEventArgs e)
        {
            if (IsPromotedFromStylus(e)) return;
            if (e.LeftButton == MouseButtonState.Pressed)
            {
                UpdateInkStraightenSession(InkStraightenMousePointerId, e.GetPosition(inkCanvas));
            }
        }

        private void HandleMouseStraightenUp(MouseButtonEventArgs e)
        {
            if (e != null && IsPromotedFromStylus(e)) return;
            if (e == null || e.ChangedButton == MouseButton.Left)
            {
                EndInkStraightenSession(InkStraightenMousePointerId);
            }
        }

        private void Window_PreviewMouseUpForStraighten(object sender, MouseButtonEventArgs e)
        {
            try
            {
                if (e.ChangedButton == MouseButton.Left)
                {
                    EndInkStraightenSession(InkStraightenMousePointerId);
                }
            }
            catch { }
        }

        private void Window_PreviewStylusUpForStraighten(object sender, StylusEventArgs e)
        {
            try { EndInkStraightenSession(e.StylusDevice.Id); } catch { }
        }

        #endregion
    }
}
