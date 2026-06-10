using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using WhisperLive.Models;

namespace WhisperLive.Helpers;

public sealed class AssistantMessageTemplateSelector : DataTemplateSelector
{
    public DataTemplate? UserTemplate { get; set; }
    public DataTemplate? ResponseTemplate { get; set; }
    public DataTemplate? ErrorTemplate { get; set; }

    protected override DataTemplate? SelectTemplateCore(object item) =>
        item is AssistantMessage { Role: "You" } ? UserTemplate
        : item is AssistantMessage { Role: "Error" } ? ErrorTemplate ?? ResponseTemplate
        : ResponseTemplate;
}
