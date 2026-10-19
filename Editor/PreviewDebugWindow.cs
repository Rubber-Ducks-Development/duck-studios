namespace DuckStudio;

public sealed class PreviewDebugWindow : TextReportWindow
{
    public PreviewDebugWindow(Func<string> report) : base("Preview debug report",
        "Reproduce the problem, then refresh and save this report. It includes the latest build failure, selected texture bindings and renderer errors without image or model contents. Review file names before sharing.",
        report(), "DuckStudio-preview-debug", report) { }
}
