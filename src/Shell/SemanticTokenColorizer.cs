using Avalonia.Media;
using AvaloniaEdit.Document;
using AvaloniaEdit.Rendering;
using Junexis.Studio.Abstractions;

namespace Junexis.Studio.Shell;

internal sealed class SemanticTokenColorizer : DocumentColorizingTransformer
{
    private static readonly IReadOnlyDictionary<string, IBrush> BrushesByType =
        new Dictionary<string, IBrush>(StringComparer.OrdinalIgnoreCase)
        {
            ["comment"] = new SolidColorBrush(Color.Parse("#6A9955")),
            ["keyword"] = new SolidColorBrush(Color.Parse("#C586C0")),
            ["string"] = new SolidColorBrush(Color.Parse("#CE9178")),
            ["number"] = new SolidColorBrush(Color.Parse("#B5CEA8")),
            ["operator"] = new SolidColorBrush(Color.Parse("#D4D4D4")),
            ["type"] = new SolidColorBrush(Color.Parse("#4EC9B0")),
            ["struct"] = new SolidColorBrush(Color.Parse("#4EC9B0")),
            ["enum"] = new SolidColorBrush(Color.Parse("#B8D7A3")),
            ["function"] = new SolidColorBrush(Color.Parse("#DCDCAA")),
            ["event"] = new SolidColorBrush(Color.Parse("#DCDCAA")),
            ["namespace"] = new SolidColorBrush(Color.Parse("#4EC9B0")),
            ["interface"] = new SolidColorBrush(Color.Parse("#B8D7A3")),
            ["decorator"] = new SolidColorBrush(Color.Parse("#D7BA7D")),
            ["variable"] = new SolidColorBrush(Color.Parse("#9CDCFE")),
            ["property"] = new SolidColorBrush(Color.Parse("#9CDCFE")),
            ["parameter"] = new SolidColorBrush(Color.Parse("#9CDCFE")),
            ["class"] = new SolidColorBrush(Color.Parse("#4EC9B0")),
            ["method"] = new SolidColorBrush(Color.Parse("#DCDCAA")),
        };

    private IReadOnlyList<LanguageSemanticToken> _tokens = [];

    public void SetTokens(IReadOnlyList<LanguageSemanticToken> tokens)
    {
        _tokens = tokens ?? [];
    }

    protected override void ColorizeLine(DocumentLine line)
    {
        foreach (var token in _tokens.Where(item => item.Line + 1 == line.LineNumber))
        {
            if (!BrushesByType.TryGetValue(token.Type, out var brush))
                continue;
            var start = line.Offset + Math.Clamp(token.Character, 0, line.Length);
            var end = Math.Min(line.EndOffset, start + Math.Max(0, token.Length));
            if (end <= start)
                continue;
            ChangeLinePart(start, end, element =>
                element.TextRunProperties.SetForegroundBrush(brush));
        }
    }
}
