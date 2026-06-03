namespace WhisperBurner.WinUI.Models;

public record CaptureRegion(int X, int Y, int Width, int Height)
{
    public bool IsValid => Width > 0 && Height > 0;

    public override string ToString() => $"{Width}x{Height} at ({X},{Y})";
}
