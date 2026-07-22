using System.Windows;
using Gateway.DutSim.ViewModels;

namespace Gateway.DutSim;

/// <summary>Interaction logic for MainWindow.xaml</summary>
public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        DataContext = new DutSimViewModel();
    }
}
