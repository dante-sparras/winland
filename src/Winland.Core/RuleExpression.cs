using System.Globalization;

namespace Winland.Core;

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

/// <summary>
/// Arithmetic over numbers and named variables: <c>+ - * / ^</c>, parentheses, unary minus,
/// the constants <c>_pi</c> and <c>_e</c>, and one-argument functions.
/// </summary>
internal static class RuleExpression
{
    public static readonly IReadOnlyList<string> Variables =
        ["monitor_w", "monitor_h", "window_x", "window_y", "window_w", "window_h", "cursor_x", "cursor_y"];

    private static readonly Dictionary<string, Func<double, double>> Functions = new(StringComparer.Ordinal)
    {
        ["abs"] = Math.Abs,
        ["sqrt"] = Math.Sqrt,
        ["sign"] = value => double.IsNaN(value) ? 0 : Math.Sign(value),
        ["rint"] = Math.Round,
        ["exp"] = Math.Exp,
        ["ln"] = Math.Log,
        ["log"] = Math.Log,
        ["log2"] = Math.Log2,
        ["log10"] = Math.Log10,
        ["sin"] = Math.Sin,
        ["cos"] = Math.Cos,
        ["tan"] = Math.Tan,
        ["asin"] = Math.Asin,
        ["acos"] = Math.Acos,
        ["atan"] = Math.Atan,
        ["sinh"] = Math.Sinh,
        ["cosh"] = Math.Cosh,
        ["tanh"] = Math.Tanh,
    };

    private static readonly Dictionary<string, double> Constants = new(StringComparer.Ordinal)
    {
        ["_pi"] = Math.PI,
        ["_e"] = Math.E,
    };

    private static readonly Dictionary<string, double> Probe = Variables.ToDictionary(name => name, _ => 1.0, StringComparer.Ordinal);

    /// <summary>True when the text parses, whatever values the variables take.</summary>
    public static bool IsValid(string text) => new Reader(text, Probe).TryRead(out _);

    /// <summary>Evaluates the text. A syntax error or a result that is not finite fails.</summary>
    public static bool TryEvaluate(string text, IReadOnlyDictionary<string, double> variables, out double value)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(variables);
        return new Reader(text, variables).TryRead(out value) && double.IsFinite(value);
    }

    // Parentheses, unary signs, exponents and function calls each recurse through TryUnary.
    // Past this depth the text is refused, so a config line cannot overflow the stack.
    private const int MaxDepth = 256;

    private ref struct Reader(string text, IReadOnlyDictionary<string, double> variables)
    {
        private readonly string _text = text;
        private int _at;
        private int _depth;

        public bool TryRead(out double value)
        {
            if (!TrySum(out value))
                return false;

            SkipSpace();
            return _at == _text.Length;
        }

        private bool TrySum(out double value)
        {
            if (!TryProduct(out value))
                return false;

            while (true)
            {
                SkipSpace();
                if (!Take('+', out var plus) && !Take('-', out _))
                    return true;

                if (!TryProduct(out var right))
                    return false;

                value = plus ? value + right : value - right;
            }
        }

        private bool TryProduct(out double value)
        {
            if (!TryUnary(out value))
                return false;

            while (true)
            {
                SkipSpace();
                if (!Take('*', out var times) && !Take('/', out _))
                    return true;

                if (!TryUnary(out var right))
                    return false;

                value = times ? value * right : value / right;
            }
        }

        private bool TryUnary(out double value)
        {
            if (_depth >= MaxDepth)
            {
                value = 0;
                return false;
            }

            _depth++;
            try
            {
                return TryUnaryAt(out value);
            }
            finally
            {
                _depth--;
            }
        }

        private bool TryUnaryAt(out double value)
        {
            SkipSpace();
            if (Take('-', out _))
            {
                if (!TryUnary(out value))
                    return false;

                value = -value;
                return true;
            }

            if (Take('+', out _))
                return TryUnary(out value);

            return TryPower(out value);
        }

        private bool TryPower(out double value)
        {
            if (!TryAtom(out value))
                return false;

            SkipSpace();
            if (!Take('^', out _))
                return true;

            if (!TryUnary(out var exponent))
                return false;

            value = Math.Pow(value, exponent);
            return true;
        }

        private bool TryAtom(out double value)
        {
            value = 0;
            SkipSpace();
            if (_at >= _text.Length)
                return false;

            var c = _text[_at];
            if (c == '(')
            {
                _at++;
                if (!TrySum(out value))
                    return false;

                SkipSpace();
                return Take(')', out _);
            }

            if (char.IsAsciiDigit(c) || c == '.')
                return TryNumber(out value);

            if (char.IsAsciiLetter(c) || c == '_')
                return TryName(out value);

            return false;
        }

        private bool TryNumber(out double value)
        {
            var start = _at;
            while (_at < _text.Length && (char.IsAsciiDigit(_text[_at]) || _text[_at] == '.'))
                _at++;

            if (_at < _text.Length && (_text[_at] == 'e' || _text[_at] == 'E'))
            {
                var mark = _at;
                _at++;
                if (_at < _text.Length && (_text[_at] == '+' || _text[_at] == '-'))
                    _at++;
                if (_at < _text.Length && char.IsAsciiDigit(_text[_at]))
                {
                    while (_at < _text.Length && char.IsAsciiDigit(_text[_at]))
                        _at++;
                }
                else
                {
                    _at = mark;
                }
            }

            return double.TryParse(_text.AsSpan(start, _at - start), NumberStyles.Float, CultureInfo.InvariantCulture, out value);
        }

        private bool TryName(out double value)
        {
            value = 0;
            var start = _at;
            while (_at < _text.Length && (char.IsAsciiLetterOrDigit(_text[_at]) || _text[_at] == '_'))
                _at++;

            var name = _text[start.._at];
            SkipSpace();
            if (Functions.TryGetValue(name, out var function))
            {
                if (!Take('(', out _) || !TrySum(out var argument))
                    return false;

                SkipSpace();
                if (!Take(')', out _))
                    return false;

                value = function(argument);
                return true;
            }

            if (Constants.TryGetValue(name, out value))
                return true;

            return variables.TryGetValue(name, out value);
        }

        private bool Take(char expected, out bool taken)
        {
            taken = _at < _text.Length && _text[_at] == expected;
            if (taken)
                _at++;

            return taken;
        }

        private void SkipSpace()
        {
            while (_at < _text.Length && char.IsWhiteSpace(_text[_at]))
                _at++;
        }
    }
}
