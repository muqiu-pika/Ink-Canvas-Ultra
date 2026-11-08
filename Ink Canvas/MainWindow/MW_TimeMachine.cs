using Ink_Canvas.Helpers;
using System.Collections.Generic;
using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Ink;
using System.Windows.Input;
using System.Diagnostics;
using System.Windows.Media;

namespace Ink_Canvas
{
    public partial class MainWindow : Window
    {
        private enum CommitReason
        {
            UserInput,
            CodeInput,
            ShapeDrawing,
            ShapeRecognition,
            ClearingCanvas,
            Manipulation,
            ImageInsert
        }

        private CommitReason _currentCommitType = CommitReason.UserInput;
        private bool IsEraseByPoint => inkCanvas.EditingMode == InkCanvasEditingMode.EraseByPoint;
        private StrokeCollection ReplacedStroke;
        private StrokeCollection AddedStroke;

        // 面积擦（EraseByPoint）手势的擦除轨迹采样：手势结束时按"圆刷沿路径扫过的区域"
        // 再补一轮命中检测并移除漏检笔迹。InkCanvas 的逐点命中检测在快速划过时会在
        // 采样间隙"隧穿"，留下应擦未擦的笔迹残留（面积擦擦不干净的经典表现）。
        private List<Point> _areaEraseSweepPoints;
        private double _areaEraseSweepRadius;

        /// <summary>挂接擦除轨迹采样：Preview 阶段统一捕获笔/触摸/鼠标提升的移动，仅在面积擦模式下记录。</summary>
        private void InitializeAreaEraseSweepTracking()
        {
            try
            {
                inkCanvas.PreviewStylusMove += InkCanvas_PreviewStylusMoveForAreaErase;
            }
            catch (Exception ex)
            {
                LogHelper.WriteLogToFile("InitializeAreaEraseSweepTracking failed | " + ex.Message, LogHelper.LogType.Warning);
            }
        }

        private void InkCanvas_PreviewStylusMoveForAreaErase(object sender, StylusEventArgs e)
        {
            try
            {
                if (inkCanvas.EditingMode != InkCanvasEditingMode.EraseByPoint) return;

                if (_areaEraseSweepPoints == null)
                {
                    _areaEraseSweepPoints = new List<Point>();
                    _areaEraseSweepRadius = 0;
                }

                var shape = inkCanvas.EraserShape;
                if (shape != null && shape.Width > 0)
                {
                    _areaEraseSweepRadius = Math.Max(_areaEraseSweepRadius, shape.Width);
                }

                _areaEraseSweepPoints.Add(e.GetPosition(inkCanvas));

                // 采样点过多时按半数抽稀（保留偶数位），路径覆盖密度仍远高于 InkCanvas 自身逐点检测
                if (_areaEraseSweepPoints.Count >= 512)
                {
                    var decimated = new List<Point>(_areaEraseSweepPoints.Count / 2 + 1);
                    for (int i = 0; i < _areaEraseSweepPoints.Count; i += 2)
                    {
                        decimated.Add(_areaEraseSweepPoints[i]);
                    }
                    _areaEraseSweepPoints = decimated;
                }
            }
            catch { }
        }

        /// <summary>
        /// 面积擦手势结束时的补擦：按采样轨迹做椭圆命中检测，移除 InkCanvas 逐点检测漏掉的笔迹。
        /// 必须在手势结束、仍处于 EraseByPoint 模式时调用——移除会进入同一份擦除缓冲，
        /// 与本手势已擦除的笔迹合成同一条撤销记录。
        /// </summary>
        private void SweepRemainingStrokesAlongAreaErasePath()
        {
            var sweepPoints = _areaEraseSweepPoints;
            _areaEraseSweepPoints = null;
            if (sweepPoints == null || sweepPoints.Count == 0) return;
            if (_currentCommitType != CommitReason.UserInput) return;
            if (_isRestoringDesktopStrokesAfterPpt) return;

            try
            {
                double radius = _areaEraseSweepRadius > 0 ? _areaEraseSweepRadius / 2.0 : 45;

                // 轨迹包围盒预筛 + 限制参与判定的采样数（≤128），手势结束时的一次性开销控制在毫秒级
                double minX = double.MaxValue, minY = double.MaxValue, maxX = double.MinValue, maxY = double.MinValue;
                foreach (var p in sweepPoints)
                {
                    if (p.X < minX) minX = p.X;
                    if (p.Y < minY) minY = p.Y;
                    if (p.X > maxX) maxX = p.X;
                    if (p.Y > maxY) maxY = p.Y;
                }
                minX -= radius; minY -= radius; maxX += radius; maxY += radius;

                int step = Math.Max(1, (int)Math.Ceiling(sweepPoints.Count / 128.0));
                var sampledPoints = new List<Point>((sweepPoints.Count / step) + 1);
                for (int i = 0; i < sweepPoints.Count; i += step)
                {
                    sampledPoints.Add(sweepPoints[i]);
                }

                var snapshot = new StrokeCollection(inkCanvas.Strokes);
                foreach (Stroke stroke in snapshot)
                {
                    try
                    {
                        var bounds = stroke.GetBounds();
                        if (bounds.Bottom < minY || bounds.Top > maxY || bounds.Right < minX || bounds.Left > maxX) continue;

                        var geometry = stroke.GetGeometry();
                        if (geometry == null || geometry.IsEmpty()) continue;

                        foreach (var p in sampledPoints)
                        {
                            var brush = new EllipseGeometry(p, radius, radius);
                            if (geometry.FillContainsWithDetail(brush) != IntersectionDetail.Empty)
                            {
                                inkCanvas.Strokes.Remove(stroke);
                                break;
                            }
                        }
                    }
                    catch { }
                }
            }
            catch { }
        }
        private StrokeCollection CuboidStrokeCollection;
        private Dictionary<Stroke, Tuple<StylusPointCollection, StylusPointCollection>> StrokeManipulationHistory;
        private Dictionary<Stroke, StylusPointCollection> StrokeInitialHistory = new Dictionary<Stroke, StylusPointCollection>();
        private Dictionary<string, Tuple<object, TransformGroup>> ElementsManipulationHistory;

        // object maybe TransformGroup or ElementData
        private Dictionary<string, object> ElementsInitialHistory = new Dictionary<string, object>();

        public void PruneElementsInitialHistory()
        {
            try
            {
                if (ElementsInitialHistory == null || ElementsInitialHistory.Count == 0) return;

                var liveKeys = new HashSet<string>();

                foreach (UIElement child in inkCanvas.Children)
                {
                    var frameworkElement = child as FrameworkElement;
                    if (frameworkElement != null && !string.IsNullOrEmpty(frameworkElement.Name))
                    {
                        liveKeys.Add(frameworkElement.Name);
                    }
                }

                // 2) 仍被撤销栈引用的元素 key
                timeMachine?.CollectManipulationElementKeys(liveKeys);

                List<string> deadKeys = null;
                foreach (var key in ElementsInitialHistory.Keys)
                {
                    if (!liveKeys.Contains(key))
                    {
                        if (deadKeys == null) deadKeys = new List<string>();
                        deadKeys.Add(key);
                    }
                }

                if (deadKeys != null)
                {
                    foreach (var key in deadKeys)
                    {
                        ElementsInitialHistory.Remove(key);
                    }
                }
            }
            catch
            {
            }
        }

        private Dictionary<Stroke, Tuple<DrawingAttributes, DrawingAttributes>> DrawingAttributesHistory = new Dictionary<Stroke, Tuple<DrawingAttributes, DrawingAttributes>>();
        private Dictionary<Guid, List<Stroke>> DrawingAttributesHistoryFlag = new Dictionary<Guid, List<Stroke>>()
        {
            { DrawingAttributeIds.Color, new List<Stroke>() },
            { DrawingAttributeIds.DrawingFlags, new List<Stroke>() },
            { DrawingAttributeIds.IsHighlighter, new List<Stroke>() },
            { DrawingAttributeIds.StylusHeight, new List<Stroke>() },
            { DrawingAttributeIds.StylusTip, new List<Stroke>() },
            { DrawingAttributeIds.StylusTipTransform, new List<Stroke>() },
            { DrawingAttributeIds.StylusWidth, new List<Stroke>() }
        };
        private TimeMachine timeMachine = new TimeMachine();

        public UIElement GetElementByTimestamp(InkCanvas inkCanvas, string timestamp)
        {
            foreach (UIElement child in inkCanvas.Children)
            {
                if (child is FrameworkElement frameworkElement && frameworkElement.Name == timestamp)
                {
                    return child;
                }
            }
            return null;
        }

        private void ApplyHistoryToCanvas(TimeMachineHistory item)
        {
            if (item == null)
            {
                _currentCommitType = CommitReason.UserInput;
                return;
            }
            _currentCommitType = CommitReason.CodeInput;
            if (item.CommitType == TimeMachineHistoryType.UserInput)
            {
                if (item.CurrentStroke == null)
                {
                    _currentCommitType = CommitReason.UserInput;
                    return;
                }
                if (!item.StrokeHasBeenCleared)
                {
                    foreach (var strokes in item.CurrentStroke)
                    {
                        if (!inkCanvas.Strokes.Contains(strokes))
                            inkCanvas.Strokes.Add(strokes);
                    }
                }
                else
                {
                    foreach (var strokes in item.CurrentStroke)
                    {
                        if (inkCanvas.Strokes.Contains(strokes))
                            inkCanvas.Strokes.Remove(strokes);
                    }
                }
            }
            else if (item.CommitType == TimeMachineHistoryType.ShapeRecognition)
            {
                if (item.CurrentStroke == null)
                {
                    _currentCommitType = CommitReason.UserInput;
                    return;
                }
                if (item.StrokeHasBeenCleared)
                {

                    foreach (var strokes in item.CurrentStroke)
                    {
                        if (inkCanvas.Strokes.Contains(strokes))
                            inkCanvas.Strokes.Remove(strokes);
                    }
                    if (item.ReplacedStroke != null)
                    {
                        foreach (var strokes in item.ReplacedStroke)
                        {
                            if (!inkCanvas.Strokes.Contains(strokes))
                                inkCanvas.Strokes.Add(strokes);
                        }
                    }
                }
                else
                {
                    foreach (var strokes in item.CurrentStroke)
                    {
                        if (!inkCanvas.Strokes.Contains(strokes))
                            inkCanvas.Strokes.Add(strokes);
                    }
                    if (item.ReplacedStroke != null)
                    {
                        foreach (var strokes in item.ReplacedStroke)
                        {
                            if (inkCanvas.Strokes.Contains(strokes))
                                inkCanvas.Strokes.Remove(strokes);
                        }
                    }
                }
            }
            else if (item.CommitType == TimeMachineHistoryType.Manipulation)
            {
                if (!item.StrokeHasBeenCleared)
                {
                    if(item.StylusPointDictionary != null)
                    {
                        foreach (var currentStroke in item.StylusPointDictionary)
                        {
                            if (inkCanvas.Strokes.Contains(currentStroke.Key))
                            {
                                currentStroke.Key.StylusPoints = currentStroke.Value.Item2;
                            }
                        }
                    }
                    if(item.ElementsManipulationHistory != null)
                    {
                        foreach (var currentElement in item.ElementsManipulationHistory)
                        {
                            UIElement element = GetElementByTimestamp(inkCanvas, currentElement.Key);
                            if (element != null && inkCanvas.Children.Contains(element))
                            {
                                element.RenderTransform = currentElement.Value.Item2;
                            }
                            else
                            {
                                if (currentElement.Value.Item1 is InkCanvasElementsHelper.ElementData)
                                {
                                    InkCanvasElementsHelper.ElementData elementData = currentElement.Value.Item1 as InkCanvasElementsHelper.ElementData;
                                    InkCanvas.SetLeft(elementData.FrameworkElement, elementData.SetLeftData);
                                    InkCanvas.SetTop(elementData.FrameworkElement, elementData.SetTopData);
                                    inkCanvas.Children.Add(elementData.FrameworkElement);
                                    elementData.FrameworkElement.RenderTransform = currentElement.Value.Item2;
                                }
                            }
                        }
                    }
                }
                else
                {
                    if(item.StylusPointDictionary != null)
                    {
                        foreach (var currentStroke in item.StylusPointDictionary)
                        {
                            if (inkCanvas.Strokes.Contains(currentStroke.Key))
                            {
                                currentStroke.Key.StylusPoints = currentStroke.Value.Item1;
                            }
                        }
                    }
                    if(item.ElementsManipulationHistory != null)
                    {
                        foreach (var currentElement in item.ElementsManipulationHistory)
                        {
                            UIElement element = GetElementByTimestamp(inkCanvas, currentElement.Key);
                            if (element != null && inkCanvas.Children.Contains(element))
                            {
                                if (currentElement.Value.Item1 is TransformGroup transformGroup)
                                {
                                    element.RenderTransform = transformGroup;
                                }
                                else if (currentElement.Value.Item1 is InkCanvasElementsHelper.ElementData)
                                {
                                    inkCanvas.Children.Remove(element);
                                }
                            }
                        }
                    }
                }
            }
            else if (item.CommitType == TimeMachineHistoryType.DrawingAttributes)
            {
                if (item.DrawingAttributes == null)
                {
                    _currentCommitType = CommitReason.UserInput;
                    return;
                }
                if (!item.StrokeHasBeenCleared)
                {
                    foreach (var currentStroke in item.DrawingAttributes)
                    {
                        if (inkCanvas.Strokes.Contains(currentStroke.Key))
                        {
                            currentStroke.Key.DrawingAttributes = currentStroke.Value.Item2;
                        }
                    }
                }
                else
                {
                    foreach (var currentStroke in item.DrawingAttributes)
                    {
                        if (inkCanvas.Strokes.Contains(currentStroke.Key))
                        {
                            currentStroke.Key.DrawingAttributes = currentStroke.Value.Item1;
                        }
                    }
                }
            }
            else if (item.CommitType == TimeMachineHistoryType.Clear)
            {
                if (!item.StrokeHasBeenCleared)
                {
                    if (item.CurrentStroke != null)
                    {
                        foreach (var currentStroke in item.CurrentStroke)
                        {
                            if (!inkCanvas.Strokes.Contains(currentStroke)) inkCanvas.Strokes.Add(currentStroke);
                        }

                    }
                    if (item.ReplacedStroke != null)
                    {
                        foreach (var replacedStroke in item.ReplacedStroke)
                        {
                            if (inkCanvas.Strokes.Contains(replacedStroke)) inkCanvas.Strokes.Remove(replacedStroke);
                        }
                    }

                }
                else
                {
                    if (item.ReplacedStroke != null)
                    {
                        foreach (var replacedStroke in item.ReplacedStroke)
                        {
                            if (!inkCanvas.Strokes.Contains(replacedStroke)) inkCanvas.Strokes.Add(replacedStroke);
                        }
                    }
                    if (item.CurrentStroke != null)
                    {
                        foreach (var currentStroke in item.CurrentStroke)
                        {
                            if (inkCanvas.Strokes.Contains(currentStroke)) inkCanvas.Strokes.Remove(currentStroke);
                        }
                    }
                }
            }
            else if (item.CommitType == TimeMachineHistoryType.ElementInsert)
            {
                if (item.Element == null)
                {
                    _currentCommitType = CommitReason.UserInput;
                    return;
                }
                if (!item.StrokeHasBeenCleared)
                {
                    // 去重：内容基线与原始撤销栈可能引用同一个元素（基线已回放添加过），
                    // 无守卫重复 Add 会抛“已存在逻辑父级”异常，在存档回放里直接中断整条记录
                    if (!inkCanvas.Children.Contains(item.Element)) inkCanvas.Children.Add(item.Element);
                }
                else
                {
                    inkCanvas.Children.Remove(item.Element);
                }
            }
            _currentCommitType = CommitReason.UserInput;
        }

        private void TimeMachine_OnUndoStateChanged(bool status)
        {
            Icon_Undo.IsEnabled = status;
        }

        private void TimeMachine_OnRedoStateChanged(bool status)
        {
            Icon_Redo.IsEnabled = status;
        }

        private void StrokesOnStrokesChanged(object sender, StrokeCollectionChangedEventArgs e)
        {
            if (!isHidingSubPanelsWhenInking)
            {
                isHidingSubPanelsWhenInking = true;
                HideSubPanels(); // 书写时自动隐藏二级菜单
            }

            foreach (var stroke in e?.Removed)
            {
                stroke.StylusPointsChanged -= Stroke_StylusPointsChanged;
                stroke.StylusPointsReplaced -= Stroke_StylusPointsReplaced;
                stroke.DrawingAttributesChanged -= Stroke_DrawingAttributesChanged;
                StrokeInitialHistory.Remove(stroke);
            }
            foreach (var stroke in e?.Added)
            {
                stroke.StylusPointsChanged += Stroke_StylusPointsChanged;
                stroke.StylusPointsReplaced += Stroke_StylusPointsReplaced;
                stroke.DrawingAttributesChanged += Stroke_DrawingAttributesChanged;
                StrokeInitialHistory[stroke] = stroke.StylusPoints.Clone();
            }
            if (_currentCommitType == CommitReason.CodeInput || _currentCommitType == CommitReason.ShapeDrawing) return;

            if (_isInBoardModeSwitch && e.Added.Count != 0)
            {
                var residual = new StrokeCollection();
                foreach (var stroke in e.Added)
                {
                    if (IsPreSwitchResidualStroke(stroke)) residual.Add(stroke);
                }
                if (residual.Count > 0)
                {
                    _currentCommitType = CommitReason.CodeInput;
                    try { inkCanvas.Strokes.Remove(residual); } catch { }
                    _currentCommitType = CommitReason.UserInput;
                    // 整批都是残留才终止本次处理；若同一批里混有新笔画（如兜底补提交与残留同时到达），
                    // 移除残留后继续往下走，让新笔画正常进入历史提交，避免被一起丢弃。
                    if (residual.Count == e.Added.Count) return;
                }
                // 没有残留笔迹：说明是切换后新画布上的正常书写，放行继续走后续历史提交流程
            }

            // 文档页笔迹任何变更（新增/擦除）后立即调度自动保存（防抖），
            // 确保文档照片上的书写内容实时落盘到对应文档照片存储文件夹内，
            // 即使用户不切换页面也不会丢失。注意必须放在下面各 return 分支之前。
            if (pageDocumentMapping.ContainsKey(CurrentWhiteboardIndex))
            {
                ScheduleDocumentPageSave(CurrentWhiteboardIndex);
            }

            if ((e.Added.Count != 0 || e.Removed.Count != 0) && IsEraseByPoint)
            {
                if (AddedStroke == null) AddedStroke = new StrokeCollection();
                if (ReplacedStroke == null) ReplacedStroke = new StrokeCollection();
                AddedStroke.Add(e.Added);
                ReplacedStroke.Add(e.Removed);
                return;
            }
            if (e.Added.Count != 0)
            {
                if (_currentCommitType == CommitReason.ShapeRecognition)
                {
                    timeMachine.CommitStrokeShapeHistory(ReplacedStroke, e.Added);
                    ReplacedStroke = null;
                    return;
                }
                else
                {
                    timeMachine.CommitStrokeUserInputHistory(e.Added);
                    return;
                }
            }
            if (e.Removed.Count != 0)
            {
                if (_currentCommitType == CommitReason.ShapeRecognition)
                {
                    ReplacedStroke = e.Removed;
                    return;
                }
                else if (!IsEraseByPoint || _currentCommitType == CommitReason.ClearingCanvas)
                {
                    timeMachine.CommitStrokeEraseHistory(e.Removed);
                    return;
                }
            }
        }

        private void Stroke_DrawingAttributesChanged(object sender, PropertyDataChangedEventArgs e)
        {
            if (!(sender is Stroke key)) return;
            if (e == null) return;
            if (!DrawingAttributesHistoryFlag.TryGetValue(e.PropertyGuid, out var flags))
            {
                flags = new List<Stroke>();
                DrawingAttributesHistoryFlag[e.PropertyGuid] = flags;
            }
            var currentValue = key.DrawingAttributes.Clone();
            DrawingAttributesHistory.TryGetValue(key, out var previousTuple);
            var previousValue = previousTuple?.Item1 ?? currentValue.Clone();
            var needUpdateValue = !flags.Contains(key);
            if (needUpdateValue)
            {
                flags.Add(key);
                if (e.PreviousValue != null) Debug.Write(e.PreviousValue.ToString());
            }
            if (e.PropertyGuid == DrawingAttributeIds.Color && needUpdateValue)
            {
                previousValue.Color = (Color)e.PreviousValue;
            }
            if (e.PropertyGuid == DrawingAttributeIds.IsHighlighter && needUpdateValue)
            {
                previousValue.IsHighlighter = (bool)e.PreviousValue;
            }
            if (e.PropertyGuid == DrawingAttributeIds.StylusHeight && needUpdateValue)
            {
                previousValue.Height = (double)e.PreviousValue;
            }
            if (e.PropertyGuid == DrawingAttributeIds.StylusWidth && needUpdateValue)
            {
                previousValue.Width = (double)e.PreviousValue;
            }
            if (e.PropertyGuid == DrawingAttributeIds.StylusTip && needUpdateValue)
            {
                previousValue.StylusTip = (StylusTip)e.PreviousValue;
            }
            if (e.PropertyGuid == DrawingAttributeIds.StylusTipTransform && needUpdateValue)
            {
                previousValue.StylusTipTransform = (Matrix)e.PreviousValue;
            }
            if (e.PropertyGuid == DrawingAttributeIds.DrawingFlags && needUpdateValue)
            {
                previousValue.IgnorePressure = (bool)e.PreviousValue;
            }
            DrawingAttributesHistory[key] = new Tuple<DrawingAttributes, DrawingAttributes>(previousValue, currentValue);
        }

        private void Stroke_StylusPointsReplaced(object sender, StylusPointsReplacedEventArgs e)
        {
            if (!(sender is Stroke stroke)) return;
            if (e?.NewStylusPoints != null)
            {
                StrokeInitialHistory[stroke] = e.NewStylusPoints.Clone();
                return;
            }
            StrokeInitialHistory[stroke] = stroke.StylusPoints?.Clone() ?? new StylusPointCollection();
        }

        private void Stroke_StylusPointsChanged(object sender, EventArgs e)
        {
            if (!(sender is Stroke stroke)) return;
            if (!StrokeInitialHistory.TryGetValue(stroke, out var initial))
            {
                initial = stroke.StylusPoints?.Clone() ?? new StylusPointCollection();
                StrokeInitialHistory[stroke] = initial;
            }
            var selectedStrokes = inkCanvas.GetSelectedStrokes();
            int count = selectedStrokes.Count > 0 ? selectedStrokes.Count : inkCanvas.Strokes.Count;

            if (StrokeManipulationHistory == null)
            {
                StrokeManipulationHistory = new Dictionary<Stroke, Tuple<StylusPointCollection, StylusPointCollection>>();
            }
            StrokeManipulationHistory[stroke] =
                new Tuple<StylusPointCollection, StylusPointCollection>(initial, stroke.StylusPoints.Clone());
            if (StrokeManipulationHistory.Count == count && dec.Count == 0 && !isGridInkCanvasSelectionCoverMouseDown && !isResizing && !isCornerTransform)
            {
                ToCommitStrokeManipulationHistoryAfterMouseUp();
            }
        }

        private void ToCommitStrokeManipulationHistoryAfterMouseUp()
        {
            if (StrokeManipulationHistory == null && ElementsManipulationHistory == null)
            {
                return;
            }
            if(StrokeManipulationHistory?.Count > 0 || ElementsManipulationHistory?.Count > 0)
            {
                timeMachine.CommitStrokeManipulationHistory(StrokeManipulationHistory, ElementsManipulationHistory);
                if (StrokeManipulationHistory != null)
                {
                    foreach (var item in StrokeManipulationHistory)
                    {
                        if (item.Key != null && item.Value?.Item2 != null)
                        {
                            StrokeInitialHistory[item.Key] = item.Value.Item2;
                        }
                    }
                    StrokeManipulationHistory = null;
                }
                if (ElementsManipulationHistory != null)
                {
                    foreach (var item in ElementsManipulationHistory)
                    {
                        ElementsInitialHistory[item.Key] = item.Value.Item2;
                    }
                    ElementsManipulationHistory = null;
                }
            }
        }
    }
}
