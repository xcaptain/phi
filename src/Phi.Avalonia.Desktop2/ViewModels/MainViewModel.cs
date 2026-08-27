using CommunityToolkit.Mvvm.ComponentModel;

namespace Phi.Avalonia.Desktop2.ViewModels;

public partial class MainViewModel : ViewModelBase
{
    [ObservableProperty]
    public partial string Greeting { get; set; } = "Welcome to Avalonia!";
}
