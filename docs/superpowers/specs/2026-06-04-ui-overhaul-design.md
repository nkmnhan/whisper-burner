# UI Overhaul + Services Reorganisation — Design Spec
**Date:** 2026-06-04
**Approach:** Option A — Clean + Compact
**Status:** Approved

---

## Overview

Two parallel goals:
1. **Services folder split** — reorganise `Services/` into `Audio/` and `Video/` subfolders to reflect the app's two capability tracks.
2. **UI modernisation** — Mica backdrop, compact left-rail nav, audio-only RecordingPage (remove capture region), animated waveform from meditrack, Fluent icons, compact density, app icon.

---

## 1. Services Reorganisation

### New folder structure

```
Services/
  Audio/
    IRecordingService.cs
    RecordingService.cs
    ITranscriptionClient.cs
    TranscriptionClient.cs
    ISubtitleService.cs
    SubtitleService.cs
  Video/
    IRegionSelectionService.cs
    RegionSelectionService.cs
    ISessionRepository.cs
    SessionRepository.cs
```

### Namespace changes

| Old namespace | New namespace |
|---|---|
| `WhisperBurner.WinUI.Services` (audio files) | `WhisperBurner.WinUI.Services.Audio` |
| `WhisperBurner.WinUI.Services` (video files) | `WhisperBurner.WinUI.Services.Video` |

All consumers (`RecordingPage.xaml.cs`, `SettingsPage.xaml.cs`, `App.xaml.cs`) get updated `using` statements. `Infrastructure/` stays unchanged.

### Rationale for split

- **Audio**: NAudio capture → chunk emission → Docker API transcription → subtitle accumulation. One pipeline, one concern.
- **Video**: Screen region selection and session storage (sessions will hold video files in Phase 2+). Grouped now so the folder is ready for `IScreenCaptureService` without a re-shuffle.
- `ISessionRepository` goes in `Video/` because sessions are anchored to video recordings — they carry `VideoFile`, `CaptureRegion`, and burn outputs.

---

## 2. Navigation — Compact Left Rail

### MainWindow.xaml changes

Replace current `NavigationView` with compact icon-only rail:

```xaml
<NavigationView
    PaneDisplayMode="LeftCompact"
    CompactPaneLength="48"
    IsPaneOpen="False"
    IsBackButtonVisible="Collapsed"
    IsSettingsVisible="False">
```

Remove `Content` text from all `NavigationViewItem`s. Add `ToolTipService.ToolTip`.

### Nav items

| Page | Glyph (Segoe Fluent Icons) | Code | Tooltip |
|---|---|---|---|
| RecordingPage | Record circle | `&#xE9D9;` | Record |
| SessionReviewPage | History | `&#xE81C;` | Sessions |
| SettingsPage | Gear | `&#xE713;` | Settings |

All icons use `FontFamily="Segoe Fluent Icons"` via `FontIcon`.

---

## 3. Mica Backdrop + Compact Density + App Icon

### Mica (MainWindow.xaml.cs constructor)

```csharp
SystemBackdrop = MicaController.IsSupported()
    ? new MicaBackdrop { Kind = MicaKind.Base }
    : new DesktopAcrylicBackdrop();
```

Mica chosen over full Acrylic for the main window — more legible over text, less visually noisy for a utility app. `DesktopAcrylicBackdrop` as fallback for VMs or older hardware.

### Compact density (App.xaml MergedDictionaries)

```xaml
<ResourceDictionary Source="ms-appx:///Microsoft.UI.Xaml/DensityStyles/Compact.xaml" />
```

Reduces default control height from 32px to 24px app-wide. Fix any controls that break (e.g., explicitly-sized elements).

### App icon

- File: `Assets/AppIcon.ico` — microphone-themed, multi-size (16×16, 32×32, 48×48, 256×256)
- Build action: `Content`, Copy to Output: `Copy if newer`
- Set in MainWindow constructor: `AppWindow.SetIcon("Assets/AppIcon.ico")`

---

## 4. RecordingPage — Audio-Only Redesign

### Remove

- "Select Capture Area" `Button` and its click handler (`SelectRegionButton_Click`)
- Region display `TextBlock`
- `_captureRegion` field
- `_regionService` field and instantiation
- All `IRegionSelectionService` references
- Start button `IsEnabled` dependency on region selection

### New layout

Three vertical zones, centred column:

```
┌────────────────────────────────────┐
│  ● API Online   model: small  [↻]  │  ← status bar row (compact, caption text)
├────────────────────────────────────┤
│                                    │
│        [ 160px animated area ]     │  ← idle:     FontIcon E720 @ 72px
│                                    │     recording: 5-bar waveform storyboard
│                                    │
├────────────────────────────────────┤
│        [ Start Recording 🎙 ]      │  ← single primary button (accent style)
│        Idle / Ready / Recording…   │  ← caption status text
└────────────────────────────────────┘
```

### Start/Stop button

| State | Label | Style | Icon glyph |
|---|---|---|---|
| Idle | Start Recording | `AccentButtonStyle` | `&#xE720;` (mic) |
| Recording | Stop | `StopButtonStyle` (red bg) | `&#xE71A;` (stop) |

`IsEnabled` depends only on API availability (no region required).

### Waveform animation (ported from meditrack)

5 `Rectangle` bars in a horizontal `StackPanel` (spacing 4px):
- Size: 4px wide, 32px max height, `CornerRadius=2`, accent brush fill
- Each bar has a `ScaleTransform` (ScaleY axis, `RenderTransformOrigin="0.5,1"` so bars grow upward)
- `Storyboard` with one `DoubleAnimation` per bar:
  - ScaleY: `0.25 → 1.0`, `AutoReverse=True`, `Duration=0:0:0.6`, `RepeatBehavior=Forever`
  - `BeginTime` stagger: 0ms / 80ms / 160ms / 240ms / 320ms
- Storyboard is defined inline in `RecordingPage.xaml` `Page.Resources` (NOT in the shared ResourceDictionary) so code-behind can target the specific named bar elements via `x:Name`
- Retrieved in code-behind: `_waveformStoryboard = (Storyboard)Resources["WaveformStoryboard"];`
- Started when recording begins (`_waveformStoryboard.Begin()`), stopped on stop

### Recording dot pulse (ported from meditrack)

The API status `Ellipse` (8px) gets a second storyboard when recording:
- `ScaleTransform` ScaleX + ScaleY: `1.0 → 1.5 → 1.0`, Duration 900ms, RepeatBehavior=Forever
- Dot colour changes to accent red (`SystemAccentColorDark2` or custom `RecordingActiveBrush`)

---

## 5. Styles Organisation

### New file: `Styles/RecordingPageStyles.xaml`

Contains:
- `StopButtonStyle` — `Button` base style, `Background` = `#C42B1C` (Windows 11 critical red), white foreground
- `WaveformBarStyle` — `Rectangle` style (4px wide, 32px tall, CornerRadius 2, accent fill)
- `WaveformStoryboard` — staggered 5-bar ScaleY animation (resource key `WaveformStoryboard`)
- `RecordingActiveBrush` — `SolidColorBrush` for recording dot (`#C42B1C`)

Referenced from `App.xaml`:
```xaml
<ResourceDictionary Source="Styles/RecordingPageStyles.xaml" />
```

---

## 6. What Does NOT Change

- `SubtitleOverlayWindow` — already has `DesktopAcrylicBackdrop`, layout is fine
- `RegionSelectorWindow` — kept as-is (used by `IRegionSelectionService` for future video phase)
- `SettingsPage` — layout unchanged; only `using` statements updated
- `SessionReviewPage` — placeholder unchanged
- `Infrastructure/` — `AppSettings`, `AppLogger` unchanged
- `Models/` — all model records unchanged
- Docker/PowerShell pipeline — completely unaffected

---

## 7. Files Changed Summary

| File | Change |
|---|---|
| `Services/Audio/*` | New subfolder — move + rename namespace |
| `Services/Video/*` | New subfolder — move + rename namespace |
| `MainWindow.xaml` | NavigationView → compact rail with FontIcons |
| `MainWindow.xaml.cs` | Add MicaBackdrop, SetIcon |
| `App.xaml` | Add Compact.xaml + RecordingPageStyles.xaml to MergedDictionaries |
| `Views/RecordingPage.xaml` | Remove region controls; add waveform bars + animated area |
| `Views/RecordingPage.xaml.cs` | Remove region service; update start/stop logic; add storyboard control; update `using` |
| `Views/SettingsPage.xaml.cs` | Update `using` statements only |
| `Styles/RecordingPageStyles.xaml` | New file — StopButtonStyle, WaveformBarStyle, WaveformStoryboard, RecordingActiveBrush |
| `Assets/AppIcon.ico` | New file — multi-size microphone icon |
| `WhisperBurner.WinUI.csproj` | Add `AppIcon.ico` as Content item |

---

## Stolen from meditrack

| meditrack pattern | Adapted to WinUI 3 as |
|---|---|
| `@keyframes waveform` (Tailwind CSS) | XAML `Storyboard` with `DoubleAnimation` on `ScaleY`, 5 bars staggered 80ms |
| `@keyframes pulse` (ClaraFab dot) | XAML `DoubleAnimation` on `ScaleTransform` X+Y for the recording status dot |
| Gradient/accent colour approach | `RecordingActiveBrush` resource token, accent brush on waveform bars |
