using Dapper;
using FluentValidation;
using Lab1.BBL.Services;
using Lab1.Clients;
using Lab1.Config;
using Lab1.DAL.Interfaces;
using Lab1.DAL.Repositories;
using Lab1.Jobs;
using Lab1.Services;
using Lab1.Validators;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using HealthChecks.NpgSql;
using Npgsql;
using Lab1.DAL.Models;

var builder = WebApplication.CreateBuilder(args);

DefaultTypeMap.MatchNamesWithUnderscores = true;

// Composite type mappings are registered per-connection in `UnitOfWork` after migrations
// have run and the types exist in the database. Global mappings here can cause
// resolution to use a type name that isn't present yet (leading to '_v1_order_dal' errors).
// NpgsqlConnection.GlobalTypeMapper.MapComposite<Lab1.DAL.Models.V1OrderDal>("v1_order_dal");
// NpgsqlConnection.GlobalTypeMapper.MapComposite<Lab1.DAL.Models.V1OrderItemDal>("v1_order_item_dal");
// NpgsqlConnection.GlobalTypeMapper.MapComposite<Lab1.DAL.Models.V1AuditLogOrderDal>("v1_audit_log_order_dal");
builder.Services.AddScoped<UnitOfWork>();

builder.Services.Configure<DbSettings>(builder.Configuration.GetSection(nameof(DbSettings)));

builder.Services.AddScoped<IOrderRepository, OrderRepository>();
builder.Services.AddScoped<IOrderItemRepository, OrderItemRepository>();
builder.Services.AddScoped<OrderService>();

builder.Services.AddValidatorsFromAssemblyContaining(typeof(Program));
builder.Services.AddScoped<IValidatorFactory, ValidatorFactory>();

builder.Services.AddScoped<IAuditLogOrderRepository, AuditLogOrderRepository>();
builder.Services.AddScoped<AuditLogOrderService>();
builder.Services.AddScoped<V1AuditLogOrderRequestValidator>();

builder.Services.AddControllers().AddJsonOptions(options =>
{
    options.JsonSerializerOptions.PropertyNamingPolicy = null;
});
//  swagger

builder.Services.Configure<RabbitMqSettings>(builder.Configuration.GetSection(nameof(RabbitMqSettings)));

builder.Services.Configure<RabbitMqSettings>(
    builder.Configuration.GetSection("RabbitMqSettings"));

builder.Services.AddScoped<RabbitMqService>();



builder.Services.AddSwaggerGen();
builder.Services.AddHealthChecks()
    .AddNpgSql(builder.Configuration.GetSection("DbSettings")["ConnectionString"],
        name: "PostgreSQL Health Check",
        tags: new[] { "db", "postgresql" },
        healthQuery: "SELECT COUNT(*) FROM audit_log_order;");
builder.Services.AddHostedService<OrderGenerator>();

builder.Services.Configure<HostOptions>(options =>
{
    options.BackgroundServiceExceptionBehavior = BackgroundServiceExceptionBehavior.Ignore;
});

var app = builder.Build();

app.UseSwagger();
app.UseSwaggerUI();

app.MapControllers();
app.MapHealthChecks("/health");

//  ***     Migrations
//          
// Migrations.Program.Main([]);

//  
app.Run();