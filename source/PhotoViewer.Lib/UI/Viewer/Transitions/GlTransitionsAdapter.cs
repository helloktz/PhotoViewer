using System.Text.RegularExpressions;

namespace PhotoViewer.UI.Viewer.Transitions;

public static partial class GlTransitionsAdapter
{
    private const string HEADER = """
        uniform shader fromImage;
        uniform shader toImage;
        uniform float progress;
        uniform float ratio;
        uniform float2 resolution;
        uniform float navigationDirection;

        half4 getFromColor(float2 uv) { return fromImage.eval(float2(uv.x, 1.0 - uv.y) * resolution); }
        half4 getToColor(float2 uv) { return toImage.eval(float2(uv.x, 1.0 - uv.y) * resolution); }

        """;

    private const string FOOTER = """

        half4 main(float2 p) {
            float2 uv = p / resolution;
            return half4(transition(float2(uv.x, 1.0 - uv.y)));
        }
        """;

    public static string ToSkSL(string glsl)
    {
        var body = glsl.ReplaceLineEndings("\n");

        body = UniformWithDefaultRegex().Replace(body, "const $1 $2 = $3;");

        body = HalfIdentifierRegex().Replace(body, "glsl_half");

        return HEADER + body + FOOTER;
    }

    public static int LineOffset { get; } = TransitionEffect.CountLines(HEADER);

    [GeneratedRegex(@"^[ \t]*uniform[ \t]+(\w+)[ \t]+(\w+)[ \t]*;[ \t]*//[ \t]*=[ \t]*([^;\r\n]+?)[ \t]*;?[ \t]*$", RegexOptions.Multiline)]
    private static partial Regex UniformWithDefaultRegex();

    [GeneratedRegex(@"\bhalf\b")]
    private static partial Regex HalfIdentifierRegex();

}
