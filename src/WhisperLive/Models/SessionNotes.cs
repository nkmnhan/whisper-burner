using System;
using System.Collections.Generic;

namespace WhisperLive.Models;

public record SessionNotes(
    IReadOnlyList<string> Reasons,
    IReadOnlyList<string> Goals,
    IReadOnlyList<string> Approaches,
    IReadOnlyList<string> Decisions,
    DateTimeOffset GeneratedAt);
