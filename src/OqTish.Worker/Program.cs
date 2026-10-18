using OqTish.Infrastructure;
using OqTish.Worker;

var builder = Host.CreateApplicationBuilder(args);

builder.Services.AddInfrastructure(builder.Configuration, builder.Environment);

// Must stay first: hosted services start in registration order.
builder.Services.AddHostedService<DatabaseReadinessGate>();
builder.Services.AddHostedService<ExpirySweeperService>();

var host = builder.Build();
await host.RunAsync();
