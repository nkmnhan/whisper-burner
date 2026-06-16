# UI Overhaul + Services Reorganisation — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Split Services/ into Audio/ and Video/ subfolders, modernise the WinUI 3 UI with Mica backdrop, compact left-rail nav, an audio-only RecordingPage with animated waveform, Fluent icons, compact density, and app icon.

**Architecture:** Services reorganisation is a pure namespace rename — git mv + find-replace. UI changes are additive: a new ResourceDictionary for styles, Mica on the main window, and a full rewrite of RecordingPage.xaml/.cs to audio-only mode with XAML storyboard animations ported from meditrack.

**Tech Stack:** C# / WinUI 3 / Windows App SDK 1.6, XAML Storyboards, NAudio, dotnet CLI (`dotnet build -t:Compile` for verification throughout).

---

## File Map

| Action | Path |
|---|---|
| Move + rename ns | `Services/IRecordingService.cs` → `Services/Audio/` |
| Move + rename ns | `Services/RecordingService.cs` → `Services/Audio/` |
| Move + rename ns | `Services/ITranscriptionClient.cs` → `Services/Audio/` |
| Move + rename ns | `Services/TranscriptionClient.cs` → `Services/Audio/` |
| Move + rename ns | `Services/ISubtitleService.cs` → `Services/Audio/` |
| Move + rename ns | `Services/SubtitleService.cs` → `Services/Audio/` |
| Move + rename ns | `Services/IRegionSelectionService.cs` → `Services/Video/` |
| Move + rename ns | `Services/RegionSelectionService.cs` → `Services/Video/` |
| Move + rename ns | `Services/ISessionRepository.cs` → `Services/Video/` |
| Move + rename ns | `Infrastructure/SessionRepository.cs` → `Services/Video/` |
| Modify | `Views/RecordingPage.xaml.cs` — remove region service, remove overlay, add storyboard control |
| Modify | `Views/RecordingPage.xaml` — full 3-zone rewrite with waveform storyboards |
| Modify | `MainWindow.xaml` — compact left-rail NavigationView |
| Modify | `MainWindow.xaml.cs` — Mica backdrop + app icon |
| Modify | `App.xaml` — add Compact.xaml + RecordingPageStyles.xaml to MergedDictionaries |
| Create | `Styles/RecordingPageStyles.xaml` |
| Create | `Assets/AppIcon.ico` |
| Modify | `WhisperBurner.WinUI.csproj` — add AppIcon.ico as Content |

---

## Task 1: Move Audio Services to Services/Audio/

**Files:**
- Move: `src/WhisperBurner.WinUI/Services/IRecordingService.cs` → `Services/Audio/`
- Move: `src/WhisperBurner.WinUI/Services/RecordingService.cs` → `Services/Audio/`
- Move: `src/WhisperBurner.WinUI/Services/ITranscriptionClient.cs` → `Services/Audio/`
- Move: `src/WhisperBurner.WinUI/Services/TranscriptionClient.cs` → `Services/Audio/`
- Move: `src/WhisperBurner.WinUI/Services/ISubtitleService.cs` → `Services/Audio/`
- Move: `src/WhisperBurner.WinUI/Services/SubtitleService.cs` → `Services/Audio/`

- [ ] **Step 1: Create the Audio subfolder and git mv all 6 files**

Run from `src/WhisperBurner.WinUI/`:
```powershell
New-Item -ItemType Directory -Path Services\Audio -Force
git mv Services/IRecordingService.cs    Services/Audio/IRecordingService.cs
git mv Services/RecordingService.cs     Services/Audio/RecordingService.cs
git mv Services/ITranscriptionClient.cs Services/Audio/ITranscriptionClient.cs
git mv Services/TranscriptionClient.cs  Services/Audio/TranscriptionClient.cs
git mv Services/ISubtitleService.cs     Services/Audio/ISubtitleService.cs
git mv Services/SubtitleService.cs      Services/Audio/SubtitleService.cs
```

- [ ] **Step 2: Update namespace declaration in all 6 files**

In every file under `Services/Audio/`, change the namespace line from:
```csharp
namespace WhisperBurner.WinUI.Services;
```
to:
```csharp
namespace WhisperBurner.WinUI.Services.Audio;
```

Run this PowerShell to do it in one shot:
```powershell
Get-ChildItem src\WhisperBurner.WinUI\Services\Audio\*.cs | ForEach-Object {
    (Get-Content $_.FullName -Raw) -replace 'namespace WhisperBurner\.WinUI\.Services;', 'namespace WhisperBurner.WinUI.Services.Audio;' |
    Set-Content $_.FullName -NoNewline
}
```

- [ ] **Step 3: Update IRecordingService.cs — change CaptureRegion to nullable**

`Services/Audio/IRecordingService.cs` — change `StartAsync` signature so audio-only callers can pass `null`:
```csharp
using WhisperBurner.WinUI.Models;

namespace WhisperBurner.WinUI.Services.Audio;

public interface IRecordingService
{
    bool IsRecording { get; }

    event EventHandler<AudioChunkInfo>? AudioChunkReady;

    Task StartAsync(CaptureRegion? region, RecordingOptions options);
    Task StopAsync();
}
```

- [ ] **Step 4: Update RecordingService.cs — match nullable signature**

Open `Services/Audio/RecordingService.cs`. Find the `StartAsync` method signature and change the parameter:
```csharp
// Before:
public async Task StartAsync(CaptureRegion region, RecordingOptions options)
// After:
public async Task StartAsync(CaptureRegion? region, RecordingOptions options)
```
The body uses `options` only (not `region`), so no other change is needed.

---

## Task 2: Move Video Services to Services/Video/

**Files:**
- Move: `src/WhisperBurner.WinUI/Services/IRegionSelectionService.cs` → `Services/Video/`
- Move: `src/WhisperBurner.WinUI/Services/RegionSelectionService.cs` → `Services/Video/`
- Move: `src/WhisperBurner.WinUI/Services/ISessionRepository.cs` → `Services/Video/`
- Move: `src/WhisperBurner.WinUI/Infrastructure/SessionRepository.cs` → `Services/Video/`

- [ ] **Step 1: Create the Video subfolder and git mv the 4 files**

```powershell
New-Item -ItemType Directory -Path src\WhisperBurner.WinUI\Services\Video -Force
cd src\WhisperBurner.WinUI
git mv Services/IRegionSelectionService.cs  Services/Video/IRegionSelectionService.cs
git mv Services/RegionSelectionService.cs   Services/Video/RegionSelectionService.cs
git mv Services/ISessionRepository.cs       Services/Video/ISessionRepository.cs
git mv Infrastructure/SessionRepository.cs  Services/Video/SessionRepository.cs
```

- [ ] **Step 2: Update namespace in the 3 files from Services/**

In `IRegionSelectionService.cs`, `RegionSelectionService.cs`, `ISessionRepository.cs`:
```powershell
Get-ChildItem src\WhisperBurner.WinUI\Services\Video\*.cs | ForEach-Object {
    (Get-Content $_.FullName -Raw) -replace 'namespace WhisperBurner\.WinUI\.Services;', 'namespace WhisperBurner.WinUI.Services.Video;' |
    Set-Content $_.FullName -NoNewline
}
```

- [ ] **Step 3: Update SessionRepository.cs — fix namespace + remove stale using**

Open `Services/Video/SessionRepository.cs`. Make these changes:

```csharp
// Change namespace from WhisperBurner.WinUI.Infrastructure to:
namespace WhisperBurner.WinUI.Services.Video;

// Remove this line (ISessionRepository is now in same namespace):
// using WhisperBurner.WinUI.Services;
```

The file top should read:
```csharp
using System.Text.Json;
using System.Text.Json.Serialization;
using WhisperBurner.WinUI.Models;

namespace WhisperBurner.WinUI.Services.Video;
```

- [ ] **Step 4: Update ISessionRepository.cs — change CaptureRegion to nullable**

Open `Services/Video/ISessionRepository.cs`. Change `CreateSessionAsync` signature:
```csharp
Task<SessionManifest> CreateSessionAsync(CaptureRegion? region, RecordingOptions options);
```

- [ ] **Step 5: Update SessionRepository.cs — match nullable signature**

Open `Services/Video/SessionRepository.cs`. Find `CreateSessionAsync` and change:
```csharp
// Before:
public async Task<SessionManifest> CreateSessionAsync(CaptureRegion region, RecordingOptions options)
// After:
public async Task<SessionManifest> CreateSessionAsync(CaptureRegion? region, RecordingOptions options)
```
The body assigns `CaptureRegion = region` into the manifest — this still works since `SessionManifest.CaptureRegion` is already `CaptureRegion?`.

---

## Task 3: Fix Consumers + Compile Check + Commit

**Files:**
- Modify: `src/WhisperBurner.WinUI/Views/RecordingPage.xaml.cs`

- [ ] **Step 1: Update using statements in RecordingPage.xaml.cs**

Replace the single services using with the two new namespace imports:
```csharp
// Remove:
using WhisperBurner.WinUI.Services;

// Add (also keep the Infrastructure using for AppLogger/AppSettings, but remove it for SessionRepository):
using WhisperBurner.WinUI.Services.Audio;
using WhisperBurner.WinUI.Services.Video;
```

The full using block at top of `RecordingPage.xaml.cs` should now be:
```csharp
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using WhisperBurner.WinUI.Infrastructure;
using WhisperBurner.WinUI.Models;
using WhisperBurner.WinUI.Overlay;
using WhisperBurner.WinUI.Services.Audio;
using WhisperBurner.WinUI.Services.Video;
```

- [ ] **Step 2: Update service instantiation lines in RecordingPage.xaml.cs**

The concrete types `RegionSelectionService` and `SessionRepository` are now in `Services.Video`. The instantiation lines at the top of the class don't need changing (namespaces are resolved by the using statements added above).

- [ ] **Step 3: Verify compile**

```powershell
cd src\WhisperBurner.WinUI
dotnet build -t:Compile
```

Expected output contains:
```
Build succeeded.
    0 Error(s)
```

If errors appear, check that every `.cs` file in `Services/Audio/` has `namespace WhisperBurner.WinUI.Services.Audio;` and every file in `Services/Video/` has `namespace WhisperBurner.WinUI.Services.Video;`.

- [ ] **Step 4: Commit**

```bash
cd src/WhisperBurner.WinUI
git add Services/ Infrastructure/ Views/RecordingPage.xaml.cs
git commit -m "refactor: split Services into Audio/ and Video/ subfolders"
```

---

## Task 4: App Icon

**Files:**
- Create: `src/WhisperBurner.WinUI/Assets/AppIcon.ico`
- Modify: `src/WhisperBurner.WinUI/WhisperBurner.WinUI.csproj`

- [ ] **Step 1: Download the Fluent microphone icon**

Download from the Microsoft Fluent UI System Icons GitHub release (search "microsoft fluentui-system-icons releases"). Get `ic_fluent_mic_20_filled.png`, `ic_fluent_mic_32_filled.png`, `ic_fluent_mic_48_filled.png` at sizes 20, 32, 48px.

Alternatively, run this PowerShell to create a simple coloured-circle placeholder ICO using .NET GDI+:
```powershell
Add-Type -AssemblyName System.Drawing
$sizes = @(16, 32, 48, 256)
$bitmaps = $sizes | ForEach-Object {
    $bmp = New-Object System.Drawing.Bitmap($_, $_)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = 'AntiAlias'
    $brush = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(0, 120, 212))
    $g.FillEllipse($brush, 2, 2, $_ - 4, $_ - 4)
    $g.Dispose()
    $bmp
}
# Save each bitmap as a temp PNG then combine with magick if available,
# or just save the 48x48 as a renamed .ico for now:
$bitmaps[2].Save("src\WhisperBurner.WinUI\Assets\AppIcon.png",
    [System.Drawing.Imaging.ImageFormat]::Png)
Copy-Item "src\WhisperBurner.WinUI\Assets\AppIcon.png" `
          "src\WhisperBurner.WinUI\Assets\AppIcon.ico"
```

For a proper multi-size ICO, use ImageMagick after downloading PNGs:
```powershell
magick convert ic_fluent_mic_16.png ic_fluent_mic_32.png ic_fluent_mic_48.png ic_fluent_mic_256.png AppIcon.ico
Move-Item AppIcon.ico src\WhisperBurner.WinUI\Assets\
```

- [ ] **Step 2: Add AppIcon.ico to the csproj**

Open `src/WhisperBurner.WinUI/WhisperBurner.WinUI.csproj` and add inside the `<ItemGroup>` that has `PackageReference` entries (or create a new `<ItemGroup>`):
```xml
<ItemGroup>
  <Content Include="Assets\AppIcon.ico">
    <CopyToOutputDirectory>PreserveNewest</CopyToOutputDirectory>
  </Content>
</ItemGroup>
```

- [ ] **Step 3: Set the icon in MainWindow constructor**

Open `src/WhisperBurner.WinUI/MainWindow.xaml.cs`. Add one line in the constructor after `_appWindow` is set:
```csharp
public MainWindow()
{
    InitializeComponent();

    var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
    _appWindow = AppWindow.GetFromWindowId(Win32Interop.GetWindowIdFromWindow(hwnd));
    _appWindow.SetIcon("Assets\\AppIcon.ico");   // ← add this line

    NavView.SelectedItem = NavView.MenuItems[0];
    ContentFrame.Navigate(typeof(RecordingPage));
}
```

- [ ] **Step 4: Compile check**

```powershell
dotnet build -t:Compile
```

Expected: `Build succeeded. 0 Error(s)`

- [ ] **Step 5: Commit**

```bash
git add src/WhisperBurner.WinUI/Assets/AppIcon.ico \
        src/WhisperBurner.WinUI/WhisperBurner.WinUI.csproj \
        src/WhisperBurner.WinUI/MainWindow.xaml.cs
git commit -m "feat: add app icon and set via AppWindow"
```

---

## Task 5: Create Styles/RecordingPageStyles.xaml

**Files:**
- Create: `src/WhisperBurner.WinUI/Styles/RecordingPageStyles.xaml`

- [ ] **Step 1: Create the Styles folder and the ResourceDictionary file**

```powershell
New-Item -ItemType Directory -Path src\WhisperBurner.WinUI\Styles -Force
```

Create `src/WhisperBurner.WinUI/Styles/RecordingPageStyles.xaml` with this content:
```xml
<ResourceDictionary
    xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">

    <!-- Red stop button — used on RecordingPage when recording is active -->
    <Style x:Key="StopButtonStyle" TargetType="Button" BasedOn="{StaticResource DefaultButtonStyle}">
        <Setter Property="Background" Value="#C42B1C" />
        <Setter Property="Foreground" Value="White" />
        <Setter Property="BorderBrush" Value="#C42B1C" />
    </Style>

    <!-- Waveform bar — 4px wide rounded pill, accent fill -->
    <Style x:Key="WaveformBarStyle" TargetType="Border">
        <Setter Property="Width" Value="4" />
        <Setter Property="Height" Value="32" />
        <Setter Property="CornerRadius" Value="2" />
        <Setter Property="Background" Value="{ThemeResource SystemAccentColorBrush}" />
    </Style>

    <!-- Recording-active dot colour -->
    <SolidColorBrush x:Key="RecordingActiveBrush" Color="#C42B1C" />

</ResourceDictionary>
```

- [ ] **Step 2: Add RecordingPageStyles.xaml to the csproj as a Page item**

Open `WhisperBurner.WinUI.csproj`. Add:
```xml
<ItemGroup>
  <Page Include="Styles\RecordingPageStyles.xaml" />
</ItemGroup>
```

- [ ] **Step 3: Compile check**

```powershell
dotnet build -t:Compile
```

Expected: `Build succeeded. 0 Error(s)`

---

## Task 6: App.xaml Resource Dictionaries + Mica Backdrop

**Files:**
- Modify: `src/WhisperBurner.WinUI/App.xaml`
- Modify: `src/WhisperBurner.WinUI/MainWindow.xaml.cs`

- [ ] **Step 1: Add Compact density + RecordingPageStyles to App.xaml**

Replace the entire `App.xaml` with:
```xml
<Application
    x:Class="WhisperBurner.WinUI.App"
    xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
    xmlns:local="using:WhisperBurner.WinUI">
    <Application.Resources>
        <ResourceDictionary>
            <ResourceDictionary.MergedDictionaries>
                <XamlControlsResources xmlns="using:Microsoft.UI.Xaml.Controls" />
                <ResourceDictionary Source="ms-appx:///Microsoft.UI.Xaml/DensityStyles/Compact.xaml" />
                <ResourceDictionary Source="Styles/RecordingPageStyles.xaml" />
            </ResourceDictionary.MergedDictionaries>
        </ResourceDictionary>
    </Application.Resources>
</Application>
```

- [ ] **Step 2: Add Mica backdrop in MainWindow.xaml.cs**

Add the required using at the top of `MainWindow.xaml.cs`:
```csharp
using Microsoft.UI.Composition.SystemBackdrops;
using Microsoft.UI.Xaml.Media;
```

Add Mica setup in the constructor (after `_appWindow` is initialised, before navigation):
```csharp
public MainWindow()
{
    InitializeComponent();

    var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
    _appWindow = AppWindow.GetFromWindowId(Win32Interop.GetWindowIdFromWindow(hwnd));
    _appWindow.SetIcon("Assets\\AppIcon.ico");

    // Mica backdrop — falls back to Acrylic on VMs / older hardware
    SystemBackdrop = MicaController.IsSupported()
        ? new MicaBackdrop { Kind = MicaKind.Base }
        : new DesktopAcrylicBackdrop();

    NavView.SelectedItem = NavView.MenuItems[0];
    ContentFrame.Navigate(typeof(RecordingPage));
}
```

- [ ] **Step 3: Make Background transparent so Mica shows through**

Open `MainWindow.xaml`. Add `Background="Transparent"` to the root `<Grid>`:
```xml
<Grid Background="Transparent">
    <NavigationView ...>
```

- [ ] **Step 4: Compile check**

```powershell
dotnet build -t:Compile
```

Expected: `Build succeeded. 0 Error(s)`

- [ ] **Step 5: Commit**

```bash
git add src/WhisperBurner.WinUI/App.xaml \
        src/WhisperBurner.WinUI/MainWindow.xaml \
        src/WhisperBurner.WinUI/MainWindow.xaml.cs \
        src/WhisperBurner.WinUI/Styles/
git commit -m "feat: add Mica backdrop, compact density, and RecordingPage styles"
```

---

## Task 7: NavigationView — Compact Left Rail

**Files:**
- Modify: `src/WhisperBurner.WinUI/MainWindow.xaml`

- [ ] **Step 1: Replace the NavigationView in MainWindow.xaml**

Replace the entire content of `MainWindow.xaml` with:
```xml
<Window
    x:Class="WhisperBurner.WinUI.MainWindow"
    xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
    xmlns:local="using:WhisperBurner.WinUI"
    Title="Whisper Burner">
    <Grid Background="Transparent">
        <NavigationView
            x:Name="NavView"
            PaneDisplayMode="LeftCompact"
            CompactPaneLength="48"
            IsPaneOpen="False"
            IsBackButtonVisible="Collapsed"
            IsSettingsVisible="False"
            SelectionChanged="NavView_SelectionChanged">
            <NavigationView.MenuItems>
                <NavigationViewItem Tag="Recording" ToolTipService.ToolTip="Record">
                    <NavigationViewItem.Icon>
                        <FontIcon FontFamily="Segoe Fluent Icons" Glyph="&#xE9D9;" />
                    </NavigationViewItem.Icon>
                </NavigationViewItem>
                <NavigationViewItem Tag="SessionReview" ToolTipService.ToolTip="Sessions">
                    <NavigationViewItem.Icon>
                        <FontIcon FontFamily="Segoe Fluent Icons" Glyph="&#xE81C;" />
                    </NavigationViewItem.Icon>
                </NavigationViewItem>
                <NavigationViewItem Tag="Settings" ToolTipService.ToolTip="Settings">
                    <NavigationViewItem.Icon>
                        <FontIcon FontFamily="Segoe Fluent Icons" Glyph="&#xE713;" />
                    </NavigationViewItem.Icon>
                </NavigationViewItem>
            </NavigationView.MenuItems>
            <Frame x:Name="ContentFrame" />
        </NavigationView>
    </Grid>
</Window>
```

Note: Settings is now a regular menu item (not FooterMenuItems), keeping all three items in one consistent group.

- [ ] **Step 2: Compile check**

```powershell
dotnet build -t:Compile
```

Expected: `Build succeeded. 0 Error(s)`

- [ ] **Step 3: Commit**

```bash
git add src/WhisperBurner.WinUI/MainWindow.xaml
git commit -m "feat: compact left-rail NavigationView with Fluent icons"
```

---

## Task 8: RecordingPage.xaml — Audio-Only Layout with Waveform

**Files:**
- Modify: `src/WhisperBurner.WinUI/Views/RecordingPage.xaml`

- [ ] **Step 1: Replace RecordingPage.xaml with the 3-zone audio layout**

Replace the entire file content:
```xml
<Page
    x:Class="WhisperBurner.WinUI.Views.RecordingPage"
    xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
    xmlns:local="using:WhisperBurner.WinUI.Views"
    Background="Transparent">

    <Page.Resources>
        <!-- Waveform: 5-bar staggered ScaleY animation (ported from meditrack @keyframes waveform) -->
        <Storyboard x:Key="WaveformStoryboard">
            <DoubleAnimation Storyboard.TargetName="Bar1Scale" Storyboard.TargetProperty="ScaleY"
                             From="0.25" To="1.0" Duration="0:0:0.6" AutoReverse="True"
                             RepeatBehavior="Forever" BeginTime="0:0:0" />
            <DoubleAnimation Storyboard.TargetName="Bar2Scale" Storyboard.TargetProperty="ScaleY"
                             From="0.25" To="1.0" Duration="0:0:0.6" AutoReverse="True"
                             RepeatBehavior="Forever" BeginTime="0:0:0.08" />
            <DoubleAnimation Storyboard.TargetName="Bar3Scale" Storyboard.TargetProperty="ScaleY"
                             From="0.25" To="1.0" Duration="0:0:0.6" AutoReverse="True"
                             RepeatBehavior="Forever" BeginTime="0:0:0.16" />
            <DoubleAnimation Storyboard.TargetName="Bar4Scale" Storyboard.TargetProperty="ScaleY"
                             From="0.25" To="1.0" Duration="0:0:0.6" AutoReverse="True"
                             RepeatBehavior="Forever" BeginTime="0:0:0.24" />
            <DoubleAnimation Storyboard.TargetName="Bar5Scale" Storyboard.TargetProperty="ScaleY"
                             From="0.25" To="1.0" Duration="0:0:0.6" AutoReverse="True"
                             RepeatBehavior="Forever" BeginTime="0:0:0.32" />
        </Storyboard>

        <!-- Pulse: API dot scale animation (ported from meditrack @keyframes pulse) -->
        <Storyboard x:Key="PulseDotStoryboard">
            <DoubleAnimation Storyboard.TargetName="ApiDotScale" Storyboard.TargetProperty="ScaleX"
                             From="1.0" To="1.5" Duration="0:0:0.9" AutoReverse="True"
                             RepeatBehavior="Forever" />
            <DoubleAnimation Storyboard.TargetName="ApiDotScale" Storyboard.TargetProperty="ScaleY"
                             From="1.0" To="1.5" Duration="0:0:0.9" AutoReverse="True"
                             RepeatBehavior="Forever" />
        </Storyboard>
    </Page.Resources>

    <Grid Padding="20,16,20,20">
        <Grid.RowDefinitions>
            <RowDefinition Height="Auto" />   <!-- Zone 1: status bar -->
            <RowDefinition Height="*" />       <!-- Zone 2: animated area -->
            <RowDefinition Height="Auto" />   <!-- Zone 3: button + status text -->
        </Grid.RowDefinitions>

        <!-- Zone 1: Compact status bar -->
        <StackPanel Orientation="Horizontal" Spacing="8" VerticalAlignment="Center">
            <Ellipse x:Name="ApiDot" Width="8" Height="8" Fill="Gray"
                     RenderTransformOrigin="0.5,0.5">
                <Ellipse.RenderTransform>
                    <ScaleTransform x:Name="ApiDotScale" />
                </Ellipse.RenderTransform>
            </Ellipse>
            <TextBlock x:Name="ApiStatusLabel" Text="Checking…"
                       Style="{StaticResource CaptionTextBlockStyle}"
                       Foreground="{ThemeResource TextFillColorSecondaryBrush}"
                       VerticalAlignment="Center" />
            <TextBlock x:Name="ModelLabel"
                       Style="{StaticResource CaptionTextBlockStyle}"
                       Foreground="{ThemeResource TextFillColorTertiaryBrush}"
                       VerticalAlignment="Center" />
        </StackPanel>

        <!-- Zone 2: Animated area — mic icon (idle) or waveform bars (recording) -->
        <Grid Grid.Row="1" HorizontalAlignment="Center" VerticalAlignment="Center">

            <!-- Idle state: large microphone icon -->
            <FontIcon x:Name="MicIcon"
                      Glyph="&#xE720;"
                      FontFamily="Segoe Fluent Icons"
                      FontSize="72"
                      Foreground="{ThemeResource TextFillColorPrimaryBrush}" />

            <!-- Recording state: 5-bar animated waveform -->
            <StackPanel x:Name="WaveformPanel"
                        Orientation="Horizontal"
                        Spacing="5"
                        VerticalAlignment="Center"
                        HorizontalAlignment="Center"
                        Visibility="Collapsed">
                <Border Style="{StaticResource WaveformBarStyle}"
                        RenderTransformOrigin="0.5,1">
                    <Border.RenderTransform>
                        <ScaleTransform x:Name="Bar1Scale" ScaleY="0.25" />
                    </Border.RenderTransform>
                </Border>
                <Border Style="{StaticResource WaveformBarStyle}"
                        RenderTransformOrigin="0.5,1">
                    <Border.RenderTransform>
                        <ScaleTransform x:Name="Bar2Scale" ScaleY="0.25" />
                    </Border.RenderTransform>
                </Border>
                <Border Style="{StaticResource WaveformBarStyle}"
                        RenderTransformOrigin="0.5,1">
                    <Border.RenderTransform>
                        <ScaleTransform x:Name="Bar3Scale" ScaleY="0.25" />
                    </Border.RenderTransform>
                </Border>
                <Border Style="{StaticResource WaveformBarStyle}"
                        RenderTransformOrigin="0.5,1">
                    <Border.RenderTransform>
                        <ScaleTransform x:Name="Bar4Scale" ScaleY="0.25" />
                    </Border.RenderTransform>
                </Border>
                <Border Style="{StaticResource WaveformBarStyle}"
                        RenderTransformOrigin="0.5,1">
                    <Border.RenderTransform>
                        <ScaleTransform x:Name="Bar5Scale" ScaleY="0.25" />
                    </Border.RenderTransform>
                </Border>
            </StackPanel>

        </Grid>

        <!-- Zone 3: Primary action button + status caption -->
        <StackPanel Grid.Row="2" HorizontalAlignment="Center" Spacing="8">
            <Button x:Name="StartButton"
                    Style="{StaticResource AccentButtonStyle}"
                    Click="StartButton_Click"
                    IsEnabled="False"
                    HorizontalAlignment="Center">
                <StackPanel Orientation="Horizontal" Spacing="8">
                    <FontIcon FontFamily="Segoe Fluent Icons" Glyph="&#xE720;" FontSize="14" />
                    <TextBlock Text="Start Recording" />
                </StackPanel>
            </Button>
            <Button x:Name="StopButton"
                    Style="{StaticResource StopButtonStyle}"
                    Click="StopButton_Click"
                    Visibility="Collapsed"
                    HorizontalAlignment="Center">
                <StackPanel Orientation="Horizontal" Spacing="8">
                    <FontIcon FontFamily="Segoe Fluent Icons" Glyph="&#xE71A;" FontSize="14" />
                    <TextBlock Text="Stop" />
                </StackPanel>
            </Button>
            <TextBlock x:Name="StatusText"
                       Text="Waiting for API…"
                       Style="{StaticResource CaptionTextBlockStyle}"
                       Foreground="{ThemeResource TextFillColorSecondaryBrush}"
                       HorizontalAlignment="Center"
                       TextWrapping="Wrap"
                       MaxWidth="320" />
        </StackPanel>

    </Grid>
</Page>
```

- [ ] **Step 2: Compile check**

```powershell
dotnet build -t:Compile
```

Expected: `Build succeeded. 0 Error(s)` (code-behind still references old names, but XAML compiles independently with -t:Compile).

---

## Task 9: RecordingPage.xaml.cs — Audio-Only Code-Behind

**Files:**
- Modify: `src/WhisperBurner.WinUI/Views/RecordingPage.xaml.cs`

- [ ] **Step 1: Replace RecordingPage.xaml.cs with the audio-only version**

Replace the entire file:
```csharp
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using WhisperBurner.WinUI.Infrastructure;
using WhisperBurner.WinUI.Models;
using WhisperBurner.WinUI.Services.Audio;
using WhisperBurner.WinUI.Services.Video;

namespace WhisperBurner.WinUI.Views;

public sealed partial class RecordingPage : Page
{
    private readonly IRecordingService _recordingService = new RecordingService();
    private readonly ITranscriptionClient _transcriptionClient = new TranscriptionClient();
    private readonly ISubtitleService _subtitleService = new SubtitleService();
    private readonly ISessionRepository _sessionRepository = new SessionRepository();

    private SessionManifest? _currentSession;
    private int _chunksSent;
    private Storyboard? _waveformStoryboard;
    private Storyboard? _pulseDotStoryboard;

    public RecordingPage()
    {
        InitializeComponent();
        AppLogger.Clear();
        AppLogger.Info("App started");
        _recordingService.AudioChunkReady += OnAudioChunkReady;
        _subtitleService.SegmentAdded += OnSegmentAdded;
        Loaded += async (_, _) =>
        {
            _waveformStoryboard = (Storyboard)Resources["WaveformStoryboard"];
            _pulseDotStoryboard = (Storyboard)Resources["PulseDotStoryboard"];
            ModelLabel.Text = $"· {AppSettings.Current.Model}";
            await RefreshApiStatusAsync();
        };
        Unloaded += (_, _) =>
        {
            _recordingService.AudioChunkReady -= OnAudioChunkReady;
            _subtitleService.SegmentAdded -= OnSegmentAdded;
            _subtitleService.EndSession();
        };
    }

    private async Task RefreshApiStatusAsync()
    {
        var ok = await _transcriptionClient.IsAvailableAsync();
        AppLogger.Info($"API health check: {(ok ? "OK" : "OFFLINE")} — {AppSettings.Current.ApiUrl}");
        ApiDot.Fill = new SolidColorBrush(ok ? Colors.Green : Colors.Red);
        ApiStatusLabel.Text = ok ? "API ready" : "API offline";
        StartButton.IsEnabled = ok;
        if (ok) StatusText.Text = "Ready — press Start Recording.";
    }

    private async void StartButton_Click(object sender, RoutedEventArgs e)
    {
        AppLogger.Info("Checking API before start...");
        if (!await _transcriptionClient.IsAvailableAsync())
        {
            AppLogger.Error("API offline — start aborted");
            StatusText.Text = "Cannot start — Docker API is offline. Run start-api-gpu.cmd first.";
            ApiDot.Fill = new SolidColorBrush(Colors.Red);
            ApiStatusLabel.Text = "API offline";
            StartButton.IsEnabled = false;
            return;
        }

        _chunksSent = 0;
        _subtitleService.Clear();

        var options = new RecordingOptions
        {
            Model = AppSettings.Current.Model,
            Language = AppSettings.Current.Language,
            ChunkDurationSeconds = AppSettings.Current.ChunkDurationSeconds,
            CaptureSystemAudio = AppSettings.Current.CaptureSystemAudio
        };
        AppLogger.Info($"Starting recording: model={options.Model} lang={options.Language} " +
                       $"chunk={options.ChunkDurationSeconds}s systemAudio={options.CaptureSystemAudio}");

        _currentSession = await _sessionRepository.CreateSessionAsync(null, options);
        AppLogger.Info($"Session created: {_currentSession.Id}");

        var sessionDir = _sessionRepository.GetSessionDirectory(_currentSession.Id);
        _subtitleService.StartSession(Path.Combine(sessionDir, "subtitles.ndjson"));

        StartButton.IsEnabled = false;
        StartButton.Visibility = Visibility.Collapsed;
        StopButton.Visibility = Visibility.Visible;

        // Switch to waveform view
        MicIcon.Visibility = Visibility.Collapsed;
        WaveformPanel.Visibility = Visibility.Visible;
        _waveformStoryboard?.Begin();

        // Pulse the status dot red
        ApiDot.Fill = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 196, 43, 28));
        ApiStatusLabel.Text = "Recording";
        _pulseDotStoryboard?.Begin();

        StatusText.Text = "Listening…";

        try
        {
            await _recordingService.StartAsync(null, options);
            AppLogger.Info("Recording started successfully");
        }
        catch (Exception ex)
        {
            AppLogger.Error("Failed to start recording", ex);
            StatusText.Text = $"Recording failed to start: {ex.Message}";
            ResetToIdleState();
        }
    }

    private async void StopButton_Click(object sender, RoutedEventArgs e) =>
        await StopRecordingAsync();

    private async Task StopRecordingAsync()
    {
        if (!_recordingService.IsRecording) return;
        AppLogger.Info("Stopping recording...");

        await _recordingService.StopAsync();

        ResetToIdleState();

        var count = _subtitleService.Segments.Count;
        AppLogger.Info($"Recording stopped — {_chunksSent} chunks sent, {count} segments");
        StatusText.Text = $"Done — {count} subtitle segment{(count == 1 ? "" : "s")} captured.";

        _subtitleService.EndSession();

        if (_currentSession is not null && count > 0)
        {
            var dir = _sessionRepository.GetSessionDirectory(_currentSession.Id);
            await _subtitleService.WriteSrtAsync(Path.Combine(dir, "subtitles.srt"));
            await _sessionRepository.SaveManifestAsync(_currentSession);
            AppLogger.Info($"Session saved: {dir}");
            StatusText.Text += $"  Saved → {dir}";
            _currentSession = null;
        }

        await RefreshApiStatusAsync();
    }

    private void ResetToIdleState()
    {
        _waveformStoryboard?.Stop();
        _pulseDotStoryboard?.Stop();
        ApiDotScale.ScaleX = 1;
        ApiDotScale.ScaleY = 1;

        WaveformPanel.Visibility = Visibility.Collapsed;
        MicIcon.Visibility = Visibility.Visible;

        StopButton.Visibility = Visibility.Collapsed;
        StartButton.Visibility = Visibility.Visible;
        StartButton.IsEnabled = true;
    }

    private async void OnAudioChunkReady(object? sender, AudioChunkInfo chunkInfo)
    {
        _chunksSent++;
        var fileSize = new FileInfo(chunkInfo.Path).Length;
        AppLogger.Info($"Chunk #{_chunksSent} ready: {Path.GetFileName(chunkInfo.Path)} " +
                       $"({fileSize:N0} bytes, offset={chunkInfo.OffsetSeconds:F1}s) — sending to API");

        try
        {
            using var stream = File.OpenRead(chunkInfo.Path);
            var segments = await _transcriptionClient.TranscribeChunkAsync(
                stream, AppSettings.Current.Model, AppSettings.Current.Language);

            var offset = chunkInfo.OffsetSeconds;
            var offsetSegments = segments
                .Select(s => s with { Start = s.Start + offset, End = s.End + offset })
                .ToList();

            AppLogger.Info($"Chunk #{_chunksSent} transcribed: {offsetSegments.Count} segments");
            _subtitleService.AppendSegments(offsetSegments);
        }
        catch (Exception ex)
        {
            AppLogger.Error($"Chunk #{_chunksSent} transcription failed", ex);
            DispatcherQueue.TryEnqueue(() =>
                StatusText.Text = $"Transcription error: {ex.Message}");
        }
        finally
        {
            try { File.Delete(chunkInfo.Path); } catch { }
        }
    }

    private void OnSegmentAdded(object? sender, SubtitleSegment segment)
    {
        AppLogger.Info($"Segment: \"{segment.Text.Trim()}\" [{segment.Start:F1}s→{segment.End:F1}s]");
        DispatcherQueue.TryEnqueue(() =>
            StatusText.Text = segment.Text.Trim());
    }
}
```

- [ ] **Step 2: Compile check**

```powershell
dotnet build -t:Compile
```

Expected: `Build succeeded. 0 Error(s)`

- [ ] **Step 3: Commit all UI changes**

```bash
git add src/WhisperBurner.WinUI/Views/RecordingPage.xaml \
        src/WhisperBurner.WinUI/Views/RecordingPage.xaml.cs
git commit -m "feat: audio-only RecordingPage with waveform animation and Fluent icons"
```

---

## Task 10: Smoke Test

This is a UI app — no automated test suite. Verify the following manually by running the app.

- [ ] **Step 1: Build and run**

```powershell
cd src\WhisperBurner.WinUI
dotnet build -t:Compile
```

Then open the project in Visual Studio 18 and press F5 (or use the `run-app.cmd` launcher if configured), targeting `win-x64 | Debug`.

- [ ] **Step 2: Verify navigation rail**

- Left rail shows 3 icon-only items (record circle, history, gear)
- Hovering each shows a tooltip ("Record", "Sessions", "Settings")
- Clicking each navigates to the correct page

- [ ] **Step 3: Verify Mica**

- Main window background has a subtle tinted blur (desktop wallpaper bleeds through)
- Window is not fully opaque/white

- [ ] **Step 4: Verify RecordingPage idle state**

- Large microphone icon centred in zone 2
- Status bar shows "Checking…" dot, then updates to green "API ready" if Docker API is running
- Start Recording button visible, disabled until API ready
- Model label shows `· small` (or current model from settings)

- [ ] **Step 5: Verify RecordingPage recording state (requires Docker API running)**

Start the Docker API: `.\start-api-gpu.cmd`

- Press Start Recording
- Microphone icon disappears; 5-bar waveform appears and animates
- Status dot turns red and pulses
- Stop button appears; Start button disappears
- StatusText updates to show transcribed text as chunks arrive

- [ ] **Step 6: Verify stop**

- Press Stop
- Waveform disappears; microphone icon returns
- Status dot returns to green
- StatusText shows segment count and saved path

- [ ] **Step 7: Final commit if smoke test passes**

```bash
git add -A
git commit -m "feat: complete UI overhaul — Mica, compact nav, audio-only RecordingPage"
```
