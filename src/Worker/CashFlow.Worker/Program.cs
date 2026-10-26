using CashFlow.Worker;

HostApplicationBuilder builder = Host.CreateApplicationBuilder(args);

builder.Services.AddWorker(builder.Configuration);

await builder.Build().RunAsync();
