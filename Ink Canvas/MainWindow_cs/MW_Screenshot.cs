using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Ink_Canvas
{
    public partial class MainWindow : Window
    {
        private enum ScreenshotAction
        {
            InsertToCanvas,
            SaveToFolder,
            AddToPhotoList,
            Cancel
        }

        public void CaptureScreenRegionAndHandle()
        {
            var region = SelectScreenRegion();
            if (!region.HasValue) return;

            using (var bitmap = new Bitmap(region.Value.Width, region.Value.Height, System.Drawing.Imaging.PixelFormat.Format32bppArgb))
            {
                using (var graphics = Graphics.FromImage(bitmap))
                {
                    graphics.CopyFromScreen(region.Value.X, region.Value.Y, 0, 0, region.Value.Size, CopyPixelOperation.SourceCopy);
                }

                var image = ConvertBitmapToBitmapImage(bitmap);
                var action = ShowScreenshotOptionsWindow(image);
                if (action == null) return;

                switch (action.Value)
                {
                    case ScreenshotAction.InsertToCanvas:
                        InsertScreenshotToCanvas(image);
                        break;
                    case ScreenshotAction.SaveToFolder:
                        SaveScreenshotToCustomFolder(bitmap);
                        break;
                    case ScreenshotAction.AddToPhotoList:
                        AddCapturedPhoto(image);
                        break;
                    case ScreenshotAction.Cancel:
                        break;
                }
            }
        }

        private Rectangle? SelectScreenRegion()
        {
            Rectangle rc = System.Windows.Forms.SystemInformation.VirtualScreen;

            var overlay = new Window
            {
                WindowStyle = WindowStyle.None,
                ResizeMode = ResizeMode.NoResize,
                Left = rc.Left,
                Top = rc.Top,
                Width = rc.Width,
                Height = rc.Height,
                Topmost = true,
                ShowInTaskbar = false,
                AllowsTransparency = true,
                Background = new SolidColorBrush(System.Windows.Media.Color.FromArgb(64, 0, 0, 0)),
                Cursor = Cursors.Cross
            };

            var canvas = new System.Windows.Controls.Canvas();
            canvas.Background = System.Windows.Media.Brushes.Transparent;
            overlay.Content = canvas;

            Rectangle? result = null;
            System.Windows.Point startPoint = new System.Windows.Point();
            System.Windows.Shapes.Rectangle selectionRect = null;
            bool isDragging = false;

            canvas.MouseLeftButtonDown += (s, e) =>
            {
                isDragging = true;
                startPoint = e.GetPosition(canvas);
                if (selectionRect == null)
                {
                    selectionRect = new System.Windows.Shapes.Rectangle
                    {
                        Stroke = System.Windows.Media.Brushes.DeepSkyBlue,
                        StrokeThickness = 2,
                        Fill = new SolidColorBrush(System.Windows.Media.Color.FromArgb(40, 135, 206, 235))
                    };
                    canvas.Children.Add(selectionRect);
                }
                System.Windows.Controls.Canvas.SetLeft(selectionRect, startPoint.X);
                System.Windows.Controls.Canvas.SetTop(selectionRect, startPoint.Y);
                selectionRect.Width = 0;
                selectionRect.Height = 0;
                selectionRect.Visibility = Visibility.Visible;
                canvas.CaptureMouse();
            };

            canvas.MouseMove += (s, e) =>
            {
                if (!isDragging || selectionRect == null) return;
                var pos = e.GetPosition(canvas);
                double x = Math.Min(pos.X, startPoint.X);
                double y = Math.Min(pos.Y, startPoint.Y);
                double w = Math.Abs(pos.X - startPoint.X);
                double h = Math.Abs(pos.Y - startPoint.Y);
                System.Windows.Controls.Canvas.SetLeft(selectionRect, x);
                System.Windows.Controls.Canvas.SetTop(selectionRect, y);
                selectionRect.Width = w;
                selectionRect.Height = h;
            };

            canvas.MouseLeftButtonUp += (s, e) =>
            {
                if (!isDragging || selectionRect == null) return;
                isDragging = false;
                canvas.ReleaseMouseCapture();

                double x = System.Windows.Controls.Canvas.GetLeft(selectionRect);
                double y = System.Windows.Controls.Canvas.GetTop(selectionRect);
                double w = selectionRect.Width;
                double h = selectionRect.Height;

                if (w < 2 || h < 2)
                {
                    overlay.DialogResult = false;
                    overlay.Close();
                    return;
                }

                int rx = rc.X + (int)Math.Round(x);
                int ry = rc.Y + (int)Math.Round(y);
                int rw = (int)Math.Round(w);
                int rh = (int)Math.Round(h);
                result = new Rectangle(rx, ry, rw, rh);
                overlay.DialogResult = true;
                overlay.Close();
            };

            overlay.KeyDown += (s, e) =>
            {
                if (e.Key == Key.Escape)
                {
                    overlay.DialogResult = false;
                    overlay.Close();
                }
            };

            overlay.ShowDialog();
            return result;
        }

        private ScreenshotAction? ShowScreenshotOptionsWindow(BitmapImage image)
        {
            var window = new Window
            {
                Owner = this,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Width = 820,
                Height = 520,
                Title = "截图操作",
                ResizeMode = ResizeMode.CanResize,
                WindowStyle = WindowStyle.ToolWindow,
                Topmost = Topmost
            };

            var grid = new Grid
            {
                Margin = new Thickness(16)
            };
            grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            var imageControl = new System.Windows.Controls.Image
            {
                Source = image,
                Stretch = Stretch.Uniform
            };
            Grid.SetRow(imageControl, 0);
            grid.Children.Add(imageControl);

            var buttonsPanel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                Margin = new Thickness(0, 16, 0, 0)
            };

            var btnInsert = new Button { Content = "插入到当前画板", Margin = new Thickness(4, 0, 4, 0), MinWidth = 140, Height = 32 };
            var btnSaveFolder = new Button { Content = "保存到文件夹", Margin = new Thickness(4, 0, 4, 0), MinWidth = 120, Height = 32 };
            var btnAddToList = new Button { Content = "加入照片列表", Margin = new Thickness(4, 0, 4, 0), MinWidth = 120, Height = 32 };
            var btnCancel = new Button { Content = "取消", Margin = new Thickness(16, 0, 0, 0), MinWidth = 80, Height = 32 };

            buttonsPanel.Children.Add(btnInsert);
            buttonsPanel.Children.Add(btnSaveFolder);
            buttonsPanel.Children.Add(btnAddToList);
            buttonsPanel.Children.Add(btnCancel);

            Grid.SetRow(buttonsPanel, 1);
            grid.Children.Add(buttonsPanel);

            ScreenshotAction? result = null;

            btnInsert.Click += (s, e) =>
            {
                result = ScreenshotAction.InsertToCanvas;
                window.DialogResult = true;
                window.Close();
            };

            btnSaveFolder.Click += (s, e) =>
            {
                result = ScreenshotAction.SaveToFolder;
                window.DialogResult = true;
                window.Close();
            };

            btnAddToList.Click += (s, e) =>
            {
                result = ScreenshotAction.AddToPhotoList;
                window.DialogResult = true;
                window.Close();
            };

            btnCancel.Click += (s, e) =>
            {
                result = ScreenshotAction.Cancel;
                window.DialogResult = false;
                window.Close();
            };

            window.Content = grid;
            window.ShowDialog();
            return result;
        }

        private void InsertScreenshotToCanvas(BitmapImage image)
        {
            var imageElement = new System.Windows.Controls.Image
            {
                Source = image,
                Width = image.PixelWidth,
                Height = image.PixelHeight,
                Name = "img_" + DateTime.Now.ToString("yyyyMMdd_HH_mm_ss_fff")
            };

            CenterAndScaleElement(imageElement);
            InkCanvas.SetLeft(imageElement, 0);
            InkCanvas.SetTop(imageElement, 0);
            inkCanvas.Children.Add(imageElement);
            timeMachine.CommitElementInsertHistory(imageElement);
        }

        private void SaveScreenshotToCustomFolder(Bitmap bitmap)
        {
            var folderBrowser = new System.Windows.Forms.FolderBrowserDialog();
            var result = folderBrowser.ShowDialog();
            if (result != System.Windows.Forms.DialogResult.OK || string.IsNullOrWhiteSpace(folderBrowser.SelectedPath)) return;

            string fileName = DateTime.Now.ToString("yyyy-MM-dd HH-mm-ss-fff") + ".png";
            string path = Path.Combine(folderBrowser.SelectedPath, fileName);
            bitmap.Save(path, ImageFormat.Png);
        }

        private void SaveScreenshot(bool isHideNotification, string fileName = null)
        {
            var bitmap = GetScreenshotBitmap();
            string savePath = Settings.Automation.AutoSavedStrokesLocation + @"\Auto Saved - Screenshots";
            if (fileName == null) fileName = DateTime.Now.ToString("u").Replace(":", "-");
            if (Settings.Automation.IsSaveScreenshotsInDateFolders)
            {
                savePath += @"\" + DateTime.Now.ToString("yyyy-MM-dd");
            }
            savePath += @"\" + fileName + ".png";
            if (!Directory.Exists(Path.GetDirectoryName(savePath)))
            {
                Directory.CreateDirectory(Path.GetDirectoryName(savePath));
            }
            bitmap.Save(savePath, ImageFormat.Png);
            if (Settings.Automation.IsAutoSaveStrokesAtScreenshot)
            {
                SaveInkCanvasFile(false, false);
            }
            if (!isHideNotification)
            {
                ShowNotificationAsync("截图成功保存至 " + savePath);
            }
        }

        private void SaveScreenShotToDesktop()
        {
            var bitmap = GetScreenshotBitmap();
            string savePath = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
            bitmap.Save(savePath + @"\" + DateTime.Now.ToString("u").Replace(':', '-') + ".png", ImageFormat.Png);
            ShowNotificationAsync("截图成功保存至【桌面" + @"\" + DateTime.Now.ToString("u").Replace(':', '-') + ".png】");
            if (Settings.Automation.IsAutoSaveStrokesAtScreenshot) SaveInkCanvasFile(false, false);
        }

        private void SavePPTScreenshot(string fileName)
        {
            var bitmap = GetScreenshotBitmap();
            string savePath = Settings.Automation.AutoSavedStrokesLocation + @"\Auto Saved - PPT Screenshots";
            if (Settings.Automation.IsSaveScreenshotsInDateFolders)
            {
                savePath += @"\" + DateTime.Now.ToString("yyyy-MM-dd");
            }
            if (fileName == null) fileName = DateTime.Now.ToString("u").Replace(":", "-");
            savePath += @"\" + fileName + ".png";
            if (!Directory.Exists(Path.GetDirectoryName(savePath)))
            {
                Directory.CreateDirectory(Path.GetDirectoryName(savePath));
            }
            bitmap.Save(savePath, ImageFormat.Png);
            if (Settings.Automation.IsAutoSaveStrokesAtScreenshot)
            {
                SaveInkCanvasFile(false, false);
            }
        }

        private Bitmap GetScreenshotBitmap()
        {
            Rectangle rc = System.Windows.Forms.SystemInformation.VirtualScreen;
            var bitmap = new Bitmap(rc.Width, rc.Height, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
            using (Graphics memoryGrahics = Graphics.FromImage(bitmap))
            {
                memoryGrahics.CopyFromScreen(rc.X, rc.Y, 0, 0, rc.Size, CopyPixelOperation.SourceCopy);
            }
            return bitmap;
        }
    }
}
