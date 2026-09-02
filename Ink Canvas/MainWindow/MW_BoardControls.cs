using Ink_Canvas.Helpers;
using System;
using System.Windows;
using System.Windows.Ink;

namespace Ink_Canvas
{
    public partial class MainWindow : Window
    {
        // 原 strokeCollections[101] 已移除：全仓只有 3 处写入、0 处读取，是纯死状态。
        // 其中 MW_FloatBarIcons 清空画板时会 inkCanvas.Strokes.Clone() 把整页笔迹存进来，
        // 既付出一次全量深拷贝的 CPU，又把整页笔迹长期钉住（最多 101 份 StrokeCollection）。
        StrokeCollection lastTouchDownStrokeCollection = new StrokeCollection();

        int CurrentWhiteboardIndex = 1, WhiteboardTotalCount = 1;
        TimeMachineHistory[][] TimeMachineHistories = new TimeMachineHistory[101][];

        private void SaveStrokes(bool isBackupMain = false)
        {
            if (isBackupMain)
            {
                var timeMachineHistory = timeMachine.ExportTimeMachineHistory();
                TimeMachineHistories[0] = timeMachineHistory;
                timeMachine.ClearStrokeHistory();

            }
            else
            {
                var timeMachineHistory = timeMachine.ExportTimeMachineHistory();
                TimeMachineHistories[CurrentWhiteboardIndex] = timeMachineHistory;
                timeMachine.ClearStrokeHistory();
            }
        }

        private void ClearStrokes(bool isErasedByCode)
        {
            _currentCommitType = CommitReason.ClearingCanvas;
            if (isErasedByCode) _currentCommitType = CommitReason.CodeInput;
            // 先清空进行中的触摸/实时墨迹跟踪状态（StrokeVisualList / VisualCanvasList /
            // TouchDownPointsList / StylusPreviewModeByDeviceId / dec 等），
            // 再清理 inkCanvas.Children。否则 inkCanvas.Children.Clear() 会把预览 VisualCanvas
            // 直接从可视树移除，却仍被跟踪字典引用（悬空），且 dec/isWaitUntilNextTouchDown 残留，
            // 导致翻页/清屏后在新页写第一笔长笔画时，后半段输入被丢（断触）。
            try { ResetTouchState(); } catch { }
            isWaitUntilNextTouchDown = false;
            inkCanvas.Strokes.Clear();
            inkCanvas.Children.Clear();

            currentCameraImage = null;
            currentPhotoImage = null;

            _currentCommitType = CommitReason.UserInput;
        }

        private void RestoreStrokes(bool isBackupMain = false)
        {
            try
            {
                if (isBackupMain)
                {
                    if (TimeMachineHistories[0] == null) return;
                    if (!timeMachine.ImportTimeMachineHistory(TimeMachineHistories[0])) return;
                    foreach (var item in TimeMachineHistories[0])
                    {
                        ApplyHistoryToCanvas(item);
                    }
                }
                else
                {
                    if (TimeMachineHistories[CurrentWhiteboardIndex] == null) return; //防止白板打开后不居中
                    if (!timeMachine.ImportTimeMachineHistory(TimeMachineHistories[CurrentWhiteboardIndex])) return;
                    foreach (var item in TimeMachineHistories[CurrentWhiteboardIndex])
                    {
                        ApplyHistoryToCanvas(item);
                    }
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
            // 删页后数组尾部会残留失效引用：上面的上移循环把 i+1 拷到 i，
            // 拷完之后旧 WhiteboardTotalCount 与 WhiteboardTotalCount+1 两个槽位仍持有
            // 被删页（及其后继页）的历史，而它们已落在新的页码范围之外，
            // 永远不会被 RestoreStrokes 读取。显式置 null 让 GC 可以回收。
            ClearStalePageHistories();
            RestoreStrokes();
            UpdateIndexInfoDisplay();
        }

        /// <summary>
        /// 把页码范围之外的历史槽位置 null，释放删除页面后残留的引用。
        ///
        /// 重要：TimeMachineHistories 对普通白板页而言就是内容存储本身——
        /// RestoreStrokes 靠回放历史重建画布，而普通页并不落盘
        /// （只有文档页会经 SaveDocumentPageIfNeeded 写入磁盘，也正因如此换页时
        ///  只有 pageDocumentMapping 中的页才会被置 null）。
        /// 因此只能清理超出 WhiteboardTotalCount 的槽位，范围内的页一律不能动，否则会丢内容。
        /// </summary>
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
            /*
            if (WhiteboardTotalCount == 1)
            {
                //BtnWhiteBoardDelete.IsEnabled = false;
            }
            else
            {
                //BtnWhiteBoardDelete.IsEnabled = true;
            }
            */
        }
        
        // 通知摄像头管理器页面切换
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
