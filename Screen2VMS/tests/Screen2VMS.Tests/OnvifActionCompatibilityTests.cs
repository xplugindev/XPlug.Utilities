using Screen2VMS.Onvif;
using SharpOnvifCommon;
using SharpOnvifServer.Media;

namespace Screen2VMS.Tests;

/// <summary>
/// Guards the workaround for malformed SOAP actions in the generated ONVIF
/// media contract.
/// </summary>
/// <remarks>
/// Several operations are declared with the separator in the wrong place, so
/// the dispatcher never matches the action a real VMS sends. These tests assert
/// the invariant that matters - every operation is reachable by its correct
/// action - which holds whether the library is broken or has since been fixed.
/// </remarks>
public class OnvifActionCompatibilityTests
{
    private static IReadOnlyDictionary<string, string> Map =>
        OnvifActionCompatibility.BuildCorrectionMap<Media>(OnvifServices.MEDIA);

    [Theory]
    [InlineData("GetProfiles")]
    [InlineData("GetProfile")]
    [InlineData("GetStreamUri")]
    [InlineData("GetSnapshotUri")]
    [InlineData("GetVideoSources")]
    [InlineData("GetVideoSourceConfigurations")]
    [InlineData("GetVideoEncoderConfigurations")]
    [InlineData("GetVideoEncoderConfigurationOptions")]
    [InlineData("GetVideoSourceConfigurationOptions")]
    [InlineData("SetVideoEncoderConfiguration")]
    public void EveryOperationAVmsCalls_IsReachableByItsCorrectAction(string operation)
    {
        var correctAction = $"{OnvifServices.MEDIA}/{operation}";
        var declared = typeof(Media).GetMethod(operation)!
            .GetCustomAttributes(typeof(CoreWCF.OperationContractAttribute), inherit: false)
            .Cast<CoreWCF.OperationContractAttribute>()
            .Single()
            .Action;

        // Either the contract already declares the right action, or the
        // correction map redirects it. Anything else is unreachable.
        var reachable = declared == correctAction
            || (Map.TryGetValue(correctAction, out var mapped) && mapped == declared);

        Assert.True(
            reachable,
            $"{operation} is declared as '{declared}' and nothing maps '{correctAction}' onto it.");
    }

    [Fact]
    public void CorrectionMap_OnlyContainsGenuinelyMismatchedOperations()
    {
        foreach (var (correct, declared) in Map)
        {
            Assert.NotEqual(correct, declared);
            Assert.StartsWith(OnvifServices.MEDIA, correct, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void CorrectionMap_KeysAreTheActionsAClientWouldSend()
    {
        foreach (var correct in Map.Keys)
        {
            var operation = correct[(OnvifServices.MEDIA.Length + 1)..];

            Assert.NotNull(typeof(Media).GetMethod(operation));
            Assert.Equal($"{OnvifServices.MEDIA}/{operation}", correct);
        }
    }
}
