using PhotoViewer.Common;
using PhotoViewer.Common.Types;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;

namespace PhotoViewer.UI.Viewer.Transitions;

public static class TransitionEffects
{

    public const string NONE = "None";

    public const string RANDOM = "Random";

    public const string FILE_EXT = ".igtransition.glsl";

    public const uint MIN_DURATION_MS = 50;
    public const uint MAX_DURATION_MS = 10_000;

    private static volatile TransitionEffect[]? _effects;

    public static IReadOnlyList<TransitionEffect> All => _effects ??= LoadEffects();

    public static void Reload()
    {
        _effects = LoadEffects();
    }

    public static TransitionEffect? Find(string? effectId)
    {
        if (string.IsNullOrWhiteSpace(effectId)) return null;
        if (effectId.Equals(NONE, StringComparison.OrdinalIgnoreCase)) return null;

        if (effectId.Equals(RANDOM, StringComparison.OrdinalIgnoreCase))
        {
            var usable = All.Where(e => e.GetEffect() is not null).ToArray();
            return usable.Length > 0 ? usable[Random.Shared.Next(usable.Length)] : null;
        }

        return All.FirstOrDefault(e => e.Id.Equals(effectId, StringComparison.OrdinalIgnoreCase));
    }

    public static TransitionRequest? CreateRequest(string? effectId, uint durationMs, int direction)
    {
        var effect = Find(effectId);
        if (effect?.GetEffect() is null) return null;

        return new TransitionRequest(effect,
            (int)Math.Clamp(durationMs, MIN_DURATION_MS, MAX_DURATION_MS),
            direction < 0 ? -1 : 1);
    }

    private static TransitionEffect[] LoadEffects()
    {
        var takenIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { NONE, RANDOM };

        var effects = new List<TransitionEffect>();
        LoadEffectsFromDir(BHelper.ConfigDir(Dir.Transitions), takenIds, effects);
        LoadEffectsFromDir(BHelper.BaseDir(Dir.BundledTransitions), takenIds, effects);

        return [.. effects];
    }

    private static void LoadEffectsFromDir(string dir, HashSet<string> takenIds, List<TransitionEffect> effects)
    {
        string[] files;
        try
        {
            if (!Directory.Exists(dir)) return;
            files = Directory.GetFiles(dir);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"❌ Unable to list transition effects in '{dir}': {ex.Message}");
            return;
        }
        Array.Sort(files, StringComparer.OrdinalIgnoreCase);

        foreach (var file in files)
        {
            var fileName = Path.GetFileName(file);
            var isEffect = fileName.EndsWith(FILE_EXT, StringComparison.OrdinalIgnoreCase);
            if (!isEffect || fileName.Length == FILE_EXT.Length) continue;

            var id = fileName[..^FILE_EXT.Length];
            if (!takenIds.Add(id)) continue;

            try
            {
                var glsl = File.ReadAllText(file);
                effects.Add(TransitionEffect.FromGlsl(id, glsl, file));
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"❌ Unable to read transition effect '{file}': {ex.Message}");
            }
        }
    }

}
