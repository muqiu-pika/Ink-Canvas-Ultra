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
    public partial class MW_Notifications : UserControl
    {
        public MW_Notifications()
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


    }
}
