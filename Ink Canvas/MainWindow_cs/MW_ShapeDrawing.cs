﻿using Ink_Canvas.Helpers;
using iNKORE.UI.WPF.Modern.Controls;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Ink;
using System.Windows.Input;
using System.Windows.Media.Animation;
using MessageBox = System.Windows.MessageBox;
using Point = System.Windows.Point;

namespace Ink_Canvas
{
    public partial class MainWindow : Window
    {
        #region Floating Bar Control

        private void ImageDrawShape_Click(object sender, RoutedEventArgs e)
        {
            if (BorderDrawShape.Visibility == Visibility.Visible)
            {
                AnimationsHelper.HideWithSlideAndFade(BorderDrawShape);
                AnimationsHelper.HideWithSlideAndFade(BoardBorderDrawShape);
            }
            else
            {
                AnimationsHelper.ShowWithSlideFromBottomAndFade(BorderDrawShape);
                AnimationsHelper.ShowWithSlideFromBottomAndFade(BoardBorderDrawShape);
            }
        }

        #endregion Floating Bar Control

        int drawingShapeMode = 0;
        bool isLongPressSelected = false; // 用于存是否是“选中”状态，便于后期抬笔后不做切换到笔的处理

        #region Buttons

        private void SymbolIconPinBorderDrawShape_MouseUp(object sender, MouseButtonEventArgs e)
        {
            ToggleSwitchDrawShapeBorderAutoHide.IsOn = !ToggleSwitchDrawShapeBorderAutoHide.IsOn;

            if (ToggleSwitchDrawShapeBorderAutoHide.IsOn)
            {
                BorderDrawShapePin.Glyph = "\uE718";
                BoardBorderDrawShapePin.Glyph = "\uE718";
            }
            else
            {
                BorderDrawShapePin.Glyph = "\uE77a";
                BoardBorderDrawShapePin.Glyph = "\uE77a";
            }
        }

        object lastMouseDownSender = null;
        DateTime lastMouseDownTime = DateTime.MinValue;

        private async void Image_MouseDown(object sender, MouseButtonEventArgs e)
        {
            lastMouseDownSender = sender;
            lastMouseDownTime = DateTime.Now;

            await Task.Delay(500);

            if (lastMouseDownSender == sender)
            {
                lastMouseDownSender = null;
                var dA = new DoubleAnimation(1, 0.3, new Duration(TimeSpan.FromMilliseconds(100)));
                ((UIElement)sender).BeginAnimation(OpacityProperty, dA);

                forceEraser = true;
                inkCanvas.EditingMode = InkCanvasEditingMode.None;
                inkCanvas.IsManipulationEnabled = true;
                if (sender == ImageDrawLine || sender == BoardImageDrawLine)
                {
                    drawingShapeMode = 1;
                }
                else if (sender == ImageDrawDashedLine || sender == BoardImageDrawDashedLine)
                {
                    drawingShapeMode = 8;
                }
                else if (sender == ImageDrawDotLine || sender == BoardImageDrawDotLine)
                {
                    drawingShapeMode = 18;
                }
                else if (sender == ImageDrawArrow || sender == BoardImageDrawArrow)
                {
                    drawingShapeMode = 2;
                }
                else if (sender == ImageDrawParallelLine || sender == BoardImageDrawParallelLine)
                {
                    drawingShapeMode = 15;
                }
                isLongPressSelected = true;
                if (isSingleFingerDragMode)
                {
                    BtnFingerDragMode_Click(null, null);
                }
            }
        }

        private void BtnPen_Click(object sender, RoutedEventArgs e)
        {
            forceEraser = false;
            drawingShapeMode = 0;
            inkCanvas.EditingMode = InkCanvasEditingMode.Ink;
            inkCanvas.IsManipulationEnabled = true;
            CancelSingleFingerDragMode();
            isLongPressSelected = false;
        }

        private Task<bool> CheckIsDrawingShapesInMultiTouchMode()
        {
            if (isInMultiTouchMode)
            {
                ToggleSwitchEnableMultiTouchMode.IsOn = false;
                lastIsInMultiTouchMode = true;
            }
            return Task.FromResult(true);
        }

        private async void BtnDrawLine_Click(object sender, MouseButtonEventArgs e)
        {
            await CheckIsDrawingShapesInMultiTouchMode();
            if (lastMouseDownSender == sender)
            {
                forceEraser = true;
                drawingShapeMode = 1;
                inkCanvas.EditingMode = InkCanvasEditingMode.None;
                inkCanvas.IsManipulationEnabled = true;
                CancelSingleFingerDragMode();
            }
            lastMouseDownSender = null;
            if (isLongPressSelected)
            {
                if (ToggleSwitchDrawShapeBorderAutoHide.IsOn)
                {
                    CollapseBorderDrawShape(true);
                }
                var dA = new DoubleAnimation(1, 1, new Duration(TimeSpan.FromMilliseconds(0)));
                ImageDrawLine.BeginAnimation(OpacityProperty, dA);
            }
            DrawShapePromptToPen();
        }

        private async void BtnDrawDashedLine_Click(object sender, MouseButtonEventArgs e)
        {
            await CheckIsDrawingShapesInMultiTouchMode();
            if (lastMouseDownSender == sender)
            {
                forceEraser = true;
                drawingShapeMode = 8;
                inkCanvas.EditingMode = InkCanvasEditingMode.None;
                inkCanvas.IsManipulationEnabled = true;
                CancelSingleFingerDragMode();
            }
            lastMouseDownSender = null;
            if (isLongPressSelected)
            {
                if (ToggleSwitchDrawShapeBorderAutoHide.IsOn)
                {
                    CollapseBorderDrawShape(true);
                }
                var dA = new DoubleAnimation(1, 1, new Duration(TimeSpan.FromMilliseconds(0)));
                ImageDrawDashedLine.BeginAnimation(OpacityProperty, dA);
            }
            DrawShapePromptToPen();
        }

        private async void BtnDrawDotLine_Click(object sender, MouseButtonEventArgs e)
        {
            await CheckIsDrawingShapesInMultiTouchMode();
            if (lastMouseDownSender == sender)
            {
                forceEraser = true;
                drawingShapeMode = 18;
                inkCanvas.EditingMode = InkCanvasEditingMode.None;
                inkCanvas.IsManipulationEnabled = true;
                CancelSingleFingerDragMode();
            }
            lastMouseDownSender = null;
            if (isLongPressSelected)
            {
                if (ToggleSwitchDrawShapeBorderAutoHide.IsOn)
                {
                    CollapseBorderDrawShape(true);
                }
                var dA = new DoubleAnimation(1, 1, new Duration(TimeSpan.FromMilliseconds(0)));
                ImageDrawDotLine.BeginAnimation(OpacityProperty, dA);
            }
            DrawShapePromptToPen();
        }

        private async void BtnDrawArrow_Click(object sender, MouseButtonEventArgs e)
        {
            await CheckIsDrawingShapesInMultiTouchMode();
            if (lastMouseDownSender == sender)
            {
                forceEraser = true;
                drawingShapeMode = 2;
                inkCanvas.EditingMode = InkCanvasEditingMode.None;
                inkCanvas.IsManipulationEnabled = true;
                CancelSingleFingerDragMode();
            }
            lastMouseDownSender = null;
            if (isLongPressSelected)
            {
                if (ToggleSwitchDrawShapeBorderAutoHide.IsOn)
                {
                    CollapseBorderDrawShape(true);
                }
                var dA = new DoubleAnimation(1, 1, new Duration(TimeSpan.FromMilliseconds(0)));
                ImageDrawArrow.BeginAnimation(OpacityProperty, dA);
            }
            DrawShapePromptToPen();
        }

        private async void BtnDrawParallelLine_Click(object sender, MouseButtonEventArgs e)
        {
            await CheckIsDrawingShapesInMultiTouchMode();
            if (lastMouseDownSender == sender)
            {
                forceEraser = true;
                drawingShapeMode = 15;
                inkCanvas.EditingMode = InkCanvasEditingMode.None;
                inkCanvas.IsManipulationEnabled = true;
                CancelSingleFingerDragMode();
            }
            lastMouseDownSender = null;
            if (isLongPressSelected)
            {
                if (ToggleSwitchDrawShapeBorderAutoHide.IsOn)
                {
                    CollapseBorderDrawShape(true);
                }
                var dA = new DoubleAnimation(1, 1, new Duration(TimeSpan.FromMilliseconds(0)));
                ImageDrawParallelLine.BeginAnimation(OpacityProperty, dA);
            }
            DrawShapePromptToPen();
        }

        private async void BtnDrawCoordinate1_Click(object sender, MouseButtonEventArgs e)
        {
            await CheckIsDrawingShapesInMultiTouchMode();
            forceEraser = true;
            drawingShapeMode = 11;
            inkCanvas.EditingMode = InkCanvasEditingMode.None;
            inkCanvas.IsManipulationEnabled = true;
            CancelSingleFingerDragMode();
            DrawShapePromptToPen();
        }

        private async void BtnDrawCoordinate2_Click(object sender, MouseButtonEventArgs e)
        {
            await CheckIsDrawingShapesInMultiTouchMode();
            forceEraser = true;
            drawingShapeMode = 12;
            inkCanvas.EditingMode = InkCanvasEditingMode.None;
            inkCanvas.IsManipulationEnabled = true;
            CancelSingleFingerDragMode();
            DrawShapePromptToPen();
        }

        private async void BtnDrawCoordinate3_Click(object sender, MouseButtonEventArgs e)
        {
            await CheckIsDrawingShapesInMultiTouchMode();
            forceEraser = true;
            drawingShapeMode = 13;
            inkCanvas.EditingMode = InkCanvasEditingMode.None;
            inkCanvas.IsManipulationEnabled = true;
            CancelSingleFingerDragMode();
            DrawShapePromptToPen();
        }

        private async void BtnDrawCoordinate4_Click(object sender, MouseButtonEventArgs e)
        {
            await CheckIsDrawingShapesInMultiTouchMode();
            forceEraser = true;
            drawingShapeMode = 14;
            inkCanvas.EditingMode = InkCanvasEditingMode.None;
            inkCanvas.IsManipulationEnabled = true;
            CancelSingleFingerDragMode();
            DrawShapePromptToPen();
        }

        private async void BtnDrawCoordinate5_Click(object sender, MouseButtonEventArgs e)
        {
            await CheckIsDrawingShapesInMultiTouchMode();
            forceEraser = true;
            drawingShapeMode = 17;
            inkCanvas.EditingMode = InkCanvasEditingMode.None;
            inkCanvas.IsManipulationEnabled = true;
            CancelSingleFingerDragMode();
            DrawShapePromptToPen();
        }

        private async void BtnDrawRectangle_Click(object sender, MouseButtonEventArgs e)
        {
            await CheckIsDrawingShapesInMultiTouchMode();
            forceEraser = true;
            drawingShapeMode = 3;
            inkCanvas.EditingMode = InkCanvasEditingMode.None;
            inkCanvas.IsManipulationEnabled = true;
            CancelSingleFingerDragMode();
            DrawShapePromptToPen();
        }

        private async void BtnDrawRectangleCenter_Click(object sender, MouseButtonEventArgs e)
        {
            await CheckIsDrawingShapesInMultiTouchMode();
            forceEraser = true;
            drawingShapeMode = 19;
            inkCanvas.EditingMode = InkCanvasEditingMode.None;
            inkCanvas.IsManipulationEnabled = true;
            CancelSingleFingerDragMode();
            DrawShapePromptToPen();
        }

        private async void BtnDrawEllipse_Click(object sender, MouseButtonEventArgs e)
        {
            await CheckIsDrawingShapesInMultiTouchMode();
            forceEraser = true;
            drawingShapeMode = 4;
            inkCanvas.EditingMode = InkCanvasEditingMode.None;
            inkCanvas.IsManipulationEnabled = true;
            CancelSingleFingerDragMode();
            DrawShapePromptToPen();
        }

        private async void BtnDrawCircle_Click(object sender, MouseButtonEventArgs e)
        {
            await CheckIsDrawingShapesInMultiTouchMode();
            forceEraser = true;
            drawingShapeMode = 5;
            inkCanvas.EditingMode = InkCanvasEditingMode.None;
            inkCanvas.IsManipulationEnabled = true;
            CancelSingleFingerDragMode();
            DrawShapePromptToPen();
        }

        private async void BtnDrawCenterEllipse_Click(object sender, MouseButtonEventArgs e)
        {
            await CheckIsDrawingShapesInMultiTouchMode();
            forceEraser = true;
            drawingShapeMode = 16;
            inkCanvas.EditingMode = InkCanvasEditingMode.None;
            inkCanvas.IsManipulationEnabled = true;
            CancelSingleFingerDragMode();
            DrawShapePromptToPen();
        }

        private async void BtnDrawCenterEllipseWithFocalPoint_Click(object sender, MouseButtonEventArgs e)
        {
            await CheckIsDrawingShapesInMultiTouchMode();
            forceEraser = true;
            drawingShapeMode = 23;
            inkCanvas.EditingMode = InkCanvasEditingMode.None;
            inkCanvas.IsManipulationEnabled = true;
            CancelSingleFingerDragMode();
            DrawShapePromptToPen();
        }

        private async void BtnDrawDashedCircle_Click(object sender, MouseButtonEventArgs e)
        {
            await CheckIsDrawingShapesInMultiTouchMode();
            forceEraser = true;
            drawingShapeMode = 10;
            inkCanvas.EditingMode = InkCanvasEditingMode.None;
            inkCanvas.IsManipulationEnabled = true;
            CancelSingleFingerDragMode();
            DrawShapePromptToPen();
        }

        private async void BtnDrawHyperbola_Click(object sender, MouseButtonEventArgs e)
        {
            await CheckIsDrawingShapesInMultiTouchMode();
            forceEraser = true;
            drawingShapeMode = 24;
            drawMultiStepShapeCurrentStep = 0;
            inkCanvas.EditingMode = InkCanvasEditingMode.None;
            inkCanvas.IsManipulationEnabled = true;
            CancelSingleFingerDragMode();
            DrawShapePromptToPen();
        }

        private async void BtnDrawHyperbolaWithFocalPoint_Click(object sender, MouseButtonEventArgs e)
        {
            await CheckIsDrawingShapesInMultiTouchMode();
            forceEraser = true;
            drawingShapeMode = 25;
            drawMultiStepShapeCurrentStep = 0;
            inkCanvas.EditingMode = InkCanvasEditingMode.None;
            inkCanvas.IsManipulationEnabled = true;
            CancelSingleFingerDragMode();
            DrawShapePromptToPen();
        }

        private async void BtnDrawParabola1_Click(object sender, MouseButtonEventArgs e)
        {
            await CheckIsDrawingShapesInMultiTouchMode();
            forceEraser = true;
            drawingShapeMode = 20;
            inkCanvas.EditingMode = InkCanvasEditingMode.None;
            inkCanvas.IsManipulationEnabled = true;
            CancelSingleFingerDragMode();
            DrawShapePromptToPen();
        }

        private async void BtnDrawParabolaWithFocalPoint_Click(object sender, MouseButtonEventArgs e)
        {
            await CheckIsDrawingShapesInMultiTouchMode();
            forceEraser = true;
            drawingShapeMode = 22;
            inkCanvas.EditingMode = InkCanvasEditingMode.None;
            inkCanvas.IsManipulationEnabled = true;
            CancelSingleFingerDragMode();
            DrawShapePromptToPen();
        }

        private async void BtnDrawParabola2_Click(object sender, MouseButtonEventArgs e)
        {
            await CheckIsDrawingShapesInMultiTouchMode();
            forceEraser = true;
            drawingShapeMode = 21;
            inkCanvas.EditingMode = InkCanvasEditingMode.None;
            inkCanvas.IsManipulationEnabled = true;
            CancelSingleFingerDragMode();
            DrawShapePromptToPen();
        }

        private async void BtnDrawCylinder_Click(object sender, MouseButtonEventArgs e)
        {
            await CheckIsDrawingShapesInMultiTouchMode();
            forceEraser = true;
            drawingShapeMode = 6;
            inkCanvas.EditingMode = InkCanvasEditingMode.None;
            inkCanvas.IsManipulationEnabled = true;
            CancelSingleFingerDragMode();
            DrawShapePromptToPen();
        }

        private async void BtnDrawCone_Click(object sender, MouseButtonEventArgs e)
        {
            await CheckIsDrawingShapesInMultiTouchMode();
            forceEraser = true;
            drawingShapeMode = 7;
            inkCanvas.EditingMode = InkCanvasEditingMode.None;
            inkCanvas.IsManipulationEnabled = true;
            CancelSingleFingerDragMode();
            DrawShapePromptToPen();
        }

        private async void BtnDrawCuboid_Click(object sender, MouseButtonEventArgs e)
        {
            await CheckIsDrawingShapesInMultiTouchMode();
            forceEraser = true;
            drawingShapeMode = 9;
            isFirstTouchCuboid = true;
            CuboidFrontRectIniP = new Point();
            CuboidFrontRectEndP = new Point();
            inkCanvas.EditingMode = InkCanvasEditingMode.None;
            inkCanvas.IsManipulationEnabled = true;
            CancelSingleFingerDragMode();
            DrawShapePromptToPen();
        }

        #endregion

        private void inkCanvas_TouchMove(object sender, TouchEventArgs e)
        {
            if (isSingleFingerDragMode) return;
            if (drawingShapeMode != 0)
            {
                if (isLastTouchEraser)
                {
                    return;
                }
                //EraserContainer.Background = null;
                //ImageEraser.Visibility = Visibility.Visible;
                if (isWaitUntilNextTouchDown) return;
                if (dec.Count > 1)
                {
                    isWaitUntilNextTouchDown = true;
                    try
                    {
                        inkCanvas.Strokes.Remove(lastTempStroke);
                        inkCanvas.Strokes.Remove(lastTempStrokeCollection);
                    }
                    catch { }
                    return;
                }
                if (inkCanvas.EditingMode != InkCanvasEditingMode.None)
                {
                    inkCanvas.EditingMode = InkCanvasEditingMode.None;
                }
            }
            MouseTouchMove(e.GetTouchPoint(inkCanvas).Position);
        }

        int drawMultiStepShapeCurrentStep = 0; //多笔完成的图形 当前所处在的笔画
        StrokeCollection drawMultiStepShapeSpecialStrokeCollection = new StrokeCollection(); //多笔完成的图形 当前所处在的笔画
                                                                                             //double drawMultiStepShapeSpecialParameter1 = 0.0; //多笔完成的图形 特殊参数 通常用于表示a
                                                                                             //double drawMultiStepShapeSpecialParameter2 = 0.0; //多笔完成的图形 特殊参数 通常用于表示b
        double drawMultiStepShapeSpecialParameter3 = 0.0; //多笔完成的图形 特殊参数 通常用于表示k

        private void MouseTouchMove(Point endP)
        {
            List<System.Windows.Point> pointList;
            StylusPointCollection point;
            Stroke stroke;
            StrokeCollection strokes = new StrokeCollection();
            Point newIniP = iniP;
            switch (drawingShapeMode)
            {
                case 1:
                    _currentCommitType = CommitReason.ShapeDrawing;
                    pointList = new List<System.Windows.Point>{
                        new System.Windows.Point(iniP.X, iniP.Y),
                        new System.Windows.Point(endP.X, endP.Y)
                    };
                    point = new StylusPointCollection(pointList);
                    stroke = new Stroke(point)
                    {
                        DrawingAttributes = inkCanvas.DefaultDrawingAttributes.Clone()
                    };
                    try
                    {
                        inkCanvas.Strokes.Remove(lastTempStroke);
                    }
                    catch { }
                    lastTempStroke = stroke;
                    inkCanvas.Strokes.Add(stroke);
                    break;
                case 8:
                    _currentCommitType = CommitReason.ShapeDrawing;
                    strokes.Add(GenerateDashedLineStrokeCollection(iniP, endP));
                    try
                    {
                        inkCanvas.Strokes.Remove(lastTempStrokeCollection);
                    }
                    catch { }
                    lastTempStrokeCollection = strokes;
                    inkCanvas.Strokes.Add(strokes);
                    break;
                case 18:
                    _currentCommitType = CommitReason.ShapeDrawing;
                    strokes.Add(GenerateDotLineStrokeCollection(iniP, endP));
                    try
                    {
                        inkCanvas.Strokes.Remove(lastTempStrokeCollection);
                    }
                    catch { }
                    lastTempStrokeCollection = strokes;
                    inkCanvas.Strokes.Add(strokes);
                    break;
                case 2:
                    _currentCommitType = CommitReason.ShapeDrawing;
                    double w = 30, h = 10;
                    double theta = Math.Atan2(iniP.Y - endP.Y, iniP.X - endP.X);
                    double sint = Math.Sin(theta);
                    double cost = Math.Cos(theta);

                    pointList = new List<Point>
                    {
                        new Point(iniP.X, iniP.Y),
                        new Point(endP.X , endP.Y),
                        new Point(endP.X + (w * cost - h * sint), endP.Y + (w * sint + h * cost)),
                        new Point(endP.X,endP.Y),
                        new Point(endP.X + (w * cost + h * sint), endP.Y - (h * cost - w * sint))
                    };
                    point = new StylusPointCollection(pointList);
                    stroke = new Stroke(point)
                    {
                        DrawingAttributes = inkCanvas.DefaultDrawingAttributes.Clone()
                    };
                    try
                    {
                        inkCanvas.Strokes.Remove(lastTempStroke);
                    }
                    catch { }
                    lastTempStroke = stroke;
                    inkCanvas.Strokes.Add(stroke);

                    break;
                case 15:
                    _currentCommitType = CommitReason.ShapeDrawing;
                    double d = GetDistance(iniP, endP);
                    if (d == 0) return;
                    double sinTheta = (iniP.Y - endP.Y) / d;
                    double cosTheta = (endP.X - iniP.X) / d;
                    double tanTheta = Math.Abs(sinTheta / cosTheta);
                    double x = 25;
                    if (Math.Abs(tanTheta) < 1.0 / 12)
                    {
                        sinTheta = 0;
                        cosTheta = 1;
                        endP.Y = iniP.Y;
                    }
                    if (tanTheta < 0.63 && tanTheta > 0.52) //30
                    {
                        sinTheta = sinTheta / Math.Abs(sinTheta) * 0.5;
                        cosTheta = cosTheta / Math.Abs(cosTheta) * 0.866;
                        endP.Y = iniP.Y - d * sinTheta;
                        endP.X = iniP.X + d * cosTheta;
                    }
                    if (tanTheta < 1.08 && tanTheta > 0.92) //45
                    {
                        sinTheta = sinTheta / Math.Abs(sinTheta) * 0.707;
                        cosTheta = cosTheta / Math.Abs(cosTheta) * 0.707;
                        endP.Y = iniP.Y - d * sinTheta;
                        endP.X = iniP.X + d * cosTheta;
                    }
                    if (tanTheta < 1.95 && tanTheta > 1.63) //60
                    {
                        sinTheta = sinTheta / Math.Abs(sinTheta) * 0.866;
                        cosTheta = cosTheta / Math.Abs(cosTheta) * 0.5;
                        endP.Y = iniP.Y - d * sinTheta;
                        endP.X = iniP.X + d * cosTheta;
                    }
                    if (Math.Abs(cosTheta / sinTheta) < 1.0 / 12)
                    {
                        endP.X = iniP.X;
                        sinTheta = 1;
                        cosTheta = 0;
                    }
                    strokes.Add(GenerateLineStroke(new Point(iniP.X - 3 * x * sinTheta, iniP.Y - 3 * x * cosTheta), new Point(endP.X - 3 * x * sinTheta, endP.Y - 3 * x * cosTheta)));
                    strokes.Add(GenerateLineStroke(new Point(iniP.X - x * sinTheta, iniP.Y - x * cosTheta), new Point(endP.X - x * sinTheta, endP.Y - x * cosTheta)));
                    strokes.Add(GenerateLineStroke(new Point(iniP.X + x * sinTheta, iniP.Y + x * cosTheta), new Point(endP.X + x * sinTheta, endP.Y + x * cosTheta)));
                    strokes.Add(GenerateLineStroke(new Point(iniP.X + 3 * x * sinTheta, iniP.Y + 3 * x * cosTheta), new Point(endP.X + 3 * x * sinTheta, endP.Y + 3 * x * cosTheta)));
                    try
                    {
                        inkCanvas.Strokes.Remove(lastTempStrokeCollection);
                    }
                    catch { }
                    lastTempStrokeCollection = strokes;
                    inkCanvas.Strokes.Add(strokes);

                    break;
                case 11:
                    _currentCommitType = CommitReason.ShapeDrawing;
                    strokes.Add(GenerateArrowLineStroke(new Point(2 * iniP.X - (endP.X - 20), iniP.Y), new Point(endP.X, iniP.Y)));
                    strokes.Add(GenerateArrowLineStroke(new Point(iniP.X, 2 * iniP.Y - (endP.Y + 20)), new Point(iniP.X, endP.Y)));
                    try
                    {
                        inkCanvas.Strokes.Remove(lastTempStrokeCollection);
                    }
                    catch { }
                    lastTempStrokeCollection = strokes;
                    inkCanvas.Strokes.Add(strokes);

                    break;
                case 12:
                    _currentCommitType = CommitReason.ShapeDrawing;
                    if (Math.Abs(iniP.X - endP.X) < 0.01) return;
                    strokes.Add(GenerateArrowLineStroke(new Point(iniP.X + (iniP.X - endP.X) / Math.Abs(iniP.X - endP.X) * 25, iniP.Y), new Point(endP.X, iniP.Y)));
                    strokes.Add(GenerateArrowLineStroke(new Point(iniP.X, 2 * iniP.Y - (endP.Y + 20)), new Point(iniP.X, endP.Y)));
                    try
                    {
                        inkCanvas.Strokes.Remove(lastTempStrokeCollection);
                    }
                    catch { }
                    lastTempStrokeCollection = strokes;
                    inkCanvas.Strokes.Add(strokes);

                    break;
                case 13:
                    _currentCommitType = CommitReason.ShapeDrawing;
                    if (Math.Abs(iniP.Y - endP.Y) < 0.01) return;
                    strokes.Add(GenerateArrowLineStroke(new Point(2 * iniP.X - (endP.X - 20), iniP.Y), new Point(endP.X, iniP.Y)));
                    strokes.Add(GenerateArrowLineStroke(new Point(iniP.X, iniP.Y + (iniP.Y - endP.Y) / Math.Abs(iniP.Y - endP.Y) * 25), new Point(iniP.X, endP.Y)));
                    try
                    {
                        inkCanvas.Strokes.Remove(lastTempStrokeCollection);
                    }
                    catch { }
                    lastTempStrokeCollection = strokes;
                    inkCanvas.Strokes.Add(strokes);

                    break;
                case 14:
                    _currentCommitType = CommitReason.ShapeDrawing;
                    if (Math.Abs(iniP.X - endP.X) < 0.01 || Math.Abs(iniP.Y - endP.Y) < 0.01) return;
                    strokes.Add(GenerateArrowLineStroke(new Point(iniP.X + (iniP.X - endP.X) / Math.Abs(iniP.X - endP.X) * 25, iniP.Y), new Point(endP.X, iniP.Y)));
                    strokes.Add(GenerateArrowLineStroke(new Point(iniP.X, iniP.Y + (iniP.Y - endP.Y) / Math.Abs(iniP.Y - endP.Y) * 25), new Point(iniP.X, endP.Y)));
                    try
                    {
                        inkCanvas.Strokes.Remove(lastTempStrokeCollection);
                    }
                    catch { }
                    lastTempStrokeCollection = strokes;
                    inkCanvas.Strokes.Add(strokes);

                    break;
                case 17:
                    _currentCommitType = CommitReason.ShapeDrawing;
                    strokes.Add(GenerateArrowLineStroke(new Point(iniP.X, iniP.Y), new Point(iniP.X + Math.Abs(endP.X - iniP.X), iniP.Y)));
                    strokes.Add(GenerateArrowLineStroke(new Point(iniP.X, iniP.Y), new Point(iniP.X, iniP.Y - Math.Abs(endP.Y - iniP.Y))));
                    d = (Math.Abs(iniP.X - endP.X) + Math.Abs(iniP.Y - endP.Y)) / 2;
                    strokes.Add(GenerateArrowLineStroke(new Point(iniP.X, iniP.Y), new Point(iniP.X - d / 1.76, iniP.Y + d / 1.76)));
                    try
                    {
                        inkCanvas.Strokes.Remove(lastTempStrokeCollection);
                    }
                    catch { }
                    lastTempStrokeCollection = strokes;
                    inkCanvas.Strokes.Add(strokes);

                    break;
                case 3:
                    _currentCommitType = CommitReason.ShapeDrawing;
                    pointList = new List<System.Windows.Point>{
                        new System.Windows.Point(iniP.X, iniP.Y),
                        new System.Windows.Point(iniP.X, endP.Y),
                        new System.Windows.Point(endP.X, endP.Y),
                        new System.Windows.Point(endP.X, iniP.Y),
                        new System.Windows.Point(iniP.X, iniP.Y)
                    };
                    point = new StylusPointCollection(pointList);
                    stroke = new Stroke(point)
                    {
                        DrawingAttributes = inkCanvas.DefaultDrawingAttributes.Clone()
                    };
                    try
                    {
                        inkCanvas.Strokes.Remove(lastTempStroke);
                    }
                    catch { }
                    lastTempStroke = stroke;
                    inkCanvas.Strokes.Add(stroke);

                    break;
                case 19:
                    _currentCommitType = CommitReason.ShapeDrawing;
                    double a = iniP.X - endP.X;
                    double b = iniP.Y - endP.Y;
                    pointList = new List<System.Windows.Point>{
                        new System.Windows.Point(iniP.X - a, iniP.Y - b),
                        new System.Windows.Point(iniP.X - a, iniP.Y + b),
                        new System.Windows.Point(iniP.X + a, iniP.Y + b),
                        new System.Windows.Point(iniP.X + a, iniP.Y - b),
                        new System.Windows.Point(iniP.X - a, iniP.Y - b)
                    };
                    point = new StylusPointCollection(pointList);
                    stroke = new Stroke(point)
                    {
                        DrawingAttributes = inkCanvas.DefaultDrawingAttributes.Clone()
                    };
                    try
                    {
                        inkCanvas.Strokes.Remove(lastTempStroke);
                    }
                    catch { }
                    lastTempStroke = stroke;
                    inkCanvas.Strokes.Add(stroke);

                    break;
                case 4:
                    _currentCommitType = CommitReason.ShapeDrawing;
                    pointList = GenerateEllipseGeometry(iniP, endP);
                    point = new StylusPointCollection(pointList);
                    stroke = new Stroke(point)
                    {
                        DrawingAttributes = inkCanvas.DefaultDrawingAttributes.Clone()
                    };
                    try
                    {
                        inkCanvas.Strokes.Remove(lastTempStroke);
                    }
                    catch { }
                    lastTempStroke = stroke;
                    inkCanvas.Strokes.Add(stroke);

                    break;
                case 5:
                    _currentCommitType = CommitReason.ShapeDrawing;
                    double R = GetDistance(iniP, endP);
                    pointList = GenerateEllipseGeometry(new Point(iniP.X - R, iniP.Y - R), new Point(iniP.X + R, iniP.Y + R));
                    point = new StylusPointCollection(pointList);
                    stroke = new Stroke(point)
                    {
                        DrawingAttributes = inkCanvas.DefaultDrawingAttributes.Clone()
                    };
                    try
                    {
                        inkCanvas.Strokes.Remove(lastTempStroke);
                    }
                    catch { }
                    lastTempStroke = stroke;
                    inkCanvas.Strokes.Add(stroke);

                    break;
                case 16:
                    _currentCommitType = CommitReason.ShapeDrawing;
                    double halfA = endP.X - iniP.X;
                    double halfB = endP.Y - iniP.Y;
                    pointList = GenerateEllipseGeometry(new Point(iniP.X - halfA, iniP.Y - halfB), new Point(iniP.X + halfA, iniP.Y + halfB));
                    point = new StylusPointCollection(pointList);
                    stroke = new Stroke(point)
                    {
                        DrawingAttributes = inkCanvas.DefaultDrawingAttributes.Clone()
                    };
                    try
                    {
                        inkCanvas.Strokes.Remove(lastTempStroke);
                    }
                    catch { }
                    lastTempStroke = stroke;
                    inkCanvas.Strokes.Add(stroke);

                    break;
                case 23:
                    _currentCommitType = CommitReason.ShapeDrawing;
                    a = Math.Abs(endP.X - iniP.X);
                    b = Math.Abs(endP.Y - iniP.Y);
                    pointList = GenerateEllipseGeometry(new Point(iniP.X - a, iniP.Y - b), new Point(iniP.X + a, iniP.Y + b));
                    point = new StylusPointCollection(pointList);
                    stroke = new Stroke(point)
                    {
                        DrawingAttributes = inkCanvas.DefaultDrawingAttributes.Clone()
                    };
                    strokes.Add(stroke);
                    double c = Math.Sqrt(Math.Abs(a * a - b * b));
                    StylusPoint stylusPoint;
                    if (a > b)
                    {
                        stylusPoint = new StylusPoint(iniP.X + c, iniP.Y, (float)1.0);
                        point = new StylusPointCollection();
                        point.Add(stylusPoint);
                        stroke = new Stroke(point)
                        {
                            DrawingAttributes = inkCanvas.DefaultDrawingAttributes.Clone()
                        };
                        strokes.Add(stroke.Clone());
                        stylusPoint = new StylusPoint(iniP.X - c, iniP.Y, (float)1.0);
                        point = new StylusPointCollection();
                        point.Add(stylusPoint);
                        stroke = new Stroke(point)
                        {
                            DrawingAttributes = inkCanvas.DefaultDrawingAttributes.Clone()
                        };
                        strokes.Add(stroke.Clone());
                    }
                    else if (a < b)
                    {
                        stylusPoint = new StylusPoint(iniP.X, iniP.Y - c, (float)1.0);
                        point = new StylusPointCollection();
                        point.Add(stylusPoint);
                        stroke = new Stroke(point)
                        {
                            DrawingAttributes = inkCanvas.DefaultDrawingAttributes.Clone()
                        };
                        strokes.Add(stroke.Clone());
                        stylusPoint = new StylusPoint(iniP.X, iniP.Y + c, (float)1.0);
                        point = new StylusPointCollection();
                        point.Add(stylusPoint);
                        stroke = new Stroke(point)
                        {
                            DrawingAttributes = inkCanvas.DefaultDrawingAttributes.Clone()
                        };
                        strokes.Add(stroke.Clone());
                    }
                    try
                    {
                        inkCanvas.Strokes.Remove(lastTempStrokeCollection);
                    }
                    catch { }
                    lastTempStrokeCollection = strokes;
                    inkCanvas.Strokes.Add(strokes);

                    break;
                case 10:
                    _currentCommitType = CommitReason.ShapeDrawing;
                    R = GetDistance(iniP, endP);
                    strokes = GenerateDashedLineEllipseStrokeCollection(new Point(iniP.X - R, iniP.Y - R), new Point(iniP.X + R, iniP.Y + R));
                    try
                    {
                        inkCanvas.Strokes.Remove(lastTempStrokeCollection);
                    }
                    catch { }
                    lastTempStrokeCollection = strokes;
                    inkCanvas.Strokes.Add(strokes);

                    break;
                case 24:
                case 25:
                    _currentCommitType = CommitReason.ShapeDrawing;
                    //双曲线 x^2/a^2 - y^2/b^2 = 1
                    if (Math.Abs(iniP.X - endP.X) < 0.01 || Math.Abs(iniP.Y - endP.Y) < 0.01) return;
                    var pointList2 = new List<Point>();
                    var pointList3 = new List<Point>();
                    var pointList4 = new List<Point>();
                    if (drawMultiStepShapeCurrentStep == 0)
                    {
                        //第一笔：画渐近线
                        double k = Math.Abs((endP.Y - iniP.Y) / (endP.X - iniP.X));
                        strokes.Add(GenerateDashedLineStrokeCollection(new Point(2 * iniP.X - endP.X, 2 * iniP.Y - endP.Y), endP));
                        strokes.Add(GenerateDashedLineStrokeCollection(new Point(2 * iniP.X - endP.X, endP.Y), new Point(endP.X, 2 * iniP.Y - endP.Y)));
                        drawMultiStepShapeSpecialParameter3 = k;
                        drawMultiStepShapeSpecialStrokeCollection = strokes;
                    }
                    else
                    {
                        //第二笔：画双曲线
                        double k = drawMultiStepShapeSpecialParameter3;
                        bool isHyperbolaFocalPointOnXAxis = Math.Abs((endP.Y - iniP.Y) / (endP.X - iniP.X)) < k;
                        if (isHyperbolaFocalPointOnXAxis)
                        { // 焦点在 x 轴上
                            a = Math.Sqrt(Math.Abs((endP.X - iniP.X) * (endP.X - iniP.X) - (endP.Y - iniP.Y) * (endP.Y - iniP.Y) / (k * k)));
                            b = a * k;
                            pointList = new List<Point>();
                            for (double i = a; i <= Math.Abs(endP.X - iniP.X); i += 0.5)
                            {
                                double rY = Math.Sqrt(Math.Abs(k * k * i * i - b * b));
                                pointList.Add(new Point(iniP.X + i, iniP.Y - rY));
                                pointList2.Add(new Point(iniP.X + i, iniP.Y + rY));
                                pointList3.Add(new Point(iniP.X - i, iniP.Y - rY));
                                pointList4.Add(new Point(iniP.X - i, iniP.Y + rY));
                            }
                        }
                        else
                        { // 焦点在 y 轴上
                            a = Math.Sqrt(Math.Abs((endP.Y - iniP.Y) * (endP.Y - iniP.Y) - (endP.X - iniP.X) * (endP.X - iniP.X) * (k * k)));
                            b = a / k;
                            pointList = new List<Point>();
                            for (double i = a; i <= Math.Abs(endP.Y - iniP.Y); i += 0.5)
                            {
                                double rX = Math.Sqrt(Math.Abs(i * i / k / k - b * b));
                                pointList.Add(new Point(iniP.X - rX, iniP.Y + i));
                                pointList2.Add(new Point(iniP.X + rX, iniP.Y + i));
                                pointList3.Add(new Point(iniP.X - rX, iniP.Y - i));
                                pointList4.Add(new Point(iniP.X + rX, iniP.Y - i));
                            }
                        }
                        try
                        {
                            point = new StylusPointCollection(pointList);
                            stroke = new Stroke(point) { DrawingAttributes = inkCanvas.DefaultDrawingAttributes.Clone() };
                            strokes.Add(stroke.Clone());
                            point = new StylusPointCollection(pointList2);
                            stroke = new Stroke(point) { DrawingAttributes = inkCanvas.DefaultDrawingAttributes.Clone() };
                            strokes.Add(stroke.Clone());
                            point = new StylusPointCollection(pointList3);
                            stroke = new Stroke(point) { DrawingAttributes = inkCanvas.DefaultDrawingAttributes.Clone() };
                            strokes.Add(stroke.Clone());
                            point = new StylusPointCollection(pointList4);
                            stroke = new Stroke(point) { DrawingAttributes = inkCanvas.DefaultDrawingAttributes.Clone() };
                            strokes.Add(stroke.Clone());
                            if (drawingShapeMode == 25)
                            {
                                //画焦点
                                c = Math.Sqrt(a * a + b * b);
                                stylusPoint = isHyperbolaFocalPointOnXAxis ? new StylusPoint(iniP.X + c, iniP.Y, (float)1.0) : new StylusPoint(iniP.X, iniP.Y + c, (float)1.0);
                                point = new StylusPointCollection();
                                point.Add(stylusPoint);
                                stroke = new Stroke(point) { DrawingAttributes = inkCanvas.DefaultDrawingAttributes.Clone() };
                                strokes.Add(stroke.Clone());
                                stylusPoint = isHyperbolaFocalPointOnXAxis ? new StylusPoint(iniP.X - c, iniP.Y, (float)1.0) : new StylusPoint(iniP.X, iniP.Y - c, (float)1.0);
                                point = new StylusPointCollection();
                                point.Add(stylusPoint);
                                stroke = new Stroke(point) { DrawingAttributes = inkCanvas.DefaultDrawingAttributes.Clone() };
                                strokes.Add(stroke.Clone());
                            }
                        }
                        catch
                        {
                            return;
                        }
                    }
                    try
                    {
                        inkCanvas.Strokes.Remove(lastTempStrokeCollection);
                    }
                    catch { }
                    lastTempStrokeCollection = strokes;
                    inkCanvas.Strokes.Add(strokes);

                    break;
                case 20:
                    _currentCommitType = CommitReason.ShapeDrawing;
                    //抛物线 y=ax^2
                    if (Math.Abs(iniP.X - endP.X) < 0.01 || Math.Abs(iniP.Y - endP.Y) < 0.01) return;
                    a = (iniP.Y - endP.Y) / ((iniP.X - endP.X) * (iniP.X - endP.X));
                    pointList = new List<Point>();
                    pointList2 = new List<Point>();
                    for (double i = 0.0; i <= Math.Abs(endP.X - iniP.X); i += 0.5)
                    {
                        pointList.Add(new Point(iniP.X + i, iniP.Y - a * i * i));
                        pointList2.Add(new Point(iniP.X - i, iniP.Y - a * i * i));
                    }
                    point = new StylusPointCollection(pointList);
                    stroke = new Stroke(point)
                    {
                        DrawingAttributes = inkCanvas.DefaultDrawingAttributes.Clone()
                    };
                    strokes.Add(stroke.Clone());
                    point = new StylusPointCollection(pointList2);
                    stroke = new Stroke(point)
                    {
                        DrawingAttributes = inkCanvas.DefaultDrawingAttributes.Clone()
                    };
                    strokes.Add(stroke.Clone());
                    try
                    {
                        inkCanvas.Strokes.Remove(lastTempStrokeCollection);
                    }
                    catch { }
                    lastTempStrokeCollection = strokes;
                    inkCanvas.Strokes.Add(strokes);

                    break;
                case 21:
                    _currentCommitType = CommitReason.ShapeDrawing;
                    //抛物线 y^2=ax
                    if (Math.Abs(iniP.X - endP.X) < 0.01 || Math.Abs(iniP.Y - endP.Y) < 0.01) return;
                    a = (iniP.X - endP.X) / ((iniP.Y - endP.Y) * (iniP.Y - endP.Y));
                    pointList = new List<Point>();
                    pointList2 = new List<Point>();
                    for (double i = 0.0; i <= Math.Abs(endP.Y - iniP.Y); i += 0.5)
                    {
                        pointList.Add(new Point(iniP.X - a * i * i, iniP.Y + i));
                        pointList2.Add(new Point(iniP.X - a * i * i, iniP.Y - i));
                    }
                    point = new StylusPointCollection(pointList);
                    stroke = new Stroke(point)
                    {
                        DrawingAttributes = inkCanvas.DefaultDrawingAttributes.Clone()
                    };
                    strokes.Add(stroke.Clone());
                    point = new StylusPointCollection(pointList2);
                    stroke = new Stroke(point)
                    {
                        DrawingAttributes = inkCanvas.DefaultDrawingAttributes.Clone()
                    };
                    strokes.Add(stroke.Clone());
                    try
                    {
                        inkCanvas.Strokes.Remove(lastTempStrokeCollection);
                    }
                    catch { }
                    lastTempStrokeCollection = strokes;
                    inkCanvas.Strokes.Add(strokes);

                    break;
                case 22:
                    _currentCommitType = CommitReason.ShapeDrawing;
                    //抛物线 y^2=ax, 含焦点
                    if (Math.Abs(iniP.X - endP.X) < 0.01 || Math.Abs(iniP.Y - endP.Y) < 0.01) return;
                    double p = (iniP.Y - endP.Y) * (iniP.Y - endP.Y) / (2 * (iniP.X - endP.X));
                    a = 0.5 / p;
                    pointList = new List<Point>();
                    pointList2 = new List<Point>();
                    for (double i = 0.0; i <= Math.Abs(endP.Y - iniP.Y); i += 0.5)
                    {
                        pointList.Add(new Point(iniP.X - a * i * i, iniP.Y + i));
                        pointList2.Add(new Point(iniP.X - a * i * i, iniP.Y - i));
                    }
                    point = new StylusPointCollection(pointList);
                    stroke = new Stroke(point)
                    {
                        DrawingAttributes = inkCanvas.DefaultDrawingAttributes.Clone()
                    };
                    strokes.Add(stroke.Clone());
                    point = new StylusPointCollection(pointList2);
                    stroke = new Stroke(point)
                    {
                        DrawingAttributes = inkCanvas.DefaultDrawingAttributes.Clone()
                    };
                    strokes.Add(stroke.Clone());
                    stylusPoint = new StylusPoint(iniP.X - p / 2, iniP.Y, (float)1.0);
                    point = new StylusPointCollection();
                    point.Add(stylusPoint);
                    stroke = new Stroke(point)
                    {
                        DrawingAttributes = inkCanvas.DefaultDrawingAttributes.Clone()
                    };
                    strokes.Add(stroke.Clone());
                    try
                    {
                        inkCanvas.Strokes.Remove(lastTempStrokeCollection);
                    }
                    catch { }
                    lastTempStrokeCollection = strokes;
                    inkCanvas.Strokes.Add(strokes);

                    break;
                case 6:
                    _currentCommitType = CommitReason.ShapeDrawing;
                    newIniP = iniP;
                    if (iniP.Y > endP.Y)
                    {
                        newIniP = new Point(iniP.X, endP.Y);
                        endP = new Point(endP.X, iniP.Y);
                    }
                    double topA = Math.Abs(newIniP.X - endP.X);
                    double topB = topA / 2.646;
                    //顶部椭圆
                    pointList = GenerateEllipseGeometry(new Point(newIniP.X, newIniP.Y - topB / 2), new Point(endP.X, newIniP.Y + topB / 2));
                    point = new StylusPointCollection(pointList);
                    stroke = new Stroke(point)
                    {
                        DrawingAttributes = inkCanvas.DefaultDrawingAttributes.Clone()
                    };
                    strokes.Add(stroke.Clone());
                    //底部椭圆
                    pointList = GenerateEllipseGeometry(new Point(newIniP.X, endP.Y - topB / 2), new Point(endP.X, endP.Y + topB / 2), false, true);
                    point = new StylusPointCollection(pointList);
                    stroke = new Stroke(point)
                    {
                        DrawingAttributes = inkCanvas.DefaultDrawingAttributes.Clone()
                    };
                    strokes.Add(stroke.Clone());
                    strokes.Add(GenerateDashedLineEllipseStrokeCollection(new Point(newIniP.X, endP.Y - topB / 2), new Point(endP.X, endP.Y + topB / 2), true, false));
                    //左侧
                    pointList = new List<System.Windows.Point>{
                        new System.Windows.Point(newIniP.X, newIniP.Y),
                        new System.Windows.Point(newIniP.X, endP.Y)
                    };
                    point = new StylusPointCollection(pointList);
                    stroke = new Stroke(point)
                    {
                        DrawingAttributes = inkCanvas.DefaultDrawingAttributes.Clone()
                    };
                    strokes.Add(stroke.Clone());
                    //右侧
                    pointList = new List<System.Windows.Point>{
                        new System.Windows.Point(endP.X, newIniP.Y),
                        new System.Windows.Point(endP.X, endP.Y)
                    };
                    point = new StylusPointCollection(pointList);
                    stroke = new Stroke(point)
                    {
                        DrawingAttributes = inkCanvas.DefaultDrawingAttributes.Clone()
                    };
                    strokes.Add(stroke.Clone());
                    try
                    {
                        inkCanvas.Strokes.Remove(lastTempStrokeCollection);
                    }
                    catch { }
                    lastTempStrokeCollection = strokes;
                    inkCanvas.Strokes.Add(strokes);

                    break;
                case 7:
                    _currentCommitType = CommitReason.ShapeDrawing;
                    if (iniP.Y > endP.Y)
                    {
                        newIniP = new Point(iniP.X, endP.Y);
                        endP = new Point(endP.X, iniP.Y);
                    }
                    double bottomA = Math.Abs(newIniP.X - endP.X);
                    double bottomB = bottomA / 2.646;
                    //底部椭圆
                    pointList = GenerateEllipseGeometry(new Point(newIniP.X, endP.Y - bottomB / 2), new Point(endP.X, endP.Y + bottomB / 2), false, true);
                    point = new StylusPointCollection(pointList);
                    stroke = new Stroke(point)
                    {
                        DrawingAttributes = inkCanvas.DefaultDrawingAttributes.Clone()
                    };
                    strokes.Add(stroke.Clone());
                    strokes.Add(GenerateDashedLineEllipseStrokeCollection(new Point(newIniP.X, endP.Y - bottomB / 2), new Point(endP.X, endP.Y + bottomB / 2), true, false));
                    //左侧
                    pointList = new List<System.Windows.Point>{
                        new System.Windows.Point((newIniP.X + endP.X) / 2, newIniP.Y),
                        new System.Windows.Point(newIniP.X, endP.Y)
                    };
                    point = new StylusPointCollection(pointList);
                    stroke = new Stroke(point)
                    {
                        DrawingAttributes = inkCanvas.DefaultDrawingAttributes.Clone()
                    };
                    strokes.Add(stroke.Clone());
                    //右侧
                    pointList = new List<System.Windows.Point>{
                        new System.Windows.Point((newIniP.X + endP.X) / 2, newIniP.Y),
                        new System.Windows.Point(endP.X, endP.Y)
                    };
                    point = new StylusPointCollection(pointList);
                    stroke = new Stroke(point)
                    {
                        DrawingAttributes = inkCanvas.DefaultDrawingAttributes.Clone()
                    };
                    strokes.Add(stroke.Clone());
                    try
                    {
                        inkCanvas.Strokes.Remove(lastTempStrokeCollection);
                    }
                    catch { }
                    lastTempStrokeCollection = strokes;
                    inkCanvas.Strokes.Add(strokes);

                    break;
                case 9:
                    _currentCommitType = CommitReason.ShapeDrawing;
                    if (isFirstTouchCuboid)
                    {
                        //分开画线条方便后期单独擦除某一条棱
                        strokes.Add(GenerateLineStroke(new Point(iniP.X, iniP.Y), new Point(iniP.X, endP.Y)));
                        strokes.Add(GenerateLineStroke(new Point(iniP.X, endP.Y), new Point(endP.X, endP.Y)));
                        strokes.Add(GenerateLineStroke(new Point(endP.X, endP.Y), new Point(endP.X, iniP.Y)));
                        strokes.Add(GenerateLineStroke(new Point(iniP.X, iniP.Y), new Point(endP.X, iniP.Y)));
                        try
                        {
                            inkCanvas.Strokes.Remove(lastTempStrokeCollection);
                        }
                        catch { }
                        lastTempStrokeCollection = strokes;
                        inkCanvas.Strokes.Add(strokes);
                        CuboidFrontRectIniP = iniP;
                        CuboidFrontRectEndP = endP;
                    }
                    else
                    {
                        d = CuboidFrontRectIniP.Y - endP.Y;
                        if (d < 0) d = -d; //就是懒不想做反向的，不要让我去做，想做自己做好之后 Pull Request
                        a = CuboidFrontRectEndP.X - CuboidFrontRectIniP.X; //正面矩形长
                        b = CuboidFrontRectEndP.Y - CuboidFrontRectIniP.Y; //正面矩形宽

                        //横上
                        Point newLineIniP = new Point(CuboidFrontRectIniP.X + d, CuboidFrontRectIniP.Y - d);
                        Point newLineEndP = new Point(CuboidFrontRectEndP.X + d, CuboidFrontRectIniP.Y - d);
                        pointList = new List<System.Windows.Point> { newLineIniP, newLineEndP };
                        point = new StylusPointCollection(pointList);
                        stroke = new Stroke(point) { DrawingAttributes = inkCanvas.DefaultDrawingAttributes.Clone() };
                        strokes.Add(stroke.Clone());
                        //横下 (虚线)
                        newLineIniP = new Point(CuboidFrontRectIniP.X + d, CuboidFrontRectEndP.Y - d);
                        newLineEndP = new Point(CuboidFrontRectEndP.X + d, CuboidFrontRectEndP.Y - d);
                        strokes.Add(GenerateDashedLineStrokeCollection(newLineIniP, newLineEndP));
                        //斜左上
                        newLineIniP = new Point(CuboidFrontRectIniP.X, CuboidFrontRectIniP.Y);
                        newLineEndP = new Point(CuboidFrontRectIniP.X + d, CuboidFrontRectIniP.Y - d);
                        pointList = new List<System.Windows.Point> { newLineIniP, newLineEndP };
                        point = new StylusPointCollection(pointList);
                        stroke = new Stroke(point) { DrawingAttributes = inkCanvas.DefaultDrawingAttributes.Clone() };
                        strokes.Add(stroke.Clone());
                        //斜右上
                        newLineIniP = new Point(CuboidFrontRectEndP.X, CuboidFrontRectIniP.Y);
                        newLineEndP = new Point(CuboidFrontRectEndP.X + d, CuboidFrontRectIniP.Y - d);
                        pointList = new List<System.Windows.Point> { newLineIniP, newLineEndP };
                        point = new StylusPointCollection(pointList);
                        stroke = new Stroke(point) { DrawingAttributes = inkCanvas.DefaultDrawingAttributes.Clone() };
                        strokes.Add(stroke.Clone());
                        //斜左下 (虚线)
                        newLineIniP = new Point(CuboidFrontRectIniP.X, CuboidFrontRectEndP.Y);
                        newLineEndP = new Point(CuboidFrontRectIniP.X + d, CuboidFrontRectEndP.Y - d);
                        strokes.Add(GenerateDashedLineStrokeCollection(newLineIniP, newLineEndP));
                        //斜右下
                        newLineIniP = new Point(CuboidFrontRectEndP.X, CuboidFrontRectEndP.Y);
                        newLineEndP = new Point(CuboidFrontRectEndP.X + d, CuboidFrontRectEndP.Y - d);
                        pointList = new List<System.Windows.Point> { newLineIniP, newLineEndP };
                        point = new StylusPointCollection(pointList);
                        stroke = new Stroke(point) { DrawingAttributes = inkCanvas.DefaultDrawingAttributes.Clone() };
                        strokes.Add(stroke.Clone());
                        //竖左 (虚线)
                        newLineIniP = new Point(CuboidFrontRectIniP.X + d, CuboidFrontRectIniP.Y - d);
                        newLineEndP = new Point(CuboidFrontRectIniP.X + d, CuboidFrontRectEndP.Y - d);
                        strokes.Add(GenerateDashedLineStrokeCollection(newLineIniP, newLineEndP));
                        //竖右
                        newLineIniP = new Point(CuboidFrontRectEndP.X + d, CuboidFrontRectIniP.Y - d);
                        newLineEndP = new Point(CuboidFrontRectEndP.X + d, CuboidFrontRectEndP.Y - d);
                        pointList = new List<System.Windows.Point> { newLineIniP, newLineEndP };
                        point = new StylusPointCollection(pointList);
                        stroke = new Stroke(point) { DrawingAttributes = inkCanvas.DefaultDrawingAttributes.Clone() };
                        strokes.Add(stroke.Clone());

                        try
                        {
                            inkCanvas.Strokes.Remove(lastTempStrokeCollection);
                        }
                        catch { }
                        lastTempStrokeCollection = strokes;
                        inkCanvas.Strokes.Add(strokes);
                    }

                    break;
            }
        }

        bool isFirstTouchCuboid = true;
        Point CuboidFrontRectIniP = new Point();
        Point CuboidFrontRectEndP = new Point();

        private void Main_Grid_TouchUp(object sender, TouchEventArgs e)
        {
            inkCanvas_MouseUp(sender, null);
            if (dec.Count == 0)
            {
                isWaitUntilNextTouchDown = false;
            }
        }
        Stroke lastTempStroke = null;
        StrokeCollection lastTempStrokeCollection = new StrokeCollection();
        bool isWaitUntilNextTouchDown = false;
        private List<System.Windows.Point> GenerateEllipseGeometry(System.Windows.Point st, System.Windows.Point ed, bool isDrawTop = true, bool isDrawBottom = true)
        {
            double a = 0.5 * (ed.X - st.X);
            double b = 0.5 * (ed.Y - st.Y);
            List<System.Windows.Point> pointList = new List<System.Windows.Point>();
            if (isDrawTop && isDrawBottom)
            {
                for (double r = 0; r <= 2 * Math.PI; r = r + 0.01)
                {
                    pointList.Add(new System.Windows.Point(0.5 * (st.X + ed.X) + a * Math.Cos(r), 0.5 * (st.Y + ed.Y) + b * Math.Sin(r)));
                }
            }
            else
            {
                if (isDrawBottom)
                {
                    for (double r = 0; r <= Math.PI; r = r + 0.01)
                    {
                        pointList.Add(new System.Windows.Point(0.5 * (st.X + ed.X) + a * Math.Cos(r), 0.5 * (st.Y + ed.Y) + b * Math.Sin(r)));
                    }
                }
                if (isDrawTop)
                {
                    for (double r = Math.PI; r <= 2 * Math.PI; r = r + 0.01)
                    {
                        pointList.Add(new System.Windows.Point(0.5 * (st.X + ed.X) + a * Math.Cos(r), 0.5 * (st.Y + ed.Y) + b * Math.Sin(r)));
                    }
                }
            }
            return pointList;
        }

        private StrokeCollection GenerateDashedLineEllipseStrokeCollection(System.Windows.Point st, System.Windows.Point ed, bool isDrawTop = true, bool isDrawBottom = true)
        {
            double a = 0.5 * (ed.X - st.X);
            double b = 0.5 * (ed.Y - st.Y);
            double step = 0.05;
            List<System.Windows.Point> pointList = new List<System.Windows.Point>();
            StylusPointCollection point;
            Stroke stroke;
            StrokeCollection strokes = new StrokeCollection();
            if (isDrawBottom)
            {
                for (double i = 0.0; i < 1.0; i += step * 1.66)
                {
                    pointList = new List<Point>();
                    for (double r = Math.PI * i; r <= Math.PI * (i + step); r = r + 0.01)
                    {
                        pointList.Add(new System.Windows.Point(0.5 * (st.X + ed.X) + a * Math.Cos(r), 0.5 * (st.Y + ed.Y) + b * Math.Sin(r)));
                    }
                    point = new StylusPointCollection(pointList);
                    stroke = new Stroke(point)
                    {
                        DrawingAttributes = inkCanvas.DefaultDrawingAttributes.Clone()
                    };
                    strokes.Add(stroke.Clone());
                }
            }
            if (isDrawTop)
            {
                for (double i = 1.0; i < 2.0; i += step * 1.66)
                {
                    pointList = new List<Point>();
                    for (double r = Math.PI * i; r <= Math.PI * (i + step); r = r + 0.01)
                    {
                        pointList.Add(new System.Windows.Point(0.5 * (st.X + ed.X) + a * Math.Cos(r), 0.5 * (st.Y + ed.Y) + b * Math.Sin(r)));
                    }
                    point = new StylusPointCollection(pointList);
                    stroke = new Stroke(point)
                    {
                        DrawingAttributes = inkCanvas.DefaultDrawingAttributes.Clone()
                    };
                    strokes.Add(stroke.Clone());
                }
            }
            return strokes;
        }

        private Stroke GenerateLineStroke(System.Windows.Point st, System.Windows.Point ed)
        {
            List<System.Windows.Point> pointList = new List<System.Windows.Point>();
            StylusPointCollection point;
            Stroke stroke;
            pointList = new List<System.Windows.Point>{
                new System.Windows.Point(st.X, st.Y),
                new System.Windows.Point(ed.X, ed.Y)
            };
            point = new StylusPointCollection(pointList);
            stroke = new Stroke(point)
            {
                DrawingAttributes = inkCanvas.DefaultDrawingAttributes.Clone()
            };
            return stroke;
        }

        private Stroke GenerateArrowLineStroke(System.Windows.Point st, System.Windows.Point ed)
        {
            List<System.Windows.Point> pointList = new List<System.Windows.Point>();
            StylusPointCollection point;
            Stroke stroke;

            double w = 20, h = 7;
            double theta = Math.Atan2(st.Y - ed.Y, st.X - ed.X);
            double sint = Math.Sin(theta);
            double cost = Math.Cos(theta);

            pointList = new List<Point>
            {
                new Point(st.X, st.Y),
                new Point(ed.X , ed.Y),
                new Point(ed.X + (w * cost - h * sint), ed.Y + (w * sint + h * cost)),
                new Point(ed.X,ed.Y),
                new Point(ed.X + (w * cost + h * sint), ed.Y - (h * cost - w * sint))
            };
            point = new StylusPointCollection(pointList);
            stroke = new Stroke(point)
            {
                DrawingAttributes = inkCanvas.DefaultDrawingAttributes.Clone()
            };
            return stroke;
        }

        private StrokeCollection GenerateDashedLineStrokeCollection(System.Windows.Point st, System.Windows.Point ed)
        {
            double step = 5;
            List<System.Windows.Point> pointList = new List<System.Windows.Point>();
            StylusPointCollection point;
            Stroke stroke;
            StrokeCollection strokes = new StrokeCollection();
            double d = GetDistance(st, ed);
            double sinTheta = (ed.Y - st.Y) / d;
            double cosTheta = (ed.X - st.X) / d;
            for (double i = 0.0; i < d; i += step * 2.76)
            {
                pointList = new List<System.Windows.Point>{
                    new System.Windows.Point(st.X + i * cosTheta, st.Y + i * sinTheta),
                    new System.Windows.Point(st.X + Math.Min(i + step, d) * cosTheta, st.Y + Math.Min(i + step, d) * sinTheta)
                };
                point = new StylusPointCollection(pointList);
                stroke = new Stroke(point)
                {
                    DrawingAttributes = inkCanvas.DefaultDrawingAttributes.Clone()
                };
                strokes.Add(stroke.Clone());
            }
            return strokes;
        }

        private StrokeCollection GenerateDotLineStrokeCollection(System.Windows.Point st, System.Windows.Point ed)
        {
            double step = 3;
            List<System.Windows.Point> pointList = new List<System.Windows.Point>();
            StylusPointCollection point;
            Stroke stroke;
            StrokeCollection strokes = new StrokeCollection();
            double d = GetDistance(st, ed);
            double sinTheta = (ed.Y - st.Y) / d;
            double cosTheta = (ed.X - st.X) / d;
            for (double i = 0.0; i < d; i += step * 2.76)
            {
                var stylusPoint = new StylusPoint(st.X + i * cosTheta, st.Y + i * sinTheta, (float)0.8);
                point = new StylusPointCollection();
                point.Add(stylusPoint);
                stroke = new Stroke(point)
                {
                    DrawingAttributes = inkCanvas.DefaultDrawingAttributes.Clone()
                };
                strokes.Add(stroke.Clone());
            }
            return strokes;
        }

        bool isMouseDown = false;
        private void inkCanvas_MouseDown(object sender, MouseButtonEventArgs e)
        {
            isMouseDown = true;
            if (NeedUpdateIniP())
            {
                iniP = e.GetPosition(inkCanvas);
            }
        }

        private void inkCanvas_MouseMove(object sender, MouseEventArgs e)
        {
            if (isMouseDown)
            {
                MouseTouchMove(e.GetPosition(inkCanvas));
            }
        }

        private void inkCanvas_MouseUp(object sender, MouseButtonEventArgs e)
        {
            if (drawingShapeMode == 5)
            {
                Circle circle = new Circle(new Point(), 0, lastTempStroke);
                circle.R = GetDistance(circle.Stroke.StylusPoints[0].ToPoint(), circle.Stroke.StylusPoints[circle.Stroke.StylusPoints.Count / 2].ToPoint()) / 2;
                circle.Centroid = new Point((circle.Stroke.StylusPoints[0].X + circle.Stroke.StylusPoints[circle.Stroke.StylusPoints.Count / 2].X) / 2,
                                            (circle.Stroke.StylusPoints[0].Y + circle.Stroke.StylusPoints[circle.Stroke.StylusPoints.Count / 2].Y) / 2);
                circles.Add(circle);
                if (lastIsInMultiTouchMode)
                {
                    ToggleSwitchEnableMultiTouchMode.IsOn = true;
                    lastIsInMultiTouchMode = false;
                }
            }
            if (drawingShapeMode != 9 && drawingShapeMode != 0 && drawingShapeMode != 24 && drawingShapeMode != 25)
            {
                if (isLongPressSelected)
                {

                }
                else
                {
                    BtnPen_Click(null, null); //画完一次还原到笔模式

                    if (lastIsInMultiTouchMode)
                    {
                        ToggleSwitchEnableMultiTouchMode.IsOn = true;
                        lastIsInMultiTouchMode = false;
                    }
                }
            }
            if (drawingShapeMode == 9)
            {
                if (isFirstTouchCuboid)
                {
                    if (CuboidStrokeCollection == null) CuboidStrokeCollection = new StrokeCollection();
                    isFirstTouchCuboid = false;
                    Point newIniP = new Point(Math.Min(CuboidFrontRectIniP.X, CuboidFrontRectEndP.X), Math.Min(CuboidFrontRectIniP.Y, CuboidFrontRectEndP.Y));
                    Point newEndP = new Point(Math.Max(CuboidFrontRectIniP.X, CuboidFrontRectEndP.X), Math.Max(CuboidFrontRectIniP.Y, CuboidFrontRectEndP.Y));
                    CuboidFrontRectIniP = newIniP;
                    CuboidFrontRectEndP = newEndP;
                    CuboidStrokeCollection.Add(lastTempStrokeCollection);
                }
                else
                {
                    BtnPen_Click(null, null); //画完还原到笔模式

                    if (lastIsInMultiTouchMode)
                    {
                        ToggleSwitchEnableMultiTouchMode.IsOn = true;
                        lastIsInMultiTouchMode = false;
                    }

                    if (_currentCommitType == CommitReason.ShapeDrawing)
                    {
                        CuboidStrokeCollection.Add(lastTempStrokeCollection);
                        _currentCommitType = CommitReason.UserInput;
                        timeMachine.CommitStrokeUserInputHistory(CuboidStrokeCollection);
                        CuboidStrokeCollection = null;
                    }
                }
            }
            if (drawingShapeMode == 24 || drawingShapeMode == 25)
            {
                if (drawMultiStepShapeCurrentStep == 0)
                {
                    drawMultiStepShapeCurrentStep = 1;
                }
                else
                {
                    drawMultiStepShapeCurrentStep = 0;
                    if (drawMultiStepShapeSpecialStrokeCollection != null)
                    {
                        bool opFlag = false;
                        switch (Settings.Canvas.HyperbolaAsymptoteOption)
                        {
                            case OptionalOperation.Yes:
                                opFlag = true;
                                break;
                            case OptionalOperation.No:
                                opFlag = false;
                                break;
                            case OptionalOperation.Ask:
                                opFlag = MessageBox.Show("是否移除渐近线？", "Ink Canvas", MessageBoxButton.YesNo) != MessageBoxResult.Yes;
                                break;
                        };
                        if (!opFlag)
                        {
                            inkCanvas.Strokes.Remove(drawMultiStepShapeSpecialStrokeCollection);
                        }
                    }
                    BtnPen_Click(null, null); //画完还原到笔模式

                    if (lastIsInMultiTouchMode)
                    {
                        ToggleSwitchEnableMultiTouchMode.IsOn = true;
                        lastIsInMultiTouchMode = false;
                    }

                }
            }
            isMouseDown = false;
            if (ReplacedStroke != null || AddedStroke != null)
            {
                timeMachine.CommitStrokeEraseHistory(ReplacedStroke, AddedStroke);
                AddedStroke = null;
                ReplacedStroke = null;
            }
            if (_currentCommitType == CommitReason.ShapeDrawing && drawingShapeMode != 9)
            {
                _currentCommitType = CommitReason.UserInput;
                StrokeCollection collection = null;
                if (lastTempStrokeCollection != null && lastTempStrokeCollection.Count > 0)
                {
                    collection = lastTempStrokeCollection;
                }
                else if (lastTempStroke != null)
                {
                    collection = new StrokeCollection() { lastTempStroke };
                }
                if (collection != null)
                {
                    timeMachine.CommitStrokeUserInputHistory(collection);
                }
            }
            lastTempStroke = null;
            lastTempStrokeCollection = null;
            if (StrokeManipulationHistory?.Count > 0 || ElementsManipulationHistory?.Count > 0)
            {
                timeMachine.CommitStrokeManipulationHistory(StrokeManipulationHistory, ElementsManipulationHistory);
                if(StrokeManipulationHistory?.Count > 0)
                {
                    foreach (var item in StrokeManipulationHistory)
                    {
                        StrokeInitialHistory[item.Key] = item.Value.Item2;
                    }
                    StrokeManipulationHistory = null;
                }
                if (ElementsManipulationHistory?.Count > 0)
                {
                    foreach (var item in ElementsManipulationHistory)
                    {
                        ElementsInitialHistory[item.Key] = item.Value.Item2;
                    }
                    ElementsManipulationHistory = null;
                }
            }
            if (DrawingAttributesHistory.Count > 0)
            {
                timeMachine.CommitStrokeDrawingAttributesHistory(DrawingAttributesHistory);
                DrawingAttributesHistory = new Dictionary<Stroke, Tuple<DrawingAttributes, DrawingAttributes>>();
                foreach (var item in DrawingAttributesHistoryFlag)
                {
                    item.Value.Clear();
                }
            }
        }

        private bool NeedUpdateIniP()
        {
            if (drawingShapeMode == 24 || drawingShapeMode == 25)
            {
                if (drawMultiStepShapeCurrentStep == 1) return false;
            }
            return true;
        }
    }
}
