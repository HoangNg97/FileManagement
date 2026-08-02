using LauncherCatalog.ViewModels;

namespace LauncherCatalog.Views;

public partial class AiBridgeView : System.Windows.Controls.UserControl
{
    private readonly AiBridgeViewModel _viewModel = new();

    public AiBridgeView()
    {
        InitializeComponent();
        DataContext = _viewModel;
    }
}
