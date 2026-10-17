using OqTish.Infrastructure;
using OqTish.Worker;

var builder = Host.CreateApplicationBuilder(args);

builder.Services.AddInfrastructure(builder.Configuration);

// Must stay first: hosted services start in registration order.
builder.Services.AddHostedService<DatabaseReadinessGate>();

var host = builder.Build();
await host.RunAsync();
