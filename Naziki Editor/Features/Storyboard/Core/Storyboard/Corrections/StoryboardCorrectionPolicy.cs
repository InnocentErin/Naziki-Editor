using Newtonsoft.Json.Linq;

namespace Naziki_Editor.Core.Storyboard.Corrections;

public static class StoryboardCorrectionPolicy
{
    public static bool CanSafelyMerge(StoryboardCorrectionIssue issue)
    {
        if (issue.Kind != StoryboardCorrectionKind.SameTimeConflict ||
            issue.Participants.Count < 2)
            return false;

        var values = new Dictionary<string, JToken>(StringComparer.Ordinal);
        foreach (var participant in issue.Participants)
        foreach (var property in participant.Properties)
        {
            if (values.TryGetValue(property.Key, out var existing) &&
                !JToken.DeepEquals(existing, property.Value))
                return false;
            values[property.Key] = property.Value;
        }
        return true;
    }

    public static StoryboardCorrectionPlan BuildSafeMergePlan(
        StoryboardCorrectionReport report,
        StoryboardCorrectionIssue issue) =>
        BuildSafeMergePlan(report.DocumentFingerprint, issue);

    public static StoryboardCorrectionPlan BuildSafeMergePlan(
        string documentFingerprint,
        StoryboardCorrectionIssue issue)
    {
        if (!CanSafelyMerge(issue))
            throw new InvalidOperationException("该冲突组包含同名但不同值的属性，不能安全一键合并。");

        var keeper = issue.Participants.FirstOrDefault(item => item.IsBaseState)
                     ?? issue.Participants
                         .OrderByDescending(item => item.Properties.Count)
                         .ThenBy(item => item.StateIndex)
                         .ThenBy(item => item.ArrayIndex ?? -1)
                         .First();
        var losers = issue.Participants
            .Where(item => item.ParticipantIndex != keeper.ParticipantIndex)
            .Select(item => new StoryboardLoserCorrection
            {
                ParticipantIndex = item.ParticipantIndex,
                DeleteScope = item.ArrayIndex.HasValue
                    ? StoryboardDeleteScope.ConflictOccurrence
                    : StoryboardDeleteScope.EntireKeyframe,
                PropertyMigrations = item.Properties.Select(property =>
                    new StoryboardPropertyMigration(
                        property.Key,
                        keeper.Properties.ContainsKey(property.Key)
                            ? StoryboardPropertyMigrationMode.Skip
                            : StoryboardPropertyMigrationMode.Add)).ToArray()
            }).ToArray();

        return new StoryboardCorrectionPlan
        {
            DocumentFingerprint = documentFingerprint,
            IssueId = issue.Id,
            KeepParticipantIndex = keeper.ParticipantIndex,
            Losers = losers
        };
    }
}
