using FluentMigrator.Runner;
using FluentMigrator.Runner.VersionTableInfo;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
namespace Migrations;
public static class Program
{
    public static void Main(string[] args)
    {
        if (args.Contains("--dryrun"))
        {
            return;
        }

        // Получаем переменную среды, отвечающую за окружение
        var environmentName = Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT") ?? "Development";

        // собираем конфигурацию на основании окружения
        var config = new ConfigurationBuilder()
            .SetBasePath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "Migrations"))
            .AddJsonFile($"appsettings.{environmentName}.json")
            .Build();

        // Получаем строку подключения из конфига `appsettings.{Environment}.json`
        var connectionString = config["DbSettings:MigrationConnectionString"];
        var migrationRunner = new MigratorRunner(connectionString);
        
        // Мигрируемся
        migrationRunner.Migrate();
    }
}