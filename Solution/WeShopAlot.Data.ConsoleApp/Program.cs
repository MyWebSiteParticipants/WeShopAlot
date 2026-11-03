using Microsoft.EntityFrameworkCore;
using WeShopAlot.Data.Seed;

namespace WeShopAlot.Data.ConsoleApp
{
    /// <summary>
    /// Recreates the WeShopAlot database and loads the sample catalog, then exits.
    ///
    ///   dotnet run --project WeShopAlot.Data.ConsoleApp
    ///
    /// Settings come from this project's user secrets, then environment variables, then the
    /// command line, later sources winning. The two it needs are the same ones the API uses:
    ///
    ///   Settings:DbServerType                      SQLServer or PostgresSql
    ///   ConnectionStrings:WeShopAlotSQLConnection  the database to rebuild
    ///
    /// Everything in that database's WSA schema is dropped first, so point it only at a
    /// database you are willing to lose. To rebuild a different one for a single run, override
    /// the connection string without touching the secrets:
    ///
    ///   dotnet run --project WeShopAlot.Data.ConsoleApp -- --ConnectionStrings:WeShopAlotSQLConnection "Server=(localdb)\MSSQLLocalDB;Database=WeShopAlot_Scratch;Trusted_Connection=true;TrustServerCertificate=true"
    ///
    /// The work itself is in WeShopAlot.Data.Seed.DatabaseSeeder, which the SQL Server
    /// integration tests call too, so tests and developers start from the same database.
    /// </summary>
    public static class Program
    {
        public static async Task<int> Main(string[] args)
        {
            var configuration = DesignTimeContextFactory.BuildConfiguration(args);

            try
            {
                await using var context = DesignTimeContextFactory.CreateContext(configuration);

                Console.WriteLine($"Provider:  {context.Database.ProviderName}");
                Console.WriteLine($"Database:  {Describe(context)}");
                Console.WriteLine();

                Console.WriteLine("1. Dropping the WSA schema and applying all migrations...");
                await DatabaseSeeder.RecreateAsync(context);
                var applied = (await context.Database.GetAppliedMigrationsAsync()).ToList();
                Console.WriteLine($"   {applied.Count} migrations applied; latest: {applied.LastOrDefault() ?? "(none)"}");

                Console.WriteLine("2. Loading the sample catalog...");
                var counts = await DatabaseSeeder.SeedAsync(context);
                Console.WriteLine($"   added {counts}");

                Console.WriteLine();
                Console.WriteLine($"Done. {await context.Products.CountAsync()} products, {await context.OrderStatuses.CountAsync()} order statuses.");
                return 0;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine();
                Console.Error.WriteLine("Seeding failed: " + ex.Message);
                Console.Error.WriteLine("Check Settings:DbServerType and ConnectionStrings:WeShopAlotSQLConnection in this project's user secrets (docs/Database.md).");
                return 1;
            }
        }

        /// <summary>Server and database name, with no password, for the console.</summary>
        private static string Describe(WeShopAlotContext context)
        {
            var connection = context.Database.GetDbConnection();
            return $"{connection.Database} on {connection.DataSource}";
        }
    }
}
