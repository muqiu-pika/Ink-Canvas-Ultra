using Microsoft.Win32;
using iNKORE.UI.WPF.Modern;
using System;
using System.Windows;
using System.Windows.Media;
using Application = System.Windows.Application;
using System.Windows.Controls;
using System.Linq;

namespace Ink_Canvas
{
    public partial class MainWindow : Window
    {
        public string GetMainWindowTheme()
        {
            if (currentMode != 0)
            {
                return Settings.Canvas.UsingWhiteboard ? "Light" : "Dark";
            }
            else
            {
                return (ThemeManager.GetRequestedTheme(window).ToString() == "Light") ? "Light" : "Dark";
            }
        }

        void RemoveResourceDictionary(Uri uri)
        {
            Helpers.ResourceDictionaryHelper.Remove(uri);
        }

        private void ComboBoxTheme_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!isLoaded || _isLoadingSettings) return;
            Settings.Appearance.Theme = ComboBoxTheme.SelectedIndex;
            SystemEvents_UserPreferenceChanged(null, null);
            SaveSettingsToFile();
        }

        private void SetBoardTheme()
        {
            var lightBoardUri = new Uri("Resources/Styles/Light-Board.xaml", UriKind.Relative);
            var darkBoardUri = new Uri("Resources/Styles/Dark-Board.xaml", UriKind.Relative);
            // 使用去重合并：主题会随系统设置/模式切换反复刷新，若每次都 Add 新字典会让应用级资源无限膨胀
            if (Settings.Canvas.UsingWhiteboard)
            {
                Helpers.ResourceDictionaryHelper.MergeOnce(lightBoardUri);
                RemoveResourceDictionary(darkBoardUri);
            }
            else
            {
                Helpers.ResourceDictionaryHelper.MergeOnce(darkBoardUri);
                RemoveResourceDictionary(lightBoardUri);
            }
        }

        private void SetTheme(string theme)
        {
            var lightUri = new Uri("Resources/Styles/Light.xaml", UriKind.Relative);
            var darkUri = new Uri("Resources/Styles/Dark.xaml", UriKind.Relative);

            // 先切换框架主题，再合并自定义主题字典。
            // Release 下 ThemeManager.Current.ApplicationTheme 赋值会重建应用级资源字典，
            // 若此前先 MergeOnce 加了自定义字典，会被这一步清空，导致浮动栏/设置面板里的
            // DynamicResource（FloatBar*、SettingsPageAnnotationForeground 等）瞬间找不到键而刷 Warning。
            // 后置合并保证刷新结束后自定义字典始终位于列表末尾、键一定可解析，且不影响外观。
            if (theme == "Light")
            {
                ThemeManager.SetRequestedTheme(window, ElementTheme.Light);
                ThemeManager.Current.ApplicationTheme = ApplicationTheme.Light;
                // 必须"先加后删"：反序会留下"目标字典尚未合并、旧字典已被移除"的空窗，
                // 期间任何 DynamicResource 重新求值都会刷 "Resource not found"
                // （ApplicationTheme 赋值会重建应用级资源字典，极易触发这种重新求值）。
                // MergeOnce 内部会把已存在的字典挪到 MergedDictionaries 末尾，
                // 因此先加目标主题仍可保证它优先级最高，最终外观不受影响。
                Helpers.ResourceDictionaryHelper.MergeOnce(lightUri);
                RemoveResourceDictionary(darkUri);
            }
            else if (theme == "Dark")
            {
                ThemeManager.SetRequestedTheme(window, ElementTheme.Dark);
                ThemeManager.Current.ApplicationTheme = ApplicationTheme.Dark;
                Helpers.ResourceDictionaryHelper.MergeOnce(darkUri);
                RemoveResourceDictionary(lightUri);
            }

            SetBoardTheme(); // 白板主题字典同样后置合并，避免被上方设置覆盖

            if (!Settings.Appearance.IsColorfulViewboxFloatingBar) // 还原浮动工具栏背景色
            {
                EnableTwoFingerGestureBorder.Background = BorderDrawShape.Background;
                BorderFloatingBarMainControls.Background = BorderDrawShape.Background;
                BorderFloatingBarMoveControls.Background = BorderDrawShape.Background;
                BtnPPTSlideShowEnd.Background = BorderDrawShape.Background;
            }
        }

        private void SystemEvents_UserPreferenceChanged(object sender, Microsoft.Win32.UserPreferenceChangedEventArgs e)
        {
            switch (Settings.Appearance.Theme)
            {
                case 0:
                    SetTheme("Light");
                    break;
                case 1:
                    SetTheme("Dark");
                    break;
                case 2:
                    if (IsSystemThemeLight()) SetTheme("Light");
                    else SetTheme("Dark");
                    break;
            }
        }

        private bool IsSystemThemeLight()
        {
            bool light = false;
            try
            {
                RegistryKey registryKey = Registry.CurrentUser;
                RegistryKey themeKey = registryKey.OpenSubKey("software\\Microsoft\\Windows\\CurrentVersion\\Themes\\Personalize");
                int keyValue = 0;
                if (themeKey != null)
                {
                    keyValue = (int)themeKey.GetValue("SystemUsesLightTheme");
                }
                if (keyValue == 1) light = true;
            }
            catch { }
            return light;
        }
    }
}