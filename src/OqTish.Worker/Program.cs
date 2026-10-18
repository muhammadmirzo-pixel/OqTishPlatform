using OqTish.Infrastructure;
using OqTish.Worker;

var builder = Host.CreateApplicationBuilder(args);

builder.Services.AddInfrastructure(builder.Configuration, builder.Environment);

// Must stay first: hosted services start in registration order.
builder.Services.AddHostedService<DatabaseReadinessGate>();
builder.Services.AddHostedService<ExpirySweeperService>();
builder.Services.AddHostedService<FinanceSummaryService>();
builder.Services.AddHostedService<OutboxDispatcherService>();
builder.Services.AddHostedService<TelegramPollingService>();
builder.Services.AddHostedService<RetentionService>();

var host = builder.Build();
await host.RunAsync();
