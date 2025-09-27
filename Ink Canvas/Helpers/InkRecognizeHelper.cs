using System.Linq;
using System.Windows;
using System.Windows.Ink;
using System.Windows.Media;
using System.Diagnostics;

namespace Ink_Canvas.Helpers
{
    public class InkRecognizeHelper
    {
        // 复用 InkAnalyzer，避免频繁分配
        private static readonly object _analyzerLock = new object();
        private static InkAnalyzer _sharedAnalyzer = null;
        private static InkAnalyzer GetAnalyzer()
        {
            if (_sharedAnalyzer == null)
            {
                _sharedAnalyzer = new InkAnalyzer();
            }
            return _sharedAnalyzer;
        }

        // 判断所有笔迹颜色和粗细是否一致
        private static bool AreStrokesUniform(StrokeCollection strokes)
        {
            if (strokes == null || strokes.Count == 0) return true;
            var first = strokes[0].DrawingAttributes;
            foreach (var stroke in strokes)
            {
                var attr = stroke.DrawingAttributes;
                if (attr.Color != first.Color || attr.Width != first.Width || attr.Height != first.Height)
                    return false;
            }
            return true;
        }

        //识别形状
        public static ShapeRecognizeResult RecognizeShape(StrokeCollection strokes)
        {
            if (strokes == null || strokes.Count == 0)
                return default;

            // 新增：多段笔迹颜色或粗细不一致时不识别
            if (!AreStrokesUniform(strokes))
                return default;

            const int MinLogMs = 30; // 轻量耗时日志阈值，避免噪声
            var swTotal = Stopwatch.StartNew();
            long analyzeLoops = 0;
            ShapeRecognizeResult result = default;

            lock (_analyzerLock)
            {
                var analyzer = GetAnalyzer();
                // 记录本次加入的笔迹，便于结束后清理
                var addedStrokes = strokes.ToList();

                AnalysisAlternate analysisAlternate = null;
                int strokesCount = strokes.Count;
                try
                {
                    analyzer.AddStrokes(strokes);
                    analyzer.SetStrokesType(strokes, System.Windows.Ink.StrokeType.Drawing);

                    var sfsaf = analyzer.Analyze();
                    analyzeLoops++;
                    if (sfsaf.Successful)
                    {
                        var alternates = analyzer.GetAlternates();
                        if (alternates.Count > 0)
                        {
                            while ((!alternates[0].Strokes.Contains(strokes.Last()) ||
                                    !IsContainShapeType(((InkDrawingNode)alternates[0].AlternateNodes[0]).GetShapeName()))
                                   && strokesCount >= 2)
                            {
                                var toRemove = strokes[strokes.Count - strokesCount];
                                analyzer.RemoveStroke(toRemove);
                                addedStrokes.Remove(toRemove);
                                strokesCount--;
                                sfsaf = analyzer.Analyze();
                                analyzeLoops++;
                                if (sfsaf.Successful)
                                {
                                    alternates = analyzer.GetAlternates();
                                }
                                else
                                {
                                    break;
                                }
                            }
                            if (sfsaf.Successful && alternates.Count > 0)
                                analysisAlternate = alternates[0];
                        }
                    }

                    if (analysisAlternate != null && analysisAlternate.AlternateNodes.Count > 0)
                    {
                        var node = analysisAlternate.AlternateNodes[0] as InkDrawingNode;
                        result = new ShapeRecognizeResult(node.Centroid, node.HotPoints, analysisAlternate, node);
                    }
                }
                finally
                {
                    // 清理：移除本次加入的所有笔迹，确保共享 Analyzer 无残留
                    foreach (var s in addedStrokes)
                    {
                        try { analyzer.RemoveStroke(s); } catch { }
                    }
                }
            }

            swTotal.Stop();
            // 轻量日志：仅在耗时超过阈值时记录
            if (swTotal.ElapsedMilliseconds >= MinLogMs)
            {
                int strokeCount = strokes.Count;
                int pointCount = 0;
                try { pointCount = strokes.Sum(s => s.StylusPoints.Count); } catch { }
                string shape = (result?.InkDrawingNode != null) ? result.InkDrawingNode.GetShapeName() : "None";
                try
                {
                    LogHelper.WriteLogToFile($"InkRecognize | strokes={strokeCount}, points={pointCount}, loops={analyzeLoops}, time={swTotal.ElapsedMilliseconds}ms, shape={shape}");
                }
                catch { }
            }

            return result;
        }

        public static bool IsContainShapeType(string name)
        {
            if (name.Contains("Triangle") || name.Contains("Circle") ||
                name.Contains("Rectangle") || name.Contains("Diamond") ||
                name.Contains("Parallelogram") || name.Contains("Square")
                || name.Contains("Ellipse"))
            {
                return true;
            }
            return false;
        }
    }

    //Recognizer 的实现

    public enum RecognizeLanguage
    {
        SimplifiedChinese = 0x0804,
        TraditionalChinese = 0x7c03,
        English = 0x0809
    }

    public class ShapeRecognizeResult
    {
        public ShapeRecognizeResult(Point centroid, PointCollection hotPoints, AnalysisAlternate analysisAlternate, InkDrawingNode node)
        {
            Centroid = centroid;
            HotPoints = hotPoints;
            AnalysisAlternate = analysisAlternate;
            InkDrawingNode = node;
        }

        public AnalysisAlternate AnalysisAlternate { get; }

        public Point Centroid { get; set; }

        public PointCollection HotPoints { get; }

        public InkDrawingNode InkDrawingNode { get; }
    }

    /// <summary>
    /// 图形识别类
    /// </summary>
    //public class ShapeRecogniser
    //{
    //    public InkAnalyzer _inkAnalyzer = null;

    //    private ShapeRecogniser()
    //    {
    //        this._inkAnalyzer = new InkAnalyzer
    //        {
    //            AnalysisModes = AnalysisModes.AutomaticReconciliationEnabled
    //        };
    //    }

    //    /// <summary>
    //    /// 根据笔迹集合返回图形名称字符串
    //    /// </summary>
    //    /// <param name="strokeCollection"></param>
    //    /// <returns></returns>
    //    public InkDrawingNode Recognition(StrokeCollection strokeCollection)
    //    {
    //        if (strokeCollection == null)
    //        {
    //            //MessageBox.Show("dddddd");
    //            return null;
    //        }

    //        InkDrawingNode result = null;
    //        try
    //        {
    //            this._inkAnalyzer.AddStrokes(strokeCollection);
    //            if (this._inkAnalyzer.Analyze().Successful)
    //            {
    //                result = _internalAnalyzer(this._inkAnalyzer);
    //                this._inkAnalyzer.RemoveStrokes(strokeCollection);
    //            }
    //        }
    //        catch (System.Exception ex)
    //        {
    //            //result = ex.Message;
    //            System.Diagnostics.Debug.WriteLine(ex.Message);
    //        }

    //        return result;
    //    }

    //    /// <summary>
    //    /// 实现笔迹的分析，返回图形对应的字符串
    //    /// 你在实际的应用中根据返回的字符串来生成对应的Shape
    //    /// </summary>
    //    /// <param name="ink"></param>
    //    /// <returns></returns>
    //    private InkDrawingNode _internalAnalyzer(InkAnalyzer ink)
    //    {
    //        try
    //        {
    //            ContextNodeCollection nodecollections = ink.FindNodesOfType(ContextNodeType.InkDrawing);
    //            foreach (ContextNode node in nodecollections)
    //            {
    //                InkDrawingNode drawingNode = node as InkDrawingNode;
    //                if (drawingNode != null)
    //                {
    //                    return drawingNode;//.GetShapeName();
    //                }
    //            }
    //        }
    //        catch (System.Exception ex)
    //        {
    //            System.Diagnostics.Debug.WriteLine(ex.Message);
    //        }

    //        return null;
    //    }


    //    private static ShapeRecogniser instance = null;
    //    public static ShapeRecogniser Instance
    //    {
    //        get
    //        {
    //            return instance == null ? (instance = new ShapeRecogniser()) : instance;
    //        }
    //    }
    //}


    //用于自动控制其他形状相对于圆的位置

    public class Circle
    {
        public Circle(Point centroid, double r, Stroke stroke)
        {
            Centroid = centroid;
            R = r;
            Stroke = stroke;
        }

        public Point Centroid { get; set; }

        public double R { get; set; }

        public Stroke Stroke { get; set; }
    }
}
