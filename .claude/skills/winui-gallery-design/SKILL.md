---
name: winui-gallery-design
description: Use when designing or styling any WinUI 3 screen, control, or layout for WhisperBurner — new pages, control choices, color/typography/spacing/iconography decisions, or auditing XAML against Microsoft's official patterns. Triggers on "design this page", "which control should I use", "style this", "follow the WinUI Gallery", "design principles", "spacing"/"typography"/"color"/"iconography" guidance, "Fluent Design".
---

# WinUI Gallery Design Reference

## Overview

`C:\nkmn\Projects\WinUI-Gallery` is a local clone of the **official Microsoft WinUI-Gallery repo** (the same reference CLAUDE.md names for WhisperBurner's code style). It contains a working sample for every control plus dedicated "Design" and "Fundamentals" pages that encode Fluent Design principles. Treat it as the canonical answer to "what does idiomatic WinUI 3 look like here?" — read the matching sample before inventing a pattern.

Each topic lives at `WinUIGallery\Samples\<Name>\<Name>Page.xaml` (+ `.xaml.cs`, sometimes a `.json`/`.txt` data file).

## Workflow

1. **Classify the task** — is it a *control choice*, a *layout*, or a *foundational design decision* (color, type, spacing, icons, motion)? Use the category map below to find the right sample folder(s).
2. **Read the sample XAML and code-behind** — note resource references (`{ThemeResource ...}`, `{StaticResource ...}`), spacing values, control composition, and naming. These are Microsoft's own idioms, not ad-hoc choices.
3. **Translate, don't copy** — adapt the *pattern* into WhisperBurner's existing structure: brushes go in `Styles/Brushes.xaml` `ThemeDictionaries` (never hardcoded in XAML), settings UI uses `SettingsCard`/`SettingsExpander`, pages delegate to services. See `CLAUDE.md` conventions.
4. **For foundational decisions** (not tied to one control), go straight to the `Design`/`Fundamentals`/`Accessibility` groups — they are the authoritative source for spacing scale, type ramp, color roles, and icon usage across the whole app.

## Category Map (`Samples/<folder>`)

| Group | Use for | Sample folders |
|---|---|---|
| **Design** (foundations) | Color roles & brushes, spacing scale, type ramp, iconography, geometry | `Color` (`brushes.json`), `Spacing`, `Typography` (`TypographyTypeRamp.txt`), `Iconography` (`IconsData.json`), `Geometry` |
| **Accessibility** | Contrast, keyboard nav, screen reader support — check before shipping any new screen | `AccessibilityColorContrast`, `AccessibilityKeyboard`, `AccessibilityScreenReader` |
| **Fundamentals** | Resource dictionaries, styling approach, templating, custom controls, data binding | `Binding`, `Templates`, `CustomUserControls`, `XamlStyles`, `XamlResources` |
| **Layout** | Page/region composition | `Grid`, `StackPanel`, `RelativePanel`, `SplitView`, `Expander`, `Viewbox`, `Border` |
| **Navigation** | App/page navigation chrome | `NavigationView`, `BreadcrumbBar`, `Pivot`, `TabView`, `SelectorBar` |
| **Basic input** | Buttons, pickers, toggles, sliders | `Button`, `ComboBox`, `CheckBox`, `RadioButton`, `Slider`, `ToggleSwitch`, `ColorPicker`, … |
| **Status & info** | Progress, badges, banners, tooltips | `ProgressRing`, `ProgressBar`, `InfoBar`, `InfoBadge`, `ToolTip` |
| **Dialogs & flyouts** | Modal/transient surfaces | `ContentDialog`, `Flyout`, `Popup`, `TeachingTip` |
| **Collections** | Lists/grids of items | `ListView`, `GridView`, `ItemsRepeater`, `ItemsView`, `TreeView` |
| **Text** | Text entry/display | `TextBlock`, `TextBox`, `RichTextBlock`, `AutoSuggestBox`, `NumberBox` |
| **Styles** (visual effects) | Backdrops, brushes, shadows, icons | `SystemBackdrops` (Mica/Acrylic — relevant to `MicaBackdrop` in `MainWindow.xaml`), `AcrylicBrush`, `ThemeShadow`, `IconElement`, `AnimatedIcon` |
| **Motion** | Animations & transitions | `ConnectedAnimation`, `EasingFunction`, `ImplicitTransition`, `PageTransition`, `ParallaxView` |
| **Windowing** | Window/title bar chrome | `AppWindow`, `AppWindowTitleBar`, `TitleBar`, `CreateMultipleWindows` — relevant to `MainWindow`/`SubtitleOverlayWindow`/`RegionSelectorWindow` |
| **System** | Clipboard, storage pickers | `Clipboard`, `StoragePickers` |
| **Shell** | OS-level surfaces | `AppNotification`, `BadgeNotificationManager`, `JumpList` |

Full list of ~140 sample folders: `WinUIGallery\Samples\*`. The category groupings above mirror the gallery's own `SampleSupport\Data\ControlInfoData.json`.

## Applying to WhisperBurner

When a design choice affects more than one screen (spacing scale, type ramp, color roles), resolve it once against the `Design` group and encode it as a shared resource (`Styles/Brushes.xaml` `ThemeDictionaries`, or a shared style in `Styles/`) rather than repeating values per-page — matches the existing `WaveformBarBrush`/`GhostButtonStyle` pattern.
