using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using WeShopAlot.Data;
using WeShopAlot.Data.Interfaces;
using WeShopAlot.Data.Repositories.Interfaces;
using WeShopAlot.Data.Seed;

namespace WeShopAlot.Testing.WebApis
{
    /// <summary>
    /// Runs the whole WeShopAlot API against a real SQL Server database that is built for this
    /// test run and dropped afterwards. The database is created the same way a developer's is:
    /// <see cref="DatabaseSeeder.RecreateAsync"/> applies the migrations and
    /// <see cref="DatabaseSeeder.SeedAsync"/> loads the shipped catalog, so a test can assert
    /// on the real data (16 products, 10 brands, 7 types, 4 delivery methods, 3 order statuses)
    /// and on what only a real database enforces: foreign keys, column sizes, SQL ordering.
    ///
    /// Where the database lives:
    /// <list type="bullet">
    /// <item>By default, SQL Server LocalDB, which the solution already requires on Windows.</item>
    /// <item>Set the <c>WESHOPALOT_TEST_SQLSERVER</c> environment variable to a connection string
    /// <em>without</em> a database name to use another server (Express, a container, CI). The
    /// login must be allowed to create and drop databases.</item>
    /// </list>
    /// Each factory instance gets its own database name, so test classes and parallel runs
    /// never share state. Redis is replaced by the same in-memory stand-ins as the fast tier.
    ///
    /// When no SQL Server answers, <see cref="TryRecreateAndSeedAsync"/> returns false and
    /// <see cref="UnavailableReason"/> says why; tests then report Inconclusive instead of failing.
    /// </summary>
    public class WeShopAlotSqlApiFactory : WebApplicationFactory<Program>
    {
        public const string ServerVariable = "WESHOPALOT_TEST_SQLSERVER";
        public const string DefaultServer = @"Server=(localdb)\MSSQLLocalDB;Trusted_Connection=true;TrustServerCertificate=true";

        private bool databaseCreated;

        public WeShopAlotSqlApiFactory()
        {
            var server = Environment.GetEnvironmentVariable(ServerVariable);
            if (string.IsNullOrWhiteSpace(server)) server = DefaultServer;

            DatabaseName = $"WeShopAlotTests_{DateTime.UtcNow:yyyyMMdd_HHmmss}_{Guid.NewGuid():N}"[..40];
            ConnectionString = new SqlConnectionStringBuilder(server) { InitialCatalog = DatabaseName }.ConnectionString;
        }

        /// <summary>The throw-away database this run uses, e.g. <c>WeShopAlotTests_20261103_143000_1a2b3c4d</c>.</summary>
        public string DatabaseName { get; }

        /// <summary>The server from <see cref="ServerVariable"/> (or LocalDB) plus <see cref="DatabaseName"/>.</summary>
        public string ConnectionString { get; }

        /// <summary>Set when <see cref="TryRecreateAndSeedAsync"/> could not reach a SQL Server.</summary>
        public string UnavailableReason { get; private set; }

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Development");
            builder.UseSetting("Token:Key", "integration-test-signing-key-integration-test-signing-key-0123456789");
            builder.UseSetting("Token:Issuer", "https://weshopalot.test");

            // The real provider switch (UseSelectedDatabaseServer) reads these, exactly as in production;
            // nothing about the DbContext registration is replaced.
            builder.UseSetting("Settings:DbServerType", "SQLServer");
            builder.UseSetting("ConnectionStrings:WeShopAlotSQLConnection", ConnectionString);
            builder.UseSetting("ConnectionStrings:Redis", "localhost:6379,abortConnect=false");

            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IResponseCacheService>();
                services.AddSingleton<IResponseCacheService, TestDoubles.NoResponseCache>();
                services.RemoveAll<IBasketRepository>();
                services.AddSingleton<IBasketRepository, TestDoubles.InMemoryBasketRepository>();
            });
        }

        /// <summary>
        /// Builds the test database the way <c>dotnet run --project WeShopAlot.Data.ConsoleApp</c>
        /// builds a developer's: migrations, then the shipped catalog. Returns false, with
        /// <see cref="UnavailableReason"/> set, when no SQL Server can be reached.
        /// </summary>
        public async Task<bool> TryRecreateAndSeedAsync()
        {
            using var scope = Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<WeShopAlotContext>();
            try
            {
                await DatabaseSeeder.RecreateAsync(db);
            }
            catch (Exception ex) when (ex is SqlException or PlatformNotSupportedException)
            {
                UnavailableReason = ex.Message.Split('\n')[0].Trim();
                return false;
            }
            databaseCreated = true;
            await DatabaseSeeder.SeedAsync(db);
            return true;
        }

        /// <summary>A DbContext on the test database, for assertions that look past the HTTP layer.</summary>
        public WeShopAlotContext CreateDbContext()
        {
            var options = new DbContextOptionsBuilder<WeShopAlotContext>().UseSqlServer(ConnectionString).Options;
            return new WeShopAlotContext(options);
        }

        /// <summary>Drops the test database, then shuts the in-memory server down.</summary>
        public override async ValueTask DisposeAsync()
        {
            if (databaseCreated)
            {
                try
                {
                    await using var db = CreateDbContext();
                    await db.Database.EnsureDeletedAsync();
                }
                catch (SqlException)
                {
                    // Best effort: a database left behind is named with its date and is harmless.
                }
            }
            await base.DisposeAsync();
        }
    }
}
