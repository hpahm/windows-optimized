using Microsoft.Extensions.Logging;
using SystemOptimizer.Core.Interfaces;
using SystemOptimizer.Core.Models;
using SystemOptimizer.Native;

namespace SystemOptimizer.Core;
public sealed class CleanerEngine(ILogger<CleanerEngine> logger) : ICleanerEngine
{
    private static readonly string UserTempPath = Path.GetTempPath();
    private static readonly string SystemTempPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Temp");
    private static readonly string PrefetchPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Prefetch");
    private const string CategoryUserTemp = "User Temp";
    private const string CategorySystemTemp = "System Temp";
    private const string CategoryPrefetch = "Windows Prefetch";
    private const string CategoryRecycleBin = "Recycle Bin";
    public async Task<ScanResult> ScanTrashAsync(CancellationToken ct = default)
    {
        logger.LogInformation("Scanning...");
        return await Task.Run(() =>
        {
            var breakdown = new Dictionary<string, long>();
            int totalFiles = 0;

            // 1. User Temp
            var (userBytes, userCount) = ScanDirectory(UserTempPath, "*", ct);
            breakdown[CategoryUserTemp] = userBytes;
            totalFiles += userCount;

            ct.ThrowIfCancellationRequested();

            // 2. System Temp
            var (sysBytes, sysCount) = ScanDirectory(SystemTempPath, "*", ct);
            breakdown[CategorySystemTemp] = sysBytes;
            totalFiles += sysCount;

            ct.ThrowIfCancellationRequested();

            // 3. Windows Prefetch (*.pf)
            var (pfBytes, pfCount) = ScanDirectory(PrefetchPath, "*.pf", ct);
            breakdown[CategoryPrefetch] = pfBytes;
            totalFiles += pfCount;

            ct.ThrowIfCancellationRequested();

            // 4.  RecycleBin bytes with Win32 API
            var (recycleBinBytes, recycleBinItems) = NativeMethods.QueryRecycleBinSize();
            breakdown[CategoryRecycleBin] = recycleBinBytes;

            long totalBytes = userBytes + sysBytes + pfBytes + recycleBinBytes;

            logger.LogInformation(
                "Scan completed: {TotalBytes:N0} bytes ({FileCount} files, {RecycleBinItems} recycle bin items)",
                totalBytes, totalFiles, recycleBinItems);

            return new ScanResult(totalBytes, totalFiles, recycleBinBytes, recycleBinItems, breakdown);
        }, ct).ConfigureAwait(false);
    }
    private (long Bytes, int Count) ScanDirectory(string path, string searchPattern, CancellationToken ct)
    {
        if (!Directory.Exists(path))
        {
            logger.LogWarning("Directory not found: {Path}", path);
            return (0, 0);
        }

        long totalBytes = 0;
        int fileCount = 0;

        try
        {
            var options = new EnumerationOptions
            {
                IgnoreInaccessible = true,
                RecurseSubdirectories = true,
                AttributesToSkip = FileAttributes.System & FileAttributes.ReparsePoint
            };

            foreach (string filePath in Directory.EnumerateFiles(path, searchPattern, options))
            {
                ct.ThrowIfCancellationRequested();

                try
                {
                    var fileInfo = new FileInfo(filePath);
                    totalBytes += fileInfo.Length;
                    fileCount++;
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or FileNotFoundException){}
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Error scanning directory: {Path}", path);
        }

        return (totalBytes, fileCount);
    }

    public async Task<CleanResult> CleanTrashAsync(IProgress<string> progress, CancellationToken ct = default)
    {
        logger.LogInformation("Starting cleaning...");

        return await Task.Run(() =>
        {
            long freedBytes = 0;
            int deletedFiles = 0;
            int failedFiles = 0;
            var errors = new List<string>();

            // 1. User Temp
            progress.Report($"Cleaning {CategoryUserTemp}...");
            var r1 = CleanDirectory(UserTempPath, "*", progress, ct);
            AccumulateResult(ref freedBytes, ref deletedFiles, ref failedFiles, errors, r1);

            ct.ThrowIfCancellationRequested();

            // 2. System Temp
            progress.Report($"Cleaning {CategorySystemTemp}...");
            var r2 = CleanDirectory(SystemTempPath, "*", progress, ct);
            AccumulateResult(ref freedBytes, ref deletedFiles, ref failedFiles, errors, r2);

            ct.ThrowIfCancellationRequested();

            // 3. Windows Prefetch
            progress.Report($"Cleaning {CategoryPrefetch}...");
            var r3 = CleanDirectory(PrefetchPath, "*.pf", progress, ct);
            AccumulateResult(ref freedBytes, ref deletedFiles, ref failedFiles, errors, r3);

            ct.ThrowIfCancellationRequested();

            // 4. RecycleBin
            progress.Report("Cleaning RecycleBin...");
            bool recycleBinEmptied = NativeMethods.EmptyRecycleBin();

            if (recycleBinEmptied)
                logger.LogInformation("RecycleBin cleaned successfully");
            else
            {
                logger.LogWarning("Can't clean RecycleBin");
                errors.Add("Can't clean RecycleBin");
            }

            progress.Report("Cleaning completed!");

            logger.LogInformation(
                "Cleaning completed: {FreedBytes:N0} bytes ({Deleted} files, {Failed} failed)",
                freedBytes, deletedFiles, failedFiles);

            return new CleanResult(freedBytes, deletedFiles, failedFiles, recycleBinEmptied, errors);
        }, ct).ConfigureAwait(false);
    }

    private (long Freed, int Deleted, int Failed, List<string> Errors) CleanDirectory(
        string path,
        string searchPattern,
        IProgress<string> progress,
        CancellationToken ct)
    {
        long freedBytes = 0;
        int deletedFiles = 0;
        int failedFiles = 0;
        var errors = new List<string>();

        if (!Directory.Exists(path))
            return (freedBytes, deletedFiles, failedFiles, errors);

        var options = new EnumerationOptions
        {
            IgnoreInaccessible = true,
            RecurseSubdirectories = true,
            AttributesToSkip = FileAttributes.System & FileAttributes.ReparsePoint
        };

        foreach (string filePath in Directory.EnumerateFiles(path, searchPattern, options))
        {
            ct.ThrowIfCancellationRequested();

            try
            {
                var fileInfo = new FileInfo(filePath);
                long fileSize = fileInfo.Length;

                progress.Report($"   🔹 {fileInfo.Name} ({FormatBytes(fileSize)})");

                if (fileInfo.IsReadOnly)
                    fileInfo.IsReadOnly = false;

                fileInfo.Delete();

                freedBytes += fileSize;
                deletedFiles++;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                failedFiles++;
                errors.Add($"{filePath}: {ex.Message}");
                logger.LogDebug(ex, "Can't delete file: {FilePath}", filePath);
            }
            catch (FileNotFoundException){}
        }

        TryDeleteEmptyDirectories(path);

        return (freedBytes, deletedFiles, failedFiles, errors);
    }

    private void TryDeleteEmptyDirectories(string rootPath)
    {
        try
        {
            var options = new EnumerationOptions
            {
                IgnoreInaccessible = true,
                RecurseSubdirectories = true
            };

            var directories = Directory.EnumerateDirectories(rootPath, "*", options)
                .OrderByDescending(d => d.Length)
                .ToList();

            foreach (string dir in directories)
            {
                try
                {
                    if (!Directory.EnumerateFileSystemEntries(dir).Any())
                        Directory.Delete(dir);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException){}
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogDebug(ex, "Can't clean empty directories: {Path}", rootPath);
        }
    }

    private static void AccumulateResult(
        ref long freedBytes,
        ref int deletedFiles,
        ref int failedFiles,
        List<string> errors,
        (long Freed, int Deleted, int Failed, List<string> Errors) result)
    {
        freedBytes += result.Freed;
        deletedFiles += result.Deleted;
        failedFiles += result.Failed;
        errors.AddRange(result.Errors);
    }
    internal static string FormatBytes(long bytes) => bytes switch
    {
        < 1024L => $"{bytes} B",
        < 1024L * 1024 => $"{bytes / 1024.0:F1} KB",
        < 1024L * 1024 * 1024 => $"{bytes / (1024.0 * 1024):F1} MB",
        _ => $"{bytes / (1024.0 * 1024 * 1024):F2} GB"
    };
}
