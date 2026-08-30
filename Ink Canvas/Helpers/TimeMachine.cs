using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Ink;
using System.Windows.Input;
using System.Windows.Media;

namespace Ink_Canvas.Helpers
{
    public class TimeMachine
    {
        private readonly List<TimeMachineHistory> _currentStrokeHistory = new List<TimeMachineHistory>();

        private int _currentIndex = -1;

        public delegate void OnUndoStateChange(bool status);

        public event OnUndoStateChange OnUndoStateChanged;

        public delegate void OnRedoStateChange(bool status);

        public event OnRedoStateChange OnRedoStateChanged;

        /// <summary>
        /// 撤销栈保留的最大记录数。
        /// 原先这个栈没有任何上限：每条 TimeMachineHistory 都强引用一份 StrokeCollection 或
        /// UIElement（ElementInsert 类型的 Element 内含全分辨率位图），而只有 redo 分支截断时会
        /// 移除元素。于是长时间书写/反复擦除/插入图片后，被擦掉的笔迹与已删除的图片都因仍被
        /// 历史强引用而永不回收，内存单调增长。
        /// 超过上限时从最早的记录开始丢弃，这符合撤销栈的语义：最早的先变得不可撤销。
        /// </summary>
        public const int MaxHistoryCount = 100;

        private void CheckHistoryIndex()
        {
            if (_currentIndex + 1 < _currentStrokeHistory.Count)
            {
                _currentStrokeHistory.RemoveRange(_currentIndex + 1, (_currentStrokeHistory.Count - 1) - _currentIndex);
            }
        }

        /// <summary>追加一条历史记录，并把栈裁剪到上限以内，最后通知撤销/重做状态变化。</summary>
        private void AppendHistory(TimeMachineHistory item)
        {
            _currentStrokeHistory.Add(item);
            _currentIndex = _currentStrokeHistory.Count - 1;
            TrimHistoryToLimit();
            NotifyUndoRedoState();
        }

        /// <summary>
        /// 把撤销栈裁剪到 MaxHistoryCount：从头部（最早的记录）移除，并同步修正当前索引，
        /// 使 Undo/Redo 落点仍然指向同一条记录。
        /// </summary>
        private void TrimHistoryToLimit()
        {
            if (_currentStrokeHistory.Count <= MaxHistoryCount) return;

            int excess = _currentStrokeHistory.Count - MaxHistoryCount;
            _currentStrokeHistory.RemoveRange(0, excess);
            _currentIndex -= excess;
            if (_currentIndex < -1) _currentIndex = -1;
        }

        public void CommitStrokeUserInputHistory(StrokeCollection stroke)
        {
            AppendHistory(new TimeMachineHistory(stroke, TimeMachineHistoryType.UserInput, false));
        }

        public void CommitStrokeShapeHistory(StrokeCollection strokeToBeReplaced, StrokeCollection generatedStroke)
        {
            CheckHistoryIndex();
            AppendHistory(new TimeMachineHistory(generatedStroke, TimeMachineHistoryType.ShapeRecognition, false, strokeToBeReplaced));
        }

        public void CommitStrokeManipulationHistory(
            Dictionary<Stroke, Tuple<StylusPointCollection, StylusPointCollection>> stylusPointDictionary,
            Dictionary<string, Tuple<object, TransformGroup>> ElementsManipulationHistory)
        {
            CheckHistoryIndex();
            AppendHistory(new TimeMachineHistory(stylusPointDictionary, ElementsManipulationHistory, TimeMachineHistoryType.Manipulation));
        }

        public void CommitStrokeDrawingAttributesHistory(Dictionary<Stroke, Tuple<DrawingAttributes, DrawingAttributes>> drawingAttributes)
        {
            CheckHistoryIndex();
            AppendHistory(new TimeMachineHistory(drawingAttributes, TimeMachineHistoryType.DrawingAttributes));
        }

        public void CommitStrokeEraseHistory(StrokeCollection stroke, StrokeCollection sourceStroke = null)
        {
            CheckHistoryIndex();
            AppendHistory(new TimeMachineHistory(stroke, TimeMachineHistoryType.Clear, true, sourceStroke));
        }

        public void CommitElementInsertHistory(UIElement element, bool strokeHasBeenCleared = false)
        {
            CheckHistoryIndex();
            AppendHistory(new TimeMachineHistory(element, TimeMachineHistoryType.ElementInsert, strokeHasBeenCleared));
        }

        public void ClearStrokeHistory()
        {
            _currentStrokeHistory.Clear();
            _currentIndex = -1;
            NotifyUndoRedoState();
        }

        public TimeMachineHistory Undo()
        {
            if (_currentIndex < 0) return null;
            var item = _currentStrokeHistory[_currentIndex];
            item.StrokeHasBeenCleared = !item.StrokeHasBeenCleared;
            _currentIndex--;
            NotifyUndoRedoState();
            return item;
        }

        public TimeMachineHistory Redo()
        {
            if (_currentIndex >= _currentStrokeHistory.Count - 1) return null;
            var item = _currentStrokeHistory[++_currentIndex];
            item.StrokeHasBeenCleared = !item.StrokeHasBeenCleared;
            NotifyUndoRedoState();
            return item;
        }

        public TimeMachineHistory[] ExportTimeMachineHistory()
        {
            CheckHistoryIndex();
            return _currentStrokeHistory.ToArray();
        }

        /// <summary>
        /// 收集当前撤销栈中所有 Manipulation 历史引用的元素 key，追加到调用方集合。
        /// 与 ExportTimeMachineHistory 不同，本方法不会调用 CheckHistoryIndex（即不会截断
        /// 重做分支），因此没有副作用，可以安全地用于判断某条数据是否仍被撤销栈引用。
        /// </summary>
        public void CollectManipulationElementKeys(HashSet<string> keys)
        {
            if (keys == null) return;

            for (int i = 0; i < _currentStrokeHistory.Count; i++)
            {
                var history = _currentStrokeHistory[i];
                if (history == null || history.ElementsManipulationHistory == null) continue;

                foreach (var pair in history.ElementsManipulationHistory)
                {
                    if (!string.IsNullOrEmpty(pair.Key)) keys.Add(pair.Key);
                }
            }
        }

        public bool ImportTimeMachineHistory(TimeMachineHistory[] sourceHistory)
        {
            if (sourceHistory == null) return false;
            _currentStrokeHistory.Clear();
            _currentStrokeHistory.AddRange(sourceHistory);
            _currentIndex = _currentStrokeHistory.Count - 1;
            NotifyUndoRedoState();
            return true;
        }

        private void NotifyUndoRedoState()
        {
            OnUndoStateChanged?.Invoke(_currentIndex > -1);
            OnRedoStateChanged?.Invoke(_currentIndex < _currentStrokeHistory.Count - 1);
        }
    }

    public class TimeMachineHistory
    {
        public TimeMachineHistoryType CommitType;
        public bool StrokeHasBeenCleared = false;
        public StrokeCollection CurrentStroke;
        public StrokeCollection ReplacedStroke;
        public UIElement Element;
        //这里说一下 Tuple 的 Value1 是初始值 ; Value 2 是改变值
        public Dictionary<Stroke, Tuple<StylusPointCollection, StylusPointCollection>> StylusPointDictionary;
        public Dictionary<string, Tuple<object, TransformGroup>> ElementsManipulationHistory;
        public Dictionary<Stroke, Tuple<DrawingAttributes, DrawingAttributes>> DrawingAttributes;
        // UserInput
        public TimeMachineHistory(StrokeCollection currentStroke, TimeMachineHistoryType commitType, bool strokeHasBeenCleared)
        {
            CommitType = commitType;
            CurrentStroke = currentStroke;
            StrokeHasBeenCleared = strokeHasBeenCleared;
            ReplacedStroke = null;
        }
        // Clear
        public TimeMachineHistory(StrokeCollection currentStroke, TimeMachineHistoryType commitType, bool strokeHasBeenCleared, StrokeCollection replacedStroke)
        {
            CommitType = commitType;
            CurrentStroke = currentStroke;
            StrokeHasBeenCleared = strokeHasBeenCleared;
            ReplacedStroke = replacedStroke;
        }
        // StrokeManipulation, ElementManipulation
        public TimeMachineHistory(
            Dictionary<Stroke, Tuple<StylusPointCollection, StylusPointCollection>> stylusPointDictionary,
            Dictionary<string, Tuple<object, TransformGroup>> elementsManipulationHistory,
            TimeMachineHistoryType commitType)
        {
            CommitType = commitType;
            ElementsManipulationHistory = elementsManipulationHistory;
            StylusPointDictionary = stylusPointDictionary;
        }
        // trokeDrawingAttributes
        public TimeMachineHistory(Dictionary<Stroke, Tuple<DrawingAttributes, DrawingAttributes>> drawingAttributes, TimeMachineHistoryType commitType)
        {
            CommitType = commitType;
            DrawingAttributes = drawingAttributes;
        }
        // Insert UIElement
        public TimeMachineHistory(UIElement element, TimeMachineHistoryType commitType, bool strokeHasBeenCleared)
        {
            CommitType = commitType;
            Element = element;
            StrokeHasBeenCleared = strokeHasBeenCleared;
        }
    }

    public enum TimeMachineHistoryType
    {
        UserInput,
        ShapeRecognition,
        Clear,
        Manipulation,
        DrawingAttributes,
        ElementInsert
    }
}
