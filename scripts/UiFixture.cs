using System;
using System.Windows;
using System.Windows.Controls;

namespace JarvisFixture
{
    public static class Program
    {
        [STAThread]
        public static void Main()
        {
            var app = new Application();
            var window = new Window { Title = "자비스 안전 시험 입력창", Width = 520, Height = 220, WindowStartupLocation = WindowStartupLocation.CenterScreen };
            var text = new TextBox { Name = "TestInput", Text = "원래 문장", FontSize = 22, Margin = new Thickness(24), AcceptsReturn = true };
            window.Content = text;
            window.Loaded += (s, e) => { text.Focus(); text.SelectAll(); };
            app.Run(window);
        }
    }
}
