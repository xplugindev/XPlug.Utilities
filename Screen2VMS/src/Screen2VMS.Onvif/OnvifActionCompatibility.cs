using System.Reflection;
using CoreWCF;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace Screen2VMS.Onvif;

/// <summary>
/// Repairs SOAP actions that the generated ONVIF contracts declare incorrectly.
/// </summary>
/// <remarks>
/// <para>
/// A handful of operations in SharpOnvifServer.Media carry a malformed action:
/// the separator lands in the wrong place, so <c>GetVideoSources</c> is
/// declared as <c>.../media/wsdlGetVideoSources/</c> instead of
/// <c>.../media/wsdl/GetVideoSources</c>. A VMS sends the correct action, the
/// CoreWCF dispatcher finds no operation matching it, and the request comes
/// back as an ActionNotSupported fault.
/// </para>
/// <para>
/// The affected operations include <c>GetVideoSources</c>, <c>GetProfile</c>
/// and <c>GetVideoSourceConfigurationOptions</c>, all of which Genetec and
/// XProtect call while adding a unit, so leaving them broken is not an option.
/// </para>
/// <para>
/// The mapping is derived by reflecting over the contract rather than being
/// hard-coded, so the middleware quietly does nothing once the library is
/// fixed, instead of silently rewriting actions that have become correct.
/// </para>
/// </remarks>
internal static class OnvifActionCompatibility
{
    /// <summary>
    /// Builds a map from the action a client will send to the one the
    /// dispatcher actually expects.
    /// </summary>
    /// <typeparam name="TContract">The generated ONVIF service contract.</typeparam>
    /// <param name="serviceNamespace">Namespace the contract's actions should be built from.</param>
    internal static IReadOnlyDictionary<string, string> BuildCorrectionMap<TContract>(string serviceNamespace)
    {
        var corrections = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var method in typeof(TContract).GetMethods())
        {
            var attribute = method.GetCustomAttribute<OperationContractAttribute>();
            var declared = attribute?.Action;

            if (string.IsNullOrEmpty(declared))
            {
                continue;
            }

            var expected = $"{serviceNamespace}/{method.Name}";
            if (!string.Equals(declared, expected, StringComparison.Ordinal))
            {
                corrections[expected] = declared;
            }
        }

        return corrections;
    }

    /// <summary>
    /// Rewrites the action on the way in, for the operations that need it.
    /// </summary>
    /// <remarks>
    /// Must sit after the ONVIF middleware, which derives the action from the
    /// message body, and before the CoreWCF dispatcher that matches on it.
    /// </remarks>
    internal static void UseOnvifActionCorrections(
        this WebApplication app,
        IReadOnlyDictionary<string, string> corrections,
        ILogger logger)
    {
        if (corrections.Count == 0)
        {
            return;
        }

        logger.LogInformation(
            "Correcting {Count} malformed ONVIF SOAP action(s) declared by the contract: {Operations}",
            corrections.Count,
            string.Join(", ", corrections.Keys.Select(k => k[(k.LastIndexOf('/') + 1)..])));

        app.Use(async (context, next) =>
        {
            var contentType = context.Request.ContentType;

            if (!string.IsNullOrEmpty(contentType))
            {
                var action = ExtractAction(contentType);

                if (action is not null && corrections.TryGetValue(action, out var declared))
                {
                    context.Request.ContentType = contentType.Replace(
                        $"\"{action}\"",
                        $"\"{declared}\"",
                        StringComparison.Ordinal);
                }
            }

            await next(context);
        });
    }

    /// <summary>Reads the <c>action</c> parameter out of a SOAP 1.2 content type.</summary>
    private static string? ExtractAction(string contentType)
    {
        const string marker = "action=";

        var index = contentType.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
        if (index < 0)
        {
            return null;
        }

        var value = contentType[(index + marker.Length)..].Trim();

        if (value.StartsWith('"'))
        {
            var end = value.IndexOf('"', 1);
            return end > 0 ? value[1..end] : null;
        }

        var separator = value.IndexOf(';');
        return separator > 0 ? value[..separator].Trim() : value;
    }
}
