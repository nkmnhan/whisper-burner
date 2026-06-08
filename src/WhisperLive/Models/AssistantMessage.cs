using System;

namespace WhisperLive.Models;

public record AssistantMessage(string Role, string Text, DateTimeOffset Timestamp);
