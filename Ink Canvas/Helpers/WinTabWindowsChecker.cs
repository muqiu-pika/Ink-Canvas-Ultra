using System.Windows.Automation;

namespace Ink_Canvas.Helpers
{
    internal class WinTabWindowsChecker
    {
        public static bool IsWindowExisted(string windowName, bool matchFullName = true)
        {
            AutomationElementCollection windows = AutomationElement.RootElement.FindAll(
                TreeScope.Children, new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Window));

            foreach (AutomationElement window in windows)
            {
                //LogHelper.WriteLogToFile("" + window.Current.Name);

                string windowTitle = window.Current.Name;

                // 如果窗口标题包含 windowName，则进行检查
                if (!string.IsNullOrEmpty(windowTitle) && windowTitle.Contains(windowName))
                {
                    if (matchFullName)
                    {
                        if (windowTitle.Length == windowName.Length)
                        {
                            WindowPattern windowPattern = window.GetCurrentPattern(WindowPattern.Pattern) as WindowPattern;
                            if (windowPattern != null)
                            {
                                return true;
                            }
                        }
                    }
                    else
                    {
                        WindowPattern windowPattern = window.GetCurrentPattern(WindowPattern.Pattern) as WindowPattern;
                        if (windowPattern != null)
                        {
                            return true;
                        }
                    }
                }
            }
            return false;
        }
    }
}
