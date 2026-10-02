using Ink_Canvas.Helpers;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Ink;
using System.Windows.Input;
using System.Windows.Media;
using System.Xml.Linq;
using System.Windows.Threading;
using Point = System.Windows.Point;

namespace Ink_Canvas
{
    public partial class MainWindow : Window
    {
        #region Multi-Touch

        bool isInMultiTouchMode = false;
        private void BorderMultiTouchMode_MouseUp(object sender, MouseButtonEventArgs e)
        {
            if (isInMultiTouchMode)
            {
                inkCanvas.StylusDown -= MainWindow_StylusDown;
                inkCanvas.StylusMove -= MainWindow_StylusMove;
                inkCanvas.StylusUp -= MainWindow_StylusUp;
                inkCanvas.TouchDown -= MainWindow_TouchDown;
                inkCanvas.TouchDown += Main_Grid_TouchDown;
                inkCanvas.EditingMode = InkCanvasEditingMode.Ink;
                isInMultiTouchMode = false;
            }
            else
            {
                inkCanvas.StylusDown += MainWindow_StylusDown;
                inkCanvas.StylusMove += MainWindow_StylusMove;
                inkCanvas.StylusUp += MainWindow_StylusUp;
                inkCanvas.TouchDown += MainWindow_TouchDown;
                inkCanvas.TouchDown -= Main_Grid_TouchDown;
                inkCanvas.EditingMode = InkCanvasEditingMode.Ink;
                isInMultiTouchMode = true;
            }
        }

        /// <summary>
        /// 多点触摸模式下的触摸按下事件处理
        /// </summary>
        private void MainWindow_TouchDown(object sender, TouchEventArgs e)
        {
            try
            {
                UpdateInputActivityTimestamp();
                RecordInputDown();
                if (inkCanvas.EditingMode == InkCanvasEditingMode.EraseByPoint
                    || inkCanvas.EditingMode == InkCanvasEditingMode.EraseByStroke
                    || inkCanvas.EditingMode == InkCanvasEditingMode.Select) return;

                if (!isHidingSubPanelsWhenInking)
                {
                    isHidingSubPanelsWhenInking = true;
                    HideSubPanels();
                }

                double boundWidth = e.GetTouchPoint(null).Bounds.Width;
                if ((Settings.Advanced.TouchMultiplier != 0 || !Settings.Advanced.IsSpecialScreen)
                    && (boundWidth > BoundsWidth))
                {
                    if (drawingShapeMode == 0 && forceEraser) return;
                    // 手掌误触保护：正在用笔书写（或刚写完）时的宽触点视为手掌/手指误触，不切橡皮擦
                    if (ShouldIgnoreTouchAsPalm()) return;
                    // 记录擦除前的工具（手指擦除是一次性手势，抬起后恢复），避免画布此后一直是橡皮擦
                    RecordFingerEraserMode();
                    double EraserThresholdValue = Settings.Startup.IsEnableNibMode ? Settings.Advanced.NibModeBoundsWidthThresholdValue : Settings.Advanced.FingerModeBoundsWidthThresholdValue;
                    if (boundWidth > BoundsWidth * EraserThresholdValue)
                    {
                        boundWidth *= (Settings.Startup.IsEnableNibMode ? Settings.Advanced.NibModeBoundsWidthEraserSize : Settings.Advanced.FingerModeBoundsWidthEraserSize);
                        if (Settings.Advanced.IsSpecialScreen) boundWidth *= Settings.Advanced.TouchMultiplier;
                        inkCanvas.EraserShape = new EllipseStylusShape(boundWidth, boundWidth);
                        TouchDownPointsList[e.TouchDevice.Id] = InkCanvasEditingMode.EraseByPoint;
                        inkCanvas.EditingMode = InkCanvasEditingMode.EraseByPoint;
                    }
                    else
                    {
                        inkCanvas.EraserShape = new EllipseStylusShape(5, 5);
                        inkCanvas.EditingMode = InkCanvasEditingMode.EraseByStroke;
                    }
                }
                else
                {
                    inkCanvas.EraserShape = forcePointEraser ? new EllipseStylusShape(50, 50) : new EllipseStylusShape(5, 5);
                    TouchDownPointsList[e.TouchDevice.Id] = InkCanvasEditingMode.None;
                    inkCanvas.EditingMode = InkCanvasEditingMode.None;
                }
            }
            catch (Exception ex)
            {
                LogHelper.WriteLogToFile("MainWindow_TouchDown error | " + ex, LogHelper.LogType.Error);
            }
        }

        private void MainWindow_StylusDown(object sender, StylusDownEventArgs e)
        {
            try
            {
                UpdateInputActivityTimestamp();
                RecordInputDown();
                // 激光笔开启期间不进入「手写预览」路径：该路径不经过 InkCanvas 的编辑模式
                // （激光态下 EditingMode 为 None 也拦不住它），抬手时会把预览笔迹直接写入
                // inkCanvas.Strokes，造成"激光轨迹已消失、画布上却残留笔迹"。
                // 这里不登记预览状态，后面的 StylusMove/StylusUp 都会因查不到登记而直接跳过。
                if (isLaserPointerEnabled) return;
                if (inkCanvas.EditingMode == InkCanvasEditingMode.EraseByPoint
                    || inkCanvas.EditingMode == InkCanvasEditingMode.EraseByStroke
                    || inkCanvas.EditingMode == InkCanvasEditingMode.Select) return;

                int stylusDeviceId = e.StylusDevice.Id;
                TouchDownPointsList[stylusDeviceId] = InkCanvasEditingMode.None;
                StylusPreviewModeByDeviceId[stylusDeviceId] = ShouldHandleStylusInputAsPreview(e);
            }
            catch (Exception ex)
            {
                LogHelper.WriteLogToFile("MainWindow_StylusDown error | " + ex, LogHelper.LogType.Error);
            }
        }

        /// <summary>
        /// 触摸抬起事件处理 - 将预览笔迹添加到画布
        /// </summary>
        private void MainWindow_StylusUp(object sender, StylusEventArgs e)
        {
            int stylusDeviceId = e.StylusDevice.Id;
            try
            {
                if (!ShouldHandleStylusPreview(stylusDeviceId))
                {
                    return;
                }

                // 激光笔已开启（例如接触过程中才打开激光笔）：丢弃本笔预览，不写入画布，
                // 否则激光轨迹消失后画布上会残留这条笔迹
                if (isLaserPointerEnabled)
                {
                    return;
                }

                try
                {
                    // 触摸屏模式处理
                    if (!StrokeVisualList.TryGetValue(stylusDeviceId, out var visual)) return;

                    var visualCanvas = GetVisualCanvas(stylusDeviceId);
                    var strokeCollection = visual.StrokeCollection;

                    // 先把预览笔画加入 inkCanvas 真笔迹，再移除预览层：
                    // 两个操作在同一帧内连续完成（WPF 在事件栈返回后才统一渲染），
                    // 避免原先"先移除预览再 await 5ms 再加真笔迹"造成的停笔瞬间墨迹消失再出现的闪烁。
                    foreach (var s in strokeCollection)
                    {
                        inkCanvas.Strokes.Add(s);
                    }

                    if (visualCanvas != null)
                    {
                        inkCanvas.Children.Remove(visualCanvas);
                    }

                    foreach (var s in strokeCollection)
                    {
                        try
                        {
                            inkCanvas_StrokeCollected(inkCanvas, new InkCanvasStrokeCollectedEventArgs(s));
                        }
                        catch { }
                    }
                }
                catch (Exception ex)
                {
                    LogHelper.WriteLogToFile(ex.ToString(), LogHelper.LogType.Error);
                }
            }
            catch (Exception ex)
            {
                LogHelper.WriteLogToFile(ex.ToString(), LogHelper.LogType.Error);
            }
            finally
            {
                CleanupTrackedStylus(stylusDeviceId);
            }
        }

        private void MainWindow_StylusMove(object sender, StylusEventArgs e)
        {
            try
            {
                int stylusDeviceId = e.StylusDevice.Id;
                if (!ShouldHandleStylusPreview(stylusDeviceId)) return;
                if (GetTouchDownPointsList(stylusDeviceId) != InkCanvasEditingMode.None) return;
                var strokeVisual = GetStrokeVisual(stylusDeviceId);
                var stylusPointCollection = e.GetStylusPoints(inkCanvas);
                if (stylusPointCollection == null) return;
                bool hasNewPoint = false;
                foreach (var stylusPoint in stylusPointCollection)
                {
                    // Add 内部会对过近的冗余点做过滤（返回 false），
                    // 全部被过滤时不为本帧安排重绘，避免空帧全量重绘的 CPU/GC 开销。
                    if (strokeVisual.Add(new StylusPoint(stylusPoint.X, stylusPoint.Y, stylusPoint.PressureFactor)))
                        hasNewPoint = true;
                }
                if (hasNewPoint) strokeVisual.RedrawThrottled();
            }
            catch (Exception ex)
            {
                LogHelper.WriteLogToFile("MainWindow_StylusMove error | " + ex, LogHelper.LogType.Error);
                try { CleanupTrackedStylus(e?.StylusDevice?.Id ?? -1); } catch { }
            }
        }

        private StrokeVisual GetStrokeVisual(int id)
        {
            if (StrokeVisualList.TryGetValue(id, out var visual))
            {
                return visual;
            }

            var strokeVisual = new StrokeVisual(inkCanvas.DefaultDrawingAttributes.Clone());
            StrokeVisualList[id] = strokeVisual;
            StrokeVisualList[id] = strokeVisual;
            var visualCanvas = new VisualCanvas(strokeVisual);
            VisualCanvasList[id] = visualCanvas;
            inkCanvas.Children.Add(visualCanvas);

            return strokeVisual;
        }

        private VisualCanvas GetVisualCanvas(int id)
        {
            if (VisualCanvasList.TryGetValue(id, out var visualCanvas))
            {
                return visualCanvas;
            }
            return null;
        }

        private InkCanvasEditingMode GetTouchDownPointsList(int id)
        {
            if (TouchDownPointsList.TryGetValue(id, out var inkCanvasEditingMode))
            {
                return inkCanvasEditingMode;
            }
            return inkCanvas.EditingMode;
        }

        private bool ShouldHandleStylusInputAsPreview(StylusEventArgs e)
        {
            try
            {
                return e?.StylusDevice?.TabletDevice?.Type != TabletDeviceType.Stylus;
            }
            catch
            {
                return false;
            }
        }

        private bool ShouldHandleStylusPreview(int id)
        {
            return StylusPreviewModeByDeviceId.TryGetValue(id, out bool shouldHandle) && shouldHandle;
        }

        private void CleanupTrackedStylus(int stylusDeviceId)
        {
            try
            {
                if (VisualCanvasList.TryGetValue(stylusDeviceId, out var visualCanvas) && visualCanvas != null)
                {
                    inkCanvas.Children.Remove(visualCanvas);
                }
            }
            catch { }

            StrokeVisualList.Remove(stylusDeviceId);
            VisualCanvasList.Remove(stylusDeviceId);
            TouchDownPointsList.Remove(stylusDeviceId);
            StylusPreviewModeByDeviceId.Remove(stylusDeviceId);

            if (StrokeVisualList.Count == 0
                && VisualCanvasList.Count == 0
                && TouchDownPointsList.Count == 0
                && StylusPreviewModeByDeviceId.Count == 0)
            {
                StrokeVisualList.Clear();
                VisualCanvasList.Clear();
                TouchDownPointsList.Clear();
                StylusPreviewModeByDeviceId.Clear();
            }
        }

        private Dictionary<int, InkCanvasEditingMode> TouchDownPointsList { get; } = new Dictionary<int, InkCanvasEditingMode>();
        private Dictionary<int, StrokeVisual> StrokeVisualList { get; } = new Dictionary<int, StrokeVisual>();
        private Dictionary<int, VisualCanvas> VisualCanvasList { get; } = new Dictionary<int, VisualCanvas>();
        private Dictionary<int, bool> StylusPreviewModeByDeviceId { get; } = new Dictionary<int, bool>();

        #endregion

        int lastTouchDownTime = 0, lastTouchUpTime = 0;

        Point iniP = new Point(0, 0);
        bool isLastTouchEraser = false;
        private bool forcePointEraser = true;

        /// <summary>
        /// 宽触点手指擦除是一次性手势：记录切换成橡皮擦之前的工具模式，手指抬起后原样恢复。
        /// 否则画布会一直停留在橡皮擦模式，随后用笔书写也会被执行成擦除（“一直处于橡皮擦状态”）。
        /// </summary>
        private InkCanvasEditingMode fingerEraserPreviousMode = InkCanvasEditingMode.Ink;
        private bool isFingerEraserModeActive = false;

        #region 手掌误触保护（用笔书写时的手指/手掌触点不切橡皮擦）

        /// <summary>最近一次真实手写笔输入的时刻（Environment.TickCount）</summary>
        private int _lastPenInputTickCount;

        /// <summary>
        /// 挂载手写笔活动监听。必须在窗口 Preview 阶段，才能拿到最早的原始输入。
        /// 只用于「手掌误触保护」，不改动任何输入行为。
        /// </summary>
        private void InitializePenActivityTracking()
        {
            PreviewStylusDown += (s, e) => MarkPenInputActivityIfPen(e.StylusDevice);
            PreviewStylusMove += (s, e) => MarkPenInputActivityIfPen(e.StylusDevice);
            PreviewStylusUp += (s, e) => MarkPenInputActivityIfPen(e.StylusDevice);
        }

        private void MarkPenInputActivityIfPen(StylusDevice device)
        {
            try
            {
                var type = device?.TabletDevice?.Type;
                // 触摸会以 TabletDeviceType.Touch 上报，那些事件不代表「用笔书写」，
                // 不能记入，否则会把手指擦除功能一起挡掉
                if (type == TabletDeviceType.Touch) return;
                _lastPenInputTickCount = Environment.TickCount;
            }
            catch { }
        }

        /// <summary>最近 windowMs 毫秒内是否有手写笔输入</summary>
        private bool HasRecentPenInput(int windowMs = 500)
        {
            int elapsed = unchecked(Environment.TickCount - _lastPenInputTickCount);
            return elapsed >= 0 && elapsed <= windowMs;
        }

        /// <summary>当前是否有手写笔画正在进行（墨迹画布持有手写笔捕获）</summary>
        private bool IsPenStrokeInProgress()
        {
            try
            {
                var stylus = Stylus.Captured as Visual;
                if (stylus != null && (ReferenceEquals(stylus, inkCanvas) || inkCanvas.IsAncestorOf(stylus))) return true;
            }
            catch { }
            return false;
        }

        /// <summary>
        /// 手掌误触保护：正在用笔书写（或刚写完）时，画布上的宽触点多半是手掌/手指误触。
        /// 此时若按宽触点规则切成橡皮擦，正在写的笔迹会被立刻当成擦除对象（表现为
        /// “写着写着突然变成橡皮擦、写不上去，过一会儿才恢复”），因此直接忽略该触点。
        /// </summary>
        private bool ShouldIgnoreTouchAsPalm()
        {
            return IsPenStrokeInProgress() || HasRecentPenInput();
        }

        #endregion

        /// <summary>
        /// 记录即将开始的宽触点手指擦除手势（仅在当前不是橡皮擦工具时记录一次）
        /// </summary>
        private void RecordFingerEraserMode()
        {
            if (inkCanvas.EditingMode == InkCanvasEditingMode.EraseByPoint ||
                inkCanvas.EditingMode == InkCanvasEditingMode.EraseByStroke)
            {
                return;
            }
            fingerEraserPreviousMode = inkCanvas.EditingMode;
            isFingerEraserModeActive = true;
        }

        /// <summary>
        /// 手指抬起后结束一次性手指擦除手势：把工具恢复成擦除前的模式
        /// </summary>
        private void EndFingerEraserModeIfNeeded()
        {
            if (!isFingerEraserModeActive) return;
            isFingerEraserModeActive = false;

            // 用户主动选择了橡皮擦工具（forceEraser）时不恢复，保持连续擦除
            if (forceEraser) return;

            if (inkCanvas.EditingMode == InkCanvasEditingMode.EraseByPoint ||
                inkCanvas.EditingMode == InkCanvasEditingMode.EraseByStroke)
            {
                inkCanvas.EditingMode = fingerEraserPreviousMode;
            }
        }

        /// <summary>
        /// 主画布触摸按下事件处理
        /// </summary>
        private void Main_Grid_TouchDown(object sender, TouchEventArgs e)
        {
            try
            {
                UpdateInputActivityTimestamp();
                RecordInputDown();
                if (!isHidingSubPanelsWhenInking)
                {
                    isHidingSubPanelsWhenInking = true;
                    HideSubPanels();
                }

                if (NeedUpdateIniP())
                {
                    iniP = e.GetTouchPoint(inkCanvas).Position;
                }
                if (Settings.Canvas.StopTimingStraighten)
                {
                    _stopTimingPoint = iniP;
                    _stopTiming = DateTime.Now;
                    _stopTimingDisable = false;
                    _stopTimingPoints.Clear();
                    _stopTimingPoints.Add(_stopTimingPoint);
                }
                if (drawingShapeMode == 9 && isFirstTouchCuboid == false)
                {
                    MouseTouchMove(iniP);
                }
                inkCanvas.Opacity = 1;
                double boundsWidth = GetTouchBoundWidth(e);
                // 启用特殊屏幕且触摸倍数为 0 时禁用橡皮
                if ((Settings.Advanced.TouchMultiplier != 0 || !Settings.Advanced.IsSpecialScreen)
                    && (boundsWidth > BoundsWidth))
                {
                    isLastTouchEraser = true;
                    if (drawingShapeMode == 0 && forceEraser) return;
                    // 手掌误触保护：正在用笔书写（或刚写完）时的宽触点视为手掌/手指误触，
                    // 不切橡皮擦，避免正在写的笔迹被立即擦除（“写着写着突然变成橡皮擦”）
                    if (ShouldIgnoreTouchAsPalm()) { isLastTouchEraser = false; return; }
                    // 记录擦除前的工具（手指擦除是一次性手势，抬起后恢复），避免画布此后一直是橡皮擦
                    RecordFingerEraserMode();
                    double EraserThresholdValue = Settings.Startup.IsEnableNibMode ? Settings.Advanced.NibModeBoundsWidthThresholdValue : Settings.Advanced.FingerModeBoundsWidthThresholdValue;
                    if (boundsWidth > BoundsWidth * EraserThresholdValue)
                    {
                        boundsWidth *= (Settings.Startup.IsEnableNibMode ? Settings.Advanced.NibModeBoundsWidthEraserSize : Settings.Advanced.FingerModeBoundsWidthEraserSize);
                        if (Settings.Advanced.IsSpecialScreen) boundsWidth *= Settings.Advanced.TouchMultiplier;
                        inkCanvas.EraserShape = new EllipseStylusShape(boundsWidth, boundsWidth);
                        inkCanvas.EditingMode = InkCanvasEditingMode.EraseByPoint;
                    }
                    else
                    {
                        // 在白板模式下(currentMode == 1)不启用PPT翻页手势控制
                        // 注意：dec.Count 在 TouchDown 事件中可能还未更新，因此不依赖它做判断
                        if (BtnPPTSlideShowEnd.Visibility == Visibility.Visible && currentMode != 1 && inkCanvas.Strokes.Count == 0 && Settings.PowerPointSettings.IsEnableFingerGestureSlideShowControl)
                        {
                            isLastTouchEraser = false;
                            inkCanvas.EditingMode = InkCanvasEditingMode.GestureOnly;
                            inkCanvas.Opacity = 0.1;
                        }
                        else
                        {
                            inkCanvas.EraserShape = new EllipseStylusShape(5, 5);
                            inkCanvas.EditingMode = InkCanvasEditingMode.EraseByStroke;
                        }
                    }
                }
                else
                {
                    isLastTouchEraser = false;
                    inkCanvas.EraserShape = forcePointEraser ? new EllipseStylusShape(50, 50) : new EllipseStylusShape(5, 5);
                    if (forceEraser) return;
                    // 保持选择状态：当前处于选择模式时不强行切回笔
                    if (inkCanvas.EditingMode != InkCanvasEditingMode.Select)
                    {
                        inkCanvas.EditingMode = InkCanvasEditingMode.Ink;
                    }
                }
            }
            catch (Exception ex)
            {
                LogHelper.WriteLogToFile("Main_Grid_TouchDown error | " + ex, LogHelper.LogType.Error);
            }
        }

        /// <summary>
        /// 获取触摸边界宽度
        /// </summary>
        public double GetTouchBoundWidth(TouchEventArgs e)
        {
            var args = e.GetTouchPoint(null).Bounds;
            if (!Settings.Advanced.IsQuadIR) return args.Width;
            // 四边红外屏幕使用宽高几何平均
            else return Math.Sqrt(args.Width * args.Height);
        }

        private List<int> dec = new List<int>();
        Point centerPoint;
        InkCanvasEditingMode lastInkCanvasEditingMode = InkCanvasEditingMode.Ink;
        bool isSingleFingerDragMode = false;
        enum TwoFingerGestureType { None, Translate, Scale, Rotate }
        TwoFingerGestureType twoFingerGestureType = TwoFingerGestureType.None;

        // 手势死区配置
        double translateDeadzone = 5.0;
        double pinchDeadzone = 0.03;
        double rotateDeadzone = 2.0;
        Vector translateAccum = new Vector(0, 0);
        double translateApplyThreshold = 2.0;

        // 手势平滑处理
        private const int gestureSmoothingWindow = 8;
        private Queue<Vector> scaleHistory = new Queue<Vector>();
        private Queue<double> rotateHistory = new Queue<double>();
        private Queue<Vector> translateHistory = new Queue<Vector>();

        /// <summary>
        /// 重置触摸状态
        /// </summary>
        private void ResetTouchState()
        {
            dec.Clear();
            foreach (var visualCanvas in VisualCanvasList.Values.ToList())
            {
                if (visualCanvas != null)
                {
                    inkCanvas.Children.Remove(visualCanvas);
                }
            }
            TouchDownPointsList.Clear();
            StrokeVisualList.Clear();
            VisualCanvasList.Clear();
            StylusPreviewModeByDeviceId.Clear();
            twoFingerGestureType = TwoFingerGestureType.None;
            translateAccum = new Vector(0, 0);
            inkCanvas.Opacity = 1;
            // 触摸状态整体重置后，挂起的一次性手指擦除恢复不再有意义
            isFingerEraserModeActive = false;
            // 激光笔开启期间不能改回墨迹：否则画布会在激光态下重新收集笔迹，
            // 出现"激光轨迹消失后画布上残留笔迹"。激光关闭时 SetLaserPointerEnabled 会恢复原模式。
            if (!forceEraser && !isLaserPointerEnabled)
            {
                inkCanvas.EditingMode = InkCanvasEditingMode.Ink;
            }

            scaleHistory.Clear();
            rotateHistory.Clear();
            translateHistory.Clear();
        }

        private void inkCanvas_PreviewTouchDown(object sender, TouchEventArgs e)
        {
            try
            {
                dec.Add(e.TouchDevice.Id);
                if (dec.Count == 1)
                {
                    TouchPoint touchPoint = e.GetTouchPoint(inkCanvas);
                    centerPoint = touchPoint.Position;

                    lastTouchDownStrokeCollection = inkCanvas.Strokes.Clone();
                    
                    if (Settings.Canvas.StopTimingStraighten)
                    {
                        _stopTimingPoint = touchPoint.Position;
                        _stopTiming = DateTime.Now;
                        _stopTimingDisable = false;
                        _stopTimingPoints.Clear();
                        _stopTimingPoints.Add(_stopTimingPoint);
                    }
                }
                if (dec.Count > 1 || isSingleFingerDragMode || !Settings.Gesture.IsEnableTwoFingerGesture)
                {
                    if (isInMultiTouchMode || !Settings.Gesture.IsEnableTwoFingerGesture) return;
                    if (inkCanvas.EditingMode != InkCanvasEditingMode.None && inkCanvas.EditingMode != InkCanvasEditingMode.Select)
                    {
                        lastInkCanvasEditingMode = inkCanvas.EditingMode;
                        inkCanvas.EditingMode = InkCanvasEditingMode.None;
                    }
                }
            }
            catch (Exception ex)
            {
                LogHelper.WriteLogToFile("inkCanvas_PreviewTouchDown error | " + ex, LogHelper.LogType.Error);
            }
        }

        private void inkCanvas_PreviewTouchUp(object sender, TouchEventArgs e)
        {
            try
            {
                if (dec.Count > 1)
                {
                    if (inkCanvas.EditingMode == InkCanvasEditingMode.None)
                    {
                        inkCanvas.EditingMode = lastInkCanvasEditingMode;
                    }
                }
                dec.Remove(e.TouchDevice.Id);
                inkCanvas.Opacity = 1;
                if (dec.Count == 0)
                {
                    twoFingerGestureType = TwoFingerGestureType.None;
                    // 原此处还有一段：比较 lastTouchDownStrokeCollection 与当前笔迹数后，写入
                    // strokeCollections[whiteboardIndex]。该数组全仓无读取（见 MW_BoardControls 注释），
                    // 整个分支除了这次写入没有其它作用，已移除。
                }
            }
            catch (Exception ex)
            {
                LogHelper.WriteLogToFile("inkCanvas_PreviewTouchUp error | " + ex, LogHelper.LogType.Error);
            }
        }
        private void inkCanvas_ManipulationStarting(object sender, ManipulationStartingEventArgs e)
        {
            e.Mode = ManipulationModes.All;
        }

        private void inkCanvas_ManipulationInertiaStarting(object sender, ManipulationInertiaStartingEventArgs e)
        {

        }

        private void Main_Grid_ManipulationCompleted(object sender, ManipulationCompletedEventArgs e)
        {
            if (e.Manipulators.Count() == 0)
            {
                if (forceEraser) return;
                // 保持选择状态：当前处于选择模式时不强行切回笔
                if (inkCanvas.EditingMode != InkCanvasEditingMode.Select)
                {
                    inkCanvas.EditingMode = InkCanvasEditingMode.Ink;
                }
                twoFingerGestureType = TwoFingerGestureType.None;
                translateAccum = new Vector(0, 0);
                
                // 重置手势平滑历史
                scaleHistory.Clear();
                rotateHistory.Clear();
                translateHistory.Clear();
            }
        }
        
        // 手势平滑处理方法 - 滑动窗口平均
        private Vector ApplyGestureSmoothing(Vector value, Queue<Vector> history)
        {
            history.Enqueue(value);
            if (history.Count > gestureSmoothingWindow)
            {
                history.Dequeue();
            }
            
            // 计算加权平均值，最新的数据权重更高
            double avgX = 0, avgY = 0;
            int weight = 1;
            int totalWeight = 0;
            
            foreach (Vector v in history)
            {
                avgX += v.X * weight;
                avgY += v.Y * weight;
                totalWeight += weight;
                weight++;
            }
            
            avgX /= totalWeight;
            avgY /= totalWeight;
            
            return new Vector(avgX, avgY);
        }
        
        // 手势平滑处理方法 - 滑动窗口平均
        private double ApplyGestureSmoothing(double value, Queue<double> history)
        {
            history.Enqueue(value);
            if (history.Count > gestureSmoothingWindow)
            {
                history.Dequeue();
            }
            
            // 计算加权平均值，最新的数据权重更高
            double avg = 0;
            int weight = 1;
            int totalWeight = 0;
            
            foreach (double v in history)
            {
                avg += v * weight;
                totalWeight += weight;
                weight++;
            }
            avg /= totalWeight;
            
            return avg;
        }
        


        // 节流机制
        private DateTime lastManipulationTime = DateTime.MinValue;
        private const int manipulationThrottleMs = 10; // 提高帧率，减少快速缩放时的延迟

        private void Main_Grid_ManipulationDelta(object sender, ManipulationDeltaEventArgs e)
        {
            // 节流处理
            if ((DateTime.Now - lastManipulationTime).TotalMilliseconds < manipulationThrottleMs)
            {
                return;
            }
            lastManipulationTime = DateTime.Now;

            if (isInMultiTouchMode || !Settings.Gesture.IsEnableTwoFingerGesture) return;
            // 在白板模式下(currentMode == 1)，手势应该遵循常规设置，不受PPT设置限制
            if ((dec.Count >= 2 && (currentMode == 1 || Settings.PowerPointSettings.IsEnableTwoFingerGestureInPresentationMode || BtnPPTSlideShowEnd.Visibility != Visibility.Visible)) || isSingleFingerDragMode)
            {
                Matrix m = new Matrix();
                ManipulationDelta md = e.DeltaManipulation;
                // Translation
                Vector trans = md.Translation;
                // Rotate, Scale
                double rotate = md.Rotation;
                Vector scale = md.Scale;
                Point center = GetMatrixTransformCenterPoint(e.ManipulationOrigin, e.Source as FrameworkElement);
                
                // 应用手势平滑处理
                trans = ApplyGestureSmoothing(trans, translateHistory);
                rotate = ApplyGestureSmoothing(rotate, rotateHistory);
                // 修正缩放平滑处理，使用Vector类型的历史队列
                scale = ApplyGestureSmoothing(scale, scaleHistory);
                
                double scaleDelta = Math.Max(Math.Abs(scale.X - 1.0), Math.Abs(scale.Y - 1.0));
                double transDelta = Math.Sqrt(trans.X * trans.X + trans.Y * trans.Y);
                double rotateDelta = Math.Abs(rotate);
                if (twoFingerGestureType == TwoFingerGestureType.None)
                {
                    if (Settings.Gesture.IsEnableTwoFingerZoom && scaleDelta > pinchDeadzone)
                    {
                        twoFingerGestureType = TwoFingerGestureType.Scale;
                        translateAccum = new Vector(0, 0);
                    }
                    else if (Settings.Gesture.IsEnableTwoFingerRotation && rotateDelta > rotateDeadzone)
                    {
                        twoFingerGestureType = TwoFingerGestureType.Rotate;
                        translateAccum = new Vector(0, 0);
                    }
                    else if (Settings.Gesture.IsEnableTwoFingerTranslate && transDelta > translateDeadzone)
                    {
                        twoFingerGestureType = TwoFingerGestureType.Translate;
                    }
                }
                else if (Settings.Gesture.AutoSwitchTwoFingerGesture)
                {
                    if (Settings.Gesture.IsEnableTwoFingerZoom && scaleDelta > pinchDeadzone && twoFingerGestureType != TwoFingerGestureType.Scale)
                    {
                        twoFingerGestureType = TwoFingerGestureType.Scale;
                        translateAccum = new Vector(0, 0);
                    }
                    else if (Settings.Gesture.IsEnableTwoFingerRotation && rotateDelta > rotateDeadzone && twoFingerGestureType != TwoFingerGestureType.Rotate)
                    {
                        twoFingerGestureType = TwoFingerGestureType.Rotate;
                        translateAccum = new Vector(0, 0);
                    }
                    else if (Settings.Gesture.IsEnableTwoFingerTranslate && transDelta > translateDeadzone && twoFingerGestureType != TwoFingerGestureType.Translate)
                    {
                        twoFingerGestureType = TwoFingerGestureType.Translate;
                    }
                }
                List<UIElement> elements = InkCanvasElementsHelper.GetAllElements(inkCanvas);
                if (twoFingerGestureType == TwoFingerGestureType.Scale)
                {
                    if (Settings.Gesture.IsEnableTwoFingerZoom)
                    {
                        // 应用缩放变换
                m.ScaleAt(scale.X, scale.Y, center.X, center.Y);
                foreach (UIElement element in elements)
                {
                    // 为每个元素创建独立矩阵，将画布坐标系的缩放中心转换为元素本地坐标
                    double left = InkCanvas.GetLeft(element);
                    double top = InkCanvas.GetTop(element);
                    if (double.IsNaN(left)) left = 0;
                    if (double.IsNaN(top)) top = 0;
                    Matrix elementMatrix = new Matrix();
                    elementMatrix.ScaleAt(scale.X, scale.Y, center.X - left, center.Y - top);
                    ApplyElementMatrixTransform(element, elementMatrix);
                }
                
                // 对笔迹进行缩放变换
                foreach (Stroke stroke in inkCanvas.Strokes)
                {
                    stroke.Transform(m, false);
                    try
                    {
                        // 使用平滑的缩放因子，避免笔迹宽度突变
                        double scaleFactor = Math.Max(scale.X, scale.Y);
                        // 限制缩放因子的变化范围，避免快速缩放时的抖动
                        double maxScaleChange = 0.1; // 每次缩放最大变化10%
                        scaleFactor = Math.Max(1 - maxScaleChange, Math.Min(scaleFactor, 1 + maxScaleChange));
                        stroke.DrawingAttributes.Width *= scaleFactor;
                        stroke.DrawingAttributes.Height *= scaleFactor;
                    }
                    catch { }
                }
                    }
                }
                else if (twoFingerGestureType == TwoFingerGestureType.Rotate)
                {
                    if (Settings.Gesture.IsEnableTwoFingerRotation)
                    {
                        m.RotateAt(rotate, center.X, center.Y);
                        foreach (UIElement element in elements)
                        {
                            // 为每个元素创建独立矩阵，将画布坐标系的旋转中心转换为元素本地坐标
                            double left = InkCanvas.GetLeft(element);
                            double top = InkCanvas.GetTop(element);
                            if (double.IsNaN(left)) left = 0;
                            if (double.IsNaN(top)) top = 0;
                            Matrix elementMatrix = new Matrix();
                            elementMatrix.RotateAt(rotate, center.X - left, center.Y - top);
                            ApplyElementMatrixTransform(element, elementMatrix);
                        }
                        foreach (Stroke stroke in inkCanvas.Strokes)
                        {
                            stroke.Transform(m, false);
                        }
                    }
                }
                else if (twoFingerGestureType == TwoFingerGestureType.Translate)
                {
                    if (Settings.Gesture.IsEnableTwoFingerTranslate)
                    {
                        translateAccum = new Vector(translateAccum.X + trans.X, translateAccum.Y + trans.Y);
                        double length = Math.Sqrt(translateAccum.X * translateAccum.X + translateAccum.Y * translateAccum.Y);
                        if (length >= translateApplyThreshold)
                        {
                            m.Translate(translateAccum.X, translateAccum.Y);
                            foreach (UIElement element in elements)
                            {
                                ApplyElementMatrixTransform(element, m);
                            }
                            foreach (Stroke stroke in inkCanvas.Strokes)
                            {
                                stroke.Transform(m, false);
                            }
                            translateAccum = new Vector(0, 0);
                        }
                    }
                }
                foreach (Circle circle in circles)
                {
                    circle.R = GetDistance(circle.Stroke.StylusPoints[0].ToPoint(), circle.Stroke.StylusPoints[circle.Stroke.StylusPoints.Count / 2].ToPoint()) / 2;
                    circle.Centroid = new Point(
                        (circle.Stroke.StylusPoints[0].X + circle.Stroke.StylusPoints[circle.Stroke.StylusPoints.Count / 2].X) / 2,
                        (circle.Stroke.StylusPoints[0].Y + circle.Stroke.StylusPoints[circle.Stroke.StylusPoints.Count / 2].Y) / 2
                    );
                }
            }
        }
    }
}
