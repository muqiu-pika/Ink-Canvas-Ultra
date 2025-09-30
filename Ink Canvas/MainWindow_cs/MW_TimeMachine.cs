﻿using Ink_Canvas.Helpers;
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
        private StrokeCollection CuboidStrokeCollection;
        private Dictionary<Stroke, Tuple<StylusPointCollection, StylusPointCollection>> StrokeManipulationHistory;
        private Dictionary<Stroke, StylusPointCollection> StrokeInitialHistory = new Dictionary<Stroke, StylusPointCollection>();
        private Dictionary<string, Tuple<object, TransformGroup>> ElementsManipulationHistory;

        // object maybe TransformGroup or ElementData
        private Dictionary<string, object> ElementsInitialHistory = new Dictionary<string, object>();

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
            _currentCommitType = CommitReason.CodeInput;
            if (item.CommitType == TimeMachineHistoryType.UserInput)
            {
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
                if (item.StrokeHasBeenCleared)
                {

                    foreach (var strokes in item.CurrentStroke)
                    {
                        if (inkCanvas.Strokes.Contains(strokes))
                            inkCanvas.Strokes.Remove(strokes);
                    }
                    foreach (var strokes in item.ReplacedStroke)
                    {
                        if (!inkCanvas.Strokes.Contains(strokes))
                            inkCanvas.Strokes.Add(strokes);
                    }
                }
                else
                {
                    foreach (var strokes in item.CurrentStroke)
                    {
                        if (!inkCanvas.Strokes.Contains(strokes))
                            inkCanvas.Strokes.Add(strokes);
                    }
                    foreach (var strokes in item.ReplacedStroke)
                    {
                        if (inkCanvas.Strokes.Contains(strokes))
                            inkCanvas.Strokes.Remove(strokes);
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
                if (!item.StrokeHasBeenCleared)
                {
                    inkCanvas.Children.Add(item.Element);
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
            var key = sender as Stroke;
            var currentValue = key.DrawingAttributes.Clone();
            DrawingAttributesHistory.TryGetValue(key, out var previousTuple);
            var previousValue = previousTuple?.Item1 ?? currentValue.Clone();
            var needUpdateValue = !DrawingAttributesHistoryFlag[e.PropertyGuid].Contains(key);
            if (needUpdateValue)
            {
                DrawingAttributesHistoryFlag[e.PropertyGuid].Add(key);
                Debug.Write(e.PreviousValue.ToString());
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
            StrokeInitialHistory[sender as Stroke] = e.NewStylusPoints.Clone();
        }

        private void Stroke_StylusPointsChanged(object sender, EventArgs e)
        {
            if (sender == null)
            {
                return;
            }
            var stroke = sender as Stroke;
            var selectedStrokes = inkCanvas.GetSelectedStrokes();
            int count = selectedStrokes.Count > 0 ? selectedStrokes.Count : inkCanvas.Strokes.Count;

            if (StrokeManipulationHistory == null)
            {
                StrokeManipulationHistory = new Dictionary<Stroke, Tuple<StylusPointCollection, StylusPointCollection>>();
            }
            StrokeManipulationHistory[stroke] =
                new Tuple<StylusPointCollection, StylusPointCollection>(StrokeInitialHistory[stroke], stroke.StylusPoints.Clone());
            if (StrokeManipulationHistory.Count == count && dec.Count == 0 && !isGridInkCanvasSelectionCoverMouseDown)
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
                        StrokeInitialHistory[item.Key] = item.Value.Item2;
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
