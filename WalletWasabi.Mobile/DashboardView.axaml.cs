using Avalonia.Controls;
using Avalonia.Interactivity;

namespace WalletWasabi.Mobile;

public partial class DashboardView : UserControl
{
    public DashboardView() => InitializeComponent();

    private void ShowSendStatus(object? sender, RoutedEventArgs args)
    {
        ActionStatus.Text = "Sending requires wallet integration. This preview uses sample data.";
    }

    private void ShowReceiveStatus(object? sender, RoutedEventArgs args)
    {
        ActionStatus.Text = "Receiving addresses require wallet integration. No address has been generated.";
    }
}
