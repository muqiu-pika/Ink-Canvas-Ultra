using Ink_Canvas.Helpers;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Ink;
using System.Windows.Input;
using System.Windows.Media;
using Point = System.Windows.Point;

namespace Ink_Canvas
{
    public partial class MainWindow : Window
    {
        StrokeCollection newStrokes = new StrokeCollection();
        List<Circle> circles = new List<Circle>();
        const double LINE_STRAIGHTEN_THRESHOLD = 0.20;

        List<RectangleGuideLine> rectangleGuideLines = new List<RectangleGuideLine>();
        const double RECTANGLE_ENDPOINT_THRESHOLD = 30.0;
        const double RECTANGLE_ANGLE_THRESHOLD = 15.0;

        class RectangleGuideLine
        {
            public Stroke OriginalStroke { get; set; }
            public Point StartPoint { get; set; }
            public Point EndPoint { get; set; }
            public DateTime CreatedTime { get; set; }
            public double Angle { get; set; }
            public bool IsHorizontal { get; set; }
            public bool IsVertical { get; set; }

            public RectangleGuideLine(Stroke stroke, Point start, Point end)
            {
                OriginalStroke = stroke;
                StartPoint = start;
                EndPoint = end;
                CreatedTime = DateTime.Now;

                double deltaX = end.X - start.X;
                double deltaY = end.Y - start.Y;
                Angle = Math.Atan2(deltaY, deltaX);

                double angleDegrees = Math.Abs(Angle * 180.0 / Math.PI);
                double angleThreshold = Settings.InkToShape.RectangleAngleThreshold;
                IsHorizontal = angleDegrees < angleThreshold || angleDegrees > (180 - angleThreshold);
                IsVertical = Math.Abs(angleDegrees - 90) < angleThreshold;
            }
        }

        //此函数中的所有代码版权所有 WXRIW，在其他项目中使用前必须提前联系（wxriw@outlook.com），谢谢！
        private void inkCanvas_StrokeCollected(object sender, InkCanvasStrokeCollectedEventArgs e)
        {
            try
            {
                inkCanvas.Opacity = 1;
                if (Settings.InkToShape.IsInkToShapeEnabled && !Environment.Is64BitProcess)
                {
                    void InkToShapeProcess()
                    {
                        try
                        {
                            var currentStroke = e.Stroke;
                            if (Settings.Canvas.AutoStraightenLine && IsPotentialStraightLine(currentStroke))
                            {
                                Point startPoint = currentStroke.StylusPoints[0].ToPoint();
                                Point endPoint = currentStroke.StylusPoints[currentStroke.StylusPoints.Count - 1].ToPoint();

                                bool shouldStraighten = ShouldStraightenLine(currentStroke);
                                if (shouldStraighten && Settings.Canvas.LineEndpointSnapping && (Settings.InkToShape.IsInkToShapeRectangle || Settings.InkToShape.IsInkToShapeTriangle))
                                {
                                    var snapped = GetSnappedEndpoints(startPoint, endPoint);
                                    if (snapped != null)
                                    {
                                        startPoint = snapped[0];
                                        endPoint = snapped[1];
                                    }
                                }

                                if (shouldStraighten)
                                {
                                    StylusPointCollection straightLinePoints = CreateStraightLine(startPoint, endPoint);
                                    var straightStroke = new Stroke(straightLinePoints)
                                    {
                                        DrawingAttributes = inkCanvas.DefaultDrawingAttributes.Clone()
                                    };
                                    SetNewBackupOfStroke();
                                    _currentCommitType = CommitReason.ShapeRecognition;
                                    inkCanvas.Strokes.Remove(currentStroke);
                                    inkCanvas.Strokes.Add(straightStroke);
                                    _currentCommitType = CommitReason.UserInput;
                                    if (newStrokes.Contains(currentStroke))
                                    {
                                        newStrokes.Remove(currentStroke);
                                        newStrokes.Add(straightStroke);
                                    }
                                    currentStroke = straightStroke;
                                }
                            }

                            newStrokes.Add(currentStroke);
                            if (newStrokes.Count > 4) newStrokes.RemoveAt(0);
                            for (int i = 0; i < newStrokes.Count; i++)
                            {
                                if (!inkCanvas.Strokes.Contains(newStrokes[i])) newStrokes.RemoveAt(i--);
                            }
                            for (int i = 0; i < circles.Count; i++)
                            {
                                if (!inkCanvas.Strokes.Contains(circles[i].Stroke)) {
                                    // 自动修复：若该圆的Stroke点数过少（如小于3），则判定为异常直线，移除
                                    if (circles[i].Stroke.StylusPoints.Count < 3) {
                                        // 新增：从画布中彻底移除异常直线
                                        inkCanvas.Strokes.Remove(circles[i].Stroke);
                                        circles.RemoveAt(i);
                                        i--;
                                        continue;
                                    }
                                    circles.RemoveAt(i);
                                    i--;
                                }
                            }
                            ProcessRectangleGuideLines(currentStroke);
                            var strokeReco = new StrokeCollection();
                            var result = InkRecognizeHelper.RecognizeShape(newStrokes);
                            // 修正：防止result为null或InkDrawingNode为null时抛出异常
                            for (int i = newStrokes.Count - 1; i >= 0; i--)
                            {
                                strokeReco.Add(newStrokes[i]);
                                var newResult = InkRecognizeHelper.RecognizeShape(strokeReco);
                                if (newResult != null && newResult.InkDrawingNode != null &&
                                    (newResult.InkDrawingNode.GetShapeName() == "Circle" || newResult.InkDrawingNode.GetShapeName() == "Ellipse"))
                                {
                                    result = newResult;
                                    break;
                                }
                            }
                            // 修正：防止result为null或InkDrawingNode为null时抛出异常
                            if (result != null && result.InkDrawingNode != null && result.InkDrawingNode.GetShapeName() == "Circle")
                            {
                                var shape = result.InkDrawingNode.GetShape();
                                if (shape.Width > 75)
                                {
                                    foreach (Circle circle in circles)
                                    {
                                        //判断是否画同心圆
                                        if (Math.Abs(result.Centroid.X - circle.Centroid.X) / shape.Width < 0.12 &&
                                            Math.Abs(result.Centroid.Y - circle.Centroid.Y) / shape.Width < 0.12)
                                        {
                                            result.Centroid = circle.Centroid;
                                            break;
                                        }
                                        else
                                        {
                                            double d = (result.Centroid.X - circle.Centroid.X) * (result.Centroid.X - circle.Centroid.X) +
                                               (result.Centroid.Y - circle.Centroid.Y) * (result.Centroid.Y - circle.Centroid.Y);
                                            d = Math.Sqrt(d);
                                            //判断是否画外切圆
                                            double x = shape.Width / 2.0 + circle.R - d;
                                            if (Math.Abs(x) / shape.Width < 0.1)
                                            {
                                                double sinTheta = (result.Centroid.Y - circle.Centroid.Y) / d;
                                                double cosTheta = (result.Centroid.X - circle.Centroid.X) / d;
                                                double newX = result.Centroid.X + x * cosTheta;
                                                double newY = result.Centroid.Y + x * sinTheta;
                                                result.Centroid = new Point(newX, newY);
                                            }
                                            //判断是否画外切圆
                                            x = Math.Abs(circle.R - shape.Width / 2.0) - d;
                                            if (Math.Abs(x) / shape.Width < 0.1)
                                            {
                                                double sinTheta = (result.Centroid.Y - circle.Centroid.Y) / d;
                                                double cosTheta = (result.Centroid.X - circle.Centroid.X) / d;
                                                double newX = result.Centroid.X + x * cosTheta;
                                                double newY = result.Centroid.Y + x * sinTheta;
                                                result.Centroid = new Point(newX, newY);
                                            }
                                        }
                                    }

                                    Point iniP = new Point(result.Centroid.X - shape.Width / 2, result.Centroid.Y - shape.Height / 2);
                                    Point endP = new Point(result.Centroid.X + shape.Width / 2, result.Centroid.Y + shape.Height / 2);
                                    var pointList = GenerateEllipseGeometry(iniP, endP);
                                    var point = new StylusPointCollection(pointList);
                                    var stroke = new Stroke(point)
                                    {
                                        DrawingAttributes = inkCanvas.DefaultDrawingAttributes.Clone()
                                    };
                                    circles.Add(new Circle(result.Centroid, shape.Width / 2.0, stroke));
                                    SetNewBackupOfStroke();
                                    _currentCommitType = CommitReason.ShapeRecognition;
                                    inkCanvas.Strokes.Remove(result.InkDrawingNode.Strokes);
                                    inkCanvas.Strokes.Add(stroke);
                                    _currentCommitType = CommitReason.UserInput;
                                    newStrokes = new StrokeCollection();
                                }
                            }
                            else if (result != null && result.InkDrawingNode != null && result.InkDrawingNode.GetShapeName().Contains("Ellipse"))
                            {
                                var shape = result.InkDrawingNode.GetShape();
                                //var shape1 = result.InkDrawingNode.GetShape();
                                //shape1.Fill = Brushes.Gray;
                                //Canvas.Children.Add(shape1);
                                var p = result.InkDrawingNode.HotPoints;
                                double a = GetDistance(p[0], p[2]) / 2; //长半轴
                                double b = GetDistance(p[1], p[3]) / 2; //短半轴
                                if (a < b)
                                {
                                    double t = a;
                                    a = b;
                                    b = t;
                                }

                                result.Centroid = new Point((p[0].X + p[2].X) / 2, (p[0].Y + p[2].Y) / 2);
                                bool needRotation = true;

                                if (shape.Width > 75 || shape.Height > 75 && p.Count == 4)
                                {
                                    Point iniP = new Point(result.Centroid.X - shape.Width / 2, result.Centroid.Y - shape.Height / 2);
                                    Point endP = new Point(result.Centroid.X + shape.Width / 2, result.Centroid.Y + shape.Height / 2);

                                    foreach (Circle circle in circles)
                                    {
                                        //判断是否画同心椭圆
                                        if (Math.Abs(result.Centroid.X - circle.Centroid.X) / a < 0.2 &&
                                            Math.Abs(result.Centroid.Y - circle.Centroid.Y) / a < 0.2)
                                        {
                                            result.Centroid = circle.Centroid;
                                            iniP = new Point(result.Centroid.X - shape.Width / 2, result.Centroid.Y - shape.Height / 2);
                                            endP = new Point(result.Centroid.X + shape.Width / 2, result.Centroid.Y + shape.Height / 2);

                                            //再判断是否与圆相切
                                            if (Math.Abs(a - circle.R) / a < 0.2)
                                            {
                                                if (shape.Width >= shape.Height)
                                                {
                                                    iniP.X = result.Centroid.X - circle.R;
                                                    endP.X = result.Centroid.X + circle.R;
                                                    iniP.Y = result.Centroid.Y - b;
                                                    endP.Y = result.Centroid.Y + b;
                                                }
                                                else
                                                {
                                                    iniP.Y = result.Centroid.Y - circle.R;
                                                    endP.Y = result.Centroid.Y + circle.R;
                                                    iniP.X = result.Centroid.X - a;
                                                    endP.X = result.Centroid.X + a;
                                                }
                                            }
                                            break;
                                        }
                                        else if (Math.Abs(result.Centroid.X - circle.Centroid.X) / a < 0.2)
                                        {
                                            double sinTheta = Math.Abs(circle.Centroid.Y - result.Centroid.Y) / circle.R;
                                            double cosTheta = Math.Sqrt(1 - sinTheta * sinTheta);
                                            double newA = circle.R * cosTheta;
                                            if (circle.R * sinTheta / circle.R < 0.9 && a / b > 2 && Math.Abs(newA - a) / newA < 0.3)
                                            {
                                                iniP.X = circle.Centroid.X - newA;
                                                endP.X = circle.Centroid.X + newA;
                                                iniP.Y = result.Centroid.Y - newA / 5;
                                                endP.Y = result.Centroid.Y + newA / 5;

                                                double topB = endP.Y - iniP.Y;

                                                SetNewBackupOfStroke();
                                                _currentCommitType = CommitReason.ShapeRecognition;
                                                inkCanvas.Strokes.Remove(result.InkDrawingNode.Strokes);
                                                newStrokes = new StrokeCollection();

                                                var _pointList = GenerateEllipseGeometry(iniP, endP, false, true);
                                                var _point = new StylusPointCollection(_pointList);
                                                var _stroke = new Stroke(_point)
                                                {
                                                    DrawingAttributes = inkCanvas.DefaultDrawingAttributes.Clone()
                                                };
                                                var _dashedLineStroke = GenerateDashedLineEllipseStrokeCollection(iniP, endP, true, false);
                                                StrokeCollection strokes = new StrokeCollection()
                                                {
                                                    _stroke,
                                                    _dashedLineStroke
                                                };
                                                inkCanvas.Strokes.Add(strokes);
                                                _currentCommitType = CommitReason.UserInput;
                                                return;
                                            }
                                        }
                                        else if (Math.Abs(result.Centroid.Y - circle.Centroid.Y) / a < 0.2)
                                        {
                                            double cosTheta = Math.Abs(circle.Centroid.X - result.Centroid.X) / circle.R;
                                            double sinTheta = Math.Sqrt(1 - cosTheta * cosTheta);
                                            double newA = circle.R * sinTheta;
                                            if (circle.R * sinTheta / circle.R < 0.9 && a / b > 2 && Math.Abs(newA - a) / newA < 0.3)
                                            {
                                                iniP.X = result.Centroid.X - newA / 5;
                                                endP.X = result.Centroid.X + newA / 5;
                                                iniP.Y = circle.Centroid.Y - newA;
                                                endP.Y = circle.Centroid.Y + newA;
                                                needRotation = false;
                                            }
                                        }
                                    }

                                    //纠正垂直与水平关系
                                    var newPoints = FixPointsDirection(p[0], p[2]);
                                    p[0] = newPoints[0];
                                    p[2] = newPoints[1];
                                    newPoints = FixPointsDirection(p[1], p[3]);
                                    p[1] = newPoints[0];
                                    p[3] = newPoints[1];

                                    var pointList = GenerateEllipseGeometry(iniP, endP);
                                    var point = new StylusPointCollection(pointList);
                                    var stroke = new Stroke(point)
                                    {
                                        DrawingAttributes = inkCanvas.DefaultDrawingAttributes.Clone()
                                    };

                                    if (needRotation)
                                    {
                                        Matrix m = new Matrix();
                                        FrameworkElement fe = e.Source as FrameworkElement;
                                        double tanTheta = (p[2].Y - p[0].Y) / (p[2].X - p[0].X);
                                        double theta = Math.Atan(tanTheta);
                                        m.RotateAt(theta * 180.0 / Math.PI, result.Centroid.X, result.Centroid.Y);
                                        stroke.Transform(m, false);
                                    }

                                    SetNewBackupOfStroke();
                                    _currentCommitType = CommitReason.ShapeRecognition;
                                    inkCanvas.Strokes.Remove(result.InkDrawingNode.Strokes);
                                    inkCanvas.Strokes.Add(stroke);
                                    _currentCommitType = CommitReason.UserInput;
                                    GridInkCanvasSelectionCover.Visibility = Visibility.Collapsed;
                                    newStrokes = new StrokeCollection();
                                }
                            }
                            else if (result != null && result.InkDrawingNode != null && result.InkDrawingNode.GetShapeName().Contains("Triangle") && Settings.InkToShape.IsInkToShapeTriangle)
                            {
                                var shape = result.InkDrawingNode.GetShape();
                                var p = result.InkDrawingNode.HotPoints;
                                if ((Math.Max(Math.Max(p[0].X, p[1].X), p[2].X) - Math.Min(Math.Min(p[0].X, p[1].X), p[2].X) >= 100 ||
                                    Math.Max(Math.Max(p[0].Y, p[1].Y), p[2].Y) - Math.Min(Math.Min(p[0].Y, p[1].Y), p[2].Y) >= 100) && result.InkDrawingNode.HotPoints.Count == 3)
                                {
                                    //纠正垂直与水平关系
                                    var newPoints = FixPointsDirection(p[0], p[1]);
                                    p[0] = newPoints[0];
                                    p[1] = newPoints[1];
                                    newPoints = FixPointsDirection(p[0], p[2]);
                                    p[0] = newPoints[0];
                                    p[2] = newPoints[1];
                                    newPoints = FixPointsDirection(p[1], p[2]);
                                    p[1] = newPoints[0];
                                    p[2] = newPoints[1];

                                    var pointList = p.ToList();
                                    //pointList.Add(p[0]);
                                    var point = new StylusPointCollection(pointList);
                                    var stroke = new Stroke(GenerateFakePressureTriangle(point))
                                    {
                                        DrawingAttributes = inkCanvas.DefaultDrawingAttributes.Clone()
                                    };
                                    SetNewBackupOfStroke();
                                    _currentCommitType = CommitReason.ShapeRecognition;
                                    inkCanvas.Strokes.Remove(result.InkDrawingNode.Strokes);
                                    inkCanvas.Strokes.Add(stroke);
                                    _currentCommitType = CommitReason.UserInput;
                                    GridInkCanvasSelectionCover.Visibility = Visibility.Collapsed;
                                    newStrokes = new StrokeCollection();
                                }
                            }
                            else if (result != null && result.InkDrawingNode != null &&
                                (result.InkDrawingNode.GetShapeName().Contains("Rectangle") ||
                                 result.InkDrawingNode.GetShapeName().Contains("Diamond") ||
                                 result.InkDrawingNode.GetShapeName().Contains("Parallelogram") ||
                                 result.InkDrawingNode.GetShapeName().Contains("Square") ||
                                 result.InkDrawingNode.GetShapeName().Contains("Trapezoid")) &&
                                Settings.InkToShape.IsInkToShapeRectangle)
                            {
                                var shape = result.InkDrawingNode.GetShape();
                                var p = result.InkDrawingNode.HotPoints;
                                if ((Math.Max(Math.Max(Math.Max(p[0].X, p[1].X), p[2].X), p[3].X) - Math.Min(Math.Min(Math.Min(p[0].X, p[1].X), p[2].X), p[3].X) >= 100 ||
                                    Math.Max(Math.Max(Math.Max(p[0].Y, p[1].Y), p[2].Y), p[3].Y) - Math.Min(Math.Min(Math.Min(p[0].Y, p[1].Y), p[2].Y), p[3].Y) >= 100) && result.InkDrawingNode.HotPoints.Count == 4)
                                {
                                    //纠正垂直与水平关系
                                    var newPoints = FixPointsDirection(p[0], p[1]);
                                    p[0] = newPoints[0];
                                    p[1] = newPoints[1];
                                    newPoints = FixPointsDirection(p[1], p[2]);
                                    p[1] = newPoints[0];
                                    p[2] = newPoints[1];
                                    newPoints = FixPointsDirection(p[2], p[3]);
                                    p[2] = newPoints[0];
                                    p[3] = newPoints[1];
                                    newPoints = FixPointsDirection(p[3], p[0]);
                                    p[3] = newPoints[0];
                                    p[0] = newPoints[1];

                                    var pointList = p.ToList();
                                    pointList.Add(p[0]);
                                    var point = new StylusPointCollection(pointList);
                                    var stroke = new Stroke(GenerateFakePressureRectangle(point))
                                    {
                                        DrawingAttributes = inkCanvas.DefaultDrawingAttributes.Clone()
                                    };
                                    SetNewBackupOfStroke();
                                    _currentCommitType = CommitReason.ShapeRecognition;
                                    inkCanvas.Strokes.Remove(result.InkDrawingNode.Strokes);
                                    inkCanvas.Strokes.Add(stroke);
                                    _currentCommitType = CommitReason.UserInput;
                                    GridInkCanvasSelectionCover.Visibility = Visibility.Collapsed;
                                    newStrokes = new StrokeCollection();
                                }
                            }
                        }
                        catch { }
                    }
                    InkToShapeProcess();
                }


                foreach (StylusPoint stylusPoint in e.Stroke.StylusPoints)
                {
                    //LogHelper.WriteLogToFile(stylusPoint.PressureFactor.ToString(), LogHelper.LogType.Info);
                    // 检查是否是压感笔书写
                    //if (stylusPoint.PressureFactor != 0.5 && stylusPoint.PressureFactor != 0)
                    if ((stylusPoint.PressureFactor > 0.501 || stylusPoint.PressureFactor < 0.5) && stylusPoint.PressureFactor != 0)
                    {
                        return;
                    }
                    if (inkColor > 100)
                    { // 荧光笔功能
                        return;
                    }
                }


                try
                {
                    if (e.Stroke.StylusPoints.Count > 3)
                    {
                        Random random = new Random();
                        double _speed = GetPointSpeed(e.Stroke.StylusPoints[random.Next(0, e.Stroke.StylusPoints.Count - 1)].ToPoint(), e.Stroke.StylusPoints[random.Next(0, e.Stroke.StylusPoints.Count - 1)].ToPoint(), e.Stroke.StylusPoints[random.Next(0, e.Stroke.StylusPoints.Count - 1)].ToPoint());

                        RandWindow.randSeed = (int)(_speed * 100000 * 1000);
                    }
                }
                catch { }

                switch (Settings.Canvas.InkStyle)
                {
                    case 1:
                        try
                        {
                            StylusPointCollection stylusPoints = new StylusPointCollection();
                            int n = e.Stroke.StylusPoints.Count - 1;
                            string s = "";

                            for (int i = 0; i <= n; i++)
                            {
                                double speed = GetPointSpeed(e.Stroke.StylusPoints[Math.Max(i - 1, 0)].ToPoint(), e.Stroke.StylusPoints[i].ToPoint(), e.Stroke.StylusPoints[Math.Min(i + 1, n)].ToPoint());
                                s += speed.ToString() + "\t";
                                StylusPoint point = new StylusPoint();
                                if (speed >= 0.25)
                                {
                                    point.PressureFactor = (float)(0.5 - 0.3 * (Math.Min(speed, 1.5) - 0.3) / 1.2);
                                }
                                else if (speed >= 0.05)
                                {
                                    point.PressureFactor = (float)0.5;
                                }
                                else
                                {
                                    point.PressureFactor = (float)(0.5 + 0.4 * (0.05 - speed) / 0.05);
                                }
                                point.X = e.Stroke.StylusPoints[i].X;
                                point.Y = e.Stroke.StylusPoints[i].Y;
                                stylusPoints.Add(point);
                            }
                            e.Stroke.StylusPoints = stylusPoints;
                        }
                        catch
                        {

                        }
                        break;
                    case 0:
                        try
                        {
                            StylusPointCollection stylusPoints = new StylusPointCollection();
                            int n = e.Stroke.StylusPoints.Count - 1;
                            double pressure = 0.1;
                            int x = 10;
                            if (n == 1) return;
                            if (n >= x)
                            {
                                for (int i = 0; i < n - x; i++)
                                {
                                    StylusPoint point = new StylusPoint();

                                    point.PressureFactor = (float)0.5;
                                    point.X = e.Stroke.StylusPoints[i].X;
                                    point.Y = e.Stroke.StylusPoints[i].Y;
                                    stylusPoints.Add(point);
                                }
                                for (int i = n - x; i <= n; i++)
                                {
                                    StylusPoint point = new StylusPoint();

                                    point.PressureFactor = (float)((0.5 - pressure) * (n - i) / x + pressure);
                                    point.X = e.Stroke.StylusPoints[i].X;
                                    point.Y = e.Stroke.StylusPoints[i].Y;
                                    stylusPoints.Add(point);
                                }
                            }
                            else
                            {
                                for (int i = 0; i <= n; i++)
                                {
                                    StylusPoint point = new StylusPoint();

                                    point.PressureFactor = (float)(0.4 * (n - i) / n + pressure);
                                    point.X = e.Stroke.StylusPoints[i].X;
                                    point.Y = e.Stroke.StylusPoints[i].Y;
                                    stylusPoints.Add(point);
                                }
                            }
                            e.Stroke.StylusPoints = stylusPoints;
                        }
                        catch
                        {

                        }
                        break;
                    case 3: //根据 mode == 0 改写，目前暂未完成
                        try
                        {
                            StylusPointCollection stylusPoints = new StylusPointCollection();
                            int n = e.Stroke.StylusPoints.Count - 1;
                            double pressure = 0.1;
                            int x = 8;
                            if (lastTouchDownTime < lastTouchUpTime)
                            {
                                double k = (lastTouchUpTime - lastTouchDownTime) / (n + 1); // 每个点之间间隔 k 毫秒
                                x = (int)(1000 / k); // 取 1000 ms 内的点
                            }

                            if (n >= x)
                            {
                                for (int i = 0; i < n - x; i++)
                                {
                                    StylusPoint point = new StylusPoint();

                                    point.PressureFactor = (float)0.5;
                                    point.X = e.Stroke.StylusPoints[i].X;
                                    point.Y = e.Stroke.StylusPoints[i].Y;
                                    stylusPoints.Add(point);
                                }
                                for (int i = n - x; i <= n; i++)
                                {
                                    StylusPoint point = new StylusPoint();

                                    point.PressureFactor = (float)((0.5 - pressure) * (n - i) / x + pressure);
                                    point.X = e.Stroke.StylusPoints[i].X;
                                    point.Y = e.Stroke.StylusPoints[i].Y;
                                    stylusPoints.Add(point);
                                }
                            }
                            else
                            {
                                for (int i = 0; i <= n; i++)
                                {
                                    StylusPoint point = new StylusPoint();

                                    point.PressureFactor = (float)(0.4 * (n - i) / n + pressure);
                                    point.X = e.Stroke.StylusPoints[i].X;
                                    point.Y = e.Stroke.StylusPoints[i].Y;
                                    stylusPoints.Add(point);
                                }
                            }
                            e.Stroke.StylusPoints = stylusPoints;
                        }
                        catch
                        {

                        }
                        break;
                }
            }
            catch { }
        }

        private void SetNewBackupOfStroke()
        {
            lastTouchDownStrokeCollection = inkCanvas.Strokes.Clone();
            int whiteboardIndex = CurrentWhiteboardIndex;
            if (currentMode == 0)
            {
                whiteboardIndex = 0;
            }
            strokeCollections[whiteboardIndex] = lastTouchDownStrokeCollection;
        }

        public double GetDistance(Point point1, Point point2)
        {
            return Math.Sqrt((point1.X - point2.X) * (point1.X - point2.X) + (point1.Y - point2.Y) * (point1.Y - point2.Y));
        }

        public double GetPointSpeed(Point point1, Point point2, Point point3)
        {
            return (Math.Sqrt((point1.X - point2.X) * (point1.X - point2.X) + (point1.Y - point2.Y) * (point1.Y - point2.Y))
                + Math.Sqrt((point3.X - point2.X) * (point3.X - point2.X) + (point3.Y - point2.Y) * (point3.Y - point2.Y)))
                / 20;
        }

        public Point[] FixPointsDirection(Point p1, Point p2)
        {
            if (Math.Abs(p1.X - p2.X) / Math.Abs(p1.Y - p2.Y) > 8)
            {
                //水平
                double x = Math.Abs(p1.Y - p2.Y) / 2;
                if (p1.Y > p2.Y)
                {
                    p1.Y -= x;
                    p2.Y += x;
                }
                else
                {
                    p1.Y += x;
                    p2.Y -= x;
                }
            }
            else if (Math.Abs(p1.Y - p2.Y) / Math.Abs(p1.X - p2.X) > 8)
            {
                //垂直
                double x = Math.Abs(p1.X - p2.X) / 2;
                if (p1.X > p2.X)
                {
                    p1.X -= x;
                    p2.X += x;
                }
                else
                {
                    p1.X += x;
                    p2.X -= x;
                }
            }

            return new Point[2] { p1, p2 };
        }

        public StylusPointCollection GenerateFakePressureTriangle(StylusPointCollection points)
        {
            var result = new StylusPointCollection();
            bool noFake = Settings.InkToShape.IsInkToShapeNoFakePressureTriangle;
            StylusPoint P(double x, double y, float pressure) => noFake ? new StylusPoint(x, y) : new StylusPoint(x, y, pressure);

            result.Add(P(points[0].X, points[0].Y, 0.4f));
            var c01 = GetCenterPoint(points[0], points[1]);
            result.Add(P(c01.X, c01.Y, 0.8f));
            result.Add(P(points[1].X, points[1].Y, 0.4f));
            result.Add(P(points[1].X, points[1].Y, 0.4f));
            var c12 = GetCenterPoint(points[1], points[2]);
            result.Add(P(c12.X, c12.Y, 0.8f));
            result.Add(P(points[2].X, points[2].Y, 0.4f));
            result.Add(P(points[2].X, points[2].Y, 0.4f));
            var c20 = GetCenterPoint(points[2], points[0]);
            result.Add(P(c20.X, c20.Y, 0.8f));
            result.Add(P(points[0].X, points[0].Y, 0.4f));
            return result;
        }

        public StylusPointCollection GenerateFakePressureRectangle(StylusPointCollection points)
        {
            if (Settings.InkToShape.IsInkToShapeNoFakePressureRectangle)
            {
                return points;
            }
            var newPoint = new StylusPointCollection();
            newPoint.Add(new StylusPoint(points[0].X, points[0].Y, (float)0.4));
            var cPoint = GetCenterPoint(points[0], points[1]);
            newPoint.Add(new StylusPoint(cPoint.X, cPoint.Y, (float)0.8));
            newPoint.Add(new StylusPoint(points[1].X, points[1].Y, (float)0.4));
            newPoint.Add(new StylusPoint(points[1].X, points[1].Y, (float)0.4));
            cPoint = GetCenterPoint(points[1], points[2]);
            newPoint.Add(new StylusPoint(cPoint.X, cPoint.Y, (float)0.8));
            newPoint.Add(new StylusPoint(points[2].X, points[2].Y, (float)0.4));
            newPoint.Add(new StylusPoint(points[2].X, points[2].Y, (float)0.4));
            cPoint = GetCenterPoint(points[2], points[3]);
            newPoint.Add(new StylusPoint(cPoint.X, cPoint.Y, (float)0.8));
            newPoint.Add(new StylusPoint(points[3].X, points[3].Y, (float)0.4));
            newPoint.Add(new StylusPoint(points[3].X, points[3].Y, (float)0.4));
            cPoint = GetCenterPoint(points[3], points[0]);
            newPoint.Add(new StylusPoint(cPoint.X, cPoint.Y, (float)0.8));
            newPoint.Add(new StylusPoint(points[0].X, points[0].Y, (float)0.4));
            return newPoint;
        }

        public Point GetCenterPoint(Point point1, Point point2)
        {
            return new Point((point1.X + point2.X) / 2, (point1.Y + point2.Y) / 2);
        }

        public StylusPoint GetCenterPoint(StylusPoint point1, StylusPoint point2)
        {
            return new StylusPoint((point1.X + point2.X) / 2, (point1.Y + point2.Y) / 2);
        }

        double GetResolutionScale()
        {
            double baseWidth = 1920.0;
            double baseHeight = 1080.0;
            double screenWidth = SystemParameters.PrimaryScreenWidth;
            double screenHeight = SystemParameters.PrimaryScreenHeight;
            double scaleW = screenWidth / baseWidth;
            double scaleH = screenHeight / baseHeight;
            return (scaleW + scaleH) / 2.0;
        }

        bool IsPotentialStraightLine(Stroke stroke)
        {
            if (stroke.StylusPoints.Count < 5) return false;
            Point start = stroke.StylusPoints.First().ToPoint();
            Point end = stroke.StylusPoints.Last().ToPoint();
            double lineLength = GetDistance(start, end);
            double adaptiveThreshold = Settings.Canvas.AutoStraightenLineThreshold * GetResolutionScale();
            if (lineLength < adaptiveThreshold) return false;
            double threshold = Math.Max(0.01, Settings.InkToShape.LineStraightenSensitivity * 0.2);
            if (stroke.StylusPoints.Count >= 10)
            {
                int quarterIdx = stroke.StylusPoints.Count / 4;
                int midIdx = stroke.StylusPoints.Count / 2;
                int threeQuarterIdx = quarterIdx * 3;
                Point quarterPoint = stroke.StylusPoints[quarterIdx].ToPoint();
                Point midPoint = stroke.StylusPoints[midIdx].ToPoint();
                Point threeQuarterPoint = stroke.StylusPoints[threeQuarterIdx].ToPoint();
                double qd = DistanceFromLineToPoint(start, end, quarterPoint);
                double md = DistanceFromLineToPoint(start, end, midPoint);
                double td = DistanceFromLineToPoint(start, end, threeQuarterPoint);
                double rel = lineLength * threshold;
                if (qd > rel || md > rel || td > rel) return false;
            }
            return true;
        }

        bool ShouldStraightenLine(Stroke stroke)
        {
            Point start = stroke.StylusPoints.First().ToPoint();
            Point end = stroke.StylusPoints.Last().ToPoint();
            double lineLength = GetDistance(start, end);
            double adaptiveThreshold = Settings.Canvas.AutoStraightenLineThreshold * GetResolutionScale();
            if (lineLength < adaptiveThreshold) return false;
            double sensitivity = Settings.InkToShape.LineStraightenSensitivity;
            double maxDeviation = 0;
            double totalDeviation = 0;
            int count = 0;
            foreach (var sp in stroke.StylusPoints)
            {
                var p = sp.ToPoint();
                double d = DistanceFromLineToPoint(start, end, p);
                if (d > maxDeviation) maxDeviation = d;
                totalDeviation += d;
                count++;
            }
            double avg = totalDeviation / Math.Max(count, 1);
            double threshold = Math.Max(0.01, sensitivity * 0.2);
            if ((maxDeviation / lineLength) > threshold) return false;
            double variance = 0;
            foreach (var sp in stroke.StylusPoints)
            {
                var p = sp.ToPoint();
                double d = DistanceFromLineToPoint(start, end, p);
                variance += (d - avg) * (d - avg);
            }
            variance /= Math.Max(count, 1);
            double varianceThreshold = threshold * lineLength * 0.25;
            if (variance > varianceThreshold) return false;
            if (stroke.StylusPoints.Count > 10)
            {
                int midIdx = stroke.StylusPoints.Count / 2;
                var mid = stroke.StylusPoints[midIdx].ToPoint();
                double midDev = DistanceFromLineToPoint(start, end, mid);
                double midThreshold = lineLength * threshold * 0.8;
                if (midDev > midThreshold) return false;
            }
            return true;
        }

        StylusPointCollection CreateStraightLine(Point start, Point end)
        {
            var points = new StylusPointCollection();
            points.Add(new StylusPoint(start.X, start.Y, 0.5f));
            double distance = GetDistance(start, end);
            if (distance > 100)
            {
                for (int i = 1; i < 3; i++)
                {
                    double r = i / 3.0;
                    Point m = new Point(start.X + (end.X - start.X) * r, start.Y + (end.Y - start.Y) * r);
                    points.Add(new StylusPoint(m.X, m.Y, 0.5f));
                }
            }
            points.Add(new StylusPoint(end.X, end.Y, 0.5f));
            return points;
        }

        double DistanceFromLineToPoint(Point lineStart, Point lineEnd, Point point)
        {
            double lineLength = GetDistance(lineStart, lineEnd);
            if (lineLength == 0) return GetDistance(point, lineStart);
            double distance = Math.Abs((lineEnd.Y - lineStart.Y) * point.X - (lineEnd.X - lineStart.X) * point.Y + lineEnd.X * lineStart.Y - lineEnd.Y * lineStart.X) / lineLength;
            return distance;
        }

        Point[] GetSnappedEndpoints(Point start, Point end)
        {
            if (!Settings.Canvas.LineEndpointSnapping) return null;
            bool startSnapped = false;
            bool endSnapped = false;
            Point snappedStart = start;
            Point snappedEnd = end;
            double snapThreshold = Settings.Canvas.LineEndpointSnappingThreshold;
            foreach (var stroke in inkCanvas.Strokes)
            {
                if (stroke.StylusPoints.Count == 0) continue;
                Point s = stroke.StylusPoints.First().ToPoint();
                Point t = stroke.StylusPoints.Last().ToPoint();
                if (!startSnapped)
                {
                    if (GetDistance(start, s) < snapThreshold)
                    {
                        snappedStart = s; startSnapped = true;
                    }
                    else if (GetDistance(start, t) < snapThreshold)
                    {
                        snappedStart = t; startSnapped = true;
                    }
                }
                if (!endSnapped)
                {
                    if (GetDistance(end, s) < snapThreshold)
                    {
                        snappedEnd = s; endSnapped = true;
                    }
                    else if (GetDistance(end, t) < snapThreshold)
                    {
                        snappedEnd = t; endSnapped = true;
                    }
                }
                if (startSnapped && endSnapped) break;
            }
            if (startSnapped || endSnapped) return new[] { snappedStart, snappedEnd };
            return null;
        }

        void ProcessRectangleGuideLines(Stroke newStroke)
        {
            if (!Settings.InkToShape.IsInkToShapeRectangle) return;
            if (!IsPotentialStraightLine(newStroke)) return;
            Point startPoint = newStroke.StylusPoints[0].ToPoint();
            Point endPoint = newStroke.StylusPoints[newStroke.StylusPoints.Count - 1].ToPoint();
            var newGuideLine = new RectangleGuideLine(newStroke, startPoint, endPoint);
            CleanupExpiredGuideLines();
            rectangleGuideLines.Add(newGuideLine);
            CheckForRectangleFormation();
        }

        void CleanupExpiredGuideLines()
        {
            var expireTime = DateTime.Now.AddSeconds(-30);
            for (int i = rectangleGuideLines.Count - 1; i >= 0; i--)
            {
                var guideLine = rectangleGuideLines[i];
                if (guideLine.CreatedTime < expireTime || !inkCanvas.Strokes.Contains(guideLine.OriginalStroke))
                {
                    rectangleGuideLines.RemoveAt(i);
                }
            }
        }

        void CheckForRectangleFormation()
        {
            if (rectangleGuideLines.Count < 4) return;
            var rectangleLines = FindRectangleLines();
            if (rectangleLines != null && rectangleLines.Count == 4)
            {
                CreateRectangleFromLines(rectangleLines);
            }
        }

        List<RectangleGuideLine> FindRectangleLines()
        {
            var sortedLines = rectangleGuideLines.OrderByDescending(l => l.CreatedTime).ToList();
            for (int i = 0; i < sortedLines.Count - 3; i++)
            {
                for (int j = i + 1; j < sortedLines.Count - 2; j++)
                {
                    for (int k = j + 1; k < sortedLines.Count - 1; k++)
                    {
                        for (int l = k + 1; l < sortedLines.Count; l++)
                        {
                            var lines = new List<RectangleGuideLine> { sortedLines[i], sortedLines[j], sortedLines[k], sortedLines[l] };
                            if (CanFormRectangle(lines)) return lines;
                        }
                    }
                }
            }
            return null;
        }

        bool CanFormRectangle(List<RectangleGuideLine> lines)
        {
            if (lines.Count != 4) return false;
            var horizontalLines = lines.Where(l => l.IsHorizontal).ToList();
            var verticalLines = lines.Where(l => l.IsVertical).ToList();
            if (horizontalLines.Count != 2 || verticalLines.Count != 2) return false;
            return CheckEndpointConnections(horizontalLines, verticalLines);
        }

        bool CheckEndpointConnections(List<RectangleGuideLine> horizontalLines, List<RectangleGuideLine> verticalLines)
        {
            var intersectionPoints = new List<Point>();
            foreach (var hLine in horizontalLines)
            {
                foreach (var vLine in verticalLines)
                {
                    var intersection = GetLineIntersection(hLine, vLine);
                    if (intersection.HasValue)
                    {
                        if (IsPointNearLineEndpoints(intersection.Value, hLine) && IsPointNearLineEndpoints(intersection.Value, vLine))
                        {
                            intersectionPoints.Add(intersection.Value);
                        }
                    }
                }
            }
            return intersectionPoints.Count >= 4;
        }

        Point? GetLineIntersection(RectangleGuideLine line1, RectangleGuideLine line2)
        {
            double x1 = line1.StartPoint.X, y1 = line1.StartPoint.Y;
            double x2 = line1.EndPoint.X, y2 = line1.EndPoint.Y;
            double x3 = line2.StartPoint.X, y3 = line2.StartPoint.Y;
            double x4 = line2.EndPoint.X, y4 = line2.EndPoint.Y;
            double denom = (x1 - x2) * (y3 - y4) - (y1 - y2) * (x3 - x4);
            if (Math.Abs(denom) < 1e-10) return null;
            double t = ((x1 - x3) * (y3 - y4) - (y1 - y3) * (x3 - x4)) / denom;
            double intersectionX = x1 + t * (x2 - x1);
            double intersectionY = y1 + t * (y2 - y1);
            return new Point(intersectionX, intersectionY);
        }

        bool IsPointNearLineEndpoints(Point point, RectangleGuideLine line)
        {
            double distToStart = GetDistance(point, line.StartPoint);
            double distToEnd = GetDistance(point, line.EndPoint);
            double endpointThreshold = Settings.InkToShape.RectangleEndpointThreshold;
            return distToStart <= endpointThreshold || distToEnd <= endpointThreshold;
        }

        void CreateRectangleFromLines(List<RectangleGuideLine> lines)
        {
            var corners = CalculateRectangleCorners(lines);
            if (corners == null || corners.Count != 4) return;
            var pointList = new List<Point>(corners) { corners[0] };
            var point = new StylusPointCollection(pointList);
            var rectangleStroke = new Stroke(GenerateFakePressureRectangle(point))
            {
                DrawingAttributes = inkCanvas.DefaultDrawingAttributes.Clone()
            };
            SetNewBackupOfStroke();
            _currentCommitType = CommitReason.ShapeRecognition;
            foreach (var line in lines)
            {
                if (inkCanvas.Strokes.Contains(line.OriginalStroke))
                {
                    inkCanvas.Strokes.Remove(line.OriginalStroke);
                }
            }
            inkCanvas.Strokes.Add(rectangleStroke);
            _currentCommitType = CommitReason.UserInput;
            foreach (var line in lines) rectangleGuideLines.Remove(line);
            newStrokes = new StrokeCollection();
        }

        List<Point> CalculateRectangleCorners(List<RectangleGuideLine> lines)
        {
            var horizontalLines = lines.Where(l => l.IsHorizontal).ToList();
            var verticalLines = lines.Where(l => l.IsVertical).ToList();
            if (horizontalLines.Count != 2 || verticalLines.Count != 2) return null;
            var corners = new List<Point>();
            foreach (var h in horizontalLines)
            {
                foreach (var v in verticalLines)
                {
                    var intersection = GetLineIntersection(h, v);
                    if (intersection.HasValue) corners.Add(intersection.Value);
                }
            }
            if (corners.Count != 4) return null;
            return SortRectangleCorners(corners);
        }

        List<Point> SortRectangleCorners(List<Point> corners)
        {
            if (corners.Count != 4) return corners;
            double centerX = corners.Average(p => p.X);
            double centerY = corners.Average(p => p.Y);
            var center = new Point(centerX, centerY);
            return corners.OrderBy(p => Math.Atan2(p.Y - center.Y, p.X - center.X)).ToList();
        }
    }
}
