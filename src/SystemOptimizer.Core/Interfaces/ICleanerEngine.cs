using SystemOptimizer.Core.Models;
namespace SystemOptimizer.Core.Interfaces;
public interface ICleanerEngine
{
    Task<ScanResult> ScanTrashAsync(CancellationToken ct = default);
    Task<CleanResult> CleanTrashAsync(IProgress<string> progress, CancellationToken ct = default);
}
