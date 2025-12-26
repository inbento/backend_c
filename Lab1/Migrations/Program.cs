using Microsoft.Extensions.Configuration;

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
        var environmentName = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") ??
                              throw new InvalidOperationException("ASPNETCORE_ENVIRONMENT in not set");

        // Сначала пытаемся получить строку подключения из переменной окружения
        var connectionString = Environment.GetEnvironmentVariable("MIGRATION_CONNECTION");

        // Если переменная окружения не установлена, читаем из конфига
        if (string.IsNullOrEmpty(connectionString))
        {
            // собираем конфигурацию на основании окружения
            // у нас будет два варианта - Development/Production
            var config = new ConfigurationBuilder()
                .SetBasePath(Directory.GetCurrentDirectory())
                .AddJsonFile($"appsettings.{environmentName}.json")
                .Build();

            // Получаем строку подключения из конфига `appsettings.{Environment}.json`
            connectionString = config["DbSettings:MigrationConnectionString"];
        }

        if (string.IsNullOrEmpty(connectionString))
        {
            throw new InvalidOperationException("Connection string not found. Set MIGRATION_CONNECTION environment variable or configure DbSettings:MigrationConnectionString in appsettings");
        }

        Console.WriteLine($"Using connection string: {connectionString}");
        var migrationRunner = new MigratorRunner(connectionString);

        // Мигрируемся
        migrationRunner.Migrate();
    }
}