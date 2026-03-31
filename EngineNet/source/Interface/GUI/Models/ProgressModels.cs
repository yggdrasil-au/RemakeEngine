namespace EngineNet.Interface.GUI.Models;

internal sealed record ProgressPanelModel {
    public required string Label { get; init; }
    public required string Spinner { get; init; }
    public required double Percent { get; init; }
    public required string ProgressLine { get; init; }
    public required string ActiveSummary { get; init; }
    public required int ActiveTotal { get; init; }
    public required List<ProgressJobSnapshot> Jobs { get; init; }
    public required List<string> Lines { get; init; }
}

internal readonly record struct ProgressJobSnapshot(string Tool, string File, string Elapsed);
