using DesktopSessionManager.Core.Models;
using DesktopSessionManager.Core.Repositories;
using DesktopSessionManager.Core.Services;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace DesktopSessionManager.Tests.Unit;

public sealed class SessionOrchestratorTests
{
    private readonly Mock<ICaptureService>   _captureA = new();
    private readonly Mock<ICaptureService>   _captureB = new();
    private readonly Mock<IRestoreService>   _restore  = new();
    private readonly Mock<ISessionRepository> _repo    = new();

    private SessionOrchestrator CreateOrchestrator()
    {
        _captureA.Setup(s => s.Name).Returns("CaptureA");
        _captureB.Setup(s => s.Name).Returns("CaptureB");
        _restore.Setup(s => s.Name).Returns("Restore");

        return new SessionOrchestrator(
            [_captureA.Object, _captureB.Object],
            [_restore.Object],
            _repo.Object,
            NullLogger<SessionOrchestrator>.Instance);
    }

    [Fact]
    public async Task CaptureAndSave_CallsAllCaptureServices()
    {
        var sut = CreateOrchestrator();
        _repo.Setup(r => r.SaveAsync(It.IsAny<SessionState>(), default))
             .Returns(Task.CompletedTask);

        await sut.CaptureAndSaveAsync("Test");

        _captureA.Verify(s => s.CaptureAsync(It.IsAny<SessionState>(), default), Times.Once);
        _captureB.Verify(s => s.CaptureAsync(It.IsAny<SessionState>(), default), Times.Once);
        _repo.Verify(r => r.SaveAsync(It.IsAny<SessionState>(), default), Times.Once);
    }

    [Fact]
    public async Task CaptureAndSave_FailingService_DoesNotStopOthers()
    {
        var sut = CreateOrchestrator();
        _captureA.Setup(s => s.CaptureAsync(It.IsAny<SessionState>(), default))
                 .ThrowsAsync(new Exception("A failed"));
        _repo.Setup(r => r.SaveAsync(It.IsAny<SessionState>(), default))
             .Returns(Task.CompletedTask);

        await sut.Invoking(o => o.CaptureAndSaveAsync()).Should().NotThrowAsync();

        _captureB.Verify(s => s.CaptureAsync(It.IsAny<SessionState>(), default), Times.Once);
    }

    [Fact]
    public async Task Restore_WhenNoSession_DoesNotThrow()
    {
        var sut = CreateOrchestrator();
        _repo.Setup(r => r.LoadLatestAsync(default)).ReturnsAsync((SessionState?)null);

        await sut.Invoking(o => o.RestoreAsync()).Should().NotThrowAsync();
        _restore.Verify(r => r.RestoreAsync(It.IsAny<SessionState>(), default), Times.Never);
    }
}
