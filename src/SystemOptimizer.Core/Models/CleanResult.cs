namespace SystemOptimizer.Core.Models;
public sealed record CleanResult(
    long FreedBytes,
    int DeletedFiles,
    int FailedFiles,
    bool IsRecycleBinEmptied,
    IReadOnlyList<string> Errors)
{
    public static CleanResult Empty => new(0, 0, 0, false, []);
}
