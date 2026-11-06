using System.Text.RegularExpressions;

namespace Orgodesk.Core.Rules;

/// <summary>Matches items against ordered rules. First enabled match wins.</summary>
public static class RulesEngine
{
    public static string? Match(IEnumerable<Rule> rules, ItemFacts item)
    {
        foreach (var r in rules)
        {
            if (!r.Enabled || string.IsNullOrEmpty(r.TargetFenceId)) continue;
            if (Matches(r, item)) return r.TargetFenceId;
        }
        return null;
    }

    public static bool Matches(Rule rule, ItemFacts item)
    {
        if (rule.ItemType != ItemType.Any && rule.ItemType != item.Type) return false;
        switch (rule.Kind)
        {
            case RuleKind.ItemType:
                return rule.ItemType != ItemType.Any;
            case RuleKind.Extension:
            {
                if (item.IsFolder || item.IsSystem) return false;
                var ext = (item.Extension ?? "").TrimStart('.');
                if (ext.Length == 0) return false;
                return rule.Pattern
                    .Split(new[] { ';', ',', ' ' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                    .Select(e => e.TrimStart('.'))
                    .Any(e => string.Equals(e, ext, StringComparison.OrdinalIgnoreCase));
            }
            case RuleKind.NamePattern:
                return rule.Pattern.Length > 0 && GlobToRegex(rule.Pattern).IsMatch(item.Name);
            default:
                return false;
        }
    }

    private static readonly Dictionary<string, Regex> _globCache = new();

    public static Regex GlobToRegex(string glob)
    {
        lock (_globCache)
        {
            if (_globCache.TryGetValue(glob, out var cached)) return cached;
            var pattern = "^" + Regex.Escape(glob).Replace(@"\*", ".*").Replace(@"\?", ".") + "$";
            var rx = new Regex(pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
            _globCache[glob] = rx;
            return rx;
        }
    }
}
