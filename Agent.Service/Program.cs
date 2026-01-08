using Agent.Service;
using Agent.Service.Identity;
using Agent.Service.Infrastructure;
using Agent.Service.Infrastructure.Outbox;
using Agent.Service.Tracking;
using Agent.Service.Workers;
using Agent.Mac.Collectors;
using Agent.Service.Collectors;
using Agent.Shared.Abstractions;
using Agent.Shared.Config;
using Agent.Windows.Collectors;
using Microsoft.Extensions.Hosting.WindowsServices;
using System.Runtime.InteropServices;

var builder = Host.CreateApplicationBuilder(args);
builder.Services.Configure<AgentConfig>(builder.Configuration.GetSection("Agent"));
var agentConfig = builder.Configuration.GetSection("Agent").Get<AgentConfig>() ?? new AgentConfig();

if (OperatingSystem.IsWindows())
{
    builder.Services.AddWindowsService(options =>
    {
        options.ServiceName = "EmployeeTrackerAgent";
    });
}

// Collectors
if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
{
    builder.Services.AddSingleton<IIdleCollector, MacIdleCollector>();
    builder.Services.AddSingleton<IAppCollector, MacAppCollector>();
}
else if (OperatingSystem.IsWindows())
{
    builder.Services.AddSingleton<IIdleCollector, WindowsIdleCollector>();
    builder.Services.AddSingleton<IAppCollector, WindowsAppCollector>();
}
else
{
    builder.Services.AddSingleton<IIdleCollector, NoopIdleCollector>();
    builder.Services.AddSingleton<IAppCollector, NoopAppCollector>();
}

// Identity
builder.Services.AddSingleton<DeviceIdentityStore>();

// Persistence (Outbox)
builder.Services.AddSingleton<OutboxRepository>();
builder.Services.AddSingleton<IOutboxService, OutboxService>();
builder.Services.AddSingleton<OutboxSenderState>();

// Sessionization
builder.Services.AddSingleton<WebSessionizer>();
builder.Services.AddSingleton<AppSessionizer>();
builder.Services.AddSingleton<IdleSessionizer>();

// Infrastructure
builder.Services.AddHttpClient("backend");

// Workers
builder.Services.AddHostedService<ShutdownFlushService>();
if (agentConfig.EnableLocalCollectors)
{
    builder.Services.AddHostedService<CollectorWorker>();
}
builder.Services.AddHostedService<OutboxSenderWorker>();
builder.Services.AddHostedService<Worker>(); // Main worker (Web Events + Local API)
builder.Services.AddHostedService<HeartbeatWorker>();

var host = builder.Build();

// Init DB
var outboxRepo = host.Services.GetRequiredService<OutboxRepository>();
outboxRepo.Init();

host.Run();
