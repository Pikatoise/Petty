using System.Windows;

namespace Petty.App.Views;

public partial class ConfirmNewPetWindow : Window
{
    public ConfirmNewPetWindow()
    {
        InitializeComponent();
        AdaptiveWindowLayout.Attach(this);
    }

    private void OnConfirm(object sender, RoutedEventArgs e) => DialogResult = true;
}
