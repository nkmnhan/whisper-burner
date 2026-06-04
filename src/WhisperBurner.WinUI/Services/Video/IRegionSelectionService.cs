using WhisperBurner.WinUI.Models;

namespace WhisperBurner.WinUI.Services.Video;

public interface IRegionSelectionService
{
    CaptureRegion? LastSelectedRegion { get; }
    Task<CaptureRegion?> SelectRegionAsync();
}
