using Ink_Canvas.Plugins;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Web.Script.Serialization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;

namespace Ink_Canvas.Plugins.PptControlCustomizer
{
    /// <summary>
    /// PPT 控件自定义插件：调整四个翻页控件（下方左/右、侧面左/右）与浮动工具栏的
    /// 缩放、透明度、位移。全部通过公开 WPF API（FindName / 属性写入）实现，不改动宿主。
    ///
    /// 实现要点（均为刻意为之，勿"优化"回去）：
    /// - 位移走 Margin（宿主显示动画 ShowWithScaleFromBottom/Left/Right 会整体覆盖 RenderTransform）；
    /// - 缩放走 LayoutTransform（与 RenderTransform 缩放动画无冲突）；
    /// - 透明度写在内层 Border 上（Viewbox 自身被显示动画体系占用）；
    /// - 每个控件独立 try/catch + 空值防护：任一控件失败不影响其余（避免"初始位置的内容还在"）；
    /// - 挂 Visibility / Opacity 属性监听自愈：宿主重新显示面板或覆写浮动栏透明度时立即按配置重应用。
    /// </summary>
    public class PptControlCustomizerPlugin : IPlugin
    {
        private const string PluginId = "ink-canvas.ppt-control-customizer";
        private const string PluginDllName = "PptControlCustomizerPlugin.dll";

        // 面板索引：0=下方左 1=下方右 2=侧面左 3=侧面右
        private const int PanelCount = 4;

        private IPluginHost _host;
        private string _pluginDirectory;

        // 配置（% 与 px），加载时从 JSON 读入
        private readonly double[] _scale = { 100, 100, 100, 100 };
        private readonly double[] _opacity = { 40, 40, 40, 40 };
        private readonly double[] _offset = { 0, 0, 0, 0 };
        private double _barOpacity = 100;

        // 解析到的宿主控件
        private Viewbox[] _navBoxes;
        private Border[] _navBorders;
        private UIElement _viewboxFloatingBar;

        // 首次解析时捕获的原始值（禁用/卸载时还原）
        private bool _capturedOriginals;
        private readonly Thickness[] _origMargins = new Thickness[PanelCount];
        private readonly Transform[] _origLayout = new Transform[PanelCount];
        private readonly double[] _origBorderOpacity = { double.NaN, double.NaN, double.NaN, double.NaN };
        private double _origBarOpacity = double.NaN;

        // 复用的缩放变换（避免每次应用都新建）
        private readonly ScaleTransform[] _scaleTransforms = new ScaleTransform[PanelCount];

        // 每面板：上次实际写入 Margin 的有效位移（越界收拢后的值）与沿位移方向的最近尺寸
        private readonly double[] _appliedOffset = new double[PanelCount];
        private readonly double[] _lastSize = new double[PanelCount];

        // 属性监听
        private bool _hooked;
        private readonly DependencyPropertyDescriptor[] _visibilityDescriptors = new DependencyPropertyDescriptor[PanelCount];
        private readonly EventHandler[] _visibilityHandlers = new EventHandler[PanelCount];
        private readonly DependencyPropertyDescriptor[] _marginDescriptors = new DependencyPropertyDescriptor[PanelCount];
        private readonly EventHandler[] _marginHandlers = new EventHandler[PanelCount];
        private DependencyPropertyDescriptor _barOpacityDescriptor;
        private EventHandler _barOpacityHandler;
        private bool _suppressBarOpacityHook;

        /// <summary>宿主最近写入的浮动栏场景不透明度（1.0 / 0.95 / 0.8 / 0.75），用户百分比叠加其上。</summary>
        private double _barBaseOpacity = 1.0;

        // 设置面板滑条引用（供「恢复默认」复位）
        private sealed class SliderRef
        {
            public Slider Slider;
            public double Def;
        }
        private readonly List<SliderRef> _panelSliders = new List<SliderRef>();

        public PluginManifest Manifest { get; } = new PluginManifest
        {
            Id = PluginId,
            Name = "PPT 控件自定义",
            Version = "1.0.0",
            Author = "muqiu",
            Description = "自定义 PPT 放映时的四个翻页控件（下方左/右、侧面左/右）与浮动工具栏的缩放、透明度和位移。在插件工坊「设置」中按控件独立调整，放映中拖动滑条实时生效；越界自动拉回屏幕，禁用插件后恢复默认外观。",
            EntryAssembly = PluginDllName,
            EntryClass = "Ink_Canvas.Plugins.PptControlCustomizer.PptControlCustomizerPlugin",
            MinHostVersion = "26.10.6"
        };

        public void Initialize(IPluginHost host)
        {
            _host = host ?? throw new ArgumentNullException(nameof(host));
            _pluginDirectory = FindPluginDirectory();

            LoadConfig();

            try { _host.RegisterSettingsPanel(BuildSettingsPanelBody); }
            catch (Exception ex) { Log("注册设置面板失败 " + ex.Message, PluginLogLevel.Warning); }

            // 宿主窗口显示后再解析/应用（Initialize 发生在 MainWindow 构造阶段）
            try
            {
                var mw = host.MainWindow;
                Action start = AttachToMainWindow;
                if (mw != null && mw.IsLoaded) Application.Current.Dispatcher.BeginInvoke(start);
                else if (mw != null) mw.Loaded += OnMainWindowLoaded;
                else Application.Current.Dispatcher.BeginInvoke(start);
            }
            catch (Exception ex) { Log("挂接主窗口失败 " + ex.Message, PluginLogLevel.Warning); }
        }

        public void Shutdown()
        {
            try { UnhookEvents(); } catch { }
            try { RestoreOriginals(); } catch { }
            try { _host?.UnregisterSettingsPanel(); } catch { }
            _panelSliders.Clear();
            _host = null;
        }

        // ---------- 挂接与解析 ----------

        private void OnMainWindowLoaded(object sender, RoutedEventArgs e)
        {
            var mw = _host?.MainWindow;
            if (mw != null) mw.Loaded -= OnMainWindowLoaded;
            Application.Current.Dispatcher.BeginInvoke((Action)AttachToMainWindow);
        }

        private void AttachToMainWindow()
        {
            try
            {
                if (!ResolveControls())
                {
                    Log("未能解析宿主控件（MWPPTHost / MWFloatBarHost），功能不可用", PluginLogLevel.Warning);
                    return;
                }
                CaptureOriginalsIfNeeded();
                HookEvents();
                ApplyAll();
            }
            catch (Exception ex) { Log("挂接主窗口异常 " + ex.Message, PluginLogLevel.Warning); }
        }

        /// <summary>通过公开 FindName API 解析宿主控件；任何一步失败都返回 false（不抛异常）。</summary>
        private bool ResolveControls()
        {
            try
            {
                var mw = _host?.MainWindow as FrameworkElement;
                if (mw == null) return false;

                var pptHost = mw.FindName("MWPPTHost") as FrameworkElement;
                if (pptHost == null) return false;

                string[] names =
                {
                    "PPTNavigationBottomLeft", "PPTNavigationBottomRight",
                    "PPTNavigationSidesLeft", "PPTNavigationSidesRight"
                };
                var boxes = new Viewbox[PanelCount];
                var borders = new Border[PanelCount];
                for (int i = 0; i < PanelCount; i++)
                {
                    boxes[i] = pptHost.FindName(names[i]) as Viewbox;
                    borders[i] = boxes[i]?.Child as Border;
                }

                var floatHost = mw.FindName("MWFloatBarHost") as FrameworkElement;
                var bar = floatHost?.FindName("ViewboxFloatingBar") as UIElement;

                if (boxes.Any(b => b == null) || bar == null) return false;

                _navBoxes = boxes;
                _navBorders = borders;
                _viewboxFloatingBar = bar;
                return true;
            }
            catch (Exception ex) { Log("解析宿主控件失败 " + ex.Message, PluginLogLevel.Warning); return false; }
        }

        private bool EnsureResolved()
        {
            if (_navBoxes != null && _navBoxes.All(b => b != null) && _viewboxFloatingBar != null) return true;
            return ResolveControls();
        }

        private void CaptureOriginalsIfNeeded()
        {
            if (_capturedOriginals || _navBoxes == null) return;
            for (int i = 0; i < PanelCount; i++)
            {
                var box = _navBoxes[i];
                if (box == null) continue;
                _origMargins[i] = box.Margin;
                _origLayout[i] = box.LayoutTransform;
                if (_navBorders[i] != null) _origBorderOpacity[i] = _navBorders[i].Opacity;
            }
            var bar = _viewboxFloatingBar as UIElement;
            if (bar != null)
            {
                _origBarOpacity = bar.Opacity;
                _barBaseOpacity = bar.Opacity;
            }
            _capturedOriginals = true;
        }

        // ---------- 属性监听（自愈） ----------

        private void HookEvents()
        {
            if (_hooked || _navBoxes == null) return;
            try
            {
                for (int i = 0; i < PanelCount; i++)
                {
                    var box = _navBoxes[i];
                    if (box == null) continue;
                    int index = i; // 闭包捕获
                    EventHandler handler = (s, e) => OnNavVisibilityChanged(index);
                    _visibilityDescriptors[i] = DependencyPropertyDescriptor.FromProperty(UIElement.VisibilityProperty, typeof(Viewbox));
                    _visibilityDescriptors[i].AddValueChanged(box, handler);
                    _visibilityHandlers[i] = handler;

                    // 宿主覆写 Margin（如放映中切换「位置」下拉）时立即恢复插件位移；
                    // 钩子按 _appliedOffset 比对，自身写入与越界回拉均满足期望值，不会循环触发
                    EventHandler marginHandler = (s, e) => OnNavMarginChanged(index);
                    _marginDescriptors[i] = DependencyPropertyDescriptor.FromProperty(FrameworkElement.MarginProperty, typeof(Viewbox));
                    _marginDescriptors[i].AddValueChanged(box, marginHandler);
                    _marginHandlers[i] = marginHandler;
                }

                var bar = _viewboxFloatingBar as UIElement;
                if (bar != null)
                {
                    _barOpacityDescriptor = DependencyPropertyDescriptor.FromProperty(UIElement.OpacityProperty, typeof(Viewbox));
                    _barOpacityHandler = OnBarOpacityChanged;
                    _barOpacityDescriptor.AddValueChanged(bar, _barOpacityHandler);
                }
                _hooked = true;
            }
            catch (Exception ex) { Log("挂接属性监听失败 " + ex.Message, PluginLogLevel.Warning); }
        }

        private void UnhookEvents()
        {
            if (!_hooked) return;
            try
            {
                for (int i = 0; i < PanelCount; i++)
                {
                    if (_visibilityDescriptors[i] != null && _navBoxes != null && _navBoxes[i] != null)
                        _visibilityDescriptors[i].RemoveValueChanged(_navBoxes[i], _visibilityHandlers[i]);
                    _visibilityDescriptors[i] = null;
                    _visibilityHandlers[i] = null;
                    if (_marginDescriptors[i] != null && _navBoxes != null && _navBoxes[i] != null)
                        _marginDescriptors[i].RemoveValueChanged(_navBoxes[i], _marginHandlers[i]);
                    _marginDescriptors[i] = null;
                    _marginHandlers[i] = null;
                }
                var bar = _viewboxFloatingBar as UIElement;
                if (_barOpacityDescriptor != null && bar != null && _barOpacityHandler != null)
                    _barOpacityDescriptor.RemoveValueChanged(bar, _barOpacityHandler);
                _barOpacityDescriptor = null;
                _barOpacityHandler = null;
            }
            catch { }
            _hooked = false;
        }

        /// <summary>面板从隐藏变可见（放映开始、折叠展开、模式切换等）时重应用一次，消除任何宿主侧的状态覆写。</summary>
        private void OnNavVisibilityChanged(int index)
        {
            try
            {
                var box = _navBoxes != null && index < PanelCount ? _navBoxes[index] : null;
                if (box == null || box.Visibility != Visibility.Visible) return;
                Application.Current?.Dispatcher?.BeginInvoke(DispatcherPriority.Loaded, (Action)(() =>
                {
                    ApplyPanel(index);
                    ScheduleBoundsCheck(index);
                }));
            }
            catch { }
        }

        /// <summary>
        /// Margin 被改写时恢复插件位移：宿主（如放映中切换「位置」下拉）写入的 Margin 与
        /// 期望值（按 _appliedOffset 计算）不符时重应用；插件自身写入与越界回拉写入的值
        /// 均等于期望值，因此不会循环触发。
        /// </summary>
        private void OnNavMarginChanged(int index)
        {
            try
            {
                var box = _navBoxes != null && index < PanelCount ? _navBoxes[index] : null;
                if (box == null || box.Visibility != Visibility.Visible) return;
                var expected = ComputeMargin(index, box, Clamp(_appliedOffset[index], -300, 300));
                if (box.Margin != expected) ApplyPanel(index);
            }
            catch { }
        }

        /// <summary>宿主覆写浮动栏不透明度（放映开始/结束写入 0.8/0.75/0.95/1）时，立即叠加用户百分比。</summary>
        private void OnBarOpacityChanged(object sender, EventArgs e)
        {
            if (_suppressBarOpacityHook) return;
            try
            {
                var bar = sender as UIElement;
                if (bar == null) return;
                _barBaseOpacity = bar.Opacity;
                ApplyFloatingBarOpacity();
            }
            catch { }
        }

        // ---------- 应用 ----------

        private void ApplyAll()
        {
            if (!EnsureResolved()) return;
            for (int i = 0; i < PanelCount; i++) ApplyPanel(i);
            ApplyFloatingBarOpacity();
        }

        /// <summary>应用单个翻页控件的缩放/透明度/位移。独立 try/catch：单个失败不影响其余控件。</summary>
        private void ApplyPanel(int index)
        {
            try
            {
                var box = _navBoxes != null && index < PanelCount ? _navBoxes[index] : null;
                if (box == null) return;
                var border = index < PanelCount && _navBorders != null ? _navBorders[index] : null;
                if (border == null) border = box.Child as Border;

                double scale = Clamp(_scale[index], 50, 200) / 100.0;
                double opacity = Clamp(_opacity[index], 10, 100) / 100.0;
                double offset = Clamp(_offset[index], -300, 300);

                // 缩放：LayoutTransform（复用变换实例，值不变不写，避免触发布局抖动）
                var st = _scaleTransforms[index];
                if (st == null)
                {
                    st = new ScaleTransform(scale, scale);
                    _scaleTransforms[index] = st;
                }
                else if (!CloseTo(st.ScaleX, scale) || !CloseTo(st.ScaleY, scale))
                {
                    st.ScaleX = scale;
                    st.ScaleY = scale;
                }
                if (box.LayoutTransform != st) box.LayoutTransform = st;

                // 透明度：内层 Border
                if (border != null && !CloseTo(border.Opacity, opacity)) border.Opacity = opacity;

                // 位移：并入 Margin（正=右/下）；先按已知尺寸做越界收拢，实际尺寸变化后再由越界回拉修正
                double size = index < 2 ? box.ActualWidth : box.ActualHeight;
                if (size > 0) _lastSize[index] = size;
                double effective = ClampOffsetToRoom(index, offset);
                _appliedOffset[index] = effective;
                var margin = ComputeMargin(index, box, effective);
                if (box.Margin != margin) box.Margin = margin;
            }
            catch (Exception ex) { Log("应用控件 #" + index + " 失败 " + ex.Message, PluginLogLevel.Warning); }
        }

        /// <summary>按面板沿位移方向的最近已知尺寸，把有效位移限制在保留 20px 可见的范围内。</summary>
        private double ClampOffsetToRoom(int index, double offset)
        {
            double size = _lastSize[index];
            var mw = _host?.MainWindow;
            if (size <= 0 || mw == null) return offset;
            double window = index < 2 ? mw.ActualWidth : mw.ActualHeight;
            if (window <= 0) return offset;
            double room = Math.Max(0, window - size - 20);
            return Math.Max(-room, Math.Min(room, offset));
        }

        /// <summary>
        /// 计算含位移的 Margin。位移语义：底部控件正=向右，侧面控件正=向下，
        /// 对三种对齐（靠边/居中）都成立（居中用对称正负 Margin 实现）。
        /// </summary>
        private static Thickness ComputeMargin(int index, FrameworkElement box, double offset)
        {
            if (index < 2) // 底部：水平位移；左间距 2、底间距 2 与宿主 XAML 默认一致
                return new Thickness(2 + offset, 0, -offset, 2);

            // 侧面：垂直位移；水平间距沿用宿主 XAML（左块左边 6 / 右块右边 6）
            double left = index == 2 ? 6 : 0;
            double right = index == 2 ? 0 : 6;
            return new Thickness(left, offset, right, -offset);
        }

        /// <summary>
        /// 布局后的越界回拉：拿到真实渲染尺寸后重算有效位移，若与当前应用值不符则
        /// 在不改配置的前提下收拢 Margin，保证控件至少留 20px 在屏幕内。
        /// </summary>
        private void ScheduleBoundsCheck(int index)
        {
            var dispatcher = Application.Current?.Dispatcher;
            if (dispatcher == null) return;
            dispatcher.BeginInvoke(DispatcherPriority.Loaded, (Action)(() =>
            {
                try
                {
                    var box = _navBoxes != null && index < PanelCount ? _navBoxes[index] : null;
                    var mw = _host?.MainWindow;
                    if (box == null || mw == null || box.Visibility != Visibility.Visible) return;
                    double size = index < 2 ? box.ActualWidth : box.ActualHeight;
                    double window = index < 2 ? mw.ActualWidth : mw.ActualHeight;
                    if (size <= 0 || window <= 0) return;

                    _lastSize[index] = size;
                    double configOff = Clamp(_offset[index], -300, 300);
                    double effective = ClampOffsetToRoom(index, configOff);
                    if (Math.Abs(effective - _appliedOffset[index]) > 0.5)
                    {
                        _appliedOffset[index] = effective;
                        box.Margin = ComputeMargin(index, box, effective);
                        Log("控件 #" + index + " 位移 " + configOff.ToString("F0") + "px 超出屏幕，已临时收拢为 " +
                            effective.ToString("F0") + "px（配置值未改动）", PluginLogLevel.Trace);
                    }
                }
                catch { }
            }));
        }

        private void ApplyFloatingBarOpacity()
        {
            var bar = _viewboxFloatingBar as UIElement;
            if (bar == null) return;
            try
            {
                double factor = Clamp(_barOpacity, 10, 100) / 100.0;
                double target = _barBaseOpacity * factor;
                if (CloseTo(bar.Opacity, target)) return;
                _suppressBarOpacityHook = true;
                try { bar.Opacity = target; }
                finally { _suppressBarOpacityHook = false; }
            }
            catch (Exception ex) { Log("应用浮动工具栏不透明度失败 " + ex.Message, PluginLogLevel.Warning); }
        }

        private void RestoreOriginals()
        {
            if (!_capturedOriginals || _navBoxes == null) return;
            for (int i = 0; i < PanelCount; i++)
            {
                try
                {
                    var box = _navBoxes[i];
                    if (box == null) continue;
                    box.Margin = _origMargins[i];
                    box.LayoutTransform = _origLayout[i];
                    var border = _navBorders != null && _navBorders[i] != null ? _navBorders[i] : (box.Child as Border);
                    if (border != null && !double.IsNaN(_origBorderOpacity[i])) border.Opacity = _origBorderOpacity[i];
                }
                catch { }
            }
            try
            {
                var bar = _viewboxFloatingBar as UIElement;
                if (bar != null && !double.IsNaN(_origBarOpacity))
                {
                    _suppressBarOpacityHook = true;
                    try { bar.Opacity = _origBarOpacity; }
                    finally { _suppressBarOpacityHook = false; }
                }
            }
            catch { }
            _capturedOriginals = false;
        }

        // ---------- 设置面板 ----------

        private UIElement BuildSettingsPanelBody()
        {
            _panelSliders.Clear();
            var root = new StackPanel { MinWidth = 460, MaxWidth = 900 };

            root.Children.Add(MakeNote(
                "自定义 PPT 放映时四个翻页控件与浮动工具栏的缩放、透明度、位移。" +
                "放映中拖动滑条立即生效；非放映状态下的调整会在下次放映时生效。" +
                "位移方向：底部控件右为正、侧面控件下为正；把控件拖出屏幕的部分会被自动拉回。"));

            string[] groupNames = { "下方翻页控件（左）", "下方翻页控件（右）", "侧面翻页控件（左）", "侧面翻页控件（右）" };
            for (int i = 0; i < PanelCount; i++)
            {
                int index = i; // 闭包捕获
                root.Children.Add(BuildGroupHeader(groupNames[i]));
                root.Children.Add(BuildSliderRow("缩放 (%)", 50, 200,
                    () => _scale[index], v => _scale[index] = v,
                    () => { ApplyPanel(index); ScheduleBoundsCheck(index); }, "%"));
                root.Children.Add(BuildSliderRow("透明度 (%)", 10, 100,
                    () => _opacity[index], v => _opacity[index] = v,
                    () => { ApplyPanel(index); ScheduleBoundsCheck(index); }, "%"));
                root.Children.Add(BuildSliderRow("位移 (px)", -300, 300,
                    () => _offset[index], v => _offset[index] = v,
                    () => { ApplyPanel(index); ScheduleBoundsCheck(index); }, "px"));
            }

            root.Children.Add(new Separator { Margin = new Thickness(0, 12, 0, 6) });
            root.Children.Add(BuildGroupHeader("浮动工具栏"));
            root.Children.Add(MakeNote("整体不透明度百分比，叠加在宿主场景值之上（放映中宿主会先调淡浮动栏）；100% 为默认。"));
            root.Children.Add(BuildSliderRow("不透明度 (%)", 10, 100,
                () => _barOpacity, v => _barOpacity = v,
                () => ApplyFloatingBarOpacity(), "%"));

            root.Children.Add(new Separator { Margin = new Thickness(0, 12, 0, 6) });
            var reset = new Button
            {
                Content = "恢复默认",
                Padding = new Thickness(15, 4, 15, 4),
                HorizontalAlignment = HorizontalAlignment.Left
            };
            reset.Click += (s, e) =>
            {
                // 复位滑条值 → 触发 ValueChanged → 写配置并应用（与全部默认一致时无变化）
                foreach (var r in _panelSliders)
                {
                    try { if (r.Slider != null) r.Slider.Value = r.Def; } catch { }
                }
            };
            root.Children.Add(reset);

            return root;
        }

        private UIElement BuildSliderRow(string label, double min, double max,
            Func<double> get, Action<double> set, Action apply, string unit)
        {
            var grid = new Grid { Margin = new Thickness(0, 3, 0, 3) };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(90) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(64) });

            var labelTb = new TextBlock
            {
                Text = label,
                FontSize = 13,
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(labelTb, 0);
            grid.Children.Add(labelTb);

            var slider = new Slider
            {
                Minimum = min,
                Maximum = max,
                Value = Clamp(get(), min, max),
                SmallChange = 1,
                LargeChange = 10,
                IsMoveToPointEnabled = true,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(10, 0, 10, 0)
            };
            Grid.SetColumn(slider, 1);
            grid.Children.Add(slider);

            var valueTb = new TextBlock
            {
                FontSize = 12,
                VerticalAlignment = VerticalAlignment.Center,
                TextAlignment = TextAlignment.Right,
                Text = FormatValue(slider.Value, unit)
            };
            Grid.SetColumn(valueTb, 2);
            grid.Children.Add(valueTb);

            slider.ValueChanged += (s, e) =>
            {
                double v = Math.Round(slider.Value);
                valueTb.Text = FormatValue(v, unit);
                set(v);
                SaveConfig();
                try { apply(); } catch { }
            };

            _panelSliders.Add(new SliderRef { Slider = slider, Def = Clamp(get(), min, max) });
            return grid;
        }

        private static TextBlock BuildGroupHeader(string text)
        {
            return new TextBlock
            {
                Text = text,
                FontSize = 14,
                FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(0, 10, 0, 2)
            };
        }

        private static TextBlock MakeNote(string text)
        {
            return new TextBlock
            {
                Text = text,
                FontSize = 12,
                TextWrapping = TextWrapping.Wrap,
                Foreground = TryBrush("SettingsPageAnnotationForeground", Brushes.Gray)
            };
        }

        private static string FormatValue(double v, string unit)
        {
            return v.ToString("F0") + unit;
        }

        private static System.Windows.Media.Brush TryBrush(string resourceKey, System.Windows.Media.Brush fallback)
        {
            try
            {
                var app = Application.Current;
                if (app != null && app.TryFindResource(resourceKey) is System.Windows.Media.Brush b) return b;
            }
            catch { }
            return fallback;
        }

        // ---------- 配置持久化 ----------

        private string ConfigPath => Path.Combine(_pluginDirectory, "ppt-control-customizer.json");

        private void LoadConfig()
        {
            try
            {
                if (!File.Exists(ConfigPath)) return;
                var data = new JavaScriptSerializer().Deserialize<ConfigData>(File.ReadAllText(ConfigPath));
                if (data == null) return;
                _scale[0] = Sanitize(data.BottomLeftScale, 100, 50, 200);
                _scale[1] = Sanitize(data.BottomRightScale, 100, 50, 200);
                _scale[2] = Sanitize(data.SideLeftScale, 100, 50, 200);
                _scale[3] = Sanitize(data.SideRightScale, 100, 50, 200);
                _opacity[0] = Sanitize(data.BottomLeftOpacity, 40, 10, 100);
                _opacity[1] = Sanitize(data.BottomRightOpacity, 40, 10, 100);
                _opacity[2] = Sanitize(data.SideLeftOpacity, 40, 10, 100);
                _opacity[3] = Sanitize(data.SideRightOpacity, 40, 10, 100);
                _offset[0] = Sanitize(data.BottomLeftOffset, 0, -300, 300);
                _offset[1] = Sanitize(data.BottomRightOffset, 0, -300, 300);
                _offset[2] = Sanitize(data.SideLeftOffset, 0, -300, 300);
                _offset[3] = Sanitize(data.SideRightOffset, 0, -300, 300);
                _barOpacity = Sanitize(data.FloatingBarOpacity, 100, 10, 100);
            }
            catch (Exception ex) { Log("读取配置失败 " + ex.Message, PluginLogLevel.Warning); }
        }

        private void SaveConfig()
        {
            try
            {
                var data = new ConfigData
                {
                    BottomLeftScale = _scale[0],
                    BottomRightScale = _scale[1],
                    SideLeftScale = _scale[2],
                    SideRightScale = _scale[3],
                    BottomLeftOpacity = _opacity[0],
                    BottomRightOpacity = _opacity[1],
                    SideLeftOpacity = _opacity[2],
                    SideRightOpacity = _opacity[3],
                    BottomLeftOffset = _offset[0],
                    BottomRightOffset = _offset[1],
                    SideLeftOffset = _offset[2],
                    SideRightOffset = _offset[3],
                    FloatingBarOpacity = _barOpacity
                };
                if (!Directory.Exists(_pluginDirectory)) Directory.CreateDirectory(_pluginDirectory);
                File.WriteAllText(ConfigPath, new JavaScriptSerializer().Serialize(data));
            }
            catch (Exception ex) { Log("保存配置失败 " + ex.Message, PluginLogLevel.Warning); }
        }

        private class ConfigData
        {
            public double BottomLeftScale { get; set; } = 100;
            public double BottomRightScale { get; set; } = 100;
            public double SideLeftScale { get; set; } = 100;
            public double SideRightScale { get; set; } = 100;
            public double BottomLeftOpacity { get; set; } = 40;
            public double BottomRightOpacity { get; set; } = 40;
            public double SideLeftOpacity { get; set; } = 40;
            public double SideRightOpacity { get; set; } = 40;
            public double BottomLeftOffset { get; set; } = 0;
            public double BottomRightOffset { get; set; } = 0;
            public double SideLeftOffset { get; set; } = 0;
            public double SideRightOffset { get; set; } = 0;
            public double FloatingBarOpacity { get; set; } = 100;
        }

        private string FindPluginDirectory()
        {
            try
            {
                string root = _host?.PluginDirectory;
                if (!string.IsNullOrEmpty(root) && Directory.Exists(root))
                {
                    foreach (var dir in Directory.GetDirectories(root))
                    {
                        if (File.Exists(Path.Combine(dir, PluginDllName)) &&
                            File.Exists(Path.Combine(dir, "plugin.icplugin")))
                            return dir;
                    }
                }
            }
            catch { }
            return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Plugins", PluginId);
        }

        // ---------- 小工具 ----------

        private void Log(string message, PluginLogLevel level = PluginLogLevel.Info)
        {
            try { _host?.Log("[PPT控件自定义] " + message, level); } catch { }
        }

        private static double Clamp(double v, double min, double max)
        {
            if (double.IsNaN(v)) return min;
            return Math.Max(min, Math.Min(max, v));
        }

        private static double Sanitize(double v, double def, double min, double max)
        {
            if (double.IsNaN(v) || double.IsInfinity(v)) return def;
            return Math.Max(min, Math.Min(max, v));
        }

        private static bool CloseTo(double a, double b)
        {
            return Math.Abs(a - b) < 0.001;
        }
    }
}
