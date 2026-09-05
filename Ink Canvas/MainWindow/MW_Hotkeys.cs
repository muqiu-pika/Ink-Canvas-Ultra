using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using Ink_Canvas.Helpers;
using System.Windows.Controls;
using System.Windows.Ink;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace Ink_Canvas
{
    public partial class MainWindow : Window
    {
        /// <summary>内置快捷键管理中心（插件可通过 IPluginHost 重绑定/复位）</summary>
        public HotkeyService HotkeyService { get; private set; }

        private void RegisterGlobalHotkeys()
        {
            if (HotkeyService != null) return; // 已在构造函数中初始化，避免重复注册

            // 使用 HotkeyService 统一管理内置快捷键，便于插件「自定义快捷键」重绑定与复位默认。
            HotkeyService = new HotkeyService(this);
            HotkeyService.RegisterAction("exit-ppt", "退出PPT放映", "放映 PPT 时按快捷键退出放映", HotkeyModifiers.MOD_SHIFT, Key.Escape, HotKey_ExitPPTSlideShow);
            HotkeyService.RegisterAction("clear", "清屏", "清除画布内容", HotkeyModifiers.MOD_CONTROL, Key.E, HotKey_Clear);
            HotkeyService.RegisterAction("capture", "截图", "截图并插入画布", HotkeyModifiers.MOD_ALT, Key.C, HotKey_Capture);
            HotkeyService.RegisterAction("toggle-visibility", "隐藏/显示", "隐藏或显示主窗口", HotkeyModifiers.MOD_ALT, Key.V, HotKey_Hide);
            HotkeyService.RegisterAction("draw-tool", "画笔", "切换到画笔", HotkeyModifiers.MOD_ALT, Key.D, HotKey_DrawTool);
            HotkeyService.RegisterAction("quit-draw-mode", "退出书写模式", "退出白板并切换到鼠标选择", HotkeyModifiers.MOD_ALT, Key.Q, HotKey_QuitDrawMode);
            HotkeyService.RegisterAction("board", "白板", "进入/退出白板模式", HotkeyModifiers.MOD_ALT, Key.B, HotKey_Board);
            HotkeyService.RegisterAction("paste", "粘贴", "从剪贴板粘贴图片", HotkeyModifiers.MOD_CONTROL | HotkeyModifiers.MOD_SHIFT, Key.V, HotKey_Paste);
            HotkeyService.RegisterAction("exit", "退出", "退出软件", HotkeyModifiers.MOD_CONTROL, Key.Q, HotKey_Exit);

            // 浮动工具栏功能快捷键（可被“自定义快捷键”插件重绑定/禁用）
            HotkeyService.RegisterAction("insert-media", "插入媒体", "打开插入媒体对话框", HotkeyModifiers.MOD_ALT, Key.M, HotKey_InsertMedia);
            HotkeyService.RegisterAction("countdown", "倒计时", "打开倒计时窗口", HotkeyModifiers.MOD_ALT, Key.T, HotKey_Countdown);
            HotkeyService.RegisterAction("random-pick", "随机抽选", "随机抽取一个序号", HotkeyModifiers.MOD_ALT, Key.R, HotKey_RandomPick);
            HotkeyService.RegisterAction("random-person", "随机选人", "随机选一名学生", HotkeyModifiers.MOD_ALT, Key.P, HotKey_RandomPerson);
            HotkeyService.RegisterAction("save-ink", "保存墨迹", "保存当前画布墨迹", HotkeyModifiers.MOD_ALT, Key.I, HotKey_SaveInk);
            HotkeyService.RegisterAction("open-ink", "打开墨迹", "打开已保存的墨迹文件", HotkeyModifiers.MOD_ALT, Key.O, HotKey_OpenInk);
            HotkeyService.RegisterAction("play-ink", "播放墨迹", "重放当前墨迹", HotkeyModifiers.MOD_ALT, Key.W, HotKey_PlayInk);

            // 窗口级快捷键（窗口聚焦时生效，走 Window.InputBindings 的 KeyBinding）
            HotkeyService.RegisterWindowAction("undo", "撤销", "撤销上一步操作", FindCommand("HotKey_Command_Undo"));
            HotkeyService.RegisterWindowAction("redo", "重做", "重做被撤销的操作", FindCommand("HotKey_Command_Redo"));
            HotkeyService.RegisterWindowAction("select", "选择", "切换到选择模式", FindCommand("HotKey_ChangeToSelect"));
            HotkeyService.RegisterWindowAction("eraser", "橡皮擦", "切换到橡皮擦（面积擦/墨迹擦）", FindCommand("HotKey_ChangeToEraser"));
            HotkeyService.RegisterWindowAction("line", "直线", "切换到单次直线绘制", FindCommand("HotKey_DrawLine"));

            // 画笔快捷键（Alt+1~Alt+5）：修复此前仅有 KeyBinding/无 Executed 处理器导致的“按键无效”问题，
            // 并纳入自定义快捷键体系，使其可被用户重绑定/复位。
            HotkeyService.RegisterWindowAction("pen1", "画笔1", "切换到第1支画笔（黑）", FindCommand("HotKey_ChangeToPen1"));
            HotkeyService.RegisterWindowAction("pen2", "画笔2", "切换到第2支画笔（红）", FindCommand("HotKey_ChangeToPen2"));
            HotkeyService.RegisterWindowAction("pen3", "画笔3", "切换到第3支画笔（绿）", FindCommand("HotKey_ChangeToPen3"));
            HotkeyService.RegisterWindowAction("pen4", "画笔4", "切换到第4支画笔（蓝）", FindCommand("HotKey_ChangeToPen4"));
            HotkeyService.RegisterWindowAction("pen5", "画笔5", "切换到第5支画笔（黄）", FindCommand("HotKey_ChangeToPen5"));

            // PPT 放映翻页/退出：由原先硬编码（PreviewKeyDown/KeyDown）改为 HotkeyService 窗口动作，
            // 使其可被“自定义快捷键”插件修改默认键（PageDown/PageUp/Escape）。
            HotkeyService.RegisterWindowAction("ppt-next", "下一页", "PPT 放映时切换到下一页", FindCommand("HotKey_PPTNext"));
            HotkeyService.RegisterWindowAction("ppt-prev", "上一页", "PPT 放映时切换到上一页", FindCommand("HotKey_PPTPrev"));
            HotkeyService.RegisterWindowAction("ppt-exit", "退出放映", "退出 PPT 放映", FindCommand("HotKey_PPTExit"));
        }

        private System.Windows.Input.RoutedCommand FindCommand(string resourceKey)
        {
            try { return FindResource(resourceKey) as System.Windows.Input.RoutedCommand; } catch { }
            return null;
        }

        private void HotKey_ExitPPTSlideShow()
        {
            if(BtnPPTSlideShowEnd.Visibility == Visibility.Visible)
            {
                BtnPPTSlideShowEnd_Click(null, null);
            }
        }

        private void HotKey_Clear()
        {
            SymbolIconDelete_MouseUp(null, null);
        }

        private async void HotKey_Capture()
        {
            await CaptureScreenshotAndInsert();
        }
        
        private void HotKey_Hide()
        {
            SymbolIconEmoji_MouseUp(null, null);
        }

        private void HotKey_DrawTool()
        {
            PenIcon_Click(null, null);
        }

        private void HotKey_QuitDrawMode()
        {
            if (currentMode != 0)
            {
                ImageBlackboard_Click(null, null);
            }
            CursorIcon_Click(null, null);
        }

        private void HotKey_Board()
        {
            ImageBlackboard_Click(null, null);
        }

        private void HotKey_Exit()
        {
            BtnExit_Click(null, null);
        }

        private async void HotKey_Paste()
        {
            // 放映 PPT 时不处理粘贴
            if (BtnPPTSlideShowEnd.Visibility == Visibility.Visible) return;
            // 主窗口被隐藏(Alt+V)时不响应，避免在没有可见主窗口的情况下弹出对话框
            if (this.Visibility != Visibility.Visible) return;

            // 在批注模式(含浮动栏模式)、白板模式下处理粘贴
            if (StackPanelCanvasControls.Visibility == Visibility.Visible 
                || currentMode == 1 
                || ViewboxFloatingBar.Visibility == Visibility.Visible)
            {
                // 记录粘贴时的当前模式
                int pasteMode = currentMode;
                int pasteWhiteboardIndex = CurrentWhiteboardIndex;
                await PasteFromClipboard(imageSource: null, pasteMode, pasteWhiteboardIndex);
            }
        }

        private async Task PasteFromClipboard(System.Windows.Media.ImageSource imageSource = null, int pasteMode = -1, int pasteWhiteboardIndex = -1)
        {
            try
            {
                // 检查剪贴板是否包含图像
                if (imageSource != null || Clipboard.ContainsImage())
                {
                    var src = imageSource ?? Clipboard.GetImage();
                    if (src != null)
                    {
                        await ShowPasteOptionDialog(src, pasteMode, pasteWhiteboardIndex);
                    }
                }
                else if (Clipboard.ContainsFileDropList())
                {
                    var files = Clipboard.GetFileDropList();
                    foreach (var file in files)
                    {
                        var ext = System.IO.Path.GetExtension(file).ToLower();
                        if (ext == ".jpg" || ext == ".jpeg" || ext == ".png" || ext == ".bmp" || ext == ".gif")
                        {
                            await PasteImageFromFile(file, pasteMode, pasteWhiteboardIndex);
                            break;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                ShowNotificationAsync($"粘贴失败: {ex.Message}");
            }
        }

        private async Task ShowPasteOptionDialog(System.Windows.Media.ImageSource imageSource, int pasteMode, int pasteWhiteboardIndex)
        {
            try
            {
                var optionWindow = new ScreenshotInsertOptionWindow();
                optionWindow.Owner = this;
                Helpers.WindowMemoryHelper.ReleaseOnClose(optionWindow);

                bool? result = optionWindow.ShowDialog();

                if (result == true)
                {
                    switch (optionWindow.SelectedOption)
                    {
                        case ScreenshotInsertOptionWindow.InsertOption.InsertToCanvas:
                            await InsertImageSourceToCanvas(imageSource, pasteMode, pasteWhiteboardIndex);
                            break;
                        case ScreenshotInsertOptionWindow.InsertOption.InsertToBoard:
                            await InsertImageSourceToBoard(imageSource);
                            break;
                    }
                }
            }
            catch (Exception ex)
            {
                ShowNotificationAsync($"粘贴失败: {ex.Message}");
            }
        }

        private async Task InsertImageSourceToCanvas(System.Windows.Media.ImageSource imageSource, int pasteMode, int pasteWhiteboardIndex)
        {
            try
            {
                // 切换到粘贴时的模式
                await Dispatcher.BeginInvoke(new Action(() =>
                {
                    if (pasteMode == 1)
                    {
                        // 粘贴时是画板模式
                        if (currentMode != 1)
                        {
                            ImageBlackboard_Click(null, null);
                        }
                        else if (pasteWhiteboardIndex > 0 && pasteWhiteboardIndex != CurrentWhiteboardIndex)
                        {
                            // 已经在画板模式，但需要切换到正确的页面
                            SwitchToWhiteboardPage(pasteWhiteboardIndex);
                        }
                    }
                    else
                    {
                        // 粘贴时是普通模式（PPT模式或其他）
                        if (currentMode == 1)
                        {
                            ImageBlackboard_Click(null, null);
                        }
                    }
                }));
                await Task.Delay(300);

                // 创建WPF Image控件
                var image = new Image
                {
                    Source = imageSource,
                    Stretch = Stretch.Uniform
                };
                RenderOptimizationHelper.EnableHighQualityCaching(image);

                string timestamp = "paste_" + DateTime.Now.ToString("yyyyMMdd_HH_mm_ss_fff");
                image.Name = timestamp;

                // 初始化TransformGroup
                var transformGroup = new TransformGroup();
                transformGroup.Children.Add(new ScaleTransform(1, 1));
                transformGroup.Children.Add(new TranslateTransform(0, 0));
                image.RenderTransform = transformGroup;

                image.IsHitTestVisible = true;
                image.Focusable = false;

                // 初始化InkCanvas选择设置
                inkCanvas.Select(new StrokeCollection());
                inkCanvas.EditingMode = InkCanvasEditingMode.None;

                // 等待图片加载完成后再进行居中处理
                image.Loaded += (sender, e) =>
                {
                    Dispatcher.BeginInvoke(new Action(() =>
                    {
                        CenterAndScaleScreenshot(image);
                        image.Cursor = Cursors.Hand;
                    }), DispatcherPriority.Loaded);
                };

                inkCanvas.Children.Add(image);
                timeMachine.CommitElementInsertHistory(image);
                inkCanvas.EditingMode = InkCanvasEditingMode.Select;

                ShowNotificationAsync("图片已粘贴到画布");
            }
            catch (Exception ex)
            {
                ShowNotificationAsync($"粘贴失败: {ex.Message}");
            }
        }

        private async Task InsertImageSourceToBoard(System.Windows.Media.ImageSource imageSource)
        {
            try
            {
                if (imageSource is BitmapSource bitmapSource)
                {
                    await Dispatcher.BeginInvoke(new Action(() =>
                    {
                        var bitmapImage = new BitmapImage();
                        using (var memoryStream = new MemoryStream())
                        {
                            var encoder = new PngBitmapEncoder();
                            encoder.Frames.Add(BitmapFrame.Create(bitmapSource));
                            encoder.Save(memoryStream);
                            memoryStream.Position = 0;

                            bitmapImage.BeginInit();
                            bitmapImage.CacheOption = BitmapCacheOption.OnLoad;
                            bitmapImage.StreamSource = memoryStream;
                            bitmapImage.EndInit();
                        }
                        AddCapturedPhoto(bitmapImage);
                        ShowNotificationAsync("图片已添加到白板照片列表");
                    }));
                }
            }
            catch (Exception ex)
            {
                ShowNotificationAsync($"添加到白板照片列表失败: {ex.Message}");
            }
        }

        private async Task PasteImageFromFile(string filePath, int pasteMode, int pasteWhiteboardIndex)
        {
            try
            {
                var bitmapImage = new BitmapImage();
                bitmapImage.BeginInit();
                bitmapImage.UriSource = new Uri(filePath);
                bitmapImage.EndInit();
                bitmapImage.Freeze();

                await ShowPasteOptionDialog(bitmapImage, pasteMode, pasteWhiteboardIndex);
            }
            catch (Exception ex)
            {
                ShowNotificationAsync($"粘贴图片失败: {ex.Message}");
            }
        }

        private void Window_MouseWheel(object sender, MouseWheelEventArgs e)
        {
            if (BtnPPTSlideShowEnd.Visibility == Visibility.Visible)
            {
                // 滚轮翻页受“自定义快捷键”中的 ppt-next/ppt-prev 启用状态约束：
                // 若用户在插件中禁用了对应动作，滚轮也不再翻页。
                bool nextEnabled = HotkeyService != null && HotkeyService.IsActionEnabled("ppt-next");
                bool prevEnabled = HotkeyService != null && HotkeyService.IsActionEnabled("ppt-prev");
                if (e.Delta >= 120 && prevEnabled)
                {
                    BtnPPTSlidesUp_Click(null, null);
                }
                else if (e.Delta <= -120 && nextEnabled)
                {
                    BtnPPTSlidesDown_Click(null, null);
                }
                else
                {
                    return;
                }
                e.Handled = true;
                return;
            }

            // 白板模式下切换到“选择”状态后，可通过滚轮操控整页内容
            if (currentMode == 1 && inkCanvas.EditingMode == InkCanvasEditingMode.Select)
            {
                HandleBoardSelectModeMouseWheel(e);
            }
        }

        /// <summary>
        /// 白板“选择”状态下的滚轮操控：
        /// Ctrl+滚轮缩放、Shift+滚轮左右平移、普通滚轮上下平移（效果与双指手势一致）。
        /// </summary>
        private void HandleBoardSelectModeMouseWheel(MouseWheelEventArgs e)
        {
            if (e == null) return;
            bool isCtrl = (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control;
            bool isShift = (Keyboard.Modifiers & ModifierKeys.Shift) == ModifierKeys.Shift;

            List<UIElement> elements = InkCanvasElementsHelper.GetAllElements(inkCanvas);

            if (isCtrl)
            {
                // 缩放：与双指放大/缩小效果一致
                double scale = e.Delta > 0 ? 1.1 : 0.9;
                Point mousePoint = e.GetPosition(inkCanvas);
                Point center = GetMatrixTransformCenterPoint(mousePoint, inkCanvas);
                Matrix m = new Matrix();
                m.ScaleAt(scale, scale, center.X, center.Y);

                foreach (UIElement element in elements)
                {
                    double left = InkCanvas.GetLeft(element);
                    double top = InkCanvas.GetTop(element);
                    if (double.IsNaN(left)) left = 0;
                    if (double.IsNaN(top)) top = 0;
                    Matrix elementMatrix = new Matrix();
                    elementMatrix.ScaleAt(scale, scale, center.X - left, center.Y - top);
                    ApplyElementMatrixTransform(element, elementMatrix);
                }

                foreach (Stroke stroke in inkCanvas.Strokes)
                {
                    stroke.Transform(m, false);
                    try
                    {
                        stroke.DrawingAttributes.Width *= scale;
                        stroke.DrawingAttributes.Height *= scale;
                    }
                    catch { }
                }
            }
            else
            {
                // 平移：Shift+滚轮左右平移，普通滚轮上下平移
                double dx = isShift ? e.Delta : 0;
                double dy = isShift ? 0 : e.Delta;
                Matrix m = new Matrix();
                m.Translate(dx, dy);

                foreach (UIElement element in elements)
                {
                    ApplyElementMatrixTransform(element, m);
                }

                foreach (Stroke stroke in inkCanvas.Strokes)
                {
                    stroke.Transform(m, false);
                }
            }

            e.Handled = true;
        }

        private void CommandBinding_CanExecute(object sender, CanExecuteRoutedEventArgs e)
        {
            e.CanExecute = true;
        }

        private void HotKey_Undo(object sender, ExecutedRoutedEventArgs e)
        {
            try
            {
                SymbolIconUndo_Click(null, null);
            }
            catch { }
        }

        private void HotKey_Redo(object sender, ExecutedRoutedEventArgs e)
        {
            try
            {
                SymbolIconRedo_Click(null, null);
            }
            catch { }
        }

        private void KeyChangeToSelect(object sender, ExecutedRoutedEventArgs e)
        {
            if (StackPanelCanvasControls.Visibility == Visibility.Visible)
            {
                SymbolIconSelect_Click(null, null);
            }
        }

        private void KeyChangeToEraser(object sender, ExecutedRoutedEventArgs e)
        {
            if (StackPanelCanvasControls.Visibility == Visibility.Visible)
            {
                if (Eraser_Icon.Background != null)
                {
                    EraserIconByStrokes_Click(null, null);
                }
                else
                {
                    EraserIcon_Click(null, null);
                }
            }
        }

        private void KeyDrawLine(object sender, ExecutedRoutedEventArgs e)
        {
            if (StackPanelCanvasControls.Visibility == Visibility.Visible)
            {
                BtnDrawLine_Click(lastMouseDownSender, null);
            }
        }

        private void KeyChangeToPen1(object sender, ExecutedRoutedEventArgs e) => SwitchToPen(0);
        private void KeyChangeToPen2(object sender, ExecutedRoutedEventArgs e) => SwitchToPen(1);
        private void KeyChangeToPen3(object sender, ExecutedRoutedEventArgs e) => SwitchToPen(2);
        private void KeyChangeToPen4(object sender, ExecutedRoutedEventArgs e) => SwitchToPen(3);
        private void KeyChangeToPen5(object sender, ExecutedRoutedEventArgs e) => SwitchToPen(4);

        /// <summary>
        /// 通过快捷键切换画笔颜色：确保处于墨迹编辑态，并切换到指定的 inkColor 索引。
        /// inkColor 映射（见 MW_PenColors.CheckLastColor）：0=黑 1=红 2=绿 3=蓝 4=黄，对应 Alt+1~Alt+5。
        /// 同时兼容浮动栏模式（currentMode==0 且 ViewboxFloatingBar 可见、StackPanelCanvasControls 折叠）——此时仅切换颜色，不破坏浮动栏布局。
        /// </summary>
        private void SwitchToPen(int inkColor)
        {
            try
            {
                // 若当前处于选择/橡皮等非墨迹态，先切回墨迹
                if (inkCanvas.EditingMode != InkCanvasEditingMode.Ink)
                {
                    inkCanvas.EditingMode = InkCanvasEditingMode.Ink;
                    forceEraser = false;
                }

                // 仅在“批注栏与浮动栏都不可见”时，才通过画笔入口进入可书写界面，
                // 避免破坏浮动栏布局（浮动栏模式下仅切换颜色即可）。
                if (currentMode == 0
                    && StackPanelCanvasControls.Visibility != Visibility.Visible
                    && ViewboxFloatingBar.Visibility != Visibility.Visible)
                {
                    PenIcon_Click(null, null);
                }

                // 切换画笔颜色（内部会调用 ColorSwitchCheck 确保 Ink 态并刷新选中提示）
                CheckLastColor(inkColor);
            }
            catch (Exception ex)
            {
                Helpers.LogHelper.WriteLogToFile($"切换到画笔颜色{inkColor}异常已拦截: {ex}");
            }
        }

        // ===== PPT 放映翻页/退出（原为硬编码 PreviewKeyDown/KeyDown，现交由 HotkeyService 管理，可自定义） =====

        private void HotKey_PPTNext(object sender, ExecutedRoutedEventArgs e)
        {
            if (BtnPPTSlideShowEnd.Visibility != Visibility.Visible) return;
            BtnPPTSlidesDown_Click(null, null);
        }

        private void HotKey_PPTPrev(object sender, ExecutedRoutedEventArgs e)
        {
            if (BtnPPTSlideShowEnd.Visibility != Visibility.Visible) return;
            BtnPPTSlidesUp_Click(null, null);
        }

        private void HotKey_PPTExit(object sender, ExecutedRoutedEventArgs e)
        {
            if (BtnPPTSlideShowEnd.Visibility != Visibility.Visible) return;
            BtnPPTSlideShowEnd_Click(null, null);
        }

        // ===== 浮动工具栏功能快捷键的回调（无参，转发到对应 Click 处理） =====

        private void HotKey_InsertMedia() => BtnMediaInsertUnified_Click(null, null);
        private void HotKey_Countdown() => ImageCountdownTimer_Click(null, null);
        private void HotKey_RandomPick() => SymbolIconRand_Click(null, null);
        private void HotKey_RandomPerson() => SymbolIconRandOne_Click(null, null);
        private void HotKey_SaveInk() => SymbolIconSaveStrokes_Click(null, null);
        private void HotKey_OpenInk() => SymbolIconOpenInkCanvasFile_Click(null, null);
        private void HotKey_PlayInk() => GridInkReplayButton_Click(null, null);
    }
}
