using Microsoft.UI.Xaml.Controls;

using miniLook.ViewModels;

namespace miniLook.Views;

public sealed partial class GraphExplainerPage : Page
{
    public GraphExplainerViewModel ViewModel
    {
        get;
    }

    public GraphExplainerPage()
    {
        ViewModel = App.GetService<GraphExplainerViewModel>();
        InitializeComponent();
    }
}
