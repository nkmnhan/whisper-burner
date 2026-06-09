using Microsoft.UI.Input;
using Microsoft.UI.Xaml.Controls;

namespace WhisperLive.Components;

/// <summary>
/// Transparent icon button that explicitly resets the cursor to Arrow.
/// Required when used inside containers that impose a different cursor (e.g. TextBox.Header),
/// where the standard Button would inherit the I-beam text cursor.
/// </summary>
public class GhostButton : Button
{
    public GhostButton() =>
        ProtectedCursor = InputSystemCursor.Create(InputSystemCursorShape.Arrow);
}
