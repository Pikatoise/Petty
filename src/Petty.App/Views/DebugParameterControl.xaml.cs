#if DEBUG
using System.Windows.Automation;
using System.Windows.Controls;

namespace Petty.App.Views;

public partial class DebugParameterControl: UserControl
{
    public DebugParameterControl() => InitializeComponent();

    public string LabelKey
    {
        set
        {
            LabelText.SetResourceReference(TextBlock.TextProperty, value);
            ValueSlider.SetResourceReference(AutomationProperties.NameProperty, value);
            ValueInput.SetResourceReference(AutomationProperties.NameProperty, value);
        }
    }

    public double Minimum { get => ValueSlider.Minimum; set => ValueSlider.Minimum = value; }
    public double Maximum { get => ValueSlider.Maximum; set => ValueSlider.Maximum = value; }
    public double Step
    {
        get => ValueSlider.TickFrequency;
        set { ValueSlider.TickFrequency = value; ValueSlider.SmallChange = value; ValueSlider.LargeChange = value * 10; }
    }
}
#endif
