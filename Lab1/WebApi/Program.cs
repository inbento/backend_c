// создается билдер веб приложения

using Dapper;
using FluentValidation;
using System.Text.Json;
using System.Threading.Tasks;
using WebApi.BLL.Services;
using WebApi.Config;
using WebApi.DAL;
using WebApi.DAL.Interfaces;
using WebApi.DAL.Repositories;
using WebApi.Validators;

var builder = WebApplication.CreateBuilder(args);

DefaultTypeMap.MatchNamesWithUnderscores = true;
builder.Services.AddScoped<UnitOfWork>();

builder.Services.Configure<DbSettings>(builder.Configuration.GetSection(nameof(DbSettings)));
builder.Services.Configure<RabbitMqSettings>(builder.Configuration.GetSection(nameof(RabbitMqSettings)));

builder.Services.AddScoped<IOrderRepository, OrderRepository>();
builder.Services.AddScoped<IOrderItemRepository, OrderItemRepository>();
builder.Services.AddScoped<IAuditLogOrderRepository, AuditLogOrderRepository>();
builder.Services.AddScoped<OrderService>();
builder.Services.AddScoped<AuditLogOrderService>();
builder.Services.AddScoped<RabbitMqService>();
builder.Services.AddValidatorsFromAssemblyContaining(typeof(Program));
builder.Services.AddScoped<ValidatorFactory>();

// зависимость, которая автоматически подхватывает все контроллеры в проекте
builder.Services.AddControllers().AddJsonOptions(options => 
{
    options.JsonSerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
});
// добавляем swagger
builder.Services.AddSwaggerGen();

// собираем билдер в приложение
var app = builder.Build();

// добавляем 2 миддлвари для обработки запросов в сваггер
app.UseSwagger();
app.UseSwaggerUI();

// добавляем миддлварю для роутинга в нужный контроллер
app.MapControllers();

// ждем, чтобы база данных успела запуститься
await Task.Delay(10000);

// вместо *** должен быть путь к проекту Migrations
// по сути в этот момент будет происходить накатка миграций на базу
Migrations.Program.Main([]);

// запускам приложение
app.Run();