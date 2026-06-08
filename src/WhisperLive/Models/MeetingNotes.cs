using System;
using System.Collections.Generic;

namespace WhisperLive.Models;

public record MeetingNotes(
    IReadOnlyList<string> KeyPoints,
    IReadOnlyList<string> Decisions,
    IReadOnlyList<string> ActionItems,
    DateTimeOffset GeneratedAt);
