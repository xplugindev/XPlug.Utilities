using DesktopSessionManager.Core.Models;
using DesktopSessionManager.Infrastructure.Storage;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace DesktopSessionManager.Tests.Unit;

public sealed class JsonSessionRepositoryTests : IDisposable
{
    private readonly string _tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
    private readonly JsonSessionRepository _repo;

    public JsonSessionRepositoryTests()
    {
        _repo = new JsonSessionRepository(_tempDir, maxBackups: 5,
            NullLogger<JsonSessionRepository>.Instance);
    }

    [Fact]
    public async Task SaveAndLoad_RoundTrip_Succeeds()
    {
        var state = new SessionState { SessionName = "Test" };
        state.ExplorerFolders.Add(new ExplorerFolder { FolderPath = @"C:\TestFolder" });

        await _repo.SaveAsync(state);
        var loaded = await _repo.LoadAsync(state.SessionId);

        loaded.Should().NotBeNull();
        loaded!.SessionId.Should().Be(state.SessionId);
        loaded.ExplorerFolders.Should().HaveCount(1);
        loaded.ExplorerFolders[0].FolderPath.Should().Be(@"C:\TestFolder");
    }

    [Fact]
    public async Task LoadLatest_ReturnsLastSaved()
    {
        var first  = new SessionState { SessionName = "First" };
        var second = new SessionState { SessionName = "Second" };

        await _repo.SaveAsync(first);
        await Task.Delay(10); // ensure ordering
        await _repo.SaveAsync(second);

        var latest = await _repo.LoadLatestAsync();
        latest.Should().NotBeNull();
        latest!.SessionName.Should().Be("Second");
    }

    [Fact]
    public async Task List_ReturnsCorrectSummaries()
    {
        var s1 = new SessionState { SessionName = "A" };
        var s2 = new SessionState { SessionName = "B" };
        s1.TextFiles.Add(new TextFileInstance());
        s2.ExplorerFolders.Add(new ExplorerFolder());

        await _repo.SaveAsync(s1);
        await _repo.SaveAsync(s2);

        var list = await _repo.ListAsync();
        list.Should().HaveCount(2);
    }

    [Fact]
    public async Task Delete_RemovesSession()
    {
        var state = new SessionState { SessionName = "ToDelete" };
        await _repo.SaveAsync(state);

        await _repo.DeleteAsync(state.SessionId);
        var loaded = await _repo.LoadAsync(state.SessionId);

        loaded.Should().BeNull();
    }

    [Fact]
    public async Task Prune_KeepsMaxBackups()
    {
        for (int i = 0; i < 8; i++)
        {
            await _repo.SaveAsync(new SessionState { SessionName = $"Session {i}" });
            await Task.Delay(5);
        }

        var list = await _repo.ListAsync();
        list.Should().HaveCountLessOrEqualTo(5);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }
}
