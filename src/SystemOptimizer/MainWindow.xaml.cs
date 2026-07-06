using Microsoft.Extensions.DependencyInjection;
using System.Windows;
using SystemOptimizer.ViewModels;

namespace SystemOptimizer;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        DataContext = App.Services.GetRequiredService<CleanerViewModel>();
    }
}
