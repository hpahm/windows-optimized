using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System;
using System.Collections.ObjectModel;
using System.Threading;
using System.Threading.Tasks;
using SystemOptimizer.Core.Interfaces;

namespace SystemOptimizer.ViewModels;

public partial class CleanerViewModel : ObservableObject
{
    private readonly ICleanerEngine _cleanerEngine;
    private CancellationTokenSource? _cts;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ScanCommand))]
    [NotifyCanExecuteChangedFor(nameof(CleanCommand))]
    private bool _isScanning;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ScanCommand))]
    [NotifyCanExecuteChangedFor(nameof(CleanCommand))]
    private bool _isCleaning;

    [ObservableProperty]
    private string _statusMessage = "Ready";

    [ObservableProperty]
    private string _totalTrashDisplay = "0 B";

    [ObservableProperty]
    private string _totalFileCount = "0";

    [ObservableProperty]
    private string _freedDisplay = string.Empty;

    public ObservableCollection<string> Logs { get; } = new();

    public CleanerViewModel(ICleanerEngine cleanerEngine)
    {
        _cleanerEngine = cleanerEngine;
    }

    private bool CanScan() => !IsScanning && !IsCleaning;
    private bool CanClean() => !IsScanning && !IsCleaning && TotalTrashDisplay != "0 B";

    [RelayCommand(CanExecute = nameof(CanScan))]
    private async Task ScanAsync()
    {
        _cts = new CancellationTokenSource();
        IsScanning = true;
        StatusMessage = "Scanning...";
        Logs.Clear();
        Logs.Add(StatusMessage);

        try
        {
            var result = await _cleanerEngine.ScanTrashAsync(_cts.Token);

            TotalTrashDisplay = FormatBytes(result.TotalBytes);
            TotalFileCount = result.FileCount.ToString();
            FreedDisplay = string.Empty;

            StatusMessage = $"Scan complete! Found {FormatBytes(result.TotalBytes)} trash ({result.FileCount} files)";
            
            Logs.Add("Details:");
            foreach (var kvp in result.CategoryBreakdown)
            {
                if (kvp.Value > 0)
                {
                    Logs.Add($"  - {kvp.Key}: {FormatBytes(kvp.Value)}");
                }
            }

            Logs.Add(result.RecycleBinItemCount == 0
                ? "  - Recycle Bin: Empty" 
                : $"  - Recycle Bin: {FormatBytes(result.RecycleBinBytes)} ({result.RecycleBinItemCount} items)");

            CleanCommand.NotifyCanExecuteChanged();
        }
        catch (OperationCanceledException)
        {
            StatusMessage = "Canceled.";
            Logs.Add("Scan canceled.");
        }
        catch (Exception ex)
        {
            StatusMessage = $"Error: {ex.Message}";
            Logs.Add($"Error: {ex.Message}");
        }
        finally
        {
            IsScanning = false;
            _cts?.Dispose();
            _cts = null;
        }
    }

    [RelayCommand(CanExecute = nameof(CanClean))]
    private async Task CleanAsync()
    {
        _cts = new CancellationTokenSource();
        IsCleaning = true;
        FreedDisplay = string.Empty;
        Logs.Clear();
        Logs.Add("Cleaning started...");

        var progress = new Progress<string>(message => 
        {
            if (Logs.Count > 1000)
                Logs.RemoveAt(0);
                
            Logs.Add(message);
        });

        try
        {
            var result = await _cleanerEngine.CleanTrashAsync(progress, _cts.Token);

            FreedDisplay = FormatBytes(result.FreedBytes);

            string recycleBinStatus = result.IsRecycleBinEmptied
                ? "Recycle bin emptied"
                : "Recycle bin could not be emptied";

            StatusMessage = $"Complete! Freed {FormatBytes(result.FreedBytes)}";
            
            Logs.Add("--- Cleaning result ---");
            Logs.Add($"  - {result.DeletedFiles} files deleted");
            Logs.Add($"  - {result.FailedFiles} files skipped (in use)");
            Logs.Add($"  - {recycleBinStatus}");
            
            TotalTrashDisplay = "0 B";
            TotalFileCount = "0";
            CleanCommand.NotifyCanExecuteChanged();
        }
        catch (OperationCanceledException)
        {
            StatusMessage = "Canceled.";
            Logs.Add("Cleaning canceled.");
        }
        catch (Exception ex)
        {
            StatusMessage = $"Error: {ex.Message}";
            Logs.Add($"Error: {ex.Message}");
        }
        finally
        {
            IsCleaning = false;
            _cts?.Dispose();
            _cts = null;
        }
    }

    [RelayCommand]
    private void Cancel()
    {
        _cts?.Cancel();
        StatusMessage = "Canceling...";
        Logs.Add("Canceling...");
    }

    private static string FormatBytes(long bytes)
    {
        if (bytes == 0) return "0 B";
        string[] sizes = { "B", "KB", "MB", "GB", "TB" };
        int order = 0;
        double len = bytes;
        while (len >= 1024 && order < sizes.Length - 1)
        {
            order++;
            len /= 1024;
        }
        return $"{len:0.##} {sizes[order]}";
    }
}
