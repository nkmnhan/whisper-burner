# AI Transcript Correction + Meeting Assistant Permissions — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Fix two Meeting Assistant bugs (file-read permissions, concurrent session-ID conflict), add a user-configured Allowed Read Paths setting, and introduce a `TranscriptCorrectionService` that batches raw transcript segments to Claude every 3 minutes and writes a `.corrected.srt` alongside the raw `.srt`.

**Architecture:** New `TranscriptCorrectionService` singleton (parallel to `MeetingAssistantService`) subscribes to `RecordingManager.SegmentAdded`, drains a buffer on a 3-minute `PeriodicTimer`, calls `claude -p` (no session-id) with the raw lines, and both calls `SubtitleService.ApplyCorrections()` (to write `.corrected.srt`) and fires `BatchCorrected` (so `LiveTranscriptPage` can update the in-memory transcript). The two bug fixes are contained entirely in `MeetingAssistantService`.

**Tech Stack:** C# / WinUI 3, Windows App SDK 2.1.3 unpackaged, CommunityToolkit.WinUI.Controls (`SettingsExpander`), `System.Text.Json`, `System.Diagnostics.Process`, `Windows.Storage.Pickers`

---

## File Map

| Action | Path | Responsibility |
|--------|------|---------------|
| Modify | `src/WhisperLive/Infrastructure/AppSettings.cs` | Add `AllowedReadPaths: List<string>` |
| Create | `src/WhisperLive/Models/CorrectedSegment.cs` | `record CorrectedSegment(int OriginalId, string CorrectedText)` |
| Modify | `src/WhisperLive/Services/ISubtitleService.cs` | Add `ApplyCorrections` to interface |
| Modify | `src/WhisperLive/Services/SubtitleService.cs` | Implement `ApplyCorrections`; open `.corrected.srt` lazily |
| Modify | `src/WhisperLive/Services/MeetingAssistantService.cs` | Fix A (allowedTools) + Fix B (SemaphoreSlim) |
| Create | `src/WhisperLive/Services/ITranscriptCorrectionService.cs` | Interface + lifecycle contract |
| Create | `src/WhisperLive/Services/TranscriptCorrectionService.cs` | Full implementation |
| Modify | `src/WhisperLive/App.xaml.cs` | Add `TranscriptCorrectionService` singleton |
| Modify | `src/WhisperLive/Views/LiveTranscriptPage.xaml.cs` | Wire Start/EndSession; subscribe `BatchCorrected` |
| Modify | `src/WhisperLive/Views/SettingsPage.xaml` | Add Allowed Read Paths `SettingsExpander` |
| Modify | `src/WhisperLive/Views/SettingsPage.xaml.cs` | `ObservableCollection<string>` + pickers + remove handler |

---

## Task 1: `AppSettings` + `CorrectedSegment` model

**Files:**
- Modify: `src/WhisperLive/Infrastructure/AppSettings.cs`
- Create: `src/WhisperLive/Models/CorrectedSegment.cs`

- [ ] **Step 1: Add `AllowedReadPaths` to `AppSettings`**

Open `src/WhisperLive/Infrastructure/AppSettings.cs`. Add the new property after `ContextFolderPath`:

```csharp
public string? ContextFolderPath { get; set; }
public List<string> AllowedReadPaths { get; set; } = [];
```

- [ ] **Step 2: Create `CorrectedSegment` record**

Create `src/WhisperLive/Models/CorrectedSegment.cs`:

```csharp
namespace WhisperLive.Models;

public record CorrectedSegment(int OriginalId, string CorrectedText);
```

- [ ] **Step 3: Build and verify**

```powershell
cd src/WhisperLive
dotnet build -c Debug
```

Expected: `Build succeeded. 0 Error(s)`

- [ ] **Step 4: Commit**

```powershell
git add src/WhisperLive/Infrastructure/AppSettings.cs src/WhisperLive/Models/CorrectedSegment.cs
git commit -m "feat: add AllowedReadPaths setting and CorrectedSegment model"
```

---

## Task 2: Bug Fix B — Serialize concurrent Claude calls (`MeetingAssistantService`)

**Files:**
- Modify: `src/WhisperLive/Services/MeetingAssistantService.cs`

- [ ] **Step 1: Add `SemaphoreSlim` field**

At the top of `MeetingAssistantService` class, after the existing fields, add:

```csharp
private readonly SemaphoreSlim _claudeLock = new(1, 1);
```

- [ ] **Step 2: Wrap `RunClaudeAsync` with the semaphore**

`RunClaudeAsync` is currently `private static`. Change it to `private` (instance method) so it can access `_claudeLock`. Then wrap the body:

```csharp
private async Task<string> RunClaudeAsync(string sessionId, string prompt, CancellationToken ct)
{
    await _claudeLock.WaitAsync(ct);
    try
    {
        var psi = new ProcessStartInfo
        {
            FileName = "claude",
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        foreach (var arg in await BuildArgumentsAsync(sessionId))
            psi.ArgumentList.Add(arg);

        Process process;
        try
        {
            process = Process.Start(psi) ?? throw new InvalidOperationException("Process.Start returned null.");
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException("Claude Code CLI not found — is it installed and on PATH?", ex);
        }

        using (process)
        {
            await process.StandardInput.WriteAsync(prompt);
            process.StandardInput.Close();

            var stdoutTask = process.StandardOutput.ReadToEndAsync(ct);
            var stderrTask = process.StandardError.ReadToEndAsync(ct);
            await process.WaitForExitAsync(ct);
            var stdout = await stdoutTask;
            var stderr = await stderrTask;

            if (process.ExitCode != 0)
                throw new InvalidOperationException($"Claude Code exited with an error: {stderr.Trim()}");

            using var doc = JsonDocument.Parse(stdout);
            var root = doc.RootElement;
            if (root.TryGetProperty("is_error", out var isError) && isError.GetBoolean())
                throw new InvalidOperationException("Claude Code reported an error for this request.");

            return root.TryGetProperty("result", out var result) ? result.GetString() ?? string.Empty : string.Empty;
        }
    }
    finally
    {
        _claudeLock.Release();
    }
}
```

- [ ] **Step 3: Release `_claudeLock` in `Dispose`**

Update `Dispose()`:

```csharp
public void Dispose()
{
    _sessionCts?.Cancel();
    _sessionCts?.Dispose();
    _claudeLock.Dispose();
}
```

- [ ] **Step 4: Build and verify**

```powershell
dotnet build -c Debug
```

Expected: `Build succeeded. 0 Error(s)`

- [ ] **Step 5: Commit**

```powershell
git add src/WhisperLive/Services/MeetingAssistantService.cs
git commit -m "fix: serialize concurrent Claude subprocess calls with SemaphoreSlim"
```

---

## Task 3: Bug Fix A — `--allowedTools` for file read permissions (`MeetingAssistantService`)

**Files:**
- Modify: `src/WhisperLive/Services/MeetingAssistantService.cs`

- [ ] **Step 1: Add `BuildAllowedTools` helper**

Add this private static method to `MeetingAssistantService`:

```csharp
private static string? BuildAllowedTools(AppSettings settings)
{
    if (settings.AllowedReadPaths.Count > 0)
    {
        var patterns = settings.AllowedReadPaths
            .Select(p => Directory.Exists(p) ? $"Read({p}/**)" : $"Read({p})")
            .ToList();
        return string.Join(",", patterns);
    }
    // Fallback: if a context folder is set but no explicit path list, allow unrestricted reads
    if (!string.IsNullOrWhiteSpace(settings.ContextFolderPath))
        return "Read";
    return null;
}
```

- [ ] **Step 2: Use `BuildAllowedTools` in `BuildArgumentsAsync`**

Replace the existing `BuildArgumentsAsync` method body with:

```csharp
private static async Task<List<string>> BuildArgumentsAsync(string sessionId)
{
    var args = new List<string> { "-p", "--session-id", sessionId, "--output-format", "json" };
    var settings = await AppSettings.LoadAsync();

    var allowedTools = BuildAllowedTools(settings);
    if (allowedTools is not null)
    {
        args.Add("--allowedTools");
        args.Add(allowedTools);
    }

    var systemPrompt = SystemPromptBase;
    if (!string.IsNullOrWhiteSpace(settings.ContextFolderPath) && Directory.Exists(settings.ContextFolderPath))
    {
        args.Add("--add-dir");
        args.Add(settings.ContextFolderPath);
        systemPrompt += $" You have read access to a project folder at \"{settings.ContextFolderPath}\" — " +
                        "consult it when the question relates to code or documents there.";
    }

    args.Add("--append-system-prompt");
    args.Add(systemPrompt);
    return args;
}
```

- [ ] **Step 3: Add missing `using` if not already present**

Ensure `using System.IO;` is at the top of `MeetingAssistantService.cs` (it already should be for `Path` / `Directory` usage).

- [ ] **Step 4: Build and verify**

```powershell
dotnet build -c Debug
```

Expected: `Build succeeded. 0 Error(s)`

- [ ] **Step 5: Commit**

```powershell
git add src/WhisperLive/Services/MeetingAssistantService.cs
git commit -m "fix: pass --allowedTools to claude -p so --add-dir paths are readable without interactive prompts"
```

---

## Task 4: `SubtitleService` — add `ApplyCorrections` + `.corrected.srt` writer

**Files:**
- Modify: `src/WhisperLive/Services/ISubtitleService.cs`
- Modify: `src/WhisperLive/Services/SubtitleService.cs`

- [ ] **Step 1: Add `ApplyCorrections` to `ISubtitleService`**

Add after `AppendSegments`:

```csharp
void ApplyCorrections(IReadOnlyList<CorrectedSegment> corrections);
```

Also add the using at the top of the file if not present:

```csharp
using WhisperLive.Models;
```

The full updated interface:

```csharp
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using WhisperLive.Models;

namespace WhisperLive.Services;

public interface ISubtitleService
{
    event EventHandler<SubtitleSegment> SegmentAdded;
    IReadOnlyList<SubtitleSegment> AllSegments { get; }
    string? CurrentSessionPath { get; }
    string SessionsDirectory { get; }
    void StartSession();
    void EndSession();
    void AppendSegments(IEnumerable<SubtitleSegment> segments);
    void ApplyCorrections(IReadOnlyList<CorrectedSegment> corrections);
    Task ExportSrtAsync(string path);
}
```

- [ ] **Step 2: Add `_correctedWriter` field to `SubtitleService`**

After the existing `private StreamWriter? _writer;` line, add:

```csharp
private StreamWriter? _correctedWriter;
```

Note: `StartSession()` already calls `EndSession()` first, so no change to `StartSession()` is needed — `EndSession()` will dispose `_correctedWriter` via the next step.

- [ ] **Step 3: Flush and dispose corrected writer in `EndSession`**

Update `EndSession()`:

```csharp
public void EndSession()
{
    _writer?.Flush();
    _writer?.Dispose();
    _writer = null;
    _correctedWriter?.Flush();
    _correctedWriter?.Dispose();
    _correctedWriter = null;
    if (CurrentSessionPath is not null)
        AppLogger.Info("Session file closed: {Path}", CurrentSessionPath);
    _sessionPending = false;
}
```

- [ ] **Step 5: Add `EnsureCorrectedFile` helper**

Add after `EnsureSessionFile()`:

```csharp
private void EnsureCorrectedFile()
{
    if (_correctedWriter is not null || CurrentSessionPath is null) return;
    var correctedPath = Path.ChangeExtension(CurrentSessionPath, ".corrected.srt");
    _correctedWriter = new StreamWriter(correctedPath, append: false, Encoding.UTF8) { AutoFlush = true };
    AppLogger.Info("Corrected session file opened: {Path}", correctedPath);
}
```

- [ ] **Step 6: Implement `ApplyCorrections`**

Add after `WriteSrtEntry`:

```csharp
public void ApplyCorrections(IReadOnlyList<CorrectedSegment> corrections)
{
    EnsureCorrectedFile();
    if (_correctedWriter is null) return;

    foreach (var correction in corrections)
    {
        var index = correction.OriginalId - 1; // IDs are 1-based sequential
        if (index < 0 || index >= _segments.Count) continue;
        _segments[index] = _segments[index] with { Text = correction.CorrectedText };
        try
        {
            _correctedWriter.Write(_segments[index].ToSrtEntry());
            _correctedWriter.WriteLine();
        }
        catch (Exception ex) { AppLogger.Warning(ex, "Failed to write corrected segment"); }
    }
}
```

- [ ] **Step 7: Update `Dispose`**

```csharp
public void Dispose()
{
    EndSession();
}
```

(No change needed — `EndSession` now disposes both writers.)

- [ ] **Step 8: Build and verify**

```powershell
dotnet build -c Debug
```

Expected: `Build succeeded. 0 Error(s)`

- [ ] **Step 9: Commit**

```powershell
git add src/WhisperLive/Services/ISubtitleService.cs src/WhisperLive/Services/SubtitleService.cs
git commit -m "feat: add ApplyCorrections to SubtitleService; write .corrected.srt lazily"
```

---

## Task 5: `ITranscriptCorrectionService` + `TranscriptCorrectionService`

**Files:**
- Create: `src/WhisperLive/Services/ITranscriptCorrectionService.cs`
- Create: `src/WhisperLive/Services/TranscriptCorrectionService.cs`

- [ ] **Step 1: Create the interface**

Create `src/WhisperLive/Services/ITranscriptCorrectionService.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using WhisperLive.Models;

namespace WhisperLive.Services;

public interface ITranscriptCorrectionService
{
    event EventHandler<IReadOnlyList<CorrectedSegment>>? BatchCorrected;
    void StartSession();
    Task FlushAsync(CancellationToken ct = default);
    void EndSession();
}
```

- [ ] **Step 2: Create the implementation**

Create `src/WhisperLive/Services/TranscriptCorrectionService.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using WhisperLive.Infrastructure;
using WhisperLive.Models;

namespace WhisperLive.Services;

public sealed class TranscriptCorrectionService : ITranscriptCorrectionService, IDisposable
{
    private static readonly TimeSpan CorrectionInterval = TimeSpan.FromMinutes(3);

    private const string CorrectionPromptTemplate =
        "Fix ONLY these issues in each transcript line:\n" +
        "1. Trailing \"...\" indicating a clipped word — complete the word using context\n" +
        "2. Trailing \"-\" indicating a word split at a chunk boundary — complete the word\n" +
        "3. Leading words duplicated from the previous line — remove the duplicates\n\n" +
        "Return ONLY a JSON array of corrected strings, one per input line, same order.\n" +
        "Do not rephrase, add meaning, or alter correct text.\n\n" +
        "Input:\n";

    private readonly RecordingManager _recordingManager;
    private readonly SubtitleService _subtitleService;
    private readonly object _bufferLock = new();
    private readonly List<SubtitleSegment> _buffer = [];

    private CancellationTokenSource? _sessionCts;

    public event EventHandler<IReadOnlyList<CorrectedSegment>>? BatchCorrected;

    public TranscriptCorrectionService(RecordingManager recordingManager, SubtitleService subtitleService)
    {
        _recordingManager = recordingManager;
        _subtitleService = subtitleService;
    }

    // ── Lifecycle ─────────────────────────────────────────────────────────────

    public void StartSession()
    {
        lock (_bufferLock) _buffer.Clear();
        _sessionCts = new CancellationTokenSource();
        _recordingManager.SegmentAdded += OnSegmentAdded;
        _ = RunPeriodicCorrectionAsync(_sessionCts.Token);
    }

    public void EndSession()
    {
        _recordingManager.SegmentAdded -= OnSegmentAdded;
        _sessionCts?.Cancel();
        _sessionCts?.Dispose();
        _sessionCts = null;
        _ = FlushAsync(CancellationToken.None);
    }

    private void OnSegmentAdded(object? sender, SubtitleSegment seg)
    {
        lock (_bufferLock) _buffer.Add(seg);
    }

    private async Task RunPeriodicCorrectionAsync(CancellationToken ct)
    {
        try
        {
            using var timer = new PeriodicTimer(CorrectionInterval);
            while (await timer.WaitForNextTickAsync(ct))
                await FlushAsync(ct);
        }
        catch (OperationCanceledException) { }
    }

    // ── Flush ─────────────────────────────────────────────────────────────────

    public async Task FlushAsync(CancellationToken ct = default)
    {
        List<SubtitleSegment> batch;
        lock (_bufferLock)
        {
            if (_buffer.Count == 0) return;
            batch = new List<SubtitleSegment>(_buffer);
            _buffer.Clear();
        }

        try
        {
            var corrected = await CorrectBatchAsync(batch, ct);
            if (corrected.Count == 0) return;
            _subtitleService.ApplyCorrections(corrected);
            BatchCorrected?.Invoke(this, corrected);
        }
        catch (Exception ex)
        {
            AppLogger.Warning(ex, "Transcript correction batch failed — raw transcript unaffected");
        }
    }

    // ── Claude subprocess ─────────────────────────────────────────────────────

    private static async Task<IReadOnlyList<CorrectedSegment>> CorrectBatchAsync(
        List<SubtitleSegment> batch, CancellationToken ct)
    {
        var inputJson = JsonSerializer.Serialize(batch.Select(s => s.Text).ToList());
        var prompt = CorrectionPromptTemplate + inputJson;

        var psi = new ProcessStartInfo
        {
            FileName = "claude",
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        psi.ArgumentList.Add("-p");
        psi.ArgumentList.Add("--output-format");
        psi.ArgumentList.Add("json");

        Process process;
        try
        {
            process = Process.Start(psi) ?? throw new InvalidOperationException("Process.Start returned null.");
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException("Claude Code CLI not found — is it installed and on PATH?", ex);
        }

        using (process)
        {
            await process.StandardInput.WriteAsync(prompt);
            process.StandardInput.Close();

            var stdoutTask = process.StandardOutput.ReadToEndAsync(ct);
            var stderrTask = process.StandardError.ReadToEndAsync(ct);
            await process.WaitForExitAsync(ct);
            var stdout = await stdoutTask;
            var stderr = await stderrTask;

            if (process.ExitCode != 0)
                throw new InvalidOperationException($"Claude exited with error: {stderr.Trim()}");

            using var doc = JsonDocument.Parse(stdout);
            var root = doc.RootElement;
            if (root.TryGetProperty("is_error", out var isError) && isError.GetBoolean())
                throw new InvalidOperationException("Claude reported an error for this correction request.");

            var resultJson = root.TryGetProperty("result", out var r) ? r.GetString() ?? "[]" : "[]";
            using var arrayDoc = JsonDocument.Parse(resultJson);

            var corrected = new List<CorrectedSegment>();
            var i = 0;
            foreach (var item in arrayDoc.RootElement.EnumerateArray())
            {
                if (i < batch.Count)
                    corrected.Add(new CorrectedSegment(batch[i].Id, item.GetString() ?? batch[i].Text));
                i++;
            }
            return corrected;
        }
    }

    public void Dispose()
    {
        _sessionCts?.Cancel();
        _sessionCts?.Dispose();
    }
}
```

- [ ] **Step 3: Build and verify**

```powershell
dotnet build -c Debug
```

Expected: `Build succeeded. 0 Error(s)`

- [ ] **Step 4: Commit**

```powershell
git add src/WhisperLive/Services/ITranscriptCorrectionService.cs src/WhisperLive/Services/TranscriptCorrectionService.cs
git commit -m "feat: add TranscriptCorrectionService — batch AI correction of clipped/duplicate words"
```

---

## Task 6: Wire `TranscriptCorrectionService` into `App` and `LiveTranscriptPage`

**Files:**
- Modify: `src/WhisperLive/App.xaml.cs`
- Modify: `src/WhisperLive/Views/LiveTranscriptPage.xaml.cs`

- [ ] **Step 1: Add singleton to `App`**

In `App.xaml.cs`, add the property and construction. After `MeetingAssistant`:

```csharp
internal MeetingAssistantService MeetingAssistant { get; }
internal TranscriptCorrectionService CorrectionService { get; }

public App()
{
    AppLogger.Initialize();
    InitializeComponent();
    RecordingManager = new RecordingManager(RecordingService, TranscriptionClient, SubtitleService);
    MeetingAssistant = new MeetingAssistantService(RecordingManager);
    CorrectionService = new TranscriptCorrectionService(RecordingManager, SubtitleService);
    UnhandledException += (_, e) =>
    {
        AppLogger.Error(e.Exception, "Unhandled exception: {Message}", e.Message);
        e.Handled = true;
    };
}
```

Also dispose `CorrectionService` in the `MainWindow.Closed` handler, after `await RecordingManager.StopAsync()`:

```csharp
MainWindow.Closed += async (s, _) =>
{
    await RecordingManager.StopAsync();
    CorrectionService.Dispose();   // add this line
    CaptionOverlay?.Close();
    // ... rest unchanged
};
```

- [ ] **Step 2: Wire Start/Stop in `LiveTranscriptPage.OnMainButtonClicked`**

In `OnMainButtonClicked`, after `Assistant.StartSession()` add `CurrentApp.CorrectionService.StartSession()`:

```csharp
if (Manager.State == RecordingState.Idle)
{
    var options = new RecordingOptions(
        Language: _settings.Language,
        ChunkDurationSeconds: _settings.ChunkDurationSeconds,
        ApiUrl: _settings.ApiUrl,
        Model: _settings.Model);

    App.CaptionOverlay?.ClearLines();
    App.CaptionOverlay?.SetLanguage(_settings.Language);
    App.CaptionOverlay?.UpdatePauseState(false);
    Assistant.StartSession();
    CurrentApp.CorrectionService.StartSession();
    await Manager.StartAsync(options);
}
else
{
    await Manager.StopAsync();
    Assistant.EndSession();
    CurrentApp.CorrectionService.EndSession();

    var saved = Manager.CurrentSessionPath is { } p
        ? $"Saved → {System.IO.Path.GetFileName(p)}" : null;
    if (saved is not null)
        ActionStatus.Text = saved;

    await CheckApiHealthAsync();
}
```

- [ ] **Step 3: Subscribe to `BatchCorrected` in `OnLoaded` / `OnUnloaded`**

Add to the field declaration at the top of `LiveTranscriptPage`:

```csharp
private static TranscriptCorrectionService CorrectionService => CurrentApp.CorrectionService;
```

In `OnLoaded`, add after `Assistant.NotesUpdated += OnNotesUpdated;`:

```csharp
CorrectionService.BatchCorrected += OnBatchCorrected;
```

In `OnUnloaded`, add after `Assistant.NotesUpdated -= OnNotesUpdated;`:

```csharp
CorrectionService.BatchCorrected -= OnBatchCorrected;
```

- [ ] **Step 4: Implement `OnBatchCorrected`**

Add to `LiveTranscriptPage`:

```csharp
private void OnBatchCorrected(object? sender, IReadOnlyList<CorrectedSegment> corrections)
{
    DispatcherQueue.TryEnqueue(() =>
    {
        foreach (var correction in corrections)
        {
            var index = correction.OriginalId - 1; // IDs are 1-based
            if (index >= 0 && index < _segments.Count)
                _segments[index] = correction.CorrectedText;
        }
    });
}
```

- [ ] **Step 5: Build and verify**

```powershell
dotnet build -c Debug
```

Expected: `Build succeeded. 0 Error(s)`

- [ ] **Step 6: Commit**

```powershell
git add src/WhisperLive/App.xaml.cs src/WhisperLive/Views/LiveTranscriptPage.xaml.cs
git commit -m "feat: wire TranscriptCorrectionService into App singleton and LiveTranscriptPage"
```

---

## Task 7: Allowed Read Paths — Settings UI

**Files:**
- Modify: `src/WhisperLive/Views/SettingsPage.xaml`
- Modify: `src/WhisperLive/Views/SettingsPage.xaml.cs`

- [ ] **Step 1: Add the `SettingsExpander` to the XAML**

In `SettingsPage.xaml`, add the expander after the existing **Meeting context folder** `SettingsCard` and before the **Appearance** `SettingsCard`:

```xml
<!-- Allowed read paths -->
<controls:SettingsExpander
    x:Name="AllowedPathsExpander"
    Description="Files and folders Claude can read during a meeting session"
    Header="Allowed Read Paths"
    IsExpanded="False">
    <controls:SettingsExpander.HeaderIcon>
        <FontIcon Glyph="&#xE8F4;" />
    </controls:SettingsExpander.HeaderIcon>
    <controls:SettingsExpander.Content>
        <StackPanel Orientation="Horizontal" Spacing="8">
            <Button x:Name="AddFolderButton" Click="OnAddFolderClicked">
                <StackPanel Orientation="Horizontal" Spacing="6">
                    <FontIcon FontSize="14" Glyph="&#xE8B7;" />
                    <TextBlock Text="Add folder" />
                </StackPanel>
            </Button>
            <Button x:Name="AddFileButton" Click="OnAddFileClicked">
                <StackPanel Orientation="Horizontal" Spacing="6">
                    <FontIcon FontSize="14" Glyph="&#xE8A5;" />
                    <TextBlock Text="Add file" />
                </StackPanel>
            </Button>
        </StackPanel>
    </controls:SettingsExpander.Content>
    <controls:SettingsExpander.ItemTemplate>
        <DataTemplate>
            <controls:SettingsCard Header="{Binding}">
                <Button
                    Click="OnRemovePathClicked"
                    Tag="{Binding}"
                    ToolTipService.ToolTip="Remove">
                    <FontIcon FontSize="12" Glyph="&#xE711;" />
                </Button>
            </controls:SettingsCard>
        </DataTemplate>
    </controls:SettingsExpander.ItemTemplate>
</controls:SettingsExpander>
```

- [ ] **Step 2: Add `ObservableCollection` field to `SettingsPage.xaml.cs`**

At the top of `SettingsPage`, add:

```csharp
using System.Collections.ObjectModel;
```

And in the class body, after `private bool _loaded;`:

```csharp
private readonly ObservableCollection<string> _allowedPaths = [];
```

- [ ] **Step 3: Populate paths in `OnLoaded`**

In `OnLoaded`, after `ContextFolderLabel.Text = ...` and before `_loaded = true;`:

```csharp
_allowedPaths.Clear();
foreach (var p in _settings.AllowedReadPaths)
    _allowedPaths.Add(p);
AllowedPathsExpander.ItemsSource = _allowedPaths;
```

- [ ] **Step 4: Add `OnAddFolderClicked` handler**

```csharp
private async void OnAddFolderClicked(object sender, RoutedEventArgs e)
{
    if (WindowHelper.GetWindowForElement(this) is not Window window) return;

    var picker = new FolderPicker { SuggestedStartLocation = PickerLocationId.ComputerFolder };
    picker.FileTypeFilter.Add("*");
    InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(window));

    var folder = await picker.PickSingleFolderAsync();
    if (folder is null) return;

    _allowedPaths.Add(folder.Path);
    _settings.AllowedReadPaths.Add(folder.Path);
    _ = _settings.SaveAsync();
}
```

- [ ] **Step 5: Add `OnAddFileClicked` handler**

```csharp
private async void OnAddFileClicked(object sender, RoutedEventArgs e)
{
    if (WindowHelper.GetWindowForElement(this) is not Window window) return;

    var picker = new FileOpenPicker { SuggestedStartLocation = PickerLocationId.ComputerFolder };
    picker.FileTypeFilter.Add("*");
    InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(window));

    var file = await picker.PickSingleFileAsync();
    if (file is null) return;

    _allowedPaths.Add(file.Path);
    _settings.AllowedReadPaths.Add(file.Path);
    _ = _settings.SaveAsync();
}
```

- [ ] **Step 6: Add `OnRemovePathClicked` handler**

```csharp
private void OnRemovePathClicked(object sender, RoutedEventArgs e)
{
    if (((Button)sender).Tag is not string path) return;

    _allowedPaths.Remove(path);
    _settings.AllowedReadPaths.Remove(path);
    _ = _settings.SaveAsync();
}
```

- [ ] **Step 7: Build and verify**

```powershell
dotnet build -c Debug
```

Expected: `Build succeeded. 0 Error(s)`

- [ ] **Step 8: Commit**

```powershell
git add src/WhisperLive/Views/SettingsPage.xaml src/WhisperLive/Views/SettingsPage.xaml.cs
git commit -m "feat: add Allowed Read Paths settings UI — folder/file pickers with remove"
```

---

## Post-implementation verification

- [ ] Start recording → Assistant panel → **Ask** tab → ask a question about a file in a configured path — confirm it answers without permission errors
- [ ] Start recording → ask two rapid questions — confirm no "session ID already in use" error
- [ ] Record for 3+ minutes — confirm a `.corrected.srt` file appears in `%USERPROFILE%\whisper.live\sessions\` alongside the raw `.srt`
- [ ] Check `LiveTranscriptPage` transcript updates in-place after the 3-minute batch
- [ ] Settings → Allowed Read Paths → Add folder → confirm path appears in list → Remove → confirm it disappears → verify saved in `%USERPROFILE%\whisper.live\settings.json`
