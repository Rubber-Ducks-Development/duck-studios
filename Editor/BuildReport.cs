using System.Text;

namespace DuckStudio;

public sealed class BuildValidationException(IReadOnlyList<ValidationFinding> findings) : InvalidOperationException($"Build blocked by {findings.Count} validation error(s).")
{
    public IReadOnlyList<ValidationFinding> Findings { get; } = findings;
}

public static class BuildReport
{
    public static string Create(ClothingProject project, Exception error)
    {
        var report = new StringBuilder("Duck Studio build report\n");
        report.AppendLine("Version: " + ApplicationUpdates.CurrentVersion).AppendLine("Build: " + PreviewDiagnostics.BuildIdentity).AppendLine("UTC: " + DateTime.UtcNow.ToString("O"));
        report.AppendLine("Project: " + project.Name).AppendLine("Collection: " + project.Collection).AppendLine("Target: " + project.BuildTarget);
        report.AppendLine("Output: " + project.OutputFolder).AppendLine("Clothing items: " + project.Items.Count).AppendLine();
        if (error is BuildValidationException validation)
        {
            report.AppendLine($"Build blocked by {validation.Findings.Count} validation error(s). No build output was published.");
            foreach (var group in validation.Findings.GroupBy(f => f.AssetId))
            {
                var item = project.Items.FirstOrDefault(a => a.Id == group.Key);
                report.AppendLine().AppendLine("Item: " + group.First().Item);
                if (item != null) report.AppendLine("Model: " + item.ModelPath).AppendLine($"PED: {item.Ped}; slot: {item.Slot}; position: {item.Drawable}");
                foreach (var finding in group) report.AppendLine().AppendLine(finding.Problem).AppendLine("Fix: " + finding.Fix);
            }
        }
        else report.AppendLine(error.ToString());
        return report.ToString();
    }
}
