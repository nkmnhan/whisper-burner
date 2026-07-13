using System;

namespace WhisperLive.Models;

public record NotesBubble(string Content, DateTimeOffset GeneratedAt)
{
    public string TimeLabel => $"Notes — {GeneratedAt:HH:mm}";
}
