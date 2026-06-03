using WhisperBurner.WinUI.Models;

namespace WhisperBurner.WinUI.Services;

public interface IRegionSelectionService
{
    CaptureRegion? LastSelectedRegion { get; }
    Task<CaptureRegion?> SelectRegionAsync();
}
