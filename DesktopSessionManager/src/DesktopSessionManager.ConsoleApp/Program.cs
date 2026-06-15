using DesktopSessionManager.Core.Repositories;
using DesktopSessionManager.Core.Services;
using DesktopSessionManager.Infrastructure.Capture;
using DesktopSessionManager.Infrastructure.Restore;
using DesktopSessionManager.Infrastructure.Storage;
using DesktopSessionManager.Infrastructure.System;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Serilog;
using Spectre.Console;
using System.Reflection;

// ── Bootstrap ─────────────────────────────────────────────────────────────────
var cfg = new ConfigurationBuilder()
    .SetBasePath(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)!)
    .AddJsonFile("appsettings.json", optional: false)
    .Build();

Log.Logger = new LoggerConfiguration()
    .ReadFrom.Configuration(cfg)
    .CreateLogger();

var services = new ServiceCollection();
services.AddLogging(b => b.AddSerilog(dispose: true));
services.AddSingleton<IConfiguration>(cfg);

// Storage
var storagePath  = cfg["SessionManager:StoragePath"] ?? "%LOCALAPPDATA%/DesktopSessionManager/sessions";
var maxBackups   = int.Parse(cfg["SessionManager:MaxSessionBackups"] ?? "10");
services.AddSingleton<ISessionRepository>(sp =>
    new JsonSessionRepository(storagePath, maxBackups,
        sp.GetRequiredService<ILogger<JsonSessionRepository>>()));

// Capture services (registration order = execution order)
services.AddSingleton<ICaptureService, FolderCaptureService>();
services.AddSingleton<ICaptureService, TextFileCaptureService>();
services.AddSingleton<ICaptureService, ApplicationCaptureService>();
services.AddSingleton<ICaptureService, BrowserCaptureService>();

// Restore services (registration order = execution order)
services.AddSingleton<IRestoreService, FolderRestoreService>();
services.AddSingleton<IRestoreService, TextFileRestoreService>();
services.AddSingleton<IRestoreService, ApplicationRestoreService>();
services.AddSingleton<IRestoreService, BrowserRestoreService>();

services.AddSingleton<SessionOrchestrator>();

var sp = services.BuildServiceProvider();
var orchestrator = sp.GetRequiredService<SessionOrchestrator>();
var repo         = sp.GetRequiredService<ISessionRepository>();

// ── Auto-restore mode (called by Windows startup registry entry) ───────────────
if (args.Contains("--autostart"))
{
    AnsiConsole.MarkupLine("[yellow]Auto-restore mode: waiting 10 s for desktop to settle...[/]");
    await Task.Delay(TimeSpan.FromSeconds(10));
    await orchestrator.RestoreAsync();
    return 0;
}

// ── Interactive CLI ───────────────────────────────────────────────────────────
AnsiConsole.Write(new FigletText("Session Mgr").Color(Color.Cyan1));
AnsiConsole.MarkupLine("[grey]Desktop Session Manager — v1.0[/]\n");

while (true)
{
    var choice = AnsiConsole.Prompt(
        new SelectionPrompt<string>()
            .Title("[green]What do you want to do?[/]")
            .AddChoices(
                "Save session",
                "Restore last session",
                "Restore named session",
                "List sessions",
                "Delete a session",
                "Enable auto-start on login",
                "Disable auto-start on login",
                "Exit"));

    switch (choice)
    {
        case "Save session":
            var name = AnsiConsole.Ask<string>(
                "Session name [grey](Enter for timestamp)[/]:", string.Empty);

            await AnsiConsole.Progress()
                .StartAsync(async ctx =>
                {
                    var task = ctx.AddTask("Capturing session...");
                    var state = await orchestrator.CaptureAndSaveAsync(
                        string.IsNullOrWhiteSpace(name) ? null : name);
                    task.Increment(100);

                    var table = new Table().AddColumn("Category").AddColumn("Count");
                    table.AddRow("Browser tabs",  state.BrowserWindows.Sum(w => w.Tabs.Count).ToString());
                    table.AddRow("Folders",        state.ExplorerFolders.Count.ToString());
                    table.AddRow("Text files",     state.TextFiles.Count.ToString());
                    table.AddRow("Applications",   state.Applications.Count.ToString());
                    AnsiConsole.Write(table);
                });
            break;

        case "Restore last session":
            AnsiConsole.MarkupLine("[yellow]Restoring last session...[/]");
            await orchestrator.RestoreAsync();
            AnsiConsole.MarkupLine("[green]Done.[/]");
            break;

        case "Restore named session":
            var sessions = await repo.ListAsync();
            if (!sessions.Any()) { AnsiConsole.MarkupLine("[red]No sessions found.[/]"); break; }

            var picked = AnsiConsole.Prompt(
                new SelectionPrompt<string>()
                    .Title("Select session:")
                    .AddChoices(sessions.Select(s =>
                        $"{s.SessionName} ({s.CreatedAt:g}) [{s.TotalItems} items]")));

            var idx = sessions.ToList().FindIndex(s =>
                picked.StartsWith(s.SessionName));
            if (idx >= 0)
                await orchestrator.RestoreAsync(sessions[idx].SessionId);
            break;

        case "List sessions":
            var list = await repo.ListAsync();
            if (!list.Any()) { AnsiConsole.MarkupLine("[red]No sessions found.[/]"); break; }

            var t = new Table()
                .AddColumn("Name")
                .AddColumn("Saved At")
                .AddColumn("Items");
            foreach (var s in list)
                t.AddRow(s.SessionName, s.CreatedAt.ToString("g"), s.TotalItems.ToString());
            AnsiConsole.Write(t);
            break;

        case "Delete a session":
            var listD = await repo.ListAsync();
            if (!listD.Any()) { AnsiConsole.MarkupLine("[red]No sessions.[/]"); break; }

            var toDelete = AnsiConsole.Prompt(
                new SelectionPrompt<string>()
                    .Title("[red]Select session to delete:[/]")
                    .AddChoices(listD.Select(s => $"{s.SessionName} ({s.CreatedAt:g})")));

            var idxD = listD.ToList().FindIndex(s => toDelete.StartsWith(s.SessionName));
            if (idxD >= 0)
            {
                await repo.DeleteAsync(listD[idxD].SessionId);
                AnsiConsole.MarkupLine("[green]Deleted.[/]");
            }
            break;

        case "Enable auto-start on login":
            var exePath = System.Reflection.Assembly.GetExecutingAssembly().Location
                         .Replace(".dll", ".exe");
            RegistryHelper.EnableAutoStart("DesktopSessionManager", exePath);
            AnsiConsole.MarkupLine("[green]Auto-start enabled. Session will restore 10 s after login.[/]");
            break;

        case "Disable auto-start on login":
            RegistryHelper.DisableAutoStart("DesktopSessionManager");
            AnsiConsole.MarkupLine("[yellow]Auto-start disabled.[/]");
            break;

        case "Exit":
            return 0;
    }

    AnsiConsole.WriteLine();
}
