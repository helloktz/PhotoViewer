using SkiaSharp;
using System.Globalization;
using System.Text.RegularExpressions;
using System.Threading;

namespace PhotoViewer.UI.Viewer.Transitions;

public sealed partial class TransitionEffect
{
    private readonly Lock _lock = new();
    private SKRuntimeEffect? _effect;
    private bool _isCompiled;

    public string Id { get; }

    public string Source { get; }

    public string? FilePath { get; }

    public int LineOffset { get; }

    public string? CompileError { get; private set; }

    private TransitionEffect(string id, string source, string? filePath, int lineOffset)
    {
        Id = id;
        Source = source;
        FilePath = filePath;
        LineOffset = lineOffset;
    }

    public static TransitionEffect FromGlsl(string id, string glsl, string? filePath = null)
    {
        return new TransitionEffect(id, GlTransitionsAdapter.ToSkSL(glsl), filePath, GlTransitionsAdapter.LineOffset);
    }

    public SKRuntimeEffect? GetEffect()
    {
        lock (_lock)
        {
            if (_isCompiled) return _effect;
            _isCompiled = true;

            var effect = SKRuntimeEffect.CreateShader(Source, out var errors);
            if (effect is null)
            {
                CompileError = ErrorLineRegex().Replace(errors, m =>
                {
                    var line = int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture) - LineOffset;
                    return $"error: {line}:";
                });
                return null;
            }

            _effect = effect;
            return _effect;
        }
    }

    public static int CountLines(string prefix) => prefix.Split('\n').Length - 1;

    [GeneratedRegex(@"^error: (\d+):", RegexOptions.Multiline)]
    private static partial Regex ErrorLineRegex();

}
