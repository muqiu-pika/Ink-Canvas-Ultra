using System.Linq;
using System.Windows;

namespace Ink_Canvas
{
    public static class MessageBoxHelper
    {
        /// <summary>
        /// 取宿主窗口：优先用当前处于激活状态的 ICU 窗口（例如从插件工坊弹出的提示应归属插件工坊），
        /// 没有激活窗口时回退为主窗口；应用尚未创建主窗口时返回 null（回退为无主弹窗）。
        /// </summary>
        private static Window Owner
        {
            get
            {
                try
                {
                    var active = Application.Current?.Windows
                        .OfType<Window>()
                        .FirstOrDefault(w => w.IsActive);
                    return active ?? Application.Current?.MainWindow;
                }
                catch { return null; }
            }
        }

        public static MessageBoxResult Show(string messageBoxText)
        {
            var owner = Owner;
            return owner != null
                ? MessageBox.Show(owner, messageBoxText)
                : MessageBox.Show(messageBoxText);
        }

        public static MessageBoxResult Show(string messageBoxText, string caption)
        {
            var owner = Owner;
            return owner != null
                ? MessageBox.Show(owner, messageBoxText, caption)
                : MessageBox.Show(messageBoxText, caption);
        }

        public static MessageBoxResult Show(string messageBoxText, string caption, MessageBoxButton button)
        {
            var owner = Owner;
            return owner != null
                ? MessageBox.Show(owner, messageBoxText, caption, button)
                : MessageBox.Show(messageBoxText, caption, button);
        }

        public static MessageBoxResult Show(string messageBoxText, string caption, MessageBoxButton button, MessageBoxImage icon)
        {
            var owner = Owner;
            return owner != null
                ? MessageBox.Show(owner, messageBoxText, caption, button, icon)
                : MessageBox.Show(messageBoxText, caption, button, icon);
        }

        public static MessageBoxResult Show(string messageBoxText, string caption, MessageBoxButton button, MessageBoxImage icon, MessageBoxResult defaultResult)
        {
            var owner = Owner;
            return owner != null
                ? MessageBox.Show(owner, messageBoxText, caption, button, icon, defaultResult)
                : MessageBox.Show(messageBoxText, caption, button, icon, defaultResult);
        }

        // ===== 显式指定 Owner（调用方持有更准确的宿主窗口时使用） =====

        public static MessageBoxResult Show(Window owner, string messageBoxText)
        {
            return owner != null
                ? MessageBox.Show(owner, messageBoxText)
                : MessageBox.Show(messageBoxText);
        }

        public static MessageBoxResult Show(Window owner, string messageBoxText, string caption, MessageBoxButton button)
        {
            return owner != null
                ? MessageBox.Show(owner, messageBoxText, caption, button)
                : MessageBox.Show(messageBoxText, caption, button);
        }

        public static MessageBoxResult Show(Window owner, string messageBoxText, string caption, MessageBoxButton button, MessageBoxImage icon)
        {
            return owner != null
                ? MessageBox.Show(owner, messageBoxText, caption, button, icon)
                : MessageBox.Show(messageBoxText, caption, button, icon);
        }
    }
}
