using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Screen2VMS.App.ViewModels;
using Screen2VMS.Camera;
using Screen2VMS.Configuration;
using Screen2VMS.Core.Cameras;
using Screen2VMS.Core.Configuration;
using Screen2VMS.Engine;
using Screen2VMS.Logging;

namespace Screen2VMS.App;

public partial class App : Application
{
    /// <summary>
    /// Global name so the mutex is shared across sessions, not just this one
    /// (spec 55). The camera can only be held by one process anyway.
    /// </summary>
    private const string SingleInstanceMutexName = @"Global\Screen2VMS.SingleInstance";

    private Mutex? singleInstanceMutex;
    private ServiceProvider? services;
    private ILoggerFactory? loggerFactory;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        singleInstanceMutex = new Mutex(initiallyOwned: true, SingleInstanceMutexName, out var isFirstInstance);
        if (!isFirstInstance)
        {
            MessageBox.Show(
                "Screen2VMS is already running.",
                "Screen2VMS",
                MessageBoxButton.OK,
                MessageBoxImage.Information);

            singleInstanceMutex.Dispose();
            singleInstanceMutex = null;
            Shutdown();
            return;
        }

        AppPaths.EnsureCreated();

        // Configuration is read before logging is configured, because the log
        // level lives in it.
        var bootstrapConfiguration = new JsonConfigurationService();
        var loaded = bootstrapConfiguration.Load();

        loggerFactory = LogSetup.Create(loaded.Logging.Level);
        var logger = loggerFactory.CreateLogger<App>();
        logger.LogInformation("ApplicationStarted: Screen2VMS {Version}", GetType().Assembly.GetName().Version);

        DispatcherUnhandledException += (_, args) =>
        {
            logger.LogError(args.Exception, "Unhandled exception on the UI thread.");
            MessageBox.Show(args.Exception.Message, "Screen2VMS", MessageBoxButton.OK, MessageBoxImage.Error);
            args.Handled = true;
        };

        services = BuildServices(bootstrapConfiguration, loggerFactory);

        var window = new MainWindow
        {
            DataContext = services.GetRequiredService<MainViewModel>(),
        };

        MainWindow = window;
        window.Show();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        (services?.GetService<MainViewModel>())?.Dispose();
        services?.Dispose();

        LogSetup.Shutdown();
        loggerFactory?.Dispose();

        singleInstanceMutex?.ReleaseMutex();
        singleInstanceMutex?.Dispose();

        base.OnExit(e);
    }

    private ServiceProvider BuildServices(IConfigurationService configuration, ILoggerFactory factory)
    {
        var collection = new ServiceCollection();

        collection.AddSingleton(factory);
        collection.AddLogging();
        collection.AddSingleton(configuration);
        collection.AddSingleton<ICameraSourceService, MediaFoundationCameraSourceService>();
        collection.AddSingleton<Screen2VmsRuntime>();
        collection.AddSingleton(Dispatcher);
        collection.AddSingleton<MainViewModel>();

        return collection.BuildServiceProvider();
    }
}
