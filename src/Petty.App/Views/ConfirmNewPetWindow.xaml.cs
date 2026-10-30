using System.Windows;

namespace Petty.App.Views;

public partial class ConfirmNewPetWindow : Window
{
    public ConfirmNewPetWindow() => InitializeComponent();

    private void OnConfirm(object sender, RoutedEventArgs e) => DialogResult = true;
}
