using System.Collections.Generic;

namespace WhisperLive.Models;

public record SessionSkill(string Name, string Prompt)
{
    public static IReadOnlyList<SessionSkill> Defaults =
    [
        new("Summarize",       "Summarize the session so far"),
        new("Action items",    "List all action items from this session, with owners if mentioned"),
        new("Decisions",       "What decisions were made in this session?"),
        new("Catch me up",     "Catch me up on what was discussed in the last few minutes"),
        new("Open questions",  "What questions were raised but not yet answered?"),
        new("Follow-up email", "Draft a concise follow-up email summarising the key points, decisions, and action items"),
        new("Meeting minutes", "Generate formal meeting minutes: topics discussed, decisions made, and action items with owners"),
        new("Next steps",      "What are the concrete next steps? Group by owner if possible"),
    ];
}
