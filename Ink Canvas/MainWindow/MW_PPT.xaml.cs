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
    public partial class MW_PPT : UserControl
    {
        public MW_PPT()
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

        private void Border_MouseDown(object sender, MouseButtonEventArgs e)
        {
            InvokeMainWindowHandler(nameof(Border_MouseDown), sender, e);
        }

        private void GridPPTControlNext_MouseUp(object sender, MouseButtonEventArgs e)
        {
            InvokeMainWindowHandler(nameof(GridPPTControlNext_MouseUp), sender, e);
        }

        private void GridPPTControlPrevious_MouseUp(object sender, MouseButtonEventArgs e)
        {
            InvokeMainWindowHandler(nameof(GridPPTControlPrevious_MouseUp), sender, e);
        }

        private void PPTNavigationBtn_Click(object sender, MouseButtonEventArgs e)
        {
            InvokeMainWindowHandler(nameof(PPTNavigationBtn_Click), sender, e);
        }
    }
}
