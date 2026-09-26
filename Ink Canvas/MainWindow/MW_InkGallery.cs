using Ink_Canvas.Helpers;
using System;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Ink;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using MessageBox = System.Windows.MessageBox;
using Point = System.Windows.Point;

namespace Ink_Canvas
{
    public partial class MainWindow : Window
    {
        #region 笔迹图库

        /// <summary>
        /// 图库目录：存放从选中墨迹保存下来的图形（ISF 二进制格式，含点数据与笔迹属性）
        /// </summary>
        private static string InkGalleryDir => Path.Combine(App.UserDataPath, "InkGallery");

        /// <summary>
        /// 待放置的图库图形（非空时处于「拖拽放置」模式，在画布上拖出一个矩形即可放置并缩放）
        /// </summary>
        private StrokeCollection _pendingGalleryStrokes;

        /// <summary>
        /// 记录缩略图上一次按下的对象，用于确保放置只响应同一次完整的左键点击
        /// </summary>
        private object _galleryThumbMouseDownObject;

        /// <summary>
        /// 触摸屏没有右键，用长按替代右键删除。按下时启动定时器并记录触点，
        /// 抬手或滑动后取消；定时器到点且手指基本没动才判定为长按
        /// </summary>
        private DispatcherTimer _galleryLongPressTimer;
        private FrameworkElement _galleryLongPressTarget;
        private TouchDevice _galleryLongPressDevice;
        private Point _galleryLongPressOrigin;

        /// <summary>
        /// 长按触发后抑制随后提升出来的鼠标事件，避免同时误入放置模式
        /// </summary>
        private DateTime _galleryThumbSuppressClickUntil = DateTime.MinValue;

        /// <summary>图库缩略图墙的手动触摸平移状态（本项目 ScrollViewer 自带的触摸平移不生效）</summary>
        private TouchDevice _galleryScrollDevice;
        private Point _galleryScrollOrigin;
        private double _galleryScrollStartOffset;
        private bool _galleryScrollDragging;

        /// <summary>
        /// 放置完成后的短暂抑制截止时间：触摸的触笔事件会被提升为鼠标事件而二次触发，
        /// 用时间窗把这些残留事件挡掉，避免多落一个点笔迹
        /// </summary>
        private DateTime _galleryInsertSuppressUntil = DateTime.MinValue;

        /// <summary>
        /// 图形面板里的图库视图（[0] 浮动栏，[1] 白板），两个面板结构相同、内容各自独立
        /// </summary>
        private readonly InkGalleryView[] _galleryViews = new InkGalleryView[2];

        /// <summary>当前拖拽放置的输入来源：触摸会同时产生触笔与提升出的鼠标事件，用来源标记去重</summary>
        private enum GalleryDragSource { None, Mouse, Stylus }

        private GalleryDragSource _galleryDragSource = GalleryDragSource.None;
        private Point _galleryDragStart;
        private GalleryPlacementAdorner _galleryAdorner;

        private sealed class InkGalleryView
        {
            public FrameworkElement PageShapes;
            public FrameworkElement PageGallery;
            public WrapPanel WrapPanel;
            public ScrollViewer Scroll;
            public TextBlock EmptyText;
            public TextBlock CountText;
            public Border TabShapes;
            public Border TabGallery;
            public string TitleBackgroundKey;
        }

        /// <summary>
        /// 初始化图库：绑定两个图形面板里的图库页与右侧竖排切换按钮，并监听画布上的拖拽放置
        /// </summary>
        private void InitInkGallery()
        {
            try
            {
                _galleryViews[0] = BindInkGalleryView(MWFloatBarHost, "FloatBarTitleBackground");
                _galleryViews[1] = BindInkGalleryView(MWBoardHost, "BoardBarTitleBackground");

                _galleryLongPressTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
                _galleryLongPressTimer.Tick += InkGalleryLongPressTimer_Tick;

                inkCanvas.PreviewStylusDown += InkCanvas_PreviewStylusDownForGallery;
                inkCanvas.PreviewStylusMove += InkCanvas_PreviewStylusMoveForGallery;
                inkCanvas.PreviewStylusUp += InkCanvas_PreviewStylusUpForGallery;
                inkCanvas.PreviewMouseDown += InkCanvas_PreviewMouseDownForGallery;
                inkCanvas.PreviewMouseMove += InkCanvas_PreviewMouseMoveForGallery;
                inkCanvas.PreviewMouseUp += InkCanvas_PreviewMouseUpForGallery;
                PreviewKeyDown += MainWindow_PreviewKeyDownForGallery;

                ReloadInkGallery();
                ShowInkGalleryPage(_galleryViews[0], false);
                ShowInkGalleryPage(_galleryViews[1], false);
            }
            catch (Exception ex)
            {
                LogHelper.WriteLogToFile($"初始化图库失败: {ex.Message}", LogHelper.LogType.Error);
            }
        }

        #region 存入图库

        /// <summary>
        /// 墨迹选中栏「添加到图库」：把当前选中的墨迹保存为图库图形
        /// </summary>
        private void BorderStrokeSelectionAddToGallery_Click(object sender, RoutedEventArgs e)
        {
            var strokes = inkCanvas.GetSelectedStrokes();
            if (strokes == null || strokes.Count == 0)
            {
                ShowNotificationAsync("请先选中要收藏的笔迹", true);
                return;
            }

            try
            {
                Directory.CreateDirectory(InkGalleryDir);
                var file = Path.Combine(InkGalleryDir, Guid.NewGuid().ToString("N") + ".isf");
                using (var fs = new FileStream(file, FileMode.Create))
                {
                    strokes.Save(fs);
                }

                ReloadInkGallery(); // 立即刷新图形面板中的图库页，立等可见
                ShowNotificationAsync("已添加到图库，可在「图形」面板的「我的图库」中查看", true);
            }
            catch (Exception ex)
            {
                LogHelper.WriteLogToFile($"墨迹存入图库失败: {ex.Message}", LogHelper.LogType.Error);
                ShowNotificationAsync("添加到图库失败", true);
            }
        }

        #endregion 存入图库

        #region 图库页与切换

        /// <summary>
        /// 绑定图形面板里已由 XAML 声明好的图库页、缩略图墙与右侧竖排切换按钮
        /// </summary>
        private InkGalleryView BindInkGalleryView(FrameworkElement host, string titleBackgroundKey)
        {
            var view = new InkGalleryView
            {
                PageShapes = (FrameworkElement)FindXamlPart(host, "ShapePageShapes"),
                PageGallery = (FrameworkElement)FindXamlPart(host, "ShapePageGallery"),
                WrapPanel = (WrapPanel)FindXamlPart(host, "WrapPanelShapeGallery"),
                Scroll = (ScrollViewer)FindXamlPart(host, "ScrollShapeGallery"),
                EmptyText = (TextBlock)FindXamlPart(host, "TextBlockShapeGalleryEmpty"),
                CountText = (TextBlock)FindXamlPart(host, "TextBlockShapeGalleryCount"),
                TabShapes = (Border)FindXamlPart(host, "BorderShapeTabShapes"),
                TabGallery = (Border)FindXamlPart(host, "BorderShapeTabGallery"),
                TitleBackgroundKey = titleBackgroundKey
            };

            if (view.PageShapes == null || view.PageGallery == null || view.WrapPanel == null || view.Scroll == null || view.TabGallery == null)
            {
                LogHelper.WriteLogToFile($"图库区块查找失败：{host?.GetType().Name} 中缺少图库页元素", LogHelper.LogType.Error);
                return null;
            }

            // 触摸屏上下滑动：本项目的 ScrollViewer 触摸平移（PanningMode）不生效，改为手动平移
            view.Scroll.TouchDown += InkGalleryScroll_TouchDown;
            view.Scroll.TouchMove += InkGalleryScroll_TouchMove;
            view.Scroll.TouchUp += InkGalleryScroll_TouchUp;
            view.Scroll.LostTouchCapture += InkGalleryScroll_LostTouchCapture;

            return view;
        }

        /// <summary>
        /// 右侧竖排切换按钮：在「默认图形」与「我的图库」之间切换面板内容
        /// </summary>
        private void ShapeTab_MouseUp(object sender, MouseButtonEventArgs e)
        {
            foreach (var view in _galleryViews)
            {
                if (view?.TabShapes == null) continue;
                if (ReferenceEquals(sender, view.TabShapes))
                {
                    ShowInkGalleryPage(view, false);
                    return;
                }
                if (ReferenceEquals(sender, view.TabGallery))
                {
                    ShowInkGalleryPage(view, true);
                    return;
                }
            }
        }

        /// <summary>
        /// 切换面板内容：两个页面叠放在同一格子里，同一时刻只显示一个
        /// </summary>
        private void ShowInkGalleryPage(InkGalleryView view, bool showGallery)
        {
            if (view?.PageShapes == null || view.PageGallery == null) return;

            view.PageShapes.Visibility = showGallery ? Visibility.Collapsed : Visibility.Visible;
            view.PageGallery.Visibility = showGallery ? Visibility.Visible : Visibility.Collapsed;

            ApplyGalleryTabVisual(view.TabShapes, !showGallery, view.TitleBackgroundKey);
            ApplyGalleryTabVisual(view.TabGallery, showGallery, view.TitleBackgroundKey);

            if (showGallery) ReloadInkGallery();
        }

        private static void ApplyGalleryTabVisual(Border tab, bool selected, string titleBackgroundKey)
        {
            if (tab == null) return;
            tab.Opacity = selected ? 1.0 : 0.5;
            if (selected) tab.SetResourceReference(Border.BackgroundProperty, titleBackgroundKey);
            else tab.Background = Brushes.Transparent;
        }

        /// <summary>
        /// 重建两个图形面板中的图库缩略图墙
        /// </summary>
        private void ReloadInkGallery()
        {
            foreach (var view in _galleryViews)
            {
                if (view?.WrapPanel == null) continue;

                view.WrapPanel.Children.Clear();

                try
                {
                    if (Directory.Exists(InkGalleryDir))
                    {
                        // 按保存时间排序：先存的在前，新存的排后面
                        foreach (var file in Directory.GetFiles(InkGalleryDir, "*.isf").OrderBy(File.GetLastWriteTime))
                        {
                            StrokeCollection strokes;
                            using (var fs = File.OpenRead(file))
                            {
                                strokes = new StrokeCollection(fs);
                            }
                            if (strokes.Count == 0) continue;

                            var thumb = RenderGalleryThumbnail(strokes);
                            if (thumb == null) continue;

                            view.WrapPanel.Children.Add(BuildGalleryThumbCell(thumb, file));
                        }
                    }
                }
                catch (Exception ex)
                {
                    LogHelper.WriteLogToFile($"加载图库失败: {ex.Message}", LogHelper.LogType.Error);
                }

                int count = view.WrapPanel.Children.Count;
                if (view.CountText != null) view.CountText.Text = count > 0 ? $"共 {count} 个" : string.Empty;
                if (view.Scroll != null) view.Scroll.Visibility = count == 0 ? Visibility.Collapsed : Visibility.Visible;
                if (view.EmptyText != null) view.EmptyText.Visibility = count == 0 ? Visibility.Visible : Visibility.Collapsed;
            }
        }

        /// <summary>
        /// 渲染墨迹集合缩略图：按 maxSize 等比缩放填满（小图形放大、大图形缩小，全貌一律可见）
        /// </summary>
        private ImageSource RenderGalleryThumbnail(StrokeCollection strokes)
        {
            try
            {
                var bounds = strokes.GetBounds();
                if (bounds.IsEmpty || bounds.Width <= 0 || bounds.Height <= 0) return null;

                const double margin = 6;
                const int maxSize = 120;

                var rawW = bounds.Width + margin * 2;
                var rawH = bounds.Height + margin * 2;
                var k = Math.Min(maxSize / rawW, maxSize / rawH);

                var dv = new DrawingVisual();
                using (var dc = dv.RenderOpen())
                {
                    // 变换：p' = (p - 包围盒左上角) * k + margin，缩放与平移合并进一个矩阵
                    var mtx = new Matrix();
                    mtx.Scale(k, k);
                    mtx.Translate(margin - k * bounds.Left, margin - k * bounds.Top);
                    dc.PushTransform(new MatrixTransform(mtx));
                    foreach (Stroke s in strokes)
                    {
                        s.Draw(dc);
                    }
                    dc.Pop();
                }

                var w = Math.Max(1, (int)Math.Ceiling(rawW * k));
                var h = Math.Max(1, (int)Math.Ceiling(rawH * k));

                var rtb = new RenderTargetBitmap(w, h, 96, 96, PixelFormats.Pbgra32);
                rtb.Render(dv);
                rtb.Freeze();
                return rtb;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// 构建单个图库缩略图格：56×48，左键选中后进入拖拽放置模式，右键可删除
        /// </summary>
        private Border BuildGalleryThumbCell(ImageSource thumb, string file)
        {
            var cell = new Border
            {
                Width = 56,
                Height = 48,
                Margin = new Thickness(2),
                CornerRadius = new CornerRadius(4),
                Background = Brushes.Transparent,
                BorderThickness = new Thickness(1),
                BorderBrush = new SolidColorBrush(Color.FromArgb(45, 128, 128, 128)), // 淡灰描边让格子可见
                Cursor = Cursors.Hand,
                Tag = file,
                ToolTip = "点击选中：在画布上按住拖动即可放置并调整大小\n右键（触摸屏长按）：从图库删除",
                Child = new Image
                {
                    Source = thumb,
                    Stretch = Stretch.Uniform,
                    Margin = new Thickness(3)
                }
            };

            cell.MouseDown += InkGalleryThumb_MouseDown;
            cell.MouseUp += InkGalleryThumb_MouseUp;
            // 触摸屏没有右键，用长按替代；这里不设置 e.Handled，让 ScrollViewer 仍能接管触摸做上下滑动
            cell.PreviewTouchDown += InkGalleryThumb_TouchDown;
            cell.PreviewTouchMove += InkGalleryThumb_TouchMove;
            cell.PreviewTouchUp += InkGalleryThumb_TouchUp;
            cell.ContextMenu = BuildGalleryContextMenu(file);
            return cell;
        }

        /// <summary>
        /// 图库缩略图的右键菜单：从图库删除（画布上已放置的内容不受影响）
        /// </summary>
        private ContextMenu BuildGalleryContextMenu(string file)
        {
            var menu = new ContextMenu();
            var deleteItem = new MenuItem { Header = "从图库删除" };
            deleteItem.Click += (s, e) => DeleteGalleryItem(file);
            menu.Items.Add(deleteItem);
            return menu;
        }

        /// <summary>
        /// 删除图库图形（带确认）：鼠标右键菜单与触摸屏长按共用同一入口
        /// </summary>
        private void DeleteGalleryItem(string file)
        {
            if (string.IsNullOrEmpty(file) || !File.Exists(file)) return;

            if (MessageBox.Show("确定要从图库删除这个图形吗？\n（画布上已插入的内容不受影响）", "删除图库图形",
                    MessageBoxButton.OKCancel, MessageBoxImage.Question) != MessageBoxResult.OK)
            {
                return;
            }

            try
            {
                File.Delete(file);
                ReloadInkGallery();
                ShowNotificationAsync("已从图库删除", true);
            }
            catch (Exception ex)
            {
                LogHelper.WriteLogToFile($"删除图库图形失败: {ex.Message}", LogHelper.LogType.Error);
            }
        }

        #region 触摸长按（替代右键）

        private void InkGalleryThumb_TouchDown(object sender, TouchEventArgs e)
        {
            _galleryLongPressTarget = sender as FrameworkElement;
            _galleryLongPressDevice = e.TouchDevice;
            _galleryLongPressOrigin = e.GetTouchPoint((IInputElement)sender).Position;

            _galleryLongPressTimer?.Stop();
            _galleryLongPressTimer?.Start();
        }

        private void InkGalleryThumb_TouchMove(object sender, TouchEventArgs e)
        {
            // 手指移动说明用户想上下滑动，取消长按判定（不设置 Handled，交给 ScrollViewer 滚动）
            var current = e.GetTouchPoint((IInputElement)sender).Position;
            if (Math.Abs(current.X - _galleryLongPressOrigin.X) > 20 ||
                Math.Abs(current.Y - _galleryLongPressOrigin.Y) > 20)
            {
                _galleryLongPressTimer?.Stop();
                _galleryLongPressTarget = null;
                _galleryLongPressDevice = null;
            }
        }

        private void InkGalleryThumb_TouchUp(object sender, TouchEventArgs e)
        {
            _galleryLongPressTimer?.Stop();
            _galleryLongPressTarget = null;
            _galleryLongPressDevice = null;
        }

        private void InkGalleryLongPressTimer_Tick(object sender, EventArgs e)
        {
            _galleryLongPressTimer?.Stop();

            var target = _galleryLongPressTarget;
            var device = _galleryLongPressDevice;
            _galleryLongPressTarget = null;
            _galleryLongPressDevice = null;

            if (target == null || device == null) return;

            // 手指已抬起 → 不是长按
            if (!device.IsActive) return;

            // 滑动开始后触摸会被 ScrollViewer 的滚动接管，此时手指已经移开原位 → 取消长按
            try
            {
                var current = device.GetTouchPoint(target).Position;
                if (Math.Abs(current.X - _galleryLongPressOrigin.X) > 20 ||
                    Math.Abs(current.Y - _galleryLongPressOrigin.Y) > 20)
                {
                    return;
                }
            }
            catch
            {
                return;
            }

            // 长按等价于右键：抑制随后提升出来的鼠标事件，避免同时误入放置模式
            _galleryThumbSuppressClickUntil = DateTime.Now.AddMilliseconds(800);
            _galleryThumbMouseDownObject = null;

            // 部分触屏设备长按后会出现触摸锁死，重新注册触摸窗口以恢复输入
            try { TouchLockFix.ReRegisterTouchWindow(this); } catch { }

            DeleteGalleryItem(target.Tag as string);
        }

        #endregion 触摸长按（替代右键）

        #region 触摸滑动（替代 ScrollViewer 自带平移）

        private void InkGalleryScroll_TouchDown(object sender, TouchEventArgs e)
        {
            if (!(sender is ScrollViewer scroll)) return;

            _galleryScrollDevice = e.TouchDevice;
            _galleryScrollOrigin = e.GetTouchPoint(scroll).Position;
            _galleryScrollStartOffset = scroll.VerticalOffset;
            _galleryScrollDragging = false;
        }

        private void InkGalleryScroll_TouchMove(object sender, TouchEventArgs e)
        {
            if (!(sender is ScrollViewer scroll)) return;
            if (_galleryScrollDevice == null || !ReferenceEquals(e.TouchDevice, _galleryScrollDevice)) return;

            var current = e.GetTouchPoint(scroll).Position;
            double dy = current.Y - _galleryScrollOrigin.Y;
            double dx = current.X - _galleryScrollOrigin.X;

            if (!_galleryScrollDragging)
            {
                // 竖直位移明显大于水平位移才算滑动，避免误伤轻点选中的语义
                if (Math.Abs(dy) < 10 || Math.Abs(dy) <= Math.Abs(dx)) return;

                _galleryScrollDragging = true;

                // 已开始滑动：取消长按删除判定，并抑制随后提升出来的鼠标事件，避免误入放置模式
                _galleryLongPressTimer?.Stop();
                _galleryLongPressTarget = null;
                _galleryLongPressDevice = null;
                _galleryThumbMouseDownObject = null;
                _galleryThumbSuppressClickUntil = DateTime.Now.AddMilliseconds(600);

                // 触摸在按下时可能已被提升为鼠标事件，此时无法捕获，退化为不捕获的平移
                try { e.TouchDevice.Capture(scroll, CaptureMode.Element); } catch { }
            }

            scroll.ScrollToVerticalOffset(_galleryScrollStartOffset - dy);
            e.Handled = true;
        }

        private void InkGalleryScroll_TouchUp(object sender, TouchEventArgs e)
        {
            EndGalleryScroll(e.TouchDevice);
        }

        private void InkGalleryScroll_LostTouchCapture(object sender, TouchEventArgs e)
        {
            EndGalleryScroll(e.TouchDevice);
        }

        private void EndGalleryScroll(TouchDevice device)
        {
            if (_galleryScrollDevice == null) return;
            if (device != null && !ReferenceEquals(device, _galleryScrollDevice)) return;

            if (_galleryScrollDragging && device != null)
            {
                try { device.Capture(null); } catch { }
            }

            _galleryScrollDevice = null;
            _galleryScrollDragging = false;
        }

        #endregion 触摸滑动（替代 ScrollViewer 自带平移）

        #endregion 图库页与切换

        #region 拖拽放置

        private void InkGalleryThumb_MouseDown(object sender, MouseButtonEventArgs e)
        {
            _galleryThumbMouseDownObject = e.ChangedButton == MouseButton.Left ? sender : null;
        }

        /// <summary>
        /// 点击图库缩略图：读取图形并进入「拖拽放置」模式，等待用户在画布上拖出放置框
        /// </summary>
        private void InkGalleryThumb_MouseUp(object sender, MouseButtonEventArgs e)
        {
            // 只响应左键：右键释放会先于 ContextMenu 弹出触发本事件，若不拦截会误入放置模式
            if (e.ChangedButton != MouseButton.Left) return;
            // 触摸长按删除或滑动刚触发：抑制随之提升出来的鼠标事件，避免同时误入放置模式
            if (DateTime.Now < _galleryThumbSuppressClickUntil) return;
            if (_galleryThumbMouseDownObject != sender) return;
            _galleryThumbMouseDownObject = null;

            var file = (sender as FrameworkElement)?.Tag as string;
            if (file == null || !File.Exists(file)) return;

            try
            {
                StrokeCollection strokes;
                using (var fs = File.OpenRead(file))
                {
                    strokes = new StrokeCollection(fs);
                }
                if (strokes.Count == 0) return;

                _pendingGalleryStrokes = strokes;

                // 清除墨迹选中：否则选中覆盖层（GridInkCanvasSelectionCover）仍可见并会截获
                // 接下来的拖拽，导致事件到不了 inkCanvas 的 Preview 处理器而无法放置
                inkCanvas.Select(new StrokeCollection());

                // 收起图形面板，避免挡住即将拖出的放置框（面板被钉住时保持展开）
                try
                {
                    if ((bool)ToggleSwitchDrawShapeBorderAutoHide.IsOn) CollapseBorderDrawShape();
                }
                catch { }

                ShowNotificationAsync("在画布上按住拖动即可放置图形（拖动范围同时决定位置与大小，按 Esc 取消）", true);
            }
            catch (Exception ex)
            {
                LogHelper.WriteLogToFile($"读取图库图形失败: {ex.Message}", LogHelper.LogType.Error);
            }
        }

        private void InkCanvas_PreviewStylusDownForGallery(object sender, StylusDownEventArgs e)
        {
            if (_pendingGalleryStrokes == null)
            {
                // 刚放置完的残留事件（触摸会被提升为鼠标事件）直接吞掉，避免多落一个点
                if (DateTime.Now < _galleryInsertSuppressUntil) e.Handled = true;
                return;
            }
            e.Handled = BeginGalleryDrag(GalleryDragSource.Stylus, e.GetPosition(inkCanvas));
        }

        private void InkCanvas_PreviewMouseDownForGallery(object sender, MouseButtonEventArgs e)
        {
            if (_pendingGalleryStrokes == null)
            {
                if (DateTime.Now < _galleryInsertSuppressUntil) e.Handled = true;
                return;
            }
            if (e.ChangedButton != MouseButton.Left) return;
            e.Handled = BeginGalleryDrag(GalleryDragSource.Mouse, e.GetPosition(inkCanvas));
        }

        private void InkCanvas_PreviewStylusMoveForGallery(object sender, StylusEventArgs e)
        {
            if (_galleryDragSource != GalleryDragSource.Stylus) return;
            e.Handled = UpdateGalleryDrag(e.GetPosition(inkCanvas));
        }

        private void InkCanvas_PreviewMouseMoveForGallery(object sender, MouseEventArgs e)
        {
            if (_galleryDragSource != GalleryDragSource.Mouse) return;
            e.Handled = UpdateGalleryDrag(e.GetPosition(inkCanvas));
        }

        private void InkCanvas_PreviewStylusUpForGallery(object sender, StylusEventArgs e)
        {
            if (_galleryDragSource != GalleryDragSource.Stylus) return;
            e.Handled = EndGalleryDrag(e.GetPosition(inkCanvas));
        }

        private void InkCanvas_PreviewMouseUpForGallery(object sender, MouseButtonEventArgs e)
        {
            if (_galleryDragSource != GalleryDragSource.Mouse) return;
            if (e.ChangedButton != MouseButton.Left) return;
            e.Handled = EndGalleryDrag(e.GetPosition(inkCanvas));
        }

        private void MainWindow_PreviewKeyDownForGallery(object sender, KeyEventArgs e)
        {
            if (e.Key != Key.Escape) return;

            if (_galleryDragSource != GalleryDragSource.None)
            {
                // 拖拽中按 Esc：只放弃当前这一笔，保留放置模式
                AbortGalleryDrag();
                e.Handled = true;
                return;
            }

            if (_pendingGalleryStrokes == null) return;

            _pendingGalleryStrokes = null;
            e.Handled = true;
            ShowNotificationAsync("已取消放置", true);
        }

        private bool BeginGalleryDrag(GalleryDragSource source, Point position)
        {
            // 同一次触摸已被触笔事件接管时，忽略随后提升出的鼠标事件
            if (_galleryDragSource != GalleryDragSource.None) return true;

            _galleryDragSource = source;
            _galleryDragStart = position;

            try
            {
                if (source == GalleryDragSource.Mouse) inkCanvas.CaptureMouse();
                else inkCanvas.CaptureStylus();
            }
            catch { }

            EnsureGalleryAdorner();
            UpdateGalleryAdorner(position);
            return true;
        }

        private bool UpdateGalleryDrag(Point position)
        {
            if (_galleryDragSource == GalleryDragSource.None) return false;
            UpdateGalleryAdorner(position);
            return true;
        }

        private bool EndGalleryDrag(Point position)
        {
            if (_galleryDragSource == GalleryDragSource.None) return false;

            var source = _galleryDragSource;
            _galleryDragSource = GalleryDragSource.None;
            ReleaseGalleryCapture(source);

            RemoveGalleryAdorner();
            return InsertPendingGalleryStrokes(BuildGalleryDragRect(position));
        }

        /// <summary>放弃当前拖拽（按 Esc），保留放置模式以便重新拖一次</summary>
        private void AbortGalleryDrag()
        {
            var source = _galleryDragSource;
            _galleryDragSource = GalleryDragSource.None;
            ReleaseGalleryCapture(source);
            RemoveGalleryAdorner();
        }

        private void ReleaseGalleryCapture(GalleryDragSource source)
        {
            try
            {
                if (source == GalleryDragSource.Mouse) inkCanvas.ReleaseMouseCapture();
                else if (source == GalleryDragSource.Stylus) inkCanvas.ReleaseStylusCapture();
            }
            catch { }
        }

        /// <summary>把拖拽的起止点换算成放置框（保持左上/右下规范化，负数宽高会被归一化）</summary>
        private Rect BuildGalleryDragRect(Point end)
        {
            var start = _galleryDragStart;
            return new Rect(
                Math.Min(start.X, end.X),
                Math.Min(start.Y, end.Y),
                Math.Abs(end.X - start.X),
                Math.Abs(end.Y - start.Y));
        }

        /// <summary>
        /// 把待放置图形等比缩放后放进拖出的矩形（居中，不拉伸变形）；轻点未拖动时按原始大小放置
        /// </summary>
        private bool InsertPendingGalleryStrokes(Rect rect)
        {
            var source = _pendingGalleryStrokes;
            _pendingGalleryStrokes = null; // 先清空：触摸的触笔/鼠标双事件只会放置一次
            if (source == null || source.Count == 0) return false;

            try
            {
                var strokes = source.Clone();
                var bounds = strokes.GetBounds();
                if (bounds.IsEmpty || bounds.Width <= 0 || bounds.Height <= 0) return false;

                // 拖动框过小视为轻点落点，按原始大小放置
                var k = rect.Width < 8 || rect.Height < 8
                    ? 1.0
                    : Math.Min(rect.Width / bounds.Width, rect.Height / bounds.Height);

                var cx = rect.Left + rect.Width / 2;
                var cy = rect.Top + rect.Height / 2;

                var mtx = new Matrix();
                mtx.Scale(k, k);
                mtx.Translate(cx - k * (bounds.Left + bounds.Width / 2),
                              cy - k * (bounds.Top + bounds.Height / 2));
                strokes.Transform(mtx, false);

                _currentCommitType = CommitReason.CodeInput;
                try
                {
                    inkCanvas.Strokes.Add(strokes);
                    timeMachine.CommitStrokeUserInputHistory(strokes);
                }
                finally
                {
                    _currentCommitType = CommitReason.UserInput;
                }

                ShowNotificationAsync("图形已插入画布", true);
                _galleryInsertSuppressUntil = DateTime.Now.AddMilliseconds(350);
                return true;
            }
            catch (Exception ex)
            {
                LogHelper.WriteLogToFile($"插入图库图形失败: {ex.Message}", LogHelper.LogType.Error);
                return false;
            }
        }

        #endregion 拖拽放置

        #region 拖拽预览

        private void EnsureGalleryAdorner()
        {
            if (_galleryAdorner != null || _pendingGalleryStrokes == null) return;

            try
            {
                var layer = AdornerLayer.GetAdornerLayer(inkCanvas);
                if (layer == null) return;

                _galleryAdorner = new GalleryPlacementAdorner(inkCanvas, _pendingGalleryStrokes);
                layer.Add(_galleryAdorner);
            }
            catch (Exception ex)
            {
                LogHelper.WriteLogToFile($"创建放置预览失败: {ex.Message}", LogHelper.LogType.Error);
                _galleryAdorner = null;
            }
        }

        private void UpdateGalleryAdorner(Point position)
        {
            _galleryAdorner?.SetRect(BuildGalleryDragRect(position));
        }

        private void RemoveGalleryAdorner()
        {
            if (_galleryAdorner == null) return;

            try
            {
                AdornerLayer.GetAdornerLayer(inkCanvas)?.Remove(_galleryAdorner);
            }
            catch { }

            _galleryAdorner = null;
        }

        /// <summary>
        /// 拖拽放置时的预览层：画出放置虚线框，并把图形等比缩放后半透明地画进框里，
        /// 让用户在松手前就能看到最终位置与大小
        /// </summary>
        private sealed class GalleryPlacementAdorner : Adorner
        {
            private static readonly Pen OutlinePen = CreateOutlinePen();
            private static readonly Brush FillBrush = CreateFillBrush();

            private readonly StrokeCollection _strokes;
            private Rect _rect;

            public GalleryPlacementAdorner(UIElement adornedElement, StrokeCollection strokes) : base(adornedElement)
            {
                _strokes = strokes;
                IsHitTestVisible = false; // 不参与命中测试，避免挡住画布输入
            }

            public void SetRect(Rect rect)
            {
                _rect = rect;
                InvalidateVisual();
            }

            private static Pen CreateOutlinePen()
            {
                var pen = new Pen(new SolidColorBrush(Color.FromRgb(0x4A, 0x90, 0xD9)), 1.5)
                {
                    DashStyle = new DashStyle(new double[] { 5, 3 }, 0)
                };
                pen.Freeze();
                return pen;
            }

            private static Brush CreateFillBrush()
            {
                var brush = new SolidColorBrush(Color.FromArgb(0x1E, 0x4A, 0x90, 0xD9));
                brush.Freeze();
                return brush;
            }

            protected override void OnRender(DrawingContext dc)
            {
                if (_rect.Width < 1 || _rect.Height < 1) return;

                dc.DrawRectangle(FillBrush, OutlinePen, _rect);

                var bounds = _strokes.GetBounds();
                if (bounds.IsEmpty || bounds.Width <= 0 || bounds.Height <= 0) return;

                // 与最终落笔使用同一套等比缩放规则，预览即所得
                var k = Math.Min(_rect.Width / bounds.Width, _rect.Height / bounds.Height);
                var mtx = new Matrix();
                mtx.Scale(k, k);
                mtx.Translate(_rect.Left + (_rect.Width - bounds.Width * k) / 2 - k * bounds.Left,
                              _rect.Top + (_rect.Height - bounds.Height * k) / 2 - k * bounds.Top);

                dc.PushTransform(new MatrixTransform(mtx));
                dc.PushOpacity(0.75);
                foreach (Stroke s in _strokes)
                {
                    s.Draw(dc);
                }
                dc.Pop();
                dc.Pop();
            }
        }

        #endregion 拖拽预览

        #endregion 笔迹图库
    }
}
