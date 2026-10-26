using Ink_Canvas.Helpers;
using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Ink;

namespace Ink_Canvas
{
    public partial class MainWindow : Window
    {
        StrokeCollection lastTouchDownStrokeCollection = new StrokeCollection();

        int CurrentWhiteboardIndex = 1, WhiteboardTotalCount = 1;
        TimeMachineHistory[][] TimeMachineHistories = new TimeMachineHistory[101][];

        private void SaveStrokes(bool isBackupMain = false)
        {
            int pageIndex = isBackupMain ? 0 : CurrentWhiteboardIndex;

            // 上次恢复时导入撤销栈的基线条目带 IsContentBaseline 标记，导出时剔除，
            // 避免每轮翻页都把旧基线重复拼进新数组（线性增长）。
            var exported = timeMachine.ExportTimeMachineHistory();
            var rawHistory = new List<TimeMachineHistory>();
            foreach (var item in exported)
            {
                if (item != null && item.IsContentBaseline) continue;
                rawHistory.Add(item);
            }

            var baseline = BuildCurrentPageContentBaseline();
            var pageHistory = new TimeMachineHistory[baseline.Length + rawHistory.Count];
            baseline.CopyTo(pageHistory, 0);
            rawHistory.CopyTo(pageHistory, baseline.Length);

            TimeMachineHistories[pageIndex] = pageHistory;
            timeMachine.ClearStrokeHistory();
        }

        private TimeMachineHistory[] BuildCurrentPageContentBaseline()
        {
            var baseline = new List<TimeMachineHistory>();
            try
            {
                if (inkCanvas == null) return baseline.ToArray();

                if (inkCanvas.Strokes != null && inkCanvas.Strokes.Count > 0)
                {
                    baseline.Add(new TimeMachineHistory(
                        new StrokeCollection(inkCanvas.Strokes),
                        TimeMachineHistoryType.UserInput, false)
                    { IsContentBaseline = true });
                }

                if (inkCanvas.Children != null)
                {
                    foreach (UIElement child in inkCanvas.Children)
                    {
                        // 书写/触摸预览层的 VisualCanvas 是临时可视化对象，不是页面内容，不能进基线
                        if (child is Helpers.VisualCanvas) continue;
                        baseline.Add(new TimeMachineHistory(
                            child, TimeMachineHistoryType.ElementInsert, false)
                        { IsContentBaseline = true });
                    }
                }
            }
            catch (Exception ex)
            {
                LogHelper.WriteLogToFile($"构建页面内容基线失败: {ex.Message}", LogHelper.LogType.Warning);
            }
            return baseline.ToArray();
        }

        private void ClearStrokes(bool isErasedByCode)
        {
            _currentCommitType = CommitReason.ClearingCanvas;
            if (isErasedByCode) _currentCommitType = CommitReason.CodeInput;
            try { ResetTouchState(); } catch { }
            isWaitUntilNextTouchDown = false;
            inkCanvas.Strokes.Clear();
            inkCanvas.Children.Clear();

            // 激光笔轨迹属于纯视觉元素，清屏/翻页时必须一并清除，避免残留在下一页
            try { ClearLaserTrails(); } catch { }

            currentCameraImage = null;
            currentPhotoImage = null;

            _currentCommitType = CommitReason.UserInput;
        }

        private void RestoreStrokes(bool isBackupMain = false)
        {
            try
            {
                var pageHistory = isBackupMain ? TimeMachineHistories[0] : TimeMachineHistories[CurrentWhiteboardIndex];
                if (pageHistory == null) return; //防止白板打开后不居中

                // 第一段：先回放「内容基线」，完整重建画布（含被撤销栈 100 条上限裁掉的早期墨迹）。
                // 全程处于 CodeInput：进入白板/换页时模式切换过渡窗口已开启，若不标记，
                // 恢复的笔迹会被 StrokesOnStrokesChanged 的残留笔画拦截误删（白板笔迹消失）。
                // 逐条 try/catch：单条记录异常不应中断整页恢复。
                foreach (var item in pageHistory)
                {
                    if (item == null || !item.IsContentBaseline) continue;
                    try { ApplyHistoryToCanvas(item); }
                    catch (Exception ex)
                    {
                        LogHelper.WriteLogToFile($"回放页面内容基线失败: {ex.Message}", LogHelper.LogType.Warning);
                    }
                }

                // 第二段：仅导入「原始撤销栈」供撤销/重做使用，不再回放到画布——
                // 基线已完整重建画布，重复回放既无必要，也会把同一批对象再施放一遍。
                var rawHistory = new List<TimeMachineHistory>();
                foreach (var item in pageHistory)
                {
                    if (item != null && item.IsContentBaseline) continue;
                    rawHistory.Add(item);
                }
                if (rawHistory.Count > 0)
                {
                    timeMachine.ImportTimeMachineHistory(rawHistory.ToArray());
                }
                else
                {
                    timeMachine.ClearStrokeHistory();
                }
            }
            catch { }
        }

        private void BtnWhiteBoardSwitchPrevious_Click(object sender, EventArgs e)
        {
            if (CurrentWhiteboardIndex <= 1) return;
            // 换页前先排空残留输入并开启过渡窗口，再取消进行中笔画，避免卡顿时延迟提交的笔迹落到错误的页面
            BeginBoardModeSwitch();
            CancelInProgressStroke();
            int oldPage = CurrentWhiteboardIndex;
            SaveStrokes();
            SaveDocumentPageIfNeeded(oldPage);
            // 离开的文档页已落盘，清空其时间机器历史以释放持有的照片大位图
            if (pageDocumentMapping.ContainsKey(oldPage)) TimeMachineHistories[oldPage] = null;
            ClearStrokes(true);
            CurrentWhiteboardIndex--;
            bool documentRestored = false;
            try { documentRestored = RestoreDocumentPageIfAvailable(CurrentWhiteboardIndex); } catch { }
            if (!documentRestored)
            {
                try { RestorePageFromDiskIfAvailable(CurrentWhiteboardIndex); } catch { }
                // 磁盘恢复失败时，若该页有文档映射，则从内存照片列表重建文档瓦片，避免照片丢失
                bool rebuiltFromMemory = ReinsertDocumentPhotosFromMemorySafe(CurrentWhiteboardIndex);
                // 已从内存照片重建文档瓦片后，不再回放时间机器历史：历史中同样含照片插入提交，
                // 回放会导致照片被重复插入。仅在内存重建失败时才回放历史以恢复笔迹。
                if (!rebuiltFromMemory) RestoreStrokes();
                if (rebuiltFromMemory)
                {
                    LogHelper.WriteLogToFile($"换页时磁盘恢复失败，已从内存照片重建文档页照片: {CurrentWhiteboardIndex}", Helpers.LogHelper.LogType.Trace);
                }
            }
            UpdateIndexInfoDisplay();

            try
            {
                HandlePhotoDisplayOnPageChange(CurrentWhiteboardIndex);
                UpdatePhotoSelectionIndicators();
            }
            catch { }

            NotifyCameraManagerPageChanged(oldPage, CurrentWhiteboardIndex);
        }

        private void BtnWhiteBoardSwitchNext_Click(object sender, EventArgs e)
        {
            if (Settings.Automation.IsAutoSaveStrokesAtClear && inkCanvas.Strokes.Count > Settings.Automation.MinimumAutomationStrokeNumber)
            {
                SaveScreenshot(true);
            }
            if (CurrentWhiteboardIndex >= WhiteboardTotalCount)
            {
                BtnWhiteBoardAdd_Click(sender, e);
                return;
            }
            // 换页前先排空残留输入并开启过渡窗口，再取消进行中笔画，避免卡顿时延迟提交的笔迹落到错误的页面
            BeginBoardModeSwitch();
            CancelInProgressStroke();
            int oldPage = CurrentWhiteboardIndex;
            SaveStrokes();
            SaveDocumentPageIfNeeded(oldPage);
            // 离开的文档页已落盘，清空其时间机器历史以释放持有的照片大位图
            if (pageDocumentMapping.ContainsKey(oldPage)) TimeMachineHistories[oldPage] = null;
            ClearStrokes(true);
            CurrentWhiteboardIndex++;
            bool documentRestored = false;
            try { documentRestored = RestoreDocumentPageIfAvailable(CurrentWhiteboardIndex); } catch { }
            if (!documentRestored)
            {
                try { RestorePageFromDiskIfAvailable(CurrentWhiteboardIndex); } catch { }
                bool rebuiltFromMemory = ReinsertDocumentPhotosFromMemorySafe(CurrentWhiteboardIndex);
                // 已从内存照片重建文档瓦片后，不再回放时间机器历史，避免照片被重复插入。
                if (!rebuiltFromMemory) RestoreStrokes();
                if (rebuiltFromMemory)
                {
                    LogHelper.WriteLogToFile($"换页时磁盘恢复失败，已从内存照片重建文档页照片: {CurrentWhiteboardIndex}", Helpers.LogHelper.LogType.Trace);
                }
            }
            UpdateIndexInfoDisplay();

            try
            {
                HandlePhotoDisplayOnPageChange(CurrentWhiteboardIndex);
                UpdatePhotoSelectionIndicators();
            }
            catch { }

            NotifyCameraManagerPageChanged(oldPage, CurrentWhiteboardIndex);
        }

        private void BtnWhiteBoardAdd_Click(object sender, EventArgs e)
        {
            if (WhiteboardTotalCount >= 99) return;
            // 加页前先排空残留输入并开启过渡窗口，再取消进行中笔画，避免卡顿时延迟提交的笔迹落到新页面
            BeginBoardModeSwitch();
            CancelInProgressStroke();
            if (Settings.Automation.IsAutoSaveStrokesAtClear && inkCanvas.Strokes.Count > Settings.Automation.MinimumAutomationStrokeNumber)
            {
                SaveScreenshot(true);
            }
            int oldPage = CurrentWhiteboardIndex;
            SaveStrokes();
            SaveDocumentPageIfNeeded(oldPage);
            // 离开的文档页已落盘，清空其时间机器历史以释放持有的照片大位图
            if (pageDocumentMapping.ContainsKey(oldPage)) TimeMachineHistories[oldPage] = null;
            ClearStrokes(true);
            WhiteboardTotalCount++;

            var currentHistory = timeMachine.ExportTimeMachineHistory();

            CurrentWhiteboardIndex = WhiteboardTotalCount;

            TimeMachineHistories[CurrentWhiteboardIndex] = new TimeMachineHistory[0];

            UpdateIndexInfoDisplay();
        }

        private void BtnWhiteBoardDelete_Click(object sender, RoutedEventArgs e)
        {
            ClearStrokes(true);
            if (CurrentWhiteboardIndex != WhiteboardTotalCount)
            {
                for (int i = CurrentWhiteboardIndex; i <= WhiteboardTotalCount; i++)
                {
                    TimeMachineHistories[i] = TimeMachineHistories[i + 1];
                }
            }
            else
            {
                CurrentWhiteboardIndex--;
            }
            WhiteboardTotalCount--;
            ClearStalePageHistories();
            RestoreStrokes();
            UpdateIndexInfoDisplay();
        }

        private void ClearStalePageHistories()
        {
            if (WhiteboardTotalCount < 1) return;

            for (int i = WhiteboardTotalCount + 1; i < TimeMachineHistories.Length; i++)
            {
                TimeMachineHistories[i] = null;
            }
        }

        private void UpdateIndexInfoDisplay()
        {
            TextBlockWhiteBoardIndexInfo.Text = string.Format("{0} / {1}", CurrentWhiteboardIndex, WhiteboardTotalCount);

            if (CurrentWhiteboardIndex == WhiteboardTotalCount)
            {
                BoardLeftPannelNextPage1.Width = 26;
                BoardLeftPannelNextPage2.Width = 0;
                BoardLeftPannelNextPageTextBlock.Text = "加页";
            }
            else
            {
                BoardLeftPannelNextPage1.Width = 0;
                BoardLeftPannelNextPage2.Width = 26;
                BoardLeftPannelNextPageTextBlock.Text = "下一页";
            }

            if (CurrentWhiteboardIndex == 1)
            {
                BtnWhiteBoardSwitchPrevious.IsEnabled = false;
            }
            else
            {
                BtnWhiteBoardSwitchPrevious.IsEnabled = true;
            }

            if (CurrentWhiteboardIndex == 99)
            {
                BoardLeftPannelNextPage1.IsEnabled = false;
            }
            else
            {
                BoardLeftPannelNextPage1.IsEnabled = true;
            }

            if (WhiteboardTotalCount == 99)
            {
                BtnBoardAddPage.IsEnabled = false;
            }
            else
            {
                BtnBoardAddPage.IsEnabled = true;
            }
        }
        
        private void NotifyCameraManagerPageChanged(int oldPage, int newPage)
        {
            // 通过反射或其他方式获取摄像头管理器实例并调用页面切换处理
            try
            {
                // 假设MainWindow类中有cameraDeviceManager字段
                var cameraManagerField = GetType().GetField("cameraDeviceManager", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                var cameraManager = cameraManagerField?.GetValue(this);
                var handlePageChangedMethod = cameraManager?.GetType().GetMethod("HandlePageChanged", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
                handlePageChangedMethod?.Invoke(cameraManager, new object[] { newPage });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"通知摄像头管理器页面切换失败: {ex.Message}");
            }
        }
    }
}
