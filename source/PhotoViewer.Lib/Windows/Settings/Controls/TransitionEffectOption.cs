using PhotoViewer.Common.Localization;
using PhotoViewer.Common.Types;
using PhotoViewer.UI.Viewer.Transitions;

namespace PhotoViewer.Common.Windows;

public sealed class TransitionEffectOption : PhReactive
{
    private readonly LangId? _nameKey;

    public string Id { get; }

    public string? FilePath { get; }

    public bool IsDivider { get; }

    public string Name => _nameKey is { } key ? Core.Lang[key] : Id;

    public bool HasFilePath => FilePath is not null;

    public string? DisplayFilePath => FilePath is null ? null : BHelper.GetRealPlatformPath(FilePath);

    private TransitionEffectOption(string id, string? filePath, bool isDivider)
    {
        Id = id;
        FilePath = filePath;
        IsDivider = isDivider;
        _nameKey = filePath is null && !isDivider ? Lang.GetKey($"{nameof(TransitionEffect)}_{id}") : null;
    }

    public static TransitionEffectOption FromId(string id) => new(id, null, false);

    public static TransitionEffectOption FromFile(string id, string filePath) => new(id, filePath, false);

    public static TransitionEffectOption CreateDivider() => new(string.Empty, null, true);

    public void RefreshLang() => OnPropertyChanged(nameof(Name));

}
