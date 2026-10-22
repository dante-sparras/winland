namespace Winland.Domain;

/// <summary>
/// The two expressions of a <c>move</c> or <c>size</c> window rule, x then y.
/// The text is split at its first whitespace, so neither expression may contain a space.
/// </summary>
public sealed record ExpressionPair(string X, string Y)
{
    internal static bool TryParse(string text, out ExpressionPair? pair)
    {
        pair = null;
        var trimmed = text.Trim();
        var space = trimmed.IndexOfAny([' ', '\t']);
        if (space <= 0)
            return false;

        var x = trimmed[..space].Trim();
        var y = trimmed[(space + 1)..].Trim();
        if (x.Length == 0 || y.Length == 0 || !RuleExpression.IsValid(x) || !RuleExpression.IsValid(y))
            return false;

        pair = new ExpressionPair(x, y);
        return true;
    }

    internal bool TryEvaluate(IReadOnlyDictionary<string, double> variables, out double x, out double y)
    {
        y = 0;
        return RuleExpression.TryEvaluate(X, variables, out x) && RuleExpression.TryEvaluate(Y, variables, out y);
    }
}
