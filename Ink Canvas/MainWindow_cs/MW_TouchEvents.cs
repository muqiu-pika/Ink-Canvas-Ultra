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

        private void MainWindow_TouchDown(object sender, TouchEventArgs e)
        {
            if (inkCanvas.EditingMode == InkCanvasEditingMode.EraseByPoint
                || inkCanvas.EditingMode == InkCanvasEditingMode.EraseByStroke
                || inkCanvas.EditingMode == InkCanvasEditingMode.Select) return;

            if (!isHidingSubPanelsWhenInking)
            {
                isHidingSubPanelsWhenInking = true;
                HideSubPanels(); // 书写时自动隐藏二级菜单
            }

            double boundWidth = e.GetTouchPoint(null).Bounds.Width;
            if ((Settings.Advanced.TouchMultiplier != 0 || !Settings.Advanced.IsSpecialScreen) //启用特殊屏幕且触摸倍数为 0 时禁用橡皮
                && (boundWidth > BoundsWidth))
            {
                if (drawingShapeMode == 0 && forceEraser) return;
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

        private void MainWindow_StylusDown(object sender, StylusDownEventArgs e)
        {
            if (inkCanvas.EditingMode == InkCanvasEditingMode.EraseByPoint
                || inkCanvas.EditingMode == InkCanvasEditingMode.EraseByStroke
                || inkCanvas.EditingMode == InkCanvasEditingMode.Select) return;

            TouchDownPointsList[e.StylusDevice.Id] = InkCanvasEditingMode.None;
        }

        private async void MainWindow_StylusUp(object sender, StylusEventArgs e)
        {
            try
            {
                if (e.StylusDevice.TabletDevice.Type == TabletDeviceType.Stylus)
                {
                    // 数位板 TabletDeviceType.Stylus
                }
                else
                {
                    try
                    {
                        // 触摸屏 TabletDeviceType.Touch 
                        var visual = GetStrokeVisual(e.StylusDevice.Id);
                        inkCanvas.Strokes.Add(visual.StrokeCollection);
                        await Task.Delay(5); // 避免渲染墨迹完成前预览墨迹被删除导致墨迹闪烁
                        inkCanvas.Children.Remove(GetVisualCanvas(e.StylusDevice.Id));
                        foreach (var s in visual.StrokeCollection)
                        {
                            await Dispatcher.InvokeAsync(() =>
                            {
                                try
                                {
                                    inkCanvas_StrokeCollected(inkCanvas, new InkCanvasStrokeCollectedEventArgs(s));
                                }
                                catch { }
                            }, DispatcherPriority.Background);
                        }
                    }
                    catch(Exception ex) {
                        LogHelper.WriteLogToFile(ex.ToString(), LogHelper.LogType.Error);
                    }
                }
            }
            catch (Exception ex)
            {
                LogHelper.WriteLogToFile(ex.ToString(), LogHelper.LogType.Error);
            }
            try
            {
                StrokeVisualList.Remove(e.StylusDevice.Id);
                VisualCanvasList.Remove(e.StylusDevice.Id);
                TouchDownPointsList.Remove(e.StylusDevice.Id);
                if (StrokeVisualList.Count == 0 || VisualCanvasList.Count == 0 || TouchDownPointsList.Count == 0)
                {
                    StrokeVisualList.Clear();
                    VisualCanvasList.Clear();
                    TouchDownPointsList.Clear();
                }
            }
            catch { }
        }

        private void MainWindow_StylusMove(object sender, StylusEventArgs e)
        {
            try
            {
                if (GetTouchDownPointsList(e.StylusDevice.Id) != InkCanvasEditingMode.None) return;
                try
                {
                    if (e.StylusDevice.StylusButtons[1].StylusButtonState == StylusButtonState.Down) return;
                }
                catch { }
                var strokeVisual = GetStrokeVisual(e.StylusDevice.Id);
                var stylusPointCollection = e.GetStylusPoints(inkCanvas);
                foreach (var stylusPoint in stylusPointCollection)
                {
                    strokeVisual.Add(new StylusPoint(stylusPoint.X, stylusPoint.Y, stylusPoint.PressureFactor));
                }
                strokeVisual.RedrawThrottled();
            }
            catch { }
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

        private Dictionary<int, InkCanvasEditingMode> TouchDownPointsList { get; } = new Dictionary<int, InkCanvasEditingMode>();
        private Dictionary<int, StrokeVisual> StrokeVisualList { get; } = new Dictionary<int, StrokeVisual>();
        private Dictionary<int, VisualCanvas> VisualCanvasList { get; } = new Dictionary<int, VisualCanvas>();

        #endregion

        int lastTouchDownTime = 0, lastTouchUpTime = 0;

        Point iniP = new Point(0, 0);
        bool isLastTouchEraser = false;
        private bool forcePointEraser = true;

        private void Main_Grid_TouchDown(object sender, TouchEventArgs e)
        {
            if (!isHidingSubPanelsWhenInking)
            {
                isHidingSubPanelsWhenInking = true;
                HideSubPanels(); // 书写时自动隐藏二级菜单
            }

            if (NeedUpdateIniP())
            {
                iniP = e.GetTouchPoint(inkCanvas).Position;
            }
            if (drawingShapeMode == 9 && isFirstTouchCuboid == false)
            {
                MouseTouchMove(iniP);
            }
            inkCanvas.Opacity = 1;
            double boundsWidth = GetTouchBoundWidth(e);
            if ((Settings.Advanced.TouchMultiplier != 0 || !Settings.Advanced.IsSpecialScreen) //启用特殊屏幕且触摸倍数为 0 时禁用橡皮
                && (boundsWidth > BoundsWidth))
            {
                isLastTouchEraser = true;
                if (drawingShapeMode == 0 && forceEraser) return;
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
                    if (BtnPPTSlideShowEnd.Visibility == Visibility.Visible && inkCanvas.Strokes.Count == 0 && Settings.PowerPointSettings.IsEnableFingerGestureSlideShowControl)
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
                inkCanvas.EditingMode = InkCanvasEditingMode.Ink;
            }
        }

        public double GetTouchBoundWidth(TouchEventArgs e)
        {
            var args = e.GetTouchPoint(null).Bounds;
            if (!Settings.Advanced.IsQuadIR) return args.Width;
            else return Math.Sqrt(args.Width * args.Height); //四边红外
        }

        //记录触摸设备ID
        private List<int> dec = new List<int>();
        //中心点
        Point centerPoint;
        InkCanvasEditingMode lastInkCanvasEditingMode = InkCanvasEditingMode.Ink;
        bool isSingleFingerDragMode = false;
        enum TwoFingerGestureType { None, Translate, Scale, Rotate }
        TwoFingerGestureType twoFingerGestureType = TwoFingerGestureType.None;
        double translateDeadzone = 3.0;
        double pinchDeadzone = 0.02;
        double rotateDeadzone = 1.0;
        Vector translateAccum = new Vector(0, 0);
        double translateApplyThreshold = 1.5;

        private void inkCanvas_PreviewTouchDown(object sender, TouchEventArgs e)
        {
            dec.Add(e.TouchDevice.Id);
            //设备1个的时候，记录中心点
            if (dec.Count == 1)
            {
                TouchPoint touchPoint = e.GetTouchPoint(inkCanvas);
                centerPoint = touchPoint.Position;

                //记录第一根手指点击时的 StrokeCollection
                lastTouchDownStrokeCollection = inkCanvas.Strokes.Clone();
            }
            //设备两个及两个以上，将画笔功能关闭
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

        private void inkCanvas_PreviewTouchUp(object sender, TouchEventArgs e)
        {
            //手势完成后切回之前的状态
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
                if (lastTouchDownStrokeCollection.Count() != inkCanvas.Strokes.Count() &&
                    !(drawingShapeMode == 9 && !isFirstTouchCuboid))
                {
                    int whiteboardIndex = CurrentWhiteboardIndex;
                    if (currentMode == 0)
                    {
                        whiteboardIndex = 0;
                    }
                    strokeCollections[whiteboardIndex] = lastTouchDownStrokeCollection;
                }
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
                inkCanvas.EditingMode = InkCanvasEditingMode.Ink;
                twoFingerGestureType = TwoFingerGestureType.None;
                translateAccum = new Vector(0, 0);
            }
        }

        private void Main_Grid_ManipulationDelta(object sender, ManipulationDeltaEventArgs e)
        {
            if (isInMultiTouchMode || !Settings.Gesture.IsEnableTwoFingerGesture) return;
            if ((dec.Count >= 2 && (Settings.PowerPointSettings.IsEnableTwoFingerGestureInPresentationMode || BtnPPTSlideShowEnd.Visibility != Visibility.Visible)) || isSingleFingerDragMode)
            {
                Matrix m = new Matrix();
                ManipulationDelta md = e.DeltaManipulation;
                // Translation
                Vector trans = md.Translation;
                // Rotate, Scale
                double rotate = md.Rotation;
                Vector scale = md.Scale;
                Point center = GetMatrixTransformCenterPoint(e.ManipulationOrigin, e.Source as FrameworkElement);
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
                        m.ScaleAt(scale.X, scale.Y, center.X, center.Y);
                        foreach (UIElement element in elements)
                        {
                            ApplyElementMatrixTransform(element, m);
                        }
                        foreach (Stroke stroke in inkCanvas.Strokes)
                        {
                            stroke.Transform(m, false);
                            try
                            {
                                stroke.DrawingAttributes.Width *= md.Scale.X;
                                stroke.DrawingAttributes.Height *= md.Scale.Y;
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
                            ApplyElementMatrixTransform(element, m);
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
