using Ink_Canvas.Helpers;
using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Ink;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using Point = System.Windows.Point;

namespace Ink_Canvas
{
    public partial class MainWindow : Window
    {
        #region Floating Control

        object lastBorderMouseDownObject;

        private void Border_MouseDown(object sender, MouseButtonEventArgs e)
        {
            lastBorderMouseDownObject = sender;
        }

        bool isStrokeSelectionCloneOn = false;

        private void BorderStrokeSelectionClone_Click(object sender, RoutedEventArgs e)
        {
            if (isStrokeSelectionCloneOn)
            {
                IconStrokeSelectionClone.SetResourceReference(TextBlock.ForegroundProperty, "FloatBarForeground");
                isStrokeSelectionCloneOn = false;
            }
            else
            {
                IconStrokeSelectionClone.SetResourceReference(TextBlock.ForegroundProperty, "FloatBarBackground");
                isStrokeSelectionCloneOn = true;
            }
        }

        private void BorderStrokeSelectionCloneToBoardOrNewPage_Click(object sender, RoutedEventArgs e)
        {
            if (currentMode == 0)
            {
                StrokeCollection strokes = inkCanvas.GetSelectedStrokes();
                List<UIElement> elements = InkCanvasElementsHelper.GetSelectedElementsCloned(inkCanvas);
                inkCanvas.Select(new StrokeCollection());
                strokes = strokes.Clone();
                ImageBlackboard_Click(null, null);
                inkCanvas.Strokes.Add(strokes);
                InkCanvasElementsHelper.AddElements(inkCanvas, elements, timeMachine);
            }
            else
            {
                StrokeCollection strokes = inkCanvas.GetSelectedStrokes();
                List<UIElement> elements = InkCanvasElementsHelper.GetSelectedElementsCloned(inkCanvas);
                inkCanvas.Select(new StrokeCollection());
                strokes = strokes.Clone();
                BtnWhiteBoardAdd_Click(null, null);
                inkCanvas.Strokes.Add(strokes);
                InkCanvasElementsHelper.AddElements(inkCanvas, elements, timeMachine);
            }
        }

        private void GridPenWidthDecrease_Click(object sender, RoutedEventArgs e)
        {
            ChangeSelectedStrokeThickness(0.8);
        }

        private void GridPenWidthIncrease_Click(object sender, RoutedEventArgs e)
        {
            ChangeSelectedStrokeThickness(1.25);
        }

        private void GridPenWidthRestore_Click(object sender, RoutedEventArgs e)
        {
            foreach (Stroke stroke in inkCanvas.GetSelectedStrokes())
            {
                stroke.DrawingAttributes.Width = inkCanvas.DefaultDrawingAttributes.Width;
                stroke.DrawingAttributes.Height = inkCanvas.DefaultDrawingAttributes.Height;
            }
        }

        private void BorderStrokeSelectionDelete_Click(object sender, RoutedEventArgs e)
        {
            SymbolIconDelete_MouseUp(sender, e);
            // 通知 plugin：元素被移除（视频控件 plugin 据此隐藏控制条）
            try
            {
                var host = Plugins.PluginHost.Instance;
                if (host != null)
                {
                    var selected = inkCanvas.GetSelectedElements();
                    foreach (var el in selected)
                    {
                        host.RaiseElementRemoved(el);
                    }
                }
            } catch { }
        }

        private void BtnStrokeSelectionSaveToImage_Click(object sender, RoutedEventArgs e)
        {
            StrokeCollection selectedStrokes = inkCanvas.GetSelectedStrokes();
            var selectedElements = inkCanvas.GetSelectedElements();

            if (selectedStrokes.Count > 0 || selectedElements.Count > 0)
            {
                Rect bounds = inkCanvas.GetSelectionBounds();

                double width = bounds.Width + 10;
                double height = bounds.Height + 10;
                RenderTargetBitmap renderTarget = new RenderTargetBitmap(
                    (int)Math.Ceiling(width), (int)Math.Ceiling(height),
                    96, 96, PixelFormats.Pbgra32);

                DrawingVisual drawingVisual = new DrawingVisual();
                using (DrawingContext drawingContext = drawingVisual.RenderOpen())
                {
                    drawingContext.PushTransform(new TranslateTransform(-bounds.X, -bounds.Y));

                    foreach (Stroke stroke in selectedStrokes)
                    {
                        stroke.Draw(drawingContext);
                    }

                    foreach (UIElement element in selectedElements)
                    {
                        VisualBrush vb = new VisualBrush(element);
                        Rect elementBounds = new Rect(element.RenderSize);

                        Transform renderTransform = element.RenderTransform;
                        if (renderTransform != null)
                        {
                            drawingContext.PushTransform(renderTransform);
                            drawingContext.DrawRectangle(vb, null, elementBounds);
                            drawingContext.Pop();
                        }
                        else
                        {
                            drawingContext.DrawRectangle(vb, null, elementBounds);
                        }
                    }
                }

                renderTarget.Render(drawingVisual);

                SaveFileDialog saveFileDialog = new SaveFileDialog
                {
                    Filter = "PNG Images|*.png",
                    Title = "Save Selected Ink as PNG",
                    FileName = DateTime.Now.ToString("yyyy-MM-dd HH-mm-ss-fff")
                };

                if (saveFileDialog.ShowDialog() == true)
                {
                    PngBitmapEncoder encoder = new PngBitmapEncoder();
                    encoder.Frames.Add(BitmapFrame.Create(renderTarget));

                    using (FileStream fileStream = new FileStream(saveFileDialog.FileName, FileMode.Create))
                    {
                        encoder.Save(fileStream);
                    }
                }
            }
        }

        private void ChangeSelectedStrokeThickness(double multipler)
        {
            foreach (Stroke stroke in inkCanvas.GetSelectedStrokes())
            {
                var newWidth = stroke.DrawingAttributes.Width * multipler;
                var newHeight = stroke.DrawingAttributes.Height * multipler;
                if (newWidth >= DrawingAttributes.MinWidth && newWidth <= DrawingAttributes.MaxWidth
                    && newHeight >= DrawingAttributes.MinHeight && newHeight <= DrawingAttributes.MaxHeight)
                {
                    stroke.DrawingAttributes.Width = newWidth;
                    stroke.DrawingAttributes.Height = newHeight;
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

        private void MatrixTransform(int type)
        {
            Matrix m = new Matrix();
            Rect bounds = inkCanvas.GetSelectionBounds();
            Point center = new Point(bounds.Left + bounds.Width / 2, bounds.Top + bounds.Height / 2);

            switch (type)
            {
                case 1: // Flip Horizontal
                    m.ScaleAt(-1, 1, center.X, center.Y);
                    break;
                case 2: // Flip Vertical
                    m.ScaleAt(1, -1, center.X, center.Y);
                    break;
                default: // Rotate
                    m.RotateAt(type, center.X, center.Y);
                    break;
            }

            List<UIElement> selectedElements = InkCanvasElementsHelper.GetSelectedElements(inkCanvas);
            foreach (UIElement element in selectedElements)
            {
                ApplyElementMatrixTransform(element, m);
            }

            StrokeCollection targetStrokes = inkCanvas.GetSelectedStrokes();
            foreach (Stroke stroke in targetStrokes)
            {
                stroke.Transform(m, false);
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
            ToCommitStrokeManipulationHistoryAfterMouseUp();
        }

        private void ApplyElementMatrixTransform(UIElement element, Matrix matrix)
        {
            FrameworkElement frameworkElement = element as FrameworkElement;
            if (!(frameworkElement.RenderTransform is TransformGroup transformGroup))
            {
                transformGroup = new TransformGroup();
                frameworkElement.RenderTransform = transformGroup;
            }

            if (!ElementsInitialHistory.ContainsKey(frameworkElement.Name))
            {
                ElementsInitialHistory[frameworkElement.Name] = transformGroup.Clone();
            }

            TransformGroup centeredTransformGroup = new TransformGroup();
            centeredTransformGroup.Children.Add(new MatrixTransform(matrix));
            transformGroup.Children.Add(centeredTransformGroup);

            // 防性能退化：滚轮/双指不断平移缩放时，每步都会向 RenderTransform 追加一个矩阵。
            // 元素越多、操作越久，TransformGroup 越长，渲染时需合成的矩阵越多，必然渐进变卡。
            // 子项超过阈值时压缩为单个等价的 MatrixTransform，值与之前完全一致，仅折叠链长度。
            const int maxTransformAppendSteps = 16;
            if (transformGroup.Children.Count > maxTransformAppendSteps)
            {
                var compactedMatrix = new MatrixTransform(transformGroup.Value);
                transformGroup = new TransformGroup();
                transformGroup.Children.Add(compactedMatrix);
                frameworkElement.RenderTransform = transformGroup;
            }

            if (ElementsManipulationHistory == null)
            {
                ElementsManipulationHistory = new Dictionary<string, Tuple<object, TransformGroup>>();
            }
            ElementsManipulationHistory[frameworkElement.Name] =
                new Tuple<object, TransformGroup>(ElementsInitialHistory[frameworkElement.Name], transformGroup.Clone());
        }

        private void BtnFlipHorizontal_Click(object sender, RoutedEventArgs e)
        {
            MatrixTransform(1);
        }

        private void BtnFlipVertical_Click(object sender, RoutedEventArgs e)
        {
            MatrixTransform(2);
        }

        private void BtnAnticlockwiseRotate15_Click(object sender, RoutedEventArgs e)
        {
            MatrixTransform(-15);
        }

        private void BtnAnticlockwiseRotate45_Click(object sender, RoutedEventArgs e)
        {
            MatrixTransform(-45);
        }

        private void BtnAnticlockwiseRotate90_Click(object sender, RoutedEventArgs e)
        {
            MatrixTransform(-90);
        }

        private void BtnClockwiseRotate15_Click(object sender, RoutedEventArgs e)
        {
            MatrixTransform(15);
        }

        private void BtnClockwiseRotate45_Click(object sender, RoutedEventArgs e)
        {
            MatrixTransform(45);
        }

        private void BtnClockwiseRotate90_Click(object sender, RoutedEventArgs e)
        {
            MatrixTransform(90);
        }

        #endregion

        bool isGridInkCanvasSelectionCoverMouseDown = false;
        private Point lastMousePoint;
        /// <summary>记录点击选中覆盖层前的编辑模式，用于取消选中后保持原工具状态（避免误切回笔）</summary>
        private InkCanvasEditingMode editingModeBeforeCoverInteraction = InkCanvasEditingMode.Ink;

        private void GridInkCanvasSelectionCover_MouseDown(object sender, MouseButtonEventArgs e)
        {
            lastMousePoint = e.GetPosition(inkCanvas);
            isGridInkCanvasSelectionCoverMouseDown = true;
            editingModeBeforeCoverInteraction = inkCanvas.EditingMode;
            if (isStrokeSelectionCloneOn)
            {
                StrokeCollection strokes = inkCanvas.GetSelectedStrokes();
                List<UIElement> elementsList = InkCanvasElementsHelper.GetSelectedElements(inkCanvas);
                isProgramChangeStrokeSelection = true;
                ElementsSelectionClone = InkCanvasElementsHelper.CloneSelectedElements(inkCanvas, ref ElementsInitialHistory);
                // 克隆会往 ElementsInitialHistory 写入新条目，而 ElementData 强引用元素
                //（内含全分辨率位图）。这里顺带清掉已失效的旧条目，防止长期累积。
                PruneElementsInitialHistory();
                inkCanvas.Select(new StrokeCollection());
                StrokesSelectionClone = strokes.Clone();
                inkCanvas.Strokes.Add(StrokesSelectionClone);
                inkCanvas.Select(strokes, elementsList);
                isProgramChangeStrokeSelection = false;
            }
            else if (lastMousePoint.X < inkCanvas.GetSelectionBounds().Left ||
            lastMousePoint.Y < inkCanvas.GetSelectionBounds().Top ||
            lastMousePoint.X > inkCanvas.GetSelectionBounds().Right ||
            lastMousePoint.Y > inkCanvas.GetSelectionBounds().Bottom)
            {
                isGridInkCanvasSelectionCoverMouseDown = false;
                inkCanvas.Select(new StrokeCollection());
                // 点击选中区域外取消选中后，保持原来的选择/工具状态，避免误切回笔
                if (inkCanvas.EditingMode != editingModeBeforeCoverInteraction)
                {
                    inkCanvas.EditingMode = editingModeBeforeCoverInteraction;
                }
                StrokesSelectionClone = new StrokeCollection();
                ElementsSelectionClone = new List<UIElement>();
            }
        }

        private void GridInkCanvasSelectionCover_MouseMove(object sender, MouseEventArgs e)
        {
            if (isGridInkCanvasSelectionCoverMouseDown == false) return;
            Point mousePoint = e.GetPosition(inkCanvas);
            Vector trans = new Vector(mousePoint.X - lastMousePoint.X, mousePoint.Y - lastMousePoint.Y);
            lastMousePoint = mousePoint;
            Matrix m = new Matrix();
            // add Translate
            m.Translate(trans.X, trans.Y);
            // handle UIElement
            List<UIElement> elements;
            if (ElementsSelectionClone.Count != 0)
            {
                elements = ElementsSelectionClone;
            }
            else
            {
                elements = InkCanvasElementsHelper.GetSelectedElements(inkCanvas);
            }
            foreach (UIElement element in elements)
            {
                ApplyElementMatrixTransform(element, m);
            }
            // handle strokes
            StrokeCollection strokes = inkCanvas.GetSelectedStrokes();
            if (StrokesSelectionClone.Count != 0)
            {
                strokes = StrokesSelectionClone;
            }
            foreach (Stroke stroke in strokes)
            {
                stroke.Transform(m, false);
            }
            UpdateBorderStrokeSelectionControlLocation();
            RaiseElementTransformedForSelected();
        }

        private void GridInkCanvasSelectionCover_MouseUp(object sender, MouseButtonEventArgs e)
        {
            ToCommitStrokeManipulationHistoryAfterMouseUp();
            isGridInkCanvasSelectionCoverMouseDown = false;
            if (InkCanvasElementsHelper.IsNotCanvasElementSelected(inkCanvas))
            {
                GridInkCanvasSelectionCover.Visibility = Visibility.Collapsed;
                StrokesSelectionClone = new StrokeCollection();
                ElementsSelectionClone = new List<UIElement>();
            }
            else
            {
                if (currentMode == 0)
                {
                    TextSelectionCloneToNewBoard.Text = "衍至画板";
                }
                else
                {
                    TextSelectionCloneToNewBoard.Text = "衍至新页";
                }
                GridInkCanvasSelectionCover.Visibility = Visibility.Visible;
                StrokesSelectionClone = new StrokeCollection();
                ElementsSelectionClone = new List<UIElement>();
            }
        }

        private void GridInkCanvasSelectionCover_MouseWheel(object sender, MouseWheelEventArgs e)
        {
            // 白板模式下已切换到“选择”状态时，滚轮交由 Window_MouseWheel 统一操控整页内容，
            // 这里不再单独缩放“选中内容”，避免与整页平移/缩放双重叠加导致元素相对漂移。
            if (currentMode == 1 && inkCanvas.EditingMode == InkCanvasEditingMode.Select)
            {
                return;
            }

            double scale = e.Delta > 0 ? 1.1 : 0.9;
            Point center = InkCanvasElementsHelper.GetAllElementsBoundsCenterPoint(inkCanvas);
            Matrix m = new Matrix();
            m.ScaleAt(scale, scale, center.X, center.Y);

            StrokeCollection strokes = inkCanvas.GetSelectedStrokes();
            List<UIElement> elements = InkCanvasElementsHelper.GetSelectedElements(inkCanvas);
            // handle UIElement
            foreach (UIElement element in elements)
            {
                ApplyElementMatrixTransform(element, m);
            }
            // handle strokes
            foreach (Stroke stroke in strokes)
            {
                stroke.Transform(m, false);
                try
                {
                    stroke.DrawingAttributes.Width *= scale;
                    stroke.DrawingAttributes.Height *= scale;
                }
                catch { }
            }
            UpdateBorderStrokeSelectionControlLocation();
            RaiseElementTransformedForSelected();
        }

        /// <summary>根据当前编辑模式设置 InkCanvas 光标：选择/圈选模式显示十字光标，其他模式恢复笔光标</summary>
        private void SetCursorBasedOnEditingMode(InkCanvas canvas)
        {
            if (canvas == null) return;
            try
            {
                if (canvas.EditingMode == InkCanvasEditingMode.Select)
                {
                    // 选择（圈选）模式：明确显示十字选择光标，避免误以为仍处于笔模式
                    canvas.UseCustomCursor = true;
                    canvas.ForceCursor = true;
                    canvas.Cursor = Cursors.Cross;
                    return;
                }
                // 非选择模式：恢复笔光标并沿用原有 ForceCursor 逻辑
                canvas.UseCustomCursor = false;
                canvas.Cursor = Cursors.Pen;
                if (Settings.Canvas.IsShowCursor)
                {
                    canvas.ForceCursor = (canvas.EditingMode == InkCanvasEditingMode.Ink || drawingShapeMode != 0);
                }
                else
                {
                    canvas.ForceCursor = false;
                }
            }
            catch { }
        }

        private void BtnSelect_Click(object sender, RoutedEventArgs e)
        {
            forceEraser = true;
            drawingShapeMode = 0;
            inkCanvas.IsManipulationEnabled = false;
            if (inkCanvas.EditingMode == InkCanvasEditingMode.Select)
            {
                if (inkCanvas.GetSelectedStrokes().Count == inkCanvas.Strokes.Count
                    && inkCanvas.GetSelectedElements().Count == inkCanvas.Children.Count)
                {
                    inkCanvas.EditingMode = InkCanvasEditingMode.Ink;
                    inkCanvas.EditingMode = InkCanvasEditingMode.Select;
                }
                else
                {
                    StrokeCollection selectedStrokes = new StrokeCollection();
                    foreach (Stroke stroke in inkCanvas.Strokes)
                    {
                        if (stroke.GetBounds().Width > 0 && stroke.GetBounds().Height > 0)
                        {
                            selectedStrokes.Add(stroke);
                        }
                    }
                    List<UIElement> selectedElements = InkCanvasElementsHelper.GetAllElements(inkCanvas);
                    inkCanvas.Select(selectedStrokes, selectedElements);
                }
            }
            else
            {
                inkCanvas.EditingMode = InkCanvasEditingMode.Select;
            }

            // 确保选择工具正确生效：清理残留状态、强制保持选择模式、设置选择光标
            isLongPressSelected = false;
            forcePointEraser = false;
            if (inkCanvas.EditingMode != InkCanvasEditingMode.Select)
            {
                inkCanvas.EditingMode = InkCanvasEditingMode.Select;
            }
            SetCursorBasedOnEditingMode(inkCanvas);
        }
        bool isProgramChangeStrokeSelection = false;

        private void InkCanvas_SelectionChanged(object sender, EventArgs e)
        {
            // 始终隐藏 InkCanvas 原生虚线选择框，只保留自定义 8 把手选择框
            HideInkCanvasNativeSelectionAdorner();
            if (isProgramChangeStrokeSelection) return;
            if (InkCanvasElementsHelper.IsNotCanvasElementSelected(inkCanvas))
            {
                GridInkCanvasSelectionCover.Visibility = Visibility.Collapsed;
                HideSelectionDisplay();
            }
            else
            {
                if (currentMode == 0)
                {
                    TextSelectionCloneToNewBoard.Text = "衍至画板";
                }
                else
                {
                    TextSelectionCloneToNewBoard.Text = "衍至新页";
                }
                GridInkCanvasSelectionCover.Visibility = Visibility.Visible;
                IconStrokeSelectionClone.SetResourceReference(TextBlock.ForegroundProperty, "FloatBarForeground");
                ToggleButtonStrokeSelectionClone.IsChecked = false;
                isStrokeSelectionCloneOn = false;
                // 每次新选中默认折叠为小按钮
                isStrokeSelectionToolbarExpanded = false;
                UpdateBorderStrokeSelectionControlLocation();
            }
        }
        double BorderStrokeSelectionControlWidth = 695;
        double BorderStrokeSelectionControlHeight = 104;
        /// <summary>设置框最小缩放比例（保证用户能看清）</summary>
        const double SelectionControlMinScale = 0.7;
        /// <summary>小按钮尺寸</summary>
        const double SelectionExpandButtonSize = 44;
        /// <summary>设置框是否展开（默认折叠为小按钮）</summary>
        bool isStrokeSelectionToolbarExpanded = false;

        /// <summary>
        /// 更新圈选设置框/折叠小按钮的位置与可见性。
        /// 折叠状态：只显示小按钮；展开状态：显示设置框，且绝不遮挡选中区域（必要时按比例缩小，仍不行则放屏幕边缘）。
        /// </summary>
        private void UpdateBorderStrokeSelectionControlLocation()
        {
            if (inkCanvas == null || SelectionRectangle == null) return;

            Rect selectionBounds = inkCanvas.GetSelectionBounds();
            // 划线元素选中或图片/元素选中时都显示圈选设置框；
            // 此前仅当选中了笔迹(Strokes)时才显示，导致仅选中图片元素时看不到设置栏按钮。
            bool anySelection = inkCanvas.GetSelectedStrokes().Count > 0 ||
                                inkCanvas.GetSelectedElements().Count > 0;
            bool coverVisible = GridInkCanvasSelectionCover != null &&
                                GridInkCanvasSelectionCover.Visibility == Visibility.Visible;

            if (!anySelection || !coverVisible)
            {
                if (BorderStrokeSelectionControl != null) BorderStrokeSelectionControl.Visibility = Visibility.Collapsed;
                if (BorderStrokeSelectionExpand != null) BorderStrokeSelectionExpand.Visibility = Visibility.Collapsed;
                UpdateSelectionDisplay();
                return;
            }

            // 计算设置框（不遮挡选中区域）的最佳位置与缩放
            double barLeft, barTop, barW, barH, scale;
            ComputeSelectionBarPlacement(selectionBounds, out barLeft, out barTop, out barW, out barH, out scale);

            if (isStrokeSelectionToolbarExpanded)
            {
                // 展开：显示设置框（按比例缩放），折叠按钮贴在左上角
                BorderStrokeSelectionControl.Visibility = Visibility.Visible;
                BorderStrokeSelectionControl.Width = barW;
                BorderStrokeSelectionControl.Height = barH;
                if (!double.IsNaN(barLeft) && !double.IsNaN(barTop))
                    BorderStrokeSelectionControl.Margin = new Thickness(barLeft, barTop, 0, 0);
                else
                    BorderStrokeSelectionControl.Margin = new Thickness(0, 0, 0, 0);

                if (BorderStrokeSelectionExpand != null)
                {
                    BorderStrokeSelectionExpand.Visibility = Visibility.Visible;
                    // 折叠按钮放在设置框左侧、靠近顶部，避免盖住设置框内容与选中区域
                    double ex = barLeft - SelectionExpandButtonSize - 4;
                    double ey = barTop + 8;
                    ex = Math.Max(0, Math.Min(ActualWidth - SelectionExpandButtonSize, ex));
                    ey = Math.Max(0, Math.Min(ActualHeight - SelectionExpandButtonSize, ey));
                    BorderStrokeSelectionExpand.Margin = new Thickness(ex, ey, 0, 0);
                }
            }
            else
            {
                // 折叠：只显示小按钮，放在选中区域附近（优先下方，放不下放上方）
                BorderStrokeSelectionControl.Visibility = Visibility.Collapsed;
                if (BorderStrokeSelectionExpand != null)
                {
                    BorderStrokeSelectionExpand.Visibility = Visibility.Visible;
                    double half = SelectionExpandButtonSize / 2;
                    double bx = selectionBounds.Left + selectionBounds.Width / 2 - half;
                    double by = selectionBounds.Bottom + 8;
                    if (by + SelectionExpandButtonSize > ActualHeight)
                        by = selectionBounds.Top - SelectionExpandButtonSize - 8;
                    if (by < 0) by = Math.Max(0, ActualHeight - SelectionExpandButtonSize - 8);
                    bx = Math.Max(0, Math.Min(ActualWidth - SelectionExpandButtonSize, bx));
                    BorderStrokeSelectionExpand.Margin = new Thickness(bx, by, 0, 0);
                }
            }

            // 同步刷新 8 个选择把手与选择框
            UpdateSelectionDisplay();
        }

        /// <summary>
        /// 计算设置框的最佳位置与缩放，保证不遮挡选中区域。
        /// 依次尝试下方、上方、右方、左方，必要时按比例缩小（不小于最小可读比例）；
        /// 均放不下时按可读比例放到屏幕边缘。
        /// </summary>
        private void ComputeSelectionBarPlacement(Rect sel, out double barLeft, out double barTop,
            out double barW, out double barH, out double scale)
        {
            const double margin = 10;
            double fullW = BorderStrokeSelectionControlWidth;
            double fullH = BorderStrokeSelectionControlHeight;
            double winW = ActualWidth;
            double winH = ActualHeight;
            double selCx = sel.Left + sel.Width / 2;
            double selCy = sel.Top + sel.Height / 2;

            // 1) 下方：区域 = (margin, sel.Bottom+margin) 起，宽度 winW-2*margin，高度到窗口底部
            if (TryFitBar(margin, sel.Bottom + margin, winW - margin * 2, winH - (sel.Bottom + margin) - margin,
                          selCx, selCy, fullW, fullH, out barLeft, out barTop, out scale))
            {
                barW = fullW * scale; barH = fullH * scale;
                NudgeAwayFromFloatingBar(ref barTop, barLeft, barW, barH, sel);
                return;
            }
            // 2) 上方
            if (TryFitBar(margin, margin, winW - margin * 2, sel.Top - margin * 2,
                          selCx, selCy, fullW, fullH, out barLeft, out barTop, out scale))
            {
                barW = fullW * scale; barH = fullH * scale;
                return;
            }
            // 3) 右方
            if (TryFitBar(sel.Right + margin, margin, winW - (sel.Right + margin) - margin, winH - margin * 2,
                          selCx, selCy, fullW, fullH, out barLeft, out barTop, out scale))
            {
                barW = fullW * scale; barH = fullH * scale;
                return;
            }
            // 4) 左方
            if (TryFitBar(margin, margin, sel.Left - margin * 2, winH - margin * 2,
                          selCx, selCy, fullW, fullH, out barLeft, out barTop, out scale))
            {
                barW = fullW * scale; barH = fullH * scale;
                return;
            }
            // 5) 兜底：按可读比例放到屏幕边缘（优先底部居中，其次顶部居中）。只缩小不放大，scale 不超过 1
            scale = Math.Max(SelectionControlMinScale, Math.Min(1, Math.Min(winW / fullW, winH / fullH)));
            barW = fullW * scale; barH = fullH * scale;
            barLeft = (winW - barW) / 2;
            barTop = winH - barH - margin;
            if (barTop < 0) barTop = margin;
            if (barLeft < 0) barLeft = 0;
            if (barLeft + barW > winW) barLeft = Math.Max(0, winW - barW);
        }

        /// <summary>
        /// 尝试把设置框放入指定区域（不遮挡选中区域），必要时按比例缩小；
        /// 区域放不下（缩放低于最小可读比例）时返回 false。
        /// </summary>
        private bool TryFitBar(double availLeft, double availTop, double availW, double availH,
            double prefCx, double prefCy, double fullW, double fullH,
            out double barLeft, out double barTop, out double scale)
        {
            barLeft = 0; barTop = 0; scale = 1;
            if (availW < 10 || availH < 10) return false;
            double s = Math.Min(1, Math.Min(availW / fullW, availH / fullH));
            if (s < SelectionControlMinScale) return false;
            scale = s;
            double bw = fullW * s;
            double bh = fullH * s;
            // 以选中区域中心线为参考，夹紧在区域内
            double left = prefCx - bw / 2;
            left = Math.Max(availLeft, Math.Min(availLeft + availW - bw, left));
            double top = prefCy - bh / 2;
            top = Math.Max(availTop, Math.Min(availTop + availH - bh, top));
            barLeft = left;
            barTop = top;
            return true;
        }

        /// <summary>如果设置框与底部浮动栏重叠，尝试把设置框移到浮动栏下方/上方（尽量不遮挡选中区域）</summary>
        private void NudgeAwayFromFloatingBar(ref double barTop, double barLeft, double barW, double barH, Rect sel)
        {
            if (currentMode != 0 || ViewboxFloatingBar == null) return;
            try
            {
                double vTop = ViewboxFloatingBar.Margin.Top;
                double vLeft = ViewboxFloatingBar.Margin.Left;
                double vBottom = vTop + ViewboxFloatingBar.ActualHeight;
                double vRight = vLeft + ViewboxFloatingBar.ActualWidth;
                bool hOverlap = barLeft < vRight && barLeft + barW > vLeft;
                bool vOverlap = barTop < vBottom && barTop + barH > vTop;
                if (!(hOverlap && vOverlap)) return;

                // 尝试放到浮动栏下方
                double below = vBottom + 5;
                if (below + barH <= ActualHeight && !(below < sel.Bottom && below + barH > sel.Top))
                {
                    barTop = below;
                    return;
                }
                // 尝试放到浮动栏上方
                double above = vTop - barH - 5;
                if (above >= 0 && !(above < sel.Bottom && above + barH > sel.Top))
                {
                    barTop = above;
                }
            }
            catch { }
        }

        /// <summary>点击圈选折叠小按钮：展开/收起设置框</summary>
        private void BorderStrokeSelectionExpand_MouseUp(object sender, MouseButtonEventArgs e)
        {
            if (lastBorderMouseDownObject != sender) return;
            isStrokeSelectionToolbarExpanded = !isStrokeSelectionToolbarExpanded;
            UpdateBorderStrokeSelectionControlLocation();
        }

        #region Selection Display and Handles (8 点: 四角缩放+旋转, 四边缩放)

        /// <summary>选择把手尺寸（像素）</summary>
        private const double SelectionHandleSize = 12;

        /// <summary>是否正在拖拽缩放（四边点）</summary>
        private bool isResizing = false;
        /// <summary>当前缩放把手名</summary>
        private string currentResizeHandle = "";
        /// <summary>缩放起始点（画布坐标）</summary>
        private Point resizeStartPoint;
        /// <summary>是否正在拖拽角点组合变换（四角点：同时缩放+旋转）</summary>
        private bool isCornerTransform = false;
        /// <summary>当前角点把手名</summary>
        private string currentCornerHandle = "";
        /// <summary>组合变换枢轴（水平对边中点，画布坐标）</summary>
        private Point cornerPivotPoint;
        /// <summary>拖拽开始时 枢轴→角点 向量的角度（度）</summary>
        private double cornerStartAngle;
        /// <summary>拖拽开始时 枢轴→角点 向量的长度</summary>
        private double cornerStartDistance;
        /// <summary>已累计应用的缩放倍数</summary>
        private double cornerAppliedScale = 1;
        /// <summary>已累计应用的旋转角度（度）</summary>
        private double cornerAppliedAngle;
        /// <summary>拖拽开始时的选区矩形</summary>
        private Rect originalSelectionBounds;

        /// <summary>
        /// 刷新选择框与 8 个选择把手的位置/可见性。
        /// 无选中墨迹时隐藏；角点组合变换过程中选择框跟随缩放+旋转、把手吸附到变换后的四角与四边。
        /// </summary>
        private void UpdateSelectionDisplay()
        {
            if (inkCanvas == null) return;
            if (SelectionRectangle == null || SelectionHandlesCanvas == null) return;

            // 没有任何选中（既无笔迹也无元素）时隐藏选择框与把手；
            // 此前仅判断笔迹，导致只选中图片元素时把手被隐藏。
            if (inkCanvas.GetSelectedStrokes().Count == 0 &&
                inkCanvas.GetSelectedElements().Count == 0)
            {
                HideSelectionDisplay();
                return;
            }

            if (isCornerTransform && currentCornerHandle != "" && originalSelectionBounds.Width > 0 && originalSelectionBounds.Height > 0)
            {
                // 角点组合变换中：选择框围绕枢轴缩放+旋转，把手吸附到变换后的四角/四边
                SelectionRectangle.Visibility = Visibility.Visible;
                SelectionRectangle.Margin = new Thickness(originalSelectionBounds.Left, originalSelectionBounds.Top, 0, 0);
                SelectionRectangle.Width = originalSelectionBounds.Width;
                SelectionRectangle.Height = originalSelectionBounds.Height;
                double localCx = cornerPivotPoint.X - originalSelectionBounds.Left;
                double localCy = cornerPivotPoint.Y - originalSelectionBounds.Top;
                var transformGroup = new TransformGroup();
                transformGroup.Children.Add(new ScaleTransform(cornerAppliedScale, cornerAppliedScale, localCx, localCy));
                transformGroup.Children.Add(new RotateTransform(cornerAppliedAngle, localCx, localCy));
                SelectionRectangle.RenderTransform = transformGroup;

                SelectionHandlesCanvas.Visibility = Visibility.Visible;
                Point[] pts = ComputeCornerTransformHandlePoints(cornerAppliedScale, cornerAppliedAngle);
                PositionHandleOnCanvas(TopLeftHandle, pts[0]);
                PositionHandleOnCanvas(TopRightHandle, pts[1]);
                PositionHandleOnCanvas(BottomLeftHandle, pts[2]);
                PositionHandleOnCanvas(BottomRightHandle, pts[3]);
                PositionHandleOnCanvas(TopHandle, pts[4]);
                PositionHandleOnCanvas(BottomHandle, pts[5]);
                PositionHandleOnCanvas(LeftHandle, pts[6]);
                PositionHandleOnCanvas(RightHandle, pts[7]);
                return;
            }

            // 普通（轴对齐）状态
            Rect bounds = inkCanvas.GetSelectionBounds();
            if (bounds.IsEmpty || bounds.Width <= 0 || bounds.Height <= 0)
            {
                HideSelectionDisplay();
                return;
            }

            SelectionRectangle.RenderTransform = Transform.Identity;
            SelectionRectangle.Visibility = Visibility.Visible;
            SelectionRectangle.Margin = new Thickness(bounds.Left, bounds.Top, 0, 0);
            SelectionRectangle.Width = bounds.Width;
            SelectionRectangle.Height = bounds.Height;

            UpdateSelectionHandles(bounds);
            SelectionHandlesCanvas.Visibility = Visibility.Visible;
        }

        /// <summary>隐藏选择框与所有把手</summary>
        private void HideSelectionDisplay()
        {
            if (SelectionRectangle == null || SelectionHandlesCanvas == null) return;
            SelectionRectangle.RenderTransform = Transform.Identity;
            SelectionRectangle.Visibility = Visibility.Collapsed;
            SelectionHandlesCanvas.Visibility = Visibility.Collapsed;
            // 同步隐藏圈选设置框与折叠小按钮
            if (BorderStrokeSelectionControl != null) BorderStrokeSelectionControl.Visibility = Visibility.Collapsed;
            if (BorderStrokeSelectionExpand != null) BorderStrokeSelectionExpand.Visibility = Visibility.Collapsed;
        }

        /// <summary>将 8 个把手定位到轴对齐选区的四角与四边中点</summary>
        private void UpdateSelectionHandles(Rect bounds)
        {
            double half = SelectionHandleSize / 2;
            double cx = bounds.Left + bounds.Width / 2;
            double cy = bounds.Top + bounds.Height / 2;

            // 四边中点
            TopHandle.Margin = new Thickness(cx - half, bounds.Top - half, 0, 0);
            BottomHandle.Margin = new Thickness(cx - half, bounds.Bottom - half, 0, 0);
            LeftHandle.Margin = new Thickness(bounds.Left - half, cy - half, 0, 0);
            RightHandle.Margin = new Thickness(bounds.Right - half, cy - half, 0, 0);

            // 四角
            TopLeftHandle.Margin = new Thickness(bounds.Left - half, bounds.Top - half, 0, 0);
            TopRightHandle.Margin = new Thickness(bounds.Right - half, bounds.Top - half, 0, 0);
            BottomLeftHandle.Margin = new Thickness(bounds.Left - half, bounds.Bottom - half, 0, 0);
            BottomRightHandle.Margin = new Thickness(bounds.Right - half, bounds.Bottom - half, 0, 0);
        }

        /// <summary>将把手定位到指定中心点（把手为 12x12，中心对准传入点）</summary>
        private void PositionHandleOnCanvas(FrameworkElement handle, Point center)
        {
            handle.Margin = new Thickness(center.X - SelectionHandleSize / 2, center.Y - SelectionHandleSize / 2, 0, 0);
        }

        /// <summary>
        /// 计算原始选区矩形围绕角点枢轴缩放 scale 倍并旋转 angle 度后的 8 个点：
        /// 0左上角 1右上角 2左下角 3右下角 4上边中点 5下边中点 6左边中点 7右边中点。
        /// </summary>
        private Point[] ComputeCornerTransformHandlePoints(double scale, double angle)
        {
            Rect b = originalSelectionBounds;
            Point p = cornerPivotPoint;
            double rad = angle * Math.PI / 180.0;
            double cos = Math.Cos(rad);
            double sin = Math.Sin(rad);

            Point[] pts =
            {
                new Point(b.Left, b.Top),
                new Point(b.Right, b.Top),
                new Point(b.Left, b.Bottom),
                new Point(b.Right, b.Bottom),
                new Point(b.Left + b.Width / 2, b.Top),
                new Point(b.Left + b.Width / 2, b.Bottom),
                new Point(b.Left, b.Top + b.Height / 2),
                new Point(b.Right, b.Top + b.Height / 2)
            };

            for (int i = 0; i < pts.Length; i++)
            {
                double dx = pts[i].X - p.X;
                double dy = pts[i].Y - p.Y;
                // 先以枢轴为原点缩放，再旋转
                double sx = dx * scale;
                double sy = dy * scale;
                pts[i] = new Point(p.X + sx * cos - sy * sin, p.Y + sx * sin + sy * cos);
            }
            return pts;
        }

        /// <summary>是否为角把手（角点用于组合变换：缩放+旋转）</summary>
        private bool IsCornerHandle(string handleName)
        {
            return handleName == "TopLeftHandle" || handleName == "TopRightHandle" ||
                   handleName == "BottomLeftHandle" || handleName == "BottomRightHandle";
        }

        /// <summary>开始一次把手拖拽（鼠标/触摸共用），按角/边初始化组合变换或缩放状态</summary>
        private void StartHandleOperation(string handleName, Point startPoint)
        {
            Rect bounds = inkCanvas.GetSelectionBounds();

            if (IsCornerHandle(handleName))
            {
                // 角点：同时缩放+旋转。枢轴 = 对角点（拖左上角→右下角固定；右上角→左下角；左下角→右上角；右下角→左上角）
                isCornerTransform = true;
                currentCornerHandle = handleName;
                isResizing = false;
                currentResizeHandle = "";
                originalSelectionBounds = bounds;

                if (handleName == "TopLeftHandle") cornerPivotPoint = new Point(bounds.Right, bounds.Bottom);
                else if (handleName == "TopRightHandle") cornerPivotPoint = new Point(bounds.Left, bounds.Bottom);
                else if (handleName == "BottomLeftHandle") cornerPivotPoint = new Point(bounds.Right, bounds.Top);
                else cornerPivotPoint = new Point(bounds.Left, bounds.Top);

                Point corner;
                if (handleName == "TopLeftHandle") corner = new Point(bounds.Left, bounds.Top);
                else if (handleName == "TopRightHandle") corner = new Point(bounds.Right, bounds.Top);
                else if (handleName == "BottomLeftHandle") corner = new Point(bounds.Left, bounds.Bottom);
                else corner = new Point(bounds.Right, bounds.Bottom);

                double dx = corner.X - cornerPivotPoint.X;
                double dy = corner.Y - cornerPivotPoint.Y;
                cornerStartAngle = Math.Atan2(dy, dx) * 180.0 / Math.PI;
                cornerStartDistance = Math.Sqrt(dx * dx + dy * dy);
                cornerAppliedScale = 1;
                cornerAppliedAngle = 0;
            }
            else
            {
                // 边点：缩放。对边固定，整体矩形大小调整
                isResizing = true;
                currentResizeHandle = handleName;
                isCornerTransform = false;
                currentCornerHandle = "";
                resizeStartPoint = startPoint;
                originalSelectionBounds = bounds;
            }
        }

        /// <summary>应用一次把手拖拽（鼠标/触摸共用）</summary>
        private void ApplyHandleMove(string handleName, Point currentPoint)
        {
            if (isCornerTransform && handleName == currentCornerHandle)
            {
                // 角点：由"枢轴→当前点"的向量同时决定缩放倍数与旋转角度
                double dx = currentPoint.X - cornerPivotPoint.X;
                double dy = currentPoint.Y - cornerPivotPoint.Y;
                double currentDistance = Math.Sqrt(dx * dx + dy * dy);
                if (cornerStartDistance < 0.5 || currentDistance < 0.5) return;

                double currentAngle = Math.Atan2(dy, dx) * 180.0 / Math.PI;
                double targetScale = currentDistance / cornerStartDistance;
                double targetAngle = currentAngle - cornerStartAngle;
                double deltaScale = targetScale / cornerAppliedScale;
                double deltaAngle = targetAngle - cornerAppliedAngle;

                if (Math.Abs(deltaAngle) < 0.05 && Math.Abs(deltaScale - 1) < 0.001) return;

                // 限制缩放范围，避免过小/过大
                if (targetScale < 0.05 || targetScale > 50) return;

                var m = new Matrix();
                m.ScaleAt(deltaScale, deltaScale, cornerPivotPoint.X, cornerPivotPoint.Y);
                m.RotateAt(deltaAngle, cornerPivotPoint.X, cornerPivotPoint.Y);

                var strokes = inkCanvas.GetSelectedStrokes();
                foreach (var s in strokes) s.Transform(m, false);

                // 同步缩放+旋转选中的媒体/图片元素（与笔迹一致的角点组合变换）
                var elements = inkCanvas.GetSelectedElements();
                foreach (var el in elements) ApplyElementMatrixTransform(el, m);

                cornerAppliedScale = targetScale;
                cornerAppliedAngle = targetAngle;

                UpdateBorderStrokeSelectionControlLocation();
                return;
            }

            if (!isResizing || handleName != currentResizeHandle) return;

            var delta = new Point(currentPoint.X - resizeStartPoint.X, currentPoint.Y - resizeStartPoint.Y);
            var newBounds = CalculateNewBounds(originalSelectionBounds, delta, currentResizeHandle);
            ApplyBoundsToSelection(newBounds);
            UpdateBorderStrokeSelectionControlLocation();
        }

        /// <summary>结束一次把手拖拽，提交历史并刷新显示</summary>
        private void EndHandleOperation(Rectangle handle)
        {
            isResizing = false;
            isCornerTransform = false;
            currentResizeHandle = "";
            currentCornerHandle = "";
            if (SelectionRectangle != null) SelectionRectangle.RenderTransform = Transform.Identity;
            if (handle != null) handle.ReleaseMouseCapture();
            ToCommitStrokeManipulationHistoryAfterMouseUp();
            UpdateSelectionDisplay();
        }

        /// <summary>
        /// 隐藏 InkCanvas 自带的原生虚线选择框（InkCanvasSelectionAdorner），
        /// 只保留自定义的 8 把手选择框，避免出现两个虚线框。
        /// </summary>
        private void HideInkCanvasNativeSelectionAdorner()
        {
            try
            {
                if (inkCanvas == null) return;
                var field = typeof(InkCanvas).GetField("_selectionAdorner",
                    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                if (field == null) return;
                var adorner = field.GetValue(inkCanvas) as UIElement;
                if (adorner == null) return;
                // 只隐藏视觉（Opacity=0）：保持布局与命中测试，否则会破坏 InkCanvas 原生圈选(lasso)与拖动功能。
                // 这样既能只显示自定义的单个虚线框，又不影响圈选选中笔迹。
                adorner.Opacity = 0;
                adorner.IsHitTestVisible = true;
            }
            catch (Exception ex)
            {
                LogHelper.WriteLogToFile($"隐藏InkCanvas原生选择框失败: {ex.Message}", LogHelper.LogType.Error);
            }
        }

        /// <summary>根据拖动增量和当前把手计算新的选区矩形（四边缩放）</summary>
        private Rect CalculateNewBounds(Rect originalBounds, Point delta, string handleName)
        {
            double newWidth = originalBounds.Width;
            double newHeight = originalBounds.Height;
            double newX = originalBounds.X;
            double newY = originalBounds.Y;

            switch (handleName)
            {
                case "TopHandle":
                    newY = originalBounds.Y + delta.Y;
                    newHeight = originalBounds.Height - delta.Y;
                    break;
                case "BottomHandle":
                    newHeight = originalBounds.Height + delta.Y;
                    break;
                case "LeftHandle":
                    newX = originalBounds.X + delta.X;
                    newWidth = originalBounds.Width - delta.X;
                    break;
                case "RightHandle":
                    newWidth = originalBounds.Width + delta.X;
                    break;
            }

            // 最小尺寸约束（保持对边不动，只调整被拖边）
            if (newWidth < 10)
            {
                newWidth = 10;
                if (handleName == "LeftHandle") newX = originalBounds.Right - 10;
            }
            if (newHeight < 10)
            {
                newHeight = 10;
                if (handleName == "TopHandle") newY = originalBounds.Bottom - 10;
            }

            return new Rect(newX, newY, newWidth, newHeight);
        }

        /// <summary>将新的选区矩形应用到选中的内容（对边固定的整体缩放），笔迹与媒体元素同时生效</summary>
        private void ApplyBoundsToSelection(Rect newBounds)
        {
            Rect currentBounds = inkCanvas.GetSelectionBounds();
            if (currentBounds.Width < 1 || currentBounds.Height < 1) return;

            double scaleX = newBounds.Width / currentBounds.Width;
            double scaleY = newBounds.Height / currentBounds.Height;
            double translateX = newBounds.X - currentBounds.X;
            double translateY = newBounds.Y - currentBounds.Y;

            var matrix = new Matrix();
            matrix.Translate(translateX, translateY);
            matrix.ScaleAt(scaleX, scaleY, currentBounds.X + currentBounds.Width / 2, currentBounds.Y + currentBounds.Height / 2);

            var selectedStrokes = inkCanvas.GetSelectedStrokes();
            foreach (var stroke in selectedStrokes)
            {
                stroke.Transform(matrix, false);
            }

            // 媒体/图片元素同样按选区缩放（通过 RenderTransform 叠加矩阵实现）
            var selectedElements = inkCanvas.GetSelectedElements();
            foreach (var element in selectedElements)
            {
                ApplyElementMatrixTransform(element, matrix);
            }
        }

        #region 选择把手鼠标事件

        private void SelectionHandle_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (!(sender is Rectangle handle)) return;
            StartHandleOperation(handle.Name, e.GetPosition(inkCanvas));
            handle.CaptureMouse();
            e.Handled = true;
        }

        private void SelectionHandle_MouseMove(object sender, MouseEventArgs e)
        {
            if (!(sender is Rectangle handle)) return;
            ApplyHandleMove(handle.Name, e.GetPosition(inkCanvas));
            e.Handled = true;
        }

        private void SelectionHandle_MouseUp(object sender, MouseButtonEventArgs e)
        {
            if (sender is Rectangle handle)
            {
                EndHandleOperation(handle);
                e.Handled = true;
            }
        }

        #endregion

        #region 选择把手触摸事件

        private void SelectionHandle_TouchDown(object sender, TouchEventArgs e)
        {
            if (!(sender is Rectangle handle)) return;
            StartHandleOperation(handle.Name, e.GetTouchPoint(inkCanvas).Position);
            e.Handled = true;
        }

        private void SelectionHandle_TouchMove(object sender, TouchEventArgs e)
        {
            if (!(sender is Rectangle handle)) return;
            ApplyHandleMove(handle.Name, e.GetTouchPoint(inkCanvas).Position);
            e.Handled = true;
        }

        private void SelectionHandle_TouchUp(object sender, TouchEventArgs e)
        {
            if (sender is Rectangle handle)
            {
                EndHandleOperation(handle);
                e.Handled = true;
            }
        }

        #endregion

        #endregion

        private void GridInkCanvasSelectionCover_ManipulationStarting(object sender, ManipulationStartingEventArgs e)
        {
            e.Mode = ManipulationModes.All;
        }

        private void GridInkCanvasSelectionCover_ManipulationCompleted(object sender, ManipulationCompletedEventArgs e)
        {
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
                if(ElementsManipulationHistory?.Count > 0)
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

        StrokeCollection StrokesSelectionClone = new StrokeCollection();
        List<UIElement> ElementsSelectionClone = new List<UIElement>();

        private void GridInkCanvasSelectionCover_ManipulationDelta(object sender, ManipulationDeltaEventArgs e)
        {
            try
            {
                // 正在拖拽选择把手时，禁用触控操作，避免与把手缩放/旋转冲突
                if (isResizing || isCornerTransform) return;
                if (dec.Count >= 1)
                {
                    ManipulationDelta md = e.DeltaManipulation;
                    Vector trans = md.Translation;
                    double rotate = md.Rotation;
                    Vector scale = md.Scale;
                    Point center = GetMatrixTransformCenterPoint(e.ManipulationOrigin, e.Source as FrameworkElement);
                    Matrix m = new Matrix();
                    // add Scale
                    m.ScaleAt(scale.X, scale.Y, center.X, center.Y);
                    StrokeCollection strokes = inkCanvas.GetSelectedStrokes();
                    if (StrokesSelectionClone.Count != 0)
                    {
                        strokes = StrokesSelectionClone;
                    }
                    else if (Settings.Gesture.IsEnableTwoFingerRotationOnSelection)
                    {
                        // add Rotate
                        m.RotateAt(rotate, center.X, center.Y);
                    }
                    // add Translate
                    m.Translate(trans.X, trans.Y);
                    List<UIElement> elements = new List<UIElement>();
                    if (ElementsSelectionClone.Count != 0)
                    {
                        elements = ElementsSelectionClone;
                    }
                    else
                    {
                        elements = InkCanvasElementsHelper.GetSelectedElements(inkCanvas);
                    }
                    // handle UIElements
                    foreach (UIElement element in elements)
                    {
                        // 为每个元素创建独立矩阵，将画布坐标系的变换中心转换为元素本地坐标
                        double left = InkCanvas.GetLeft(element);
                        double top = InkCanvas.GetTop(element);
                        if (double.IsNaN(left)) left = 0;
                        if (double.IsNaN(top)) top = 0;
                        Matrix elementMatrix = new Matrix();
                        elementMatrix.ScaleAt(scale.X, scale.Y, center.X - left, center.Y - top);
                        if (Settings.Gesture.IsEnableTwoFingerRotationOnSelection)
                        {
                            elementMatrix.RotateAt(rotate, center.X - left, center.Y - top);
                        }
                        elementMatrix.Translate(trans.X, trans.Y);
                        ApplyElementMatrixTransform(element, elementMatrix);
                    }
                    // handle strokes
                    foreach (Stroke stroke in strokes)
                    {
                        stroke.Transform(m, false);
                        try
                        {
                            stroke.DrawingAttributes.Width *= md.Scale.X;
                            stroke.DrawingAttributes.Height *= md.Scale.Y;
                        }
                        catch { }
                    }
                    UpdateBorderStrokeSelectionControlLocation();
                    try { RaiseElementTransformedForSelected(); } catch { }
                }
            }
            catch { }
        }

        Point lastTouchPointOnGridInkCanvasCover = new Point(0, 0);
        private void GridInkCanvasSelectionCover_PreviewTouchDown(object sender, TouchEventArgs e)
        {
            dec.Add(e.TouchDevice.Id);
            //设备1个的时候，记录中心点
            if (dec.Count == 1)
            {
                TouchPoint touchPoint = e.GetTouchPoint(null);
                centerPoint = touchPoint.Position;
                lastTouchPointOnGridInkCanvasCover = touchPoint.Position;
                editingModeBeforeCoverInteraction = inkCanvas.EditingMode;

                if (isStrokeSelectionCloneOn)
                {
                    StrokeCollection strokes = inkCanvas.GetSelectedStrokes();
                    List<UIElement> elementsList = InkCanvasElementsHelper.GetSelectedElements(inkCanvas);
                    isProgramChangeStrokeSelection = true;
                    ElementsSelectionClone = InkCanvasElementsHelper.CloneSelectedElements(inkCanvas, ref ElementsInitialHistory);
                    // 同上：克隆写入新条目后，顺带清理已失效的旧条目
                    PruneElementsInitialHistory();
                    inkCanvas.Select(new StrokeCollection());
                    StrokesSelectionClone = strokes.Clone();
                    inkCanvas.Strokes.Add(StrokesSelectionClone);
                    inkCanvas.Select(strokes, elementsList);
                    isProgramChangeStrokeSelection = false;
                }
            }
        }

        private void GridInkCanvasSelectionCover_PreviewTouchUp(object sender, TouchEventArgs e)
        {
            dec.Remove(e.TouchDevice.Id);
            if (dec.Count >= 1) return;
            isProgramChangeStrokeSelection = false;
            if (lastTouchPointOnGridInkCanvasCover == e.GetTouchPoint(null).Position)
            {
                if (lastTouchPointOnGridInkCanvasCover.X < inkCanvas.GetSelectionBounds().Left ||
                    lastTouchPointOnGridInkCanvasCover.Y < inkCanvas.GetSelectionBounds().Top ||
                    lastTouchPointOnGridInkCanvasCover.X > inkCanvas.GetSelectionBounds().Right ||
                    lastTouchPointOnGridInkCanvasCover.Y > inkCanvas.GetSelectionBounds().Bottom)
                {
                    inkCanvas.Select(new StrokeCollection());
                    // 点击选中区域外取消选中后，保持原来的选择/工具状态，避免误切回笔
                    if (inkCanvas.EditingMode != editingModeBeforeCoverInteraction)
                    {
                        inkCanvas.EditingMode = editingModeBeforeCoverInteraction;
                    }
                    StrokesSelectionClone = new StrokeCollection();
                    ElementsSelectionClone = new List<UIElement>();
                }
            }
            else if (InkCanvasElementsHelper.IsNotCanvasElementSelected(inkCanvas))
            {
                GridInkCanvasSelectionCover.Visibility = Visibility.Collapsed;
                StrokesSelectionClone = new StrokeCollection();
                ElementsSelectionClone = new List<UIElement>();
            }
            else
            {
                if (currentMode == 0)
                {
                    TextSelectionCloneToNewBoard.Text = "衍至画板";
                }
                else
                {
                    TextSelectionCloneToNewBoard.Text = "衍至新页";
                }
                GridInkCanvasSelectionCover.Visibility = Visibility.Visible;
                StrokesSelectionClone = new StrokeCollection();
                ElementsSelectionClone = new List<UIElement>();
            }
        }

        /// <summary>通知 plugin：当前选中元素被变换（移动/缩放/旋转）</summary>
        private void RaiseElementTransformedForSelected()
        {
            try
            {
                var host = Plugins.PluginHost.Instance;
                if (host == null) return;
                var selected = inkCanvas.GetSelectedElements();
                foreach (var el in selected)
                {
                    host.RaiseElementTransformed(el);
                }
            }
            catch { }
        }
    }
}
