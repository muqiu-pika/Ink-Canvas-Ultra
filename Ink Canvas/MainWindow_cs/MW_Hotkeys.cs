using System;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
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
        private void RegisterGlobalHotkeys()
        {
            Hotkey.Regist(this, HotkeyModifiers.MOD_SHIFT, Key.Escape, HotKey_ExitPPTSlideShow);
            Hotkey.Regist(this, HotkeyModifiers.MOD_CONTROL, Key.E, HotKey_Clear);
            Hotkey.Regist(this, HotkeyModifiers.MOD_ALT, Key.C, HotKey_Capture);
            Hotkey.Regist(this, HotkeyModifiers.MOD_ALT, Key.V, HotKey_Hide);
            Hotkey.Regist(this, HotkeyModifiers.MOD_ALT, Key.D, HotKey_DrawTool);
            Hotkey.Regist(this, HotkeyModifiers.MOD_ALT, Key.Q, HotKey_QuitDrawMode);
            Hotkey.Regist(this, HotkeyModifiers.MOD_ALT, Key.B, HotKey_Board);
            Hotkey.Regist(this, HotkeyModifiers.MOD_CONTROL, Key.V, HotKey_Paste);
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

        private async void HotKey_Paste()
        {
            // 仅在批注模式或白板模式下处理粘贴
            if (StackPanelCanvasControls.Visibility == Visibility.Visible || currentMode == 1)
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
                var optionWindow = new Windows.ScreenshotInsertOptionWindow();
                optionWindow.Owner = this;

                bool? result = optionWindow.ShowDialog();

                if (result == true)
                {
                    switch (optionWindow.SelectedOption)
                    {
                        case Windows.ScreenshotInsertOptionWindow.InsertOption.InsertToCanvas:
                            await InsertImageSourceToCanvas(imageSource, pasteMode, pasteWhiteboardIndex);
                            break;
                        case Windows.ScreenshotInsertOptionWindow.InsertOption.InsertToBoard:
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
                RenderOptions.SetBitmapScalingMode(image, BitmapScalingMode.HighQuality);

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
            if (BtnPPTSlideShowEnd.Visibility != Visibility.Visible) return;
            if (e.Delta >= 120)
            {
                BtnPPTSlidesUp_Click(null, null);
            }
            else if (e.Delta <= -120)
            {
                BtnPPTSlidesDown_Click(null, null);
            }
        }

        private void Main_Grid_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (BtnPPTSlideShowEnd.Visibility != Visibility.Visible) return;

            if (e.Key == Key.Down || e.Key == Key.PageDown || e.Key == Key.Right || e.Key == Key.N || e.Key == Key.Space)
            {
                BtnPPTSlidesDown_Click(null, null);
            }
            if (e.Key == Key.Up || e.Key == Key.PageUp || e.Key == Key.Left || e.Key == Key.P)
            {
                BtnPPTSlidesUp_Click(null, null);
            }
        }

        private void Window_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Escape)
            {
                KeyExit(null, null);
            }
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

        private void KeyExit(object sender, ExecutedRoutedEventArgs e)
        {
            BtnPPTSlideShowEnd_Click(null, null);
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
    }
}
