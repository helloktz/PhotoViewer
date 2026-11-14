using PhotoViewer.Common.Types;
using PhotoViewer.Common.Types.JsonTypeConverters;
using System;
using System.Collections.Frozen;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace PhotoViewer.Common.Localization;

[JsonSerializable(typeof(Lang))]
public partial class LangJsonContext : JsonSerializerContext { }

public class Lang
{

    public const string DefaultLanguageCode = "en-US";
    private const string MissingTranslation = "undefined";
    private const string FileSuffix = ".iglang.json";
    private const float SpecVersion = 10;
    private static readonly string[] _languageCodes = [DefaultLanguageCode, "zh-CN"];

    [JsonPropertyName("_Metadata")]
    public LangMetadata Metadata { get; set; } = new();

    [JsonConverter(typeof(JsonLangItemsConverter))]
    public IDictionary<LangId, string> Items { get; set; } = FrozenDictionary<LangId, string>.Empty;

    [JsonIgnore]
    public string FilePath { get; private set; } = string.Empty;

    [JsonIgnore]
    public string DisplayFilePath => BHelper.GetRealPlatformPath(FilePath);

    [JsonIgnore]
    public string FileName => Path.GetFileName(FilePath);

    [JsonIgnore]
    public string this[string? key, params object?[] args] => Get(key, args);

    [JsonIgnore]
    public string this[LangId? key, params object?[] args] => Get(key, args);

    public async Task SaveAsFileAsync(string filePath)
    {
        var context = new LangJsonContext(BHelper.CreateJsonOptions());
        await BHelper.WriteJsonToFileAsync(filePath, this, context.Lang);
    }

    public static LangId? GetKey(string? key)
        => key is not null && _keysByName.TryGetValue(key, out var langKey) ? langKey : null;

    public string Get(string? key, params object?[] args) => Get(GetKey(key), args);

    public string Get(LangId? key, params object?[] args)
    {
        if (key is null || !Items.TryGetValue(key.Value, out var value) || string.IsNullOrWhiteSpace(value))
        {
            return MissingTranslation;
        }

        if (args.Length == 0) return value;

        try { return string.Format(value, args); }
        catch (FormatException) { return MissingTranslation; }
    }

    public static string NormalizeLanguageCode(string? language)
    {
        if (string.IsNullOrEmpty(language)) return string.Empty;

        var code = language.EndsWith(FileSuffix, StringComparison.OrdinalIgnoreCase)
            ? language[..^FileSuffix.Length]
            : language;

        if (code.Equals("English", StringComparison.OrdinalIgnoreCase)) return DefaultLanguageCode;
        if (code.Equals("Chinese Simplified", StringComparison.OrdinalIgnoreCase)) return "zh-CN";

        return Array.Find(_languageCodes, candidate => candidate.Equals(code, StringComparison.OrdinalIgnoreCase)) ?? code;
    }

    public static async Task<Lang> LoadAsync(string language)
    {
        var code = NormalizeLanguageCode(language);
        if (!_languageCodes.Contains(code, StringComparer.Ordinal)) return new();

        var path = BHelper.BaseDir(Dir.Language, code + FileSuffix);
        if (!File.Exists(path)) return new();

        var context = new LangJsonContext(BHelper.CreateJsonOptions());

        try
        {
            var lang = await BHelper.ReadJsonFromFileAsync(path, context.Lang).ConfigureAwait(false);
            if (lang is { Metadata: { MinVersion: >= SpecVersion } metadata, Items: not null }
                && code.Equals(metadata.Code, StringComparison.OrdinalIgnoreCase))
            {
                lang.FilePath = path;
                lang.Items = lang.Items.ToFrozenDictionary();
                return lang;
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Lang.LoadAsync] failed for '{path}': {ex.Message}");
        }

        return new();
    }

    public static async Task<Lang[]> LoadAllLanguagePacksAsync()
    {
        var packs = await Task.WhenAll(_languageCodes.Select(LoadAsync)).ConfigureAwait(false);
        return packs.Where(pack => pack.FilePath.Length > 0).ToArray();
    }

    public static FrozenDictionary<LangId, string> KeysMap { get; } =
        Enum.GetValues<LangId>().ToFrozenDictionary(key => key, key => key.ToString());

    private static readonly FrozenDictionary<string, LangId> _keysByName =
        KeysMap.ToFrozenDictionary(entry => entry.Value, entry => entry.Key, StringComparer.OrdinalIgnoreCase);

}
