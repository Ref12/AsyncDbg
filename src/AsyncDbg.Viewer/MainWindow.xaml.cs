using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using System.Windows.Threading;
using AsyncCausalityDebugger;

namespace AsyncCausalityViewer
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        public MainWindow()
        {
            InitializeComponent();
        }

        private async void OnClickInitialize(object sender, RoutedEventArgs e)
        {
            var context = await RunStaAsync(() =>
            {
                var result = CausalityContext.LoadCausalityContextFromDump(null);
                foreach (var node in result.Nodes.Values)
                {
                    var displayString = node.DisplayString;
                }

                return result;
            });

            ViewerControl.LoadFromCausalityContext(context);
        }

        private Task<T> RunStaAsync<T>(Func<T> action)
        {
            TaskCompletionSource<T> completionSource = new TaskCompletionSource<T>();

            Thread thread = new Thread(() =>
            {
                var result = action();

                Dispatcher.InvokeAsync(() =>
                {
                    completionSource.SetResult(result);
                });
            });

            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();

            return completionSource.Task;
        }
    }
}
