using System;
using System.Windows;

namespace Agent.UI
{
    public partial class App : Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);
            
            // Запускаем WinForms-цикл в фоне для трея
            System.Windows.Forms.Integration.WindowsFormsHost.EnableWindowsFormsInterop();
        }
    }
}