using System;
using System.Windows;

[assembly: System.Runtime.Versioning.TargetFramework(".NETFramework,Version=v4.8")]
[assembly: System.Reflection.AssemblyTitle("Fast Image Viewer")]
[assembly: System.Reflection.AssemblyVersion("0.3.0.0")]

namespace FastImageViewer
{
    internal static class Program
    {
        [STAThread]
        public static void Main(string[] args)
        {
            var app = new Application();
            app.ShutdownMode = ShutdownMode.OnMainWindowClose;
            app.DispatcherUnhandledException += delegate(object sender, System.Windows.Threading.DispatcherUnhandledExceptionEventArgs e)
            {
                MessageBox.Show("操作未完成：" + e.Exception.Message, "Fast Image Viewer", MessageBoxButton.OK, MessageBoxImage.Warning);
                e.Handled = true;
            };
            app.Run(new MainWindow(args));
        }
    }
}
