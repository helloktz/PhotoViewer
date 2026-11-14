using Avalonia.Controls;
using PhotoViewer.Common.Localization;
using System.Collections.Generic;

namespace PhotoViewer.Common.Windows;

public sealed class SettingItem
{

    public ConfigId? Id { get; init; }

    public LangId Label { get; init; }

    public SettingsNavId PageNavId { get; init; }

    public LangId? Page { get; init; }

    public LangId? Section { get; init; }

    public Control? Target { get; set; }

    public string LabelText => Core.Lang[Label];

    public string SectionText
    {
        get
        {
            var parts = new List<string>(2);
            if (Page is { } p) parts.Add(Core.Lang[p]);
            if (Section is { } s) parts.Add(Core.Lang[s]);
            return string.Join(" > ", parts);
        }
    }
}
