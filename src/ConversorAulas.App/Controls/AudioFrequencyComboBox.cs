using Microsoft.UI.Input;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;

namespace ConversorAulas.App.Controls;

public sealed class AudioFrequencyComboBox : ComboBox
{
    public AudioFrequencyComboBox()
    {
        Loaded += (_, _) => ProtectedCursor = InputSystemCursor.Create(InputSystemCursorShape.Arrow);
    }

    protected override void OnApplyTemplate()
    {
        base.OnApplyTemplate();
        // Keep the list within the XAML window instead of a separate native popup
        // whose non-client border can leave the pointer showing a resize cursor.
        if (GetTemplateChild("Popup") is Popup popup)
            popup.ShouldConstrainToRootBounds = true;
    }
}
