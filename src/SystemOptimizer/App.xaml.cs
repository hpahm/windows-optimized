using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.Windows;
using SystemOptimizer.Core;
using SystemOptimizer.Core.Interfaces;
using SystemOptimizer.ViewModels;

namespace SystemOptimizer;

public partial class App : Application
{
    public static IServiceProvider Services { get; private set; } = null!;

    public App()
    {
        var services = new ServiceCollection();
        ConfigureServices(services);
        Services = services.BuildServiceProvider();
    }

    private static void ConfigureServices(IServiceCollection services)
    {
        services.AddLogging(builder =>
        {
            builder.SetMinimumLevel(LogLevel.Debug);
            builder.AddDebug();
        });

        services.AddSingleton<ICleanerEngine, CleanerEngine>();
        services.AddTransient<CleanerViewModel>();
    }
}
