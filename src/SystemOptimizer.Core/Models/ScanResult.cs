namespace SystemOptimizer.Core.Models;
public sealed record ScanResult(
    long TotalBytes,
    int FileCount,
    long RecycleBinBytes,
    long RecycleBinItemCount,
    IReadOnlyDictionary<string, long> CategoryBreakdown)
{
    public static ScanResult Empty => new(0, 0, 0, 0, new Dictionary<string, long>());
}
