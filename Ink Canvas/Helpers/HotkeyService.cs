using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;

namespace Ink_Canvas
{
    /// <summary>
    /// 内置快捷键管理中心：管理「动作 → 组合键」的注册，支持被插件动态重绑定与复位默认。
    /// 软件自带的默认按键在此注册；插件可通过 IPluginHost 的 Get/Set/Reset Hotkey 修改。
    /// 若插件被禁用或未安装，动作将保持默认按键搭配。
    /// </summary>
    public class HotkeyService
    {
        private readonly Window _window;

        // 每个动作的注册状态（actionId → 动作）
        private readonly Dictionary<string, HotkeyAction> _actions =
            new Dictionary<string, HotkeyAction>(StringComparer.OrdinalIgnoreCase);

        public HotkeyService(Window window)
        {
            _window = window ?? throw new ArgumentNullException(nameof(window));
            _suspendWatchdog = new System.Windows.Threading.DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(SuspendAutoResumeSeconds)
            };
            _suspendWatchdog.Tick += (s, e) =>
            {
                // 兜底：捕获超时未恢复时自动恢复快捷键，避免一直挂起导致快捷键失效
                if (_suspended) ResumeAll();
            };
        }

        /// <summary>
        /// 暂时挂起全部快捷键（全局注销 + 窗口级移除 KeyBinding）。
        /// 捕获设置新快捷键期间调用，避免按下与现有键冲突的按键时执行对应功能。
        /// 一定时间后若未 Resume，会自动恢复以防止异常路径导致卡死。
        /// </summary>
        public void SuspendAll()
        {
            if (_suspended) return;
            _suspended = true;
            foreach (var action in _actions.Values)
                Unregister(action);
            _suspendWatchdog.Stop();
            _suspendWatchdog.Start();
        }

        /// <summary>恢复被 SuspendAll 挂起的全部快捷键，并按当前配置重新注册。</summary>
        public void ResumeAll()
        {
            if (!_suspended) return;
            _suspended = false;
            _suspendWatchdog.Stop();
            SyncRegistrations();
        }

        /// <summary>
        /// 返回与指定组合键冲突的其他动作（用于提示“重复”）。不包含 actionId 本身。
        /// </summary>
        public IReadOnlyList<HotkeyActionInfo> GetConflictingHotkeys(string actionId, string combo)
        {
            var result = new List<HotkeyActionInfo>();
            if (string.IsNullOrWhiteSpace(combo)) return result;
            if (!TryParseCombo(combo, out var mods, out var key)) return result;
            foreach (var a in _actions.Values)
            {
                if (a.Disabled) continue;
                if (!string.IsNullOrEmpty(actionId) &&
                    string.Equals(a.Id, actionId, StringComparison.OrdinalIgnoreCase)) continue;
                if (a.ActiveModifiers == mods && a.ActiveKey == key)
                {
                    result.Add(new HotkeyActionInfo
                    {
                        Id = a.Id,
                        Name = a.Name,
                        Description = a.Description,
                        DefaultCombo = FormatCombo(a.DefaultModifiers, a.DefaultKey),
                        Combo = FormatCombo(a.ActiveModifiers, a.ActiveKey)
                    });
                }
            }
            return result;
        }

        /// <summary>
        /// 注册一个软件自带动作的默认快捷键。
        /// </summary>
        internal void RegisterAction(string actionId, string name, string description,
            HotkeyModifiers defaultModifiers, Key defaultKey, Hotkey.HotKeyCallBackHanlder callback)
        {
            if (string.IsNullOrWhiteSpace(actionId)) return;
            var action = new HotkeyAction
            {
                Id = actionId,
                Name = name ?? actionId,
                Description = description,
                DefaultModifiers = defaultModifiers,
                DefaultKey = defaultKey,
                ActiveModifiers = defaultModifiers,
                ActiveKey = defaultKey,
                Disabled = false,
                Callback = callback
            };
            _actions[actionId] = action;
            Register(action);
        }

        /// <summary>返回所有动作的当前信息（供插件设置界面展示）。</summary>
        public IReadOnlyList<HotkeyActionInfo> GetActions()
        {
            return _actions.Values
                .OrderBy(a => a.Name, StringComparer.CurrentCulture)
                .Select(a => new HotkeyActionInfo
                {
                    Id = a.Id,
                    Name = a.Name,
                    Description = a.Description,
                    DefaultCombo = FormatCombo(a.DefaultModifiers, a.DefaultKey),
                    Combo = a.Disabled ? string.Empty : FormatCombo(a.ActiveModifiers, a.ActiveKey)
                })
                .ToList();
        }

        /// <summary>
        /// 查询某个动作当前是否处于启用状态（未被禁用）。
        /// 用于运行时按自定义状态决定是否响应（如滚轮翻页受 PPT 翻页动作启用状态约束）。
        /// </summary>
        public bool IsActionEnabled(string actionId)
        {
            if (string.IsNullOrWhiteSpace(actionId)) return false;
            return _actions.TryGetValue(actionId, out var action) && !action.Disabled;
        }

        /// <summary>
        /// 为一个动作设置新的组合键。
        /// </summary>
        /// <param name="actionId">动作标识</param>
        /// <param name="combo">组合键文本，如 "Ctrl+Shift+V"；空字符串/空值为禁用该动作。</param>
        /// <returns>是否设置成功（含 Win32 注册失败返回 false）。</returns>
        public bool SetHotkey(string actionId, string combo)
        {
            if (!_actions.TryGetValue(actionId, out var action)) return false;

            // 禁用：注销当前注册
            if (string.IsNullOrWhiteSpace(combo))
            {
                Unregister(action);
                action.Disabled = true;
                return true;
            }

            if (!TryParseCombo(combo, out var mods, out var key))
                return false;

            Unregister(action);
            action.ActiveModifiers = mods;
            action.ActiveKey = key;
            action.Disabled = false;
            return Register(action);
        }

        /// <summary>将某个动作恢复为软件默认按键。</summary>
        public bool ResetHotkey(string actionId)
        {
            if (!_actions.TryGetValue(actionId, out var action)) return false;
            Unregister(action);
            action.ActiveModifiers = action.DefaultModifiers;
            action.ActiveKey = action.DefaultKey;
            action.Disabled = false;
            return Register(action);
        }

        /// <summary>将所有动作恢复为软件默认按键。</summary>
        public void ResetAllHotkeys()
        {
            foreach (var id in _actions.Keys.ToList())
                ResetHotkey(id);
        }

        private readonly HashSet<string> _registered = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        // 记录哪些动作已经通过 Win32 注册（用于重复注册/注销与延迟同步）

        // 捕获设置期间暂时挂起全部快捷键，避免按下与现有键冲突的键会执行对应功能
        private bool _suspended;
        private readonly System.Windows.Threading.DispatcherTimer _suspendWatchdog;
        private const int SuspendAutoResumeSeconds = 30;

        /// <summary>
        /// 窗口句柄可用后一次性同步所有动作的 Win32 注册。
        /// 构造函数里注册动作元数据时句柄尚不存在，先记录目标状态，待 Loaded 时调用此方法真正注册。
        /// </summary>
        public void SyncRegistrations()
        {
            if (!IsHwndReady()) return;
            foreach (var action in _actions.Values)
            {
                if (action.Disabled) Unregister(action);
                else Register(action);
            }
        }

        private bool IsHwndReady()
        {
            try
            {
                IntPtr hwnd = new WindowInteropHelper(_window).Handle;
                if (hwnd == IntPtr.Zero) return false;
                return HwndSource.FromHwnd(hwnd) != null;
            }
            catch
            {
                return false;
            }
        }

        private bool Register(HotkeyAction action)
        {
            if (action.Disabled) return true;
            // 捕获设置期间（挂起中）不实际注册，等 Resume 后由 SyncRegistrations 统一恢复
            if (_suspended) return true;
            if (action.IsGlobal)
            {
                if (_registered.Contains(action.Id)) return true;
                // 句柄未就绪（如构造函数阶段）时先记录目标状态，成功由 SyncRegistrations 补齐
                if (!IsHwndReady()) return true;
                bool ok = Hotkey.Regist(_window, action.ActiveModifiers, action.ActiveKey, action.Callback);
                if (ok) _registered.Add(action.Id);
                return ok;
            }
            else
            {
                // 窗口级快捷键：改写对应 KeyBinding 的手势，并确保其存在于 InputBindings
                if (action.KeyBinding != null)
                {
                    try
                    {
                        action.KeyBinding.Key = action.ActiveKey;
                        action.KeyBinding.Modifiers = ToModifierKeys(action.ActiveModifiers);
                    }
                    catch { }
                }
                try
                {
                    if (!_window.InputBindings.Contains(action.KeyBinding))
                        _window.InputBindings.Add(action.KeyBinding);
                }
                catch { }
                return true;
            }
        }

        private void Unregister(HotkeyAction action)
        {
            if (action.IsGlobal)
            {
                if (!_registered.Contains(action.Id)) return;
                var hwnd = new WindowInteropHelper(_window).Handle;
                try { Hotkey.UnRegist(hwnd, action.Callback); } catch { }
                _registered.Remove(action.Id);
            }
            else
            {
                if (action.KeyBinding != null)
                {
                    try
                    {
                        if (_window.InputBindings.Contains(action.KeyBinding))
                            _window.InputBindings.Remove(action.KeyBinding);
                    }
                    catch { }
                }
            }
        }

        /// <summary>
        /// 注册一个窗口级快捷键动作（基于 Window.InputBindings 中的 KeyBinding）。
        /// 用于撤销/重做/选择/橡皮/直线等仅在窗口聚焦时生效的快捷键。
        /// </summary>
        internal void RegisterWindowAction(string actionId, string name, string description,
            System.Windows.Input.RoutedCommand command)
        {
            if (string.IsNullOrWhiteSpace(actionId) || command == null) return;

            System.Windows.Input.KeyBinding kb = null;
            foreach (System.Windows.Input.InputBinding ib in _window.InputBindings)
            {
                if (ib is System.Windows.Input.KeyBinding k && ReferenceEquals(k.Command, command))
                {
                    kb = k;
                    break;
                }
            }
            if (kb == null) return;

            var defaultMods = FromModifierKeys(kb.Modifiers);
            var action = new HotkeyAction
            {
                Id = actionId,
                Name = name ?? actionId,
                Description = description,
                DefaultModifiers = defaultMods,
                DefaultKey = kb.Key,
                ActiveModifiers = defaultMods,
                ActiveKey = kb.Key,
                Disabled = false,
                IsGlobal = false,
                KeyBinding = kb
            };
            _actions[actionId] = action;
            Register(action);
        }

        private static ModifierKeys ToModifierKeys(HotkeyModifiers mods)
        {
            ModifierKeys m = 0;
            if ((mods & HotkeyModifiers.MOD_CONTROL) != 0) m |= ModifierKeys.Control;
            if ((mods & HotkeyModifiers.MOD_SHIFT) != 0) m |= ModifierKeys.Shift;
            if ((mods & HotkeyModifiers.MOD_ALT) != 0) m |= ModifierKeys.Alt;
            if ((mods & HotkeyModifiers.MOD_WIN) != 0) m |= ModifierKeys.Windows;
            return m;
        }

        private static HotkeyModifiers FromModifierKeys(ModifierKeys mods)
        {
            HotkeyModifiers m = 0;
            if ((mods & ModifierKeys.Control) != 0) m |= HotkeyModifiers.MOD_CONTROL;
            if ((mods & ModifierKeys.Shift) != 0) m |= HotkeyModifiers.MOD_SHIFT;
            if ((mods & ModifierKeys.Alt) != 0) m |= HotkeyModifiers.MOD_ALT;
            if ((mods & ModifierKeys.Windows) != 0) m |= HotkeyModifiers.MOD_WIN;
            return m;
        }

        // ===== 组合键文本转换 =====

        internal static string FormatCombo(HotkeyModifiers mods, Key key)
        {
            var parts = new List<string>();
            if ((mods & HotkeyModifiers.MOD_CONTROL) != 0) parts.Add("Ctrl");
            if ((mods & HotkeyModifiers.MOD_SHIFT) != 0) parts.Add("Shift");
            if ((mods & HotkeyModifiers.MOD_ALT) != 0) parts.Add("Alt");
            if ((mods & HotkeyModifiers.MOD_WIN) != 0) parts.Add("Win");
            parts.Add(key.ToString());
            return string.Join("+", parts);
        }

        internal static bool TryParseCombo(string combo, out HotkeyModifiers modifiers, out Key key)
        {
            modifiers = 0;
            key = Key.None;
            if (string.IsNullOrWhiteSpace(combo)) return false;

            var rawParts = combo.Replace(" ", string.Empty).Split('+');
            if (rawParts.Length < 1) return false; // 至少需要一个主键（修饰键可选，便于绑定无修饰键如 PageDown/Escape）

            for (int i = 0; i < rawParts.Length - 1; i++)
            {
                switch (rawParts[i].ToUpperInvariant())
                {
                    case "CTRL":
                    case "CONTROL":
                        modifiers |= HotkeyModifiers.MOD_CONTROL;
                        break;
                    case "SHIFT":
                        modifiers |= HotkeyModifiers.MOD_SHIFT;
                        break;
                    case "ALT":
                        modifiers |= HotkeyModifiers.MOD_ALT;
                        break;
                    case "WIN":
                    case "WINDOWS":
                        modifiers |= HotkeyModifiers.MOD_WIN;
                        break;
                    default:
                        return false;
                }
            }

            string keyStr = rawParts[rawParts.Length - 1];
            if (!Enum.TryParse(keyStr, true, out key)) return false;
            if (key == Key.None || key == Key.System) return false;
            return true;
        }

        private class HotkeyAction
        {
            public string Id;
            public string Name;
            public string Description;
            public HotkeyModifiers DefaultModifiers;
            public Key DefaultKey;
            public HotkeyModifiers ActiveModifiers;
            public Key ActiveKey;
            public bool Disabled;
            // 全局快捷键（Win32 RegisterHotKey）
            public Hotkey.HotKeyCallBackHanlder Callback;
            public bool IsGlobal = true;
            // 窗口级快捷键（Window.InputBindings 中的 KeyBinding）
            public System.Windows.Input.KeyBinding KeyBinding;
        }
    }

    /// <summary>一个内置快捷键动作的展示信息（供插件设置界面与工坊展示）。</summary>
    public class HotkeyActionInfo
    {
        /// <summary>动作唯一标识（如 "capture"）</summary>
        public string Id { get; set; }

        /// <summary>动作名称（如 "截图"）</summary>
        public string Name { get; set; }

        /// <summary>动作说明</summary>
        public string Description { get; set; }

        /// <summary>软件默认组合键（如 "Alt+C"）</summary>
        public string DefaultCombo { get; set; }

        /// <summary>当前生效的组合键；空字符串表示已禁用。</summary>
        public string Combo { get; set; }
    }
}