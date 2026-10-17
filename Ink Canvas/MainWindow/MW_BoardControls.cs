using Ink_Canvas.Helpers;
using System;
using System.Collections.Generic;
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

        /// <summary>
        /// 换页/切模式前的页面保存：页面历史数组 = 「内容基线」+「原始撤销栈」两段。
        ///
        /// 背景：普通白板页不落盘，<see cref="TimeMachineHistories"/>[page] 就是内容存储本身，
        /// RestoreStrokes 靠回放它重建画布。而撤销栈有 MaxHistoryCount=100 的上限
        /// （超限即从头部丢弃最早记录），当一页累计超过 100 条历史时
        /// （>100 笔墨迹，或墨迹夹杂移动/擦除/图形识别），早期记录已被裁掉，
        /// 翻页回来只回放得到最后 ≤100 条 —— 之前的墨迹永久丢失。
        ///
        /// 因此保存时先构建「内容基线」：把当前画布上全部存活笔迹与全部子元素包成
        /// 带 IsContentBaseline 标记的记录放在数组头部，再拼接原始撤销栈。
        /// 恢复时先回放基线完整重建画布（不受 100 条上限影响），再仅导入原始撤销栈
        /// 供撤销/重做使用（撤销深度与上限策略保持不变）。
        /// 基线只引用画布上仍存在的对象，不会钉住已擦除的笔迹/已删除的元素，
        /// 因此不破坏 100 条上限原本的内存保护意图。
        /// </summary>
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

        /// <summary>
        /// 构建当前页的「内容基线」：全部存活笔迹打成一条 UserInput 记录，
        /// 全部子元素各打一条 ElementInsert 记录，均标记 IsContentBaseline。
        /// 只做引用拷贝（不深拷），且仅引用画布上仍存在的对象。
        /// </summary>
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
            // 先清空进行中的触摸/实时墨迹跟踪状态（StrokeVisualList / VisualCanvasList /
            // TouchDownPointsList / StylusPreviewModeByDeviceId / dec 等），
            // 再清理 inkCanvas.Children。否则 inkCanvas.Children.Clear() 会把预览 VisualCanvas
            // 直接从可视树移除，却仍被跟踪字典引用（悬空），且 dec/isWaitUntilNextTouchDown 残留，
            // 导致翻页/清屏后在新页写第一笔长笔画时，后半段输入被丢（断触）。
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
        /// RestoreStrokes 先回放数组头部的「内容基线」记录（IsContentBaseline）重建画布，
        /// 再导入其余「原始撤销栈」记录供撤销/重做，而普通页并不落盘
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
