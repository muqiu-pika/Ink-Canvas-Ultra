using System;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Ink;
using iNKORE.UI.WPF.Modern;

namespace Ink_Canvas
{
    public partial class MW_Sidebar : UserControl
    {
        public MW_Sidebar()
        {
            InitializeComponent();
        }

        private void InvokeMainWindowHandler(string handlerName, params object[] args)
        {
            var window = Window.GetWindow(this) as MainWindow;
            if (window == null)
            {
                return;
            }

            var method = typeof(MainWindow).GetMethod(handlerName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (method == null)
            {
                throw new MissingMethodException(typeof(MainWindow).FullName, handlerName);
            }

            method.Invoke(window, args);
        }

        private void BtnCapturePhoto_Click(object sender, RoutedEventArgs e)
        {
            InvokeMainWindowHandler(nameof(BtnCapturePhoto_Click), sender, e);
        }

        private void BtnCloseVideoPresenter_Click(object sender, RoutedEventArgs e)
        {
            InvokeMainWindowHandler(nameof(BtnCloseVideoPresenter_Click), sender, e);
        }

        private void BtnRotateImage_Click(object sender, RoutedEventArgs e)
        {
            InvokeMainWindowHandler(nameof(BtnRotateImage_Click), sender, e);
        }

        private void CheckBoxEnablePhotoCorrection_Checked(object sender, RoutedEventArgs e)
        {
            InvokeMainWindowHandler(nameof(CheckBoxEnablePhotoCorrection_Checked), sender, e);
        }

        private void CheckBoxEnablePhotoCorrection_Unchecked(object sender, RoutedEventArgs e)
        {
            InvokeMainWindowHandler(nameof(CheckBoxEnablePhotoCorrection_Unchecked), sender, e);
        }

        private void SCManipulationBoundaryFeedback(object sender, ManipulationBoundaryFeedbackEventArgs e)
        {
            InvokeMainWindowHandler(nameof(SCManipulationBoundaryFeedback), sender, e);
        }

        private void UnFoldFloatingBar_MouseUp(object sender, MouseButtonEventArgs e)
        {
            InvokeMainWindowHandler(nameof(UnFoldFloatingBar_MouseUp), sender, e);
        }

        private void BtnClearAllContent_Click(object sender, RoutedEventArgs e)
        {
            InvokeMainWindowHandler(nameof(BtnClearAllContent_Click), sender, e);
        }
    }
}
