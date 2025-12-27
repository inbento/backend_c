using Consumer.Config;
using Consumer.Consumers;
using Lab1.Clients;
using System.Text.Json;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddLogging();
builder.Services.AddSingleton<ILoggerFactory, LoggerFactory>();
builder.Services.Configure<RabbitMqSettings>(builder.Configuration.GetSection(nameof(RabbitMqSettings)));
builder.Services.AddHostedService<BatchOmsOrderCreatedConsumer>();
builder.Services.AddHostedService<BatchOmsOrderStatusChangedConsumer>();
builder.Services.AddHttpClient<OmsClient>((serviceProvider, client) =>
{
    client.BaseAddress = new Uri(builder.Configuration["HttpClient:Oms:BaseAddress"]);
}).AddTypedClient<OmsClient>();

builder.Services.Configure<HostOptions>(options =>
{
    options.ServicesStartConcurrently = true;
    options.ServicesStopConcurrently = true;
});

var app = builder.Build();
await app.RunAsync();