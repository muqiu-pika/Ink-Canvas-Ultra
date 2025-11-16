using Ink_Canvas.Helpers;
using System;
using System.Windows;
using System.Windows.Ink;

namespace Ink_Canvas
{
    public partial class MainWindow : Window
    {
        StrokeCollection[] strokeCollections = new StrokeCollection[101];
        StrokeCollection lastTouchDownStrokeCollection = new StrokeCollection();

        int CurrentWhiteboardIndex = 1, WhiteboardTotalCount = 1;
        TimeMachineHistory[][] TimeMachineHistories = new TimeMachineHistory[101][]; //最多99页，0用来存储非白板时的墨迹以便还原

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
            inkCanvas.Strokes.Clear();
            inkCanvas.Children.Clear();
            _currentCommitType = CommitReason.UserInput;
        }

        private void RestoreStrokes(bool isBackupMain = false)
        {
            try
            {
                if (TimeMachineHistories[CurrentWhiteboardIndex] == null) return; //防止白板打开后不居中
                if (isBackupMain)
                {
                    timeMachine.ImportTimeMachineHistory(TimeMachineHistories[0]);
                    foreach (var item in TimeMachineHistories[0])
                    {
                        ApplyHistoryToCanvas(item);
                    }
                }
                else
                {
                    timeMachine.ImportTimeMachineHistory(TimeMachineHistories[CurrentWhiteboardIndex]);
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
            SaveStrokes();
            ClearStrokes(true);
            int oldPage = CurrentWhiteboardIndex;
            CurrentWhiteboardIndex--;
            try { RestorePageFromDiskIfAvailable(CurrentWhiteboardIndex); } catch { }
            RestoreStrokes();
            UpdateIndexInfoDisplay();
            
            // 同步照片与设备选中状态
            try
            {
                HandlePhotoDisplayOnPageChange(CurrentWhiteboardIndex);
                UpdatePhotoSelectionIndicators();
            }
            catch { }
            
            // 通知摄像头管理器页面切换
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
            SaveStrokes();
            ClearStrokes(true);
            int oldPage = CurrentWhiteboardIndex;
            CurrentWhiteboardIndex++;
            try { RestorePageFromDiskIfAvailable(CurrentWhiteboardIndex); } catch { }
            RestoreStrokes();
            UpdateIndexInfoDisplay();
            
            // 同步照片与设备选中状态
            try
            {
                HandlePhotoDisplayOnPageChange(CurrentWhiteboardIndex);
                UpdatePhotoSelectionIndicators();
            }
            catch { }
            
            // 通知摄像头管理器页面切换
            NotifyCameraManagerPageChanged(oldPage, CurrentWhiteboardIndex);
        }

        private void BtnWhiteBoardAdd_Click(object sender, EventArgs e)
        {
            if (WhiteboardTotalCount >= 99) return;
            if (Settings.Automation.IsAutoSaveStrokesAtClear && inkCanvas.Strokes.Count > Settings.Automation.MinimumAutomationStrokeNumber)
            {
                SaveScreenshot(true);
            }
            SaveStrokes();
            ClearStrokes(true);
            WhiteboardTotalCount++;
            CurrentWhiteboardIndex++;
            if (CurrentWhiteboardIndex != WhiteboardTotalCount)
            {
                for (int i = WhiteboardTotalCount; i > CurrentWhiteboardIndex; i--)
                {
                    TimeMachineHistories[i] = TimeMachineHistories[i - 1];
                }
            }
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
            RestoreStrokes();
            UpdateIndexInfoDisplay();
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
