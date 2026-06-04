using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using Microsoft.UI.Xaml.Media.Imaging;
using WhisperBurner.WinUI.Infrastructure;
using WhisperBurner.WinUI.Models;
using WhisperBurner.WinUI.Overlay;

namespace WhisperBurner.WinUI.Services.Video;

public class RegionSelectionService : IRegionSelectionService
{
    [DllImport("user32.dll")] private static extern int GetSystemMetrics(int n);
    private const int SM_XVIRTUALSCREEN = 76;
    private const int SM_YVIRTUALSCREEN = 77;
    private const int SM_CXVIRTUALSCREEN = 78;
    private const int SM_CYVIRTUALSCREEN = 79;

    public CaptureRegion? LastSelectedRegion { get; private set; }

    public async Task<CaptureRegion?> SelectRegionAsync()
    {
        // 1. Take screenshot to a temp file (fast on SSD, ~50 ms)
        AppLogger.Info("Screenshot: capturing screen...");
        var path = CaptureScreen();
        AppLogger.Info($"Screenshot: saved to {path}");

        // 2. Load image via StorageFile + SetSourceAsync.
        //    BitmapImage(Uri) with file:/// does NOT work in unpackaged WinUI 3 apps.
        //    StorageFile API is the correct path for local files.
        AppLogger.Info("Screenshot: loading via StorageFile...");
        var storageFile = await Windows.Storage.StorageFile.GetFileFromPathAsync(path);
        using var fileStream = await storageFile.OpenReadAsync();
        var bitmapImage = new BitmapImage();
        await bitmapImage.SetSourceAsync(fileStream);
        try { File.Delete(path); } catch { }
        AppLogger.Info($"Screenshot: BitmapImage ready ({bitmapImage.PixelWidth}×{bitmapImage.PixelHeight}) — opening selector");

        // 3. Create window with already-decoded image — shows instantly, no flash
        var tcs = new TaskCompletionSource<CaptureRegion?>();
        var window = new RegionSelectorWindow(bitmapImage);

        window.RegionSelected += (_, region) =>
        {
            LastSelectedRegion = region;
            tcs.TrySetResult(region);
        };
        window.SelectionCancelled += (_, _) => tcs.TrySetResult(null);
        window.Closed += (_, _) => tcs.TrySetResult(null);

        window.Activate();
        return await tcs.Task;
    }

    private static string CaptureScreen()
    {
        int x = GetSystemMetrics(SM_XVIRTUALSCREEN);
        int y = GetSystemMetrics(SM_YVIRTUALSCREEN);
        int w = GetSystemMetrics(SM_CXVIRTUALSCREEN);
        int h = GetSystemMetrics(SM_CYVIRTUALSCREEN);
        AppLogger.Info($"Screenshot: virtual screen ({x},{y}) {w}×{h}");
        using var bmp = new Bitmap(w, h);
        using (var g = Graphics.FromImage(bmp))
            g.CopyFromScreen(x, y, 0, 0, new Size(w, h));
        Directory.CreateDirectory(AppSettings.TempRoot);
        var path = Path.Combine(AppSettings.TempRoot, $"snap_{Guid.NewGuid():N}.png");
        bmp.Save(path, ImageFormat.Png);
        return path;
    }
}
