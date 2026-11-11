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
            Settings.Canvas.InkAlpha = (int)((Slider)sender).Value;
            SaveSettingsToFile();
            CheckColorTheme();
        }

        /// <summary>
        /// 「批注」（普通笔）不透明度：与荧光笔的 InkAlphaSlider 同一套状态同步链路，
        /// 仅作用于普通笔迹颜色 alpha，不影响荧光笔与笔迹尺寸。
        /// </summary>
        private void InkAnnotationAlphaSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (!isLoaded || _isLoadingSettings) return;
            if (sender == BoardInkAnnotationAlphaSlider) InkAnnotationAlphaSlider.Value = ((Slider)sender).Value;
            if (sender == InkAnnotationAlphaSlider) BoardInkAnnotationAlphaSlider.Value = ((Slider)sender).Value;
            Settings.Canvas.AnnotationInkAlpha = (int)((Slider)sender).Value;
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