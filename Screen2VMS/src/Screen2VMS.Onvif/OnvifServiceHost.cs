using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Screen2VMS.Core.Networking;
using Screen2VMS.Core.Onvif;
using Screen2VMS.Discovery;
using SharpOnvifServer;
using SharpOnvifServer.Discovery;
using SharpOnvifServer.Security;
using SharpOnvifServer.DeviceMgmt;
using SharpOnvifServer.Media;
using CoreWCF.Configuration;

namespace Screen2VMS.Onvif;

/// <summary>
/// Hosts the ONVIF SOAP services and WS-Discovery (spec 16, 17, 18).
/// </summary>
/// <remarks>
/// <para>
/// Kestrel rather than HttpListener: binding a non-localhost HttpListener
/// prefix needs a URL ACL or elevation, and Screen2VMS is required to run
/// unelevated (spec 5).
/// </para>
/// <para>
/// Both authentication schemes are enabled. ONVIF clients lead with
/// WS-Security UsernameToken and fall back to HTTP Digest, and which one a VMS
/// uses varies by product and version, so supporting only one guarantees
/// failure against half the field (spec 24).
/// </para>
/// </remarks>
public sealed class OnvifServiceHost : IOnvifServiceHost
{
    /// <summary>How long to wait for Kestrel and the discovery service to bind.</summary>
    private static readonly TimeSpan StartTimeout = TimeSpan.FromSeconds(30);

    /// <summary>Backstop for shutdown, so a wedged host cannot hang the caller.</summary>
    private static readonly TimeSpan StopTimeout = TimeSpan.FromSeconds(15);

    /// <summary>Grace period given to in-flight requests before the host is torn down.</summary>
    private static readonly TimeSpan ShutdownGrace = TimeSpan.FromSeconds(5);

    private readonly IOnvifDeviceContext context;
    private readonly OnvifHostOptions options;
    private readonly ILoggerFactory loggerFactory;
    private readonly ILogger logger;

    private WebApplication? application;
    private bool disposed;

    public OnvifServiceHost(
        IOnvifDeviceContext context,
        OnvifHostOptions options,
        ILoggerFactory loggerFactory)
    {
        this.context = context;
        this.options = options;
        this.loggerFactory = loggerFactory;
        logger = loggerFactory.CreateLogger<OnvifServiceHost>();
    }

    public bool IsRunning { get; private set; }

    public int Port => options.Port;

    public string? DeviceServiceUri { get; private set; }

    public void Start(int port)
    {
        ObjectDisposedException.ThrowIf(disposed, this);

        if (IsRunning)
        {
            return;
        }

        if (string.IsNullOrEmpty(options.Password))
        {
            throw new InvalidOperationException(
                "An ONVIF password must be configured before the ONVIF service can start.");
        }

        options.Port = port;

        try
        {
            application = BuildApplication(port);
            RunDetached(() => application.StartAsync(), "start", StartTimeout);

            IsRunning = true;
            DeviceServiceUri = $"http://{options.FallbackAddress}:{port}{OnvifHostOptions.DeviceServicePath}";

            logger.LogInformation("OnvifStarted: {Uri}", DeviceServiceUri);
        }
        catch (Exception ex)
        {
            Cleanup();
            throw new OnvifHostException(
                $"Could not start the ONVIF service on port {port}. " +
                "The port may already be in use or blocked by the firewall.",
                ex);
        }
    }

    public void Stop()
    {
        if (!IsRunning)
        {
            return;
        }

        Cleanup();
        IsRunning = false;
        DeviceServiceUri = null;
        logger.LogInformation("OnvifStopped");
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        Stop();
    }

    private WebApplication BuildApplication(int port)
    {
        // The full builder rather than the slim one: CoreWCF needs services the
        // slim host trims away. The content root is pinned to the install
        // directory because the process is started from wherever the user
        // happened to be, and ASP.NET would otherwise root itself there.
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            ContentRootPath = AppContext.BaseDirectory,
            ApplicationName = "Screen2VMS",
        });

        builder.Logging.ClearProviders();
        builder.Services.AddSingleton(loggerFactory);

        builder.WebHost.ConfigureKestrel(kestrel =>
        {
            // Bound on every interface: a VMS may reach us on any of them, and
            // discovery advertises whichever one routes to the caller.
            kestrel.ListenAnyIP(port);
        });

        builder.Services.AddSingleton(context);
        builder.Services.AddSingleton(options);
        builder.Services.AddHttpContextAccessor();

        builder.Services.AddSingleton<IUserRepository>(
            new OnvifUserRepository(options.UserName, options.Password));

        builder.Services.AddOnvifDigestAuthentication(digest =>
        {
            // Accept either scheme; see the note on this class.
            digest.Authentication = DigestAuthentication.HttpDigest | DigestAuthentication.WsUsernameToken;
            digest.HttpDigestRealm = "Screen2VMS";

            // Clock skew allowance for the WS-Security timestamp. ONVIF's
            // default is five minutes and VMS servers are not always in step
            // with the camera.
            digest.WsUsernameTokenMaxTimeDeltaInMilliseconds = TimeSpan.FromMinutes(5).TotalMilliseconds;
        });

        builder.Services.AddOnvifDiscovery(BuildDiscoveryOptions());

        builder.Services.AddSingleton<Screen2VmsDeviceService>();
        builder.Services.AddSingleton<Screen2VmsMediaService>();

        builder.Services.AddServiceModelServices();

        var app = builder.Build();

        // UseOnvif routes by the SOAP action inside the envelope rather than by
        // Content-Type, which is what ONVIF clients actually send.
        app.UseOnvif();

        // Sits between the two: the generated media contract declares a few
        // actions incorrectly, and a VMS sends the correct ones.
        app.UseOnvifActionCorrections(
            OnvifActionCompatibility.BuildCorrectionMap<Media>(SharpOnvifCommon.OnvifServices.MEDIA),
            logger);

        app.UseServiceModel(services =>
        {
            services.AddService<Screen2VmsDeviceService>();
            services.AddServiceEndpoint<Screen2VmsDeviceService, Device>(
                OnvifBindingFactory.CreateBinding(),
                OnvifHostOptions.DeviceServicePath);

            services.AddService<Screen2VmsMediaService>();
            services.AddServiceEndpoint<Screen2VmsMediaService, Media>(
                OnvifBindingFactory.CreateBinding(),
                OnvifHostOptions.MediaServicePath);
        });

        MapSnapshotEndpoint(app);

        return app;
    }

    private OnvifDiscoveryOptions BuildDiscoveryOptions()
    {
        var device = context.DeviceInfo;
        var interfaces = SelectDiscoveryInterfaces();

        return new OnvifDiscoveryOptions
        {
            Name = device.Name,
            Manufacturer = device.Manufacturer,
            Hardware = device.Model,
            MAC = device.MacAddress,
            NetworkInterfaces = interfaces,
            ServiceAddresses = interfaces
                .Select(address => $"http://{address}:{options.Port}{OnvifHostOptions.DeviceServicePath}")
                .ToList(),
            Scopes = OnvifDiscoveryScopes.Build(device, options.Location).ToList(),
            Types =
            [
                new OnvifType(
                    OnvifDiscoveryScopes.NetworkVideoTransmitterNamespace,
                    OnvifDiscoveryScopes.NetworkVideoTransmitterName),
            ],
        };
    }

    /// <summary>
    /// The addresses worth announcing in discovery.
    /// </summary>
    /// <remarks>
    /// Announcing every interface means a probe response carrying loopback and
    /// the virtual adapters WSL and Hyper-V install. A VMS walks that list in
    /// order and can settle on an address it cannot reach, which looks like a
    /// camera that answers discovery and then fails to add. Only real LAN
    /// addresses are advertised (spec 26).
    /// </remarks>
    private List<string> SelectDiscoveryInterfaces()
    {
        var addresses = NetworkAddressResolver.EnumerateInterfaces()
            .Where(option => !option.IsLikelyVirtual)
            .Select(option => option.Address.ToString())
            .Distinct()
            .ToList();

        if (addresses.Count > 0)
        {
            return addresses;
        }

        // Every adapter looked virtual. Better to announce something than to
        // become undiscoverable.
        logger.LogWarning(
            "No physical network interface was found; announcing all addresses in discovery.");

        return NetworkAddressResolver.EnumerateInterfaces()
            .Select(option => option.Address.ToString())
            .Distinct()
            .ToList();
    }

    /// <summary>
    /// Serves the JPEG snapshot a VMS uses for thumbnails.
    /// </summary>
    /// <remarks>
    /// Spec 3 leaves snapshots out of the MVP, but both target platforms fetch
    /// one when a unit is added and show a broken camera without it, so it is
    /// treated as part of getting ONVIF accepted rather than as a feature.
    /// </remarks>
    private void MapSnapshotEndpoint(WebApplication app)
    {
        app.MapGet(OnvifHostOptions.SnapshotPath, (HttpContext http) =>
        {
            var jpeg = context.CaptureSnapshotJpeg();

            if (jpeg is null || jpeg.Length == 0)
            {
                logger.LogDebug("Snapshot requested by {Client} while not streaming.",
                    http.Connection.RemoteIpAddress);

                return Results.StatusCode(StatusCodes.Status503ServiceUnavailable);
            }

            return Results.File(jpeg, "image/jpeg");
        });
    }

    private void Cleanup()
    {
        if (application is null)
        {
            return;
        }

        var host = application;
        application = null;

        try
        {
            RunDetached(() => host.StopAsync(ShutdownGrace), "stop", StopTimeout);
            RunDetached(() => host.DisposeAsync().AsTask(), "dispose", StopTimeout);
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "Ignoring an error while shutting the ONVIF host down.");
        }
    }

    /// <summary>
    /// Runs an asynchronous host operation to completion from a synchronous
    /// caller, without letting it capture the caller's synchronisation context.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Start and Stop are synchronous because the user interface calls them
    /// from a button click, and that click runs on the WPF dispatcher thread.
    /// Awaiting the host directly there and blocking on the result deadlocks:
    /// the host's continuations are posted back to the dispatcher, which is
    /// the very thread sitting blocked waiting for them. The application stops
    /// answering and looks like a crash.
    /// </para>
    /// <para>
    /// Handing the work to the thread pool detaches it from the dispatcher, so
    /// the continuations have somewhere to run. The timeout is a backstop: a
    /// host that will not shut down must not strand the caller forever.
    /// </para>
    /// </remarks>
    private void RunDetached(Func<Task> operation, string description, TimeSpan timeout)
    {
        var task = Task.Run(operation);

        if (!task.Wait(timeout))
        {
            logger.LogWarning("The ONVIF host did not {Description} within {Timeout}.", description, timeout);
            return;
        }

        // Surfaces a genuine failure; Wait already returned so this cannot block.
        task.GetAwaiter().GetResult();
    }
}

/// <summary>The ONVIF host could not be started.</summary>
public sealed class OnvifHostException : Exception
{
    public OnvifHostException(string message)
        : base(message)
    {
    }

    public OnvifHostException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
