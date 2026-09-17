using System.Windows;
using Apollo.ViewModels;

namespace Apollo;

public partial class MainWindow : Window
{
    internal MainWindow(MainWindowViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
    }
}
