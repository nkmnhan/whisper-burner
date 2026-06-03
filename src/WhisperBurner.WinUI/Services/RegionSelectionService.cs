using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.WindowsRuntime;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml.Media.Imaging;
using WhisperBurner.WinUI.Infrastructure;
using WhisperBurner.WinUI.Models;
using WhisperBurner.WinUI.Overlay;

namespace WhisperBurner.WinUI.Services;

public class RegionSelectionService : IRegionSelectionService
{
    public CaptureRegion? LastSelectedRegion { get; private set; }

    public Task<CaptureRegion?> SelectRegionAsync()
    {
        var screenshot = CaptureScreenToWriteableBitmap();
        AppLogger.Info($"Screenshot captured: {screenshot.PixelWidth}×{screenshot.PixelHeight}");

        var tcs = new TaskCompletionSource<CaptureRegion?>();
        var window = new RegionSelectorWindow(screenshot);

        window.RegionSelected += (_, region) =>
        {
            LastSelectedRegion = region;
            tcs.TrySetResult(region);
        };
        window.SelectionCancelled += (_, _) => tcs.TrySetResult(null);
        window.Closed += (_, _) => tcs.TrySetResult(null);

        window.Activate();
        return tcs.Task;
    }

    private static WriteableBitmap CaptureScreenToWriteableBitmap()
    {
        // Use DisplayArea.FindAll() — Windows App SDK API, no P/Invoke GetSystemMetrics
        var displays = DisplayArea.FindAll();
        int sx, sy, sw, sh;
        if (displays.Count > 0)
        {
            sx = displays.Min(d => d.OuterBounds.X);
            sy = displays.Min(d => d.OuterBounds.Y);
            sw = displays.Max(d => d.OuterBounds.X + d.OuterBounds.Width) - sx;
            sh = displays.Max(d => d.OuterBounds.Y + d.OuterBounds.Height) - sy;
        }
        else
        {
            var b = DisplayArea.Primary.OuterBounds;
            (sx, sy, sw, sh) = (b.X, b.Y, b.Width, b.Height);
        }

        // System.Drawing.CopyFromScreen has no WinUI equivalent without the capture consent dialog
        using var bmp = new Bitmap(sw, sh);
        using (var g = Graphics.FromImage(bmp))
            g.CopyFromScreen(sx, sy, 0, 0, new Size(sw, sh));

        var wb = new WriteableBitmap(sw, sh);
        var bmpData = bmp.LockBits(
            new Rectangle(0, 0, sw, sh),
            ImageLockMode.ReadOnly,
            PixelFormat.Format32bppArgb);
        try
        {
            var bytes = new byte[Math.Abs(bmpData.Stride) * sh];
            Marshal.Copy(bmpData.Scan0, bytes, 0, bytes.Length);
            using var stream = wb.PixelBuffer.AsStream();
            stream.Write(bytes, 0, bytes.Length);
        }
        finally
        {
            bmp.UnlockBits(bmpData);
        }
        return wb;
    }
}
