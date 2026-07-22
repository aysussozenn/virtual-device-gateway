using System.Globalization;

namespace Gateway.Icd;

/// <summary>Thrown when a scenario <c>where</c> expression cannot be parsed.</summary>
public sealed class ExpressionException : Exception
{
    public ExpressionException(string message) : base(message) { }
}

/// <summary>
/// A tiny arithmetic/relational expression evaluator for scenario <c>where</c> rules — so
/// choreography conditions live in the ICD/scenario files instead of C#. Supports numeric
/// literals, identifiers resolved by the caller (e.g. <c>trig.pitch</c>, <c>resp.deflection</c>),
/// <c>+ - * /</c>, comparisons, <c>&amp;&amp;</c>/<c>||</c>, parentheses, and the functions
/// clamp/abs/min/max/round/floor/ceil. Comparisons yield 1/0; <see cref="EvalBool"/> reads
/// the result as a boolean. Deliberately small and side-effect free.
/// </summary>
public sealed class Expression
{
    private readonly Func<Func<string, double>, double> _eval;
    private Expression(Func<Func<string, double>, double> eval) => _eval = eval;

    public double Eval(Func<string, double> vars) => _eval(vars);
    public bool EvalBool(Func<string, double> vars) => _eval(vars) != 0;

    public static Expression Parse(string text)
    {
        var p = new Parser(text);
        var root = p.ParseOr();
        p.ExpectEnd();
        return new Expression(root);
    }

    private sealed class Parser
    {
        private readonly List<string> _tok = new();
        private int _i;

        public Parser(string s)
        {
            for (var i = 0; i < s.Length;)
            {
                var c = s[i];
                if (char.IsWhiteSpace(c)) { i++; continue; }
                if (char.IsLetter(c) || c == '_')
                {
                    var j = i + 1;
                    while (j < s.Length && (char.IsLetterOrDigit(s[j]) || s[j] == '_' || s[j] == '.')) j++;
                    _tok.Add(s[i..j]); i = j; continue;
                }
                if (char.IsDigit(c) || (c == '.' && i + 1 < s.Length && char.IsDigit(s[i + 1])))
                {
                    var j = i + 1;
                    while (j < s.Length && (char.IsDigit(s[j]) || s[j] == '.')) j++;
                    _tok.Add(s[i..j]); i = j; continue;
                }
                if (i + 1 < s.Length)
                {
                    var two = s.Substring(i, 2);
                    if (two is "==" or "!=" or "<=" or ">=" or "&&" or "||") { _tok.Add(two); i += 2; continue; }
                }
                if ("+-*/()<>,".IndexOf(c) >= 0) { _tok.Add(c.ToString()); i++; continue; }
                throw new ExpressionException($"unexpected character '{c}' in expression");
            }
        }

        private string? Peek => _i < _tok.Count ? _tok[_i] : null;
        private string Next() => _tok[_i++];
        private bool Take(string t) { if (Peek == t) { _i++; return true; } return false; }
        public void ExpectEnd() { if (_i != _tok.Count) throw new ExpressionException($"unexpected '{Peek}' in expression"); }

        private static Func<Func<string, double>, double> Bool(bool b) => _ => b ? 1 : 0;

        public Func<Func<string, double>, double> ParseOr()
        {
            var left = ParseAnd();
            while (Take("||")) { var r = ParseAnd(); var l = left; left = v => (l(v) != 0 || r(v) != 0) ? 1 : 0; }
            return left;
        }

        private Func<Func<string, double>, double> ParseAnd()
        {
            var left = ParseCmp();
            while (Take("&&")) { var r = ParseCmp(); var l = left; left = v => (l(v) != 0 && r(v) != 0) ? 1 : 0; }
            return left;
        }

        private Func<Func<string, double>, double> ParseCmp()
        {
            var left = ParseAdd();
            var op = Peek;
            if (op is "==" or "!=" or "<" or "<=" or ">" or ">=")
            {
                _i++;
                var r = ParseAdd(); var l = left;
                return op switch
                {
                    "==" => v => l(v) == r(v) ? 1 : 0,
                    "!=" => v => l(v) != r(v) ? 1 : 0,
                    "<" => v => l(v) < r(v) ? 1 : 0,
                    "<=" => v => l(v) <= r(v) ? 1 : 0,
                    ">" => v => l(v) > r(v) ? 1 : 0,
                    _ => v => l(v) >= r(v) ? 1 : 0,
                };
            }
            return left;
        }

        private Func<Func<string, double>, double> ParseAdd()
        {
            var left = ParseMul();
            while (Peek is "+" or "-")
            {
                var op = Next(); var r = ParseMul(); var l = left;
                left = op == "+" ? v => l(v) + r(v) : v => l(v) - r(v);
            }
            return left;
        }

        private Func<Func<string, double>, double> ParseMul()
        {
            var left = ParseUnary();
            while (Peek is "*" or "/")
            {
                var op = Next(); var r = ParseUnary(); var l = left;
                left = op == "*" ? v => l(v) * r(v) : v => l(v) / r(v);
            }
            return left;
        }

        private Func<Func<string, double>, double> ParseUnary()
        {
            if (Take("-")) { var e = ParseUnary(); return v => -e(v); }
            return ParsePrimary();
        }

        private Func<Func<string, double>, double> ParsePrimary()
        {
            var t = Peek ?? throw new ExpressionException("unexpected end of expression");
            if (t == "(") { _i++; var e = ParseOr(); if (!Take(")")) throw new ExpressionException("missing ')'"); return e; }

            if (double.TryParse(t, NumberStyles.Float, CultureInfo.InvariantCulture, out var num)) { _i++; return _ => num; }

            // identifier or function call
            _i++;
            if (Take("("))
            {
                var args = new List<Func<Func<string, double>, double>>();
                if (Peek != ")")
                {
                    args.Add(ParseOr());
                    while (Take(",")) args.Add(ParseOr());
                }
                if (!Take(")")) throw new ExpressionException($"missing ')' after {t}(");
                return MakeCall(t, args);
            }
            return v => v(t);   // variable resolved by caller
        }

        private static Func<Func<string, double>, double> MakeCall(string name, List<Func<Func<string, double>, double>> a)
        {
            void Need(int n) { if (a.Count != n) throw new ExpressionException($"{name}() expects {n} argument(s)"); }
            switch (name)
            {
                case "clamp": Need(3); return v => Math.Clamp(a[0](v), a[1](v), a[2](v));
                case "abs": Need(1); return v => Math.Abs(a[0](v));
                case "min": Need(2); return v => Math.Min(a[0](v), a[1](v));
                case "max": Need(2); return v => Math.Max(a[0](v), a[1](v));
                case "round": Need(1); return v => Math.Round(a[0](v));
                case "floor": Need(1); return v => Math.Floor(a[0](v));
                case "ceil": Need(1); return v => Math.Ceiling(a[0](v));
                default: throw new ExpressionException($"unknown function '{name}'");
            }
        }
    }
}
