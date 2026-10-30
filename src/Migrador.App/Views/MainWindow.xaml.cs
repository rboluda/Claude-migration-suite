using System.ComponentModel;
using System.Windows;
using Migrador.App.ViewModels;

namespace Migrador.App.Views;

public partial class MainWindow : Window
{
    public MainWindow() => InitializeComponent();

    private void OnClosing(object? sender, CancelEventArgs e)
    {
        if (DataContext is MainViewModel vm && !vm.ConfirmClose())
            e.Cancel = true;
    }
}
