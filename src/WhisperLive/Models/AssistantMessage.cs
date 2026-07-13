using System;

namespace WhisperLive.Models;

public record AssistantMessage(string Role, string Text, DateTimeOffset Timestamp)
{
    public string TimeDisplay => Timestamp.ToLocalTime().ToString("HH:mm");
}
