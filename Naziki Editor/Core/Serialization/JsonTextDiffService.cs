using System.IO;
using Naziki_Editor.Core.Abstractions;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Naziki_Editor.Core.Serialization;

/// <summary>
/// Locates semantic JSON changes and maps them back to source line numbers.
/// Object property order and whitespace are ignored.
/// </summary>
public sealed class JsonTextDiffService : IJsonTextDiffService
{
    private const long MaximumLcsCells = 4_000_000;

    public JsonTextDiffResult Analyze(string beforeJson, string afterJson)
    {
        var before = ParseWithLineInfo(beforeJson);
        var after = ParseWithLineInfo(afterJson);
        var beforeLines = new HashSet<int>();
        var afterLines = new HashSet<int>();
        var changes = 0;
        Compare(before, after, beforeLines, afterLines, ref changes);
        return new JsonTextDiffResult(beforeLines, afterLines, changes);
    }

    private static JToken ParseWithLineInfo(string json)
    {
        using var stringReader = new StringReader(json);
        using var jsonReader = new JsonTextReader(stringReader)
        {
            DateParseHandling = DateParseHandling.None,
            FloatParseHandling = FloatParseHandling.Double
        };
        return JToken.Load(jsonReader, new JsonLoadSettings
        {
            LineInfoHandling = LineInfoHandling.Load,
            CommentHandling = CommentHandling.Ignore,
            DuplicatePropertyNameHandling = DuplicatePropertyNameHandling.Error
        });
    }

    private static void Compare(
        JToken before,
        JToken after,
        HashSet<int> beforeLines,
        HashSet<int> afterLines,
        ref int changes)
    {
        if (SemanticEquals(before, after)) return;
        if (before.Type != after.Type)
        {
            Mark(before, beforeLines);
            Mark(after, afterLines);
            changes++;
            return;
        }

        switch (before)
        {
            case JObject beforeObject when after is JObject afterObject:
                CompareObjects(beforeObject, afterObject, beforeLines, afterLines, ref changes);
                break;
            case JArray beforeArray when after is JArray afterArray:
                CompareArrays(beforeArray, afterArray, beforeLines, afterLines, ref changes);
                break;
            default:
                Mark(before, beforeLines);
                Mark(after, afterLines);
                changes++;
                break;
        }
    }

    private static void CompareObjects(
        JObject before,
        JObject after,
        HashSet<int> beforeLines,
        HashSet<int> afterLines,
        ref int changes)
    {
        var beforeProperties = before.Properties()
            .ToDictionary(property => property.Name, StringComparer.Ordinal);
        var afterProperties = after.Properties()
            .ToDictionary(property => property.Name, StringComparer.Ordinal);

        foreach (var name in beforeProperties.Keys.Union(afterProperties.Keys, StringComparer.Ordinal))
        {
            var hasBefore = beforeProperties.TryGetValue(name, out var beforeProperty);
            var hasAfter = afterProperties.TryGetValue(name, out var afterProperty);
            if (!hasBefore)
            {
                Mark(afterProperty!, afterLines);
                changes++;
            }
            else if (!hasAfter)
            {
                Mark(beforeProperty!, beforeLines);
                changes++;
            }
            else
            {
                Compare(beforeProperty!.Value, afterProperty!.Value,
                    beforeLines, afterLines, ref changes);
            }
        }
    }

    private static void CompareArrays(
        JArray before,
        JArray after,
        HashSet<int> beforeLines,
        HashSet<int> afterLines,
        ref int changes)
    {
        var pairs = AlignEqualItems(before, after);
        var previousBefore = -1;
        var previousAfter = -1;
        foreach (var (beforeIndex, afterIndex) in pairs.Append((before.Count, after.Count)))
        {
            CompareUnmatchedRange(
                before, previousBefore + 1, beforeIndex,
                after, previousAfter + 1, afterIndex,
                beforeLines, afterLines, ref changes);
            previousBefore = beforeIndex;
            previousAfter = afterIndex;
        }
    }

    private static void CompareUnmatchedRange(
        JArray before,
        int beforeStart,
        int beforeEnd,
        JArray after,
        int afterStart,
        int afterEnd,
        HashSet<int> beforeLines,
        HashSet<int> afterLines,
        ref int changes)
    {
        var beforeCount = beforeEnd - beforeStart;
        var afterCount = afterEnd - afterStart;
        var paired = Math.Min(beforeCount, afterCount);
        for (var index = 0; index < paired; index++)
            Compare(before[beforeStart + index], after[afterStart + index],
                beforeLines, afterLines, ref changes);
        for (var index = paired; index < beforeCount; index++)
        {
            Mark(before[beforeStart + index], beforeLines);
            changes++;
        }
        for (var index = paired; index < afterCount; index++)
        {
            Mark(after[afterStart + index], afterLines);
            changes++;
        }
    }

    private static IReadOnlyList<(int Before, int After)> AlignEqualItems(
        JArray before,
        JArray after)
    {
        var beforeKeys = before.Select(Canonical).ToArray();
        var afterKeys = after.Select(Canonical).ToArray();
        var prefix = 0;
        while (prefix < beforeKeys.Length && prefix < afterKeys.Length &&
               beforeKeys[prefix] == afterKeys[prefix])
            prefix++;

        var suffix = 0;
        while (suffix < beforeKeys.Length - prefix &&
               suffix < afterKeys.Length - prefix &&
               beforeKeys[^(suffix + 1)] == afterKeys[^(suffix + 1)])
            suffix++;

        var result = new List<(int, int)>();
        for (var index = 0; index < prefix; index++) result.Add((index, index));

        var beforeMiddle = beforeKeys.Length - prefix - suffix;
        var afterMiddle = afterKeys.Length - prefix - suffix;
        if ((long)(beforeMiddle + 1) * (afterMiddle + 1) <= MaximumLcsCells)
        {
            var lengths = new int[beforeMiddle + 1, afterMiddle + 1];
            for (var left = beforeMiddle - 1; left >= 0; left--)
            for (var right = afterMiddle - 1; right >= 0; right--)
                lengths[left, right] =
                    beforeKeys[prefix + left] == afterKeys[prefix + right]
                        ? lengths[left + 1, right + 1] + 1
                        : Math.Max(lengths[left + 1, right], lengths[left, right + 1]);

            var beforeIndex = 0;
            var afterIndex = 0;
            while (beforeIndex < beforeMiddle && afterIndex < afterMiddle)
            {
                if (beforeKeys[prefix + beforeIndex] == afterKeys[prefix + afterIndex])
                {
                    result.Add((prefix + beforeIndex, prefix + afterIndex));
                    beforeIndex++;
                    afterIndex++;
                }
                else if (lengths[beforeIndex + 1, afterIndex] >=
                         lengths[beforeIndex, afterIndex + 1])
                    beforeIndex++;
                else
                    afterIndex++;
            }
        }

        for (var index = suffix; index > 0; index--)
            result.Add((beforeKeys.Length - index, afterKeys.Length - index));
        return result;
    }

    private static bool SemanticEquals(JToken left, JToken right) =>
        string.Equals(Canonical(left), Canonical(right), StringComparison.Ordinal);

    private static string Canonical(JToken token) => token switch
    {
        JObject obj => "{" + string.Join(",", obj.Properties()
            .OrderBy(property => property.Name, StringComparer.Ordinal)
            .Select(property =>
                JsonConvert.ToString(property.Name) + ":" + Canonical(property.Value))) + "}",
        JArray array => "[" + string.Join(",", array.Select(Canonical)) + "]",
        _ => token.ToString(Formatting.None)
    };

    private static void Mark(JToken token, HashSet<int> lines)
    {
        if (token.Parent is JProperty property) AddLine(property, lines);
        AddLine(token, lines);
        if (token is JContainer container)
            foreach (var descendant in container.Descendants())
                AddLine(descendant, lines);
    }

    private static void AddLine(JToken token, HashSet<int> lines)
    {
        if (token is IJsonLineInfo lineInfo &&
            lineInfo.HasLineInfo() && lineInfo.LineNumber > 0)
            lines.Add(lineInfo.LineNumber);
    }
}
