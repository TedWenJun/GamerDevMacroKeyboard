using System.Text.Json;
using System.Text.Json.Nodes;

namespace MacroHub.Core;

/// <summary>
/// Pairs the display names of the two default configurations (hub.json in Chinese, hub.en.json in English), so a UI
/// can show a name that is still the untouched default in its own language. Names the user changed match neither side
/// and are shown as typed.
/// </summary>
public static class DefaultNames
{
    private static readonly string[] Keys = ["name", "label", "category"];

    /// <summary>Distinct (Chinese, English) pairs, walking both documents in step; they share one structure.</summary>
    public static List<(string Zh, string En)> Pairs(JsonNode? zh, JsonNode? en)
    {
        var pairs = new List<(string, string)>();
        var seen = new HashSet<string>();
        void Walk(JsonNode? a, JsonNode? b)
        {
            switch (a)
            {
                case JsonObject oa when b is JsonObject ob:
                    foreach (var (key, va) in oa)
                    {
                        var vb = ob[key];
                        if (Keys.Contains(key) && Text(va) is { } sa && Text(vb) is { } sb)
                        {
                            if (sa != sb && seen.Add(sa)) pairs.Add((sa, sb));
                        }
                        else Walk(va, vb);
                    }
                    break;
                case JsonArray aa when b is JsonArray ab:
                    for (int i = 0; i < Math.Min(aa.Count, ab.Count); i++) Walk(aa[i], ab[i]);
                    break;
            }
        }
        static string? Text(JsonNode? n) =>
            n is JsonValue v && v.GetValueKind() == JsonValueKind.String ? v.GetValue<string>() : null;
        Walk(zh, en);
        return pairs;
    }
}
