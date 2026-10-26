using System.Windows;
using System.Windows.Controls;

namespace Ink_Canvas
{
    public partial class MainWindow : Window
    {
        private void InkAlphaSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (!isLoaded || _isLoadingSettings) return;
            if (sender == BoardInkAlphaSlider) InkAlphaSlider.Value = ((Slider)sender).Value;
            if (sender == InkAlphaSlider) BoardInkAlphaSlider.Value = ((Slider)sender).Value;
            // 透明度只改 Alpha，不再顺手把尺寸硬编码成 20/5：
            // 那会让普通笔在调透明度后瞬间变粗，且若此刻 CheckColorTheme 的尺寸分支未执行
            // （isLoaded 为 false 等），粗尺寸就会卡在画布上。尺寸统一交给 CheckColorTheme 按
            // 当前颜色（普通笔/荧光笔）与 Settings.Canvas.InkWidth 计算。
            Settings.Canvas.InkAlpha = (int)((Slider)sender).Value;
            SaveSettingsToFile();
            CheckColorTheme();
        }

        private void BtnHighlighterColorRed_Click(object sender, RoutedEventArgs e)
        {
            CheckLastColor(101);
        }

        private void BtnHighlighterColorOrange_Click(object sender, RoutedEventArgs e)
        {
            CheckLastColor(102);
        }

        private void BtnHighlighterColorYellow_Click(object sender, RoutedEventArgs e)
        {
            CheckLastColor(103);
        }

        private void BtnHighlighterColorTeal_Click(object sender, RoutedEventArgs e)
        {
            CheckLastColor(104);
        }
        private void BtnHighlighterColorBlue_Click(object sender, RoutedEventArgs e)
        {
            CheckLastColor(105);
        }

        private void BtnHighlighterColorPurple_Click(object sender, RoutedEventArgs e)
        {
            CheckLastColor(106);
        }
    }
}