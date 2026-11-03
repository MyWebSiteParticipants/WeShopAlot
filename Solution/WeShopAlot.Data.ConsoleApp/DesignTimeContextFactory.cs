using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.Configuration;
using WeShopAlot.Data.Extensions;

namespace WeShopAlot.Data.ConsoleApp
{
    /// <summary>
    /// Builds a <see cref="WeShopAlotContext"/> from this project's settings. Two callers:
    /// <see cref="Program"/> at run time, and the EF Core tools at design time, so this project
    /// can stay the startup project for migrations:
    ///
    ///   cd Solution
    ///   dotnet ef migrations add &lt;Name&gt; --project WeShopAlot.Data --startup-project WeShopAlot.Data.ConsoleApp
    /// </summary>
    public sealed class DesignTimeContextFactory : IDesignTimeDbContextFactory<WeShopAlotContext>
    {
        /// <summary>User secrets, then environment variables, then command-line arguments; later sources win.</summary>
        public static IConfigurationRoot BuildConfiguration(string[] args)
        {
            return new ConfigurationBuilder()
                .AddUserSecrets<DesignTimeContextFactory>(optional: true)
                .AddEnvironmentVariables()
                .AddCommandLine(args ?? Array.Empty<string>())
                .Build();
        }

        /// <summary>Chooses the provider and connection string the same way the API does (Settings:DbServerType).</summary>
        public static WeShopAlotContext CreateContext(IConfiguration configuration)
        {
            var options = new DbContextOptionsBuilder<WeShopAlotContext>();
            options.UseSelectedDatabaseServer(configuration);
            return new WeShopAlotContext(options.Options);
        }

        public WeShopAlotContext CreateDbContext(string[] args) => CreateContext(BuildConfiguration(args));
    }
}
