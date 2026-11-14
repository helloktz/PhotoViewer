
using PhotoViewer.Common.Types.JsonTypeConverters;
using System.IO;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using System.Threading;
using System.Threading.Tasks;

namespace PhotoViewer.Common;

public partial class BHelper
{

    public static JsonSerializerOptions CreateJsonOptions()
    {
        return new()
        {
            PropertyNameCaseInsensitive = true,
            AllowTrailingCommas = true,
            WriteIndented = true,
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
            NumberHandling = JsonNumberHandling.AllowReadingFromString | JsonNumberHandling.AllowNamedFloatingPointLiterals,

            Converters =
            {
                new JsonDateTimeConverter(),
                new JsonHotkeyArrayConverter(),
            },

            IgnoreReadOnlyProperties = true,
            IgnoreReadOnlyFields = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        };
    }

    public static JsonDocument? ReadJsonDocFromFile(string jsonFilePath)
    {
        if (!File.Exists(jsonFilePath)) return null;

        using var stream = OpenReadShared(jsonFilePath);

        return JsonDocument.Parse(stream, new JsonDocumentOptions
        {
            AllowTrailingCommas = true,
            CommentHandling = JsonCommentHandling.Skip,
        });
    }

    public static T? ReadJsonFromFile<T>(string jsonFilePath, JsonTypeInfo<T> jsonTypeInfo)
    {
        using var stream = OpenReadShared(jsonFilePath);

        return JsonSerializer.Deserialize<T>(stream, jsonTypeInfo);
    }

    public static async Task<T?> ReadJsonFromFileAsync<T>(string jsonFilePath, JsonTypeInfo<T> jsonTypeInfo)
    {
        using var stream = OpenReadShared(jsonFilePath);

        return await JsonSerializer.DeserializeAsync<T>(stream, jsonTypeInfo);
    }

    public static async Task WriteJsonToFileAsync<T>(string jsonFilePath, T value, JsonTypeInfo<T> jsonTypeInfo, CancellationToken token = default)
    {
        var jsonString = JsonSerializer.Serialize(value, jsonTypeInfo);

        await WriteAllTextSharedAsync(jsonFilePath, jsonString, token);
    }
}
