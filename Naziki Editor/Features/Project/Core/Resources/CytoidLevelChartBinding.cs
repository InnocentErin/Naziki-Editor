using System.IO;
using Newtonsoft.Json.Linq;

namespace Naziki_Editor.Features.Project.Resources;

public static class CytoidLevelChartBinding
{
    private static readonly HashSet<string> SupportedDifficulties =
        new(StringComparer.OrdinalIgnoreCase) { "easy", "hard", "extreme" };

    public static string Resolve(string levelPath, string chartPath)
    {
        var fullLevelPath = Path.GetFullPath(levelPath);
        var fullChartPath = Path.GetFullPath(chartPath);
        var levelDirectory = Path.GetDirectoryName(fullLevelPath)
            ?? throw new InvalidDataException("LEVEL_CHART_BINDING_LEVEL_DIRECTORY_MISSING");
        var levelRoot = levelDirectory + Path.DirectorySeparatorChar;
        var level = JObject.Parse(File.ReadAllText(fullLevelPath));
        if (level["charts"] is not JArray charts)
            throw new InvalidDataException("LEVEL_CHART_BINDING_CHARTS_MISSING: level.json does not contain charts.");

        var matches = new List<string>();
        foreach (var chart in charts.OfType<JObject>())
        {
            var difficulty = chart.Value<string>("type");
            var relativePath = chart.Value<string>("path");
            if (string.IsNullOrWhiteSpace(difficulty) ||
                !SupportedDifficulties.Contains(difficulty) ||
                string.IsNullOrWhiteSpace(relativePath))
                continue;

            var candidate = Path.GetFullPath(Path.Combine(levelDirectory,
                relativePath.Replace('/', Path.DirectorySeparatorChar)));
            if (!candidate.StartsWith(levelRoot, StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(candidate, levelDirectory, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException(
                    $"LEVEL_CHART_BINDING_PATH_ESCAPE: charts[].path '{relativePath}' escapes the level directory.");
            if (string.Equals(candidate, fullChartPath, StringComparison.OrdinalIgnoreCase))
                matches.Add(difficulty.ToLowerInvariant());
        }

        if (matches.Count == 0)
            throw new InvalidDataException(
                "LEVEL_CHART_BINDING_NOT_FOUND: the selected chart does not match any level charts[].path.");
        if (matches.Count > 1)
            throw new InvalidDataException(
                "LEVEL_CHART_BINDING_AMBIGUOUS: the selected chart matches more than one level chart entry.");
        return matches[0];
    }
}
