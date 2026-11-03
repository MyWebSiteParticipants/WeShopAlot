using Microsoft.EntityFrameworkCore;
using WeShopAlot.Data.Models;

namespace WeShopAlot.Data.Seed
{
    /// <summary>
    /// Rebuilds the WeShopAlot database and loads the shipped sample data.
    ///
    /// Two callers share this code so that they start from the same database:
    /// <c>WeShopAlot.Data.ConsoleApp</c> (what a developer runs once per machine, and again
    /// to reset) and the SQL Server integration tests in <c>WeShopAlot.Testing.WebApis</c>
    /// (<c>WeShopAlotSqlApiFactory</c>, which creates a throw-away database per test run).
    ///
    /// The two steps are separate on purpose. <see cref="RecreateAsync"/> is destructive and
    /// needs a relational provider; <see cref="SeedAsync"/> only adds what is missing and works
    /// on any provider, including the in-memory one.
    /// </summary>
    public static class DatabaseSeeder
    {
        public const string SqlServerProvider = "Microsoft.EntityFrameworkCore.SqlServer";
        public const string PostgresProvider = "Npgsql.EntityFrameworkCore.PostgreSQL";

        /// <summary>How many rows each step added. Zero everywhere means the data was already there.</summary>
        public sealed record SeedCounts(int ProductTypes, int ProductBrands, int DeliveryMethods, int Products)
        {
            public int Total => ProductTypes + ProductBrands + DeliveryMethods + Products;
            public override string ToString() =>
                $"{ProductTypes} product types, {ProductBrands} brands, {DeliveryMethods} delivery methods, {Products} products";
        }

        /// <summary>
        /// Drops every WeShopAlot table (the WSA schema and the migrations history) and applies
        /// all migrations, which also inserts the model-level seed data (the OrderStatus rows
        /// from <see cref="Seeding.SeedMasterData"/>).
        ///
        /// Destructive: everything in the WSA schema of the target database is gone afterwards,
        /// orders and users included. If the database itself does not exist yet it is created,
        /// which needs a login allowed to CREATE DATABASE (LocalDB: always).
        /// </summary>
        public static async Task RecreateAsync(WeShopAlotContext context, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(context);

            var script = context.Database.ProviderName switch
            {
                SqlServerProvider => "DropSqlServerTables.sql",
                PostgresProvider => "DropPostgresTables.sql",
                _ => throw new InvalidOperationException(
                    $"RecreateAsync needs SQL Server or PostgreSQL; the context uses '{context.Database.ProviderName}'. " +
                    "For the in-memory provider use EnsureCreated and SeedAsync instead."),
            };

            // A brand-new database has nothing to drop, and the script would fail to connect.
            // Migrate creates the database when it is missing.
            if (await context.Database.CanConnectAsync(cancellationToken))
            {
                await context.Database.ExecuteSqlRawAsync(SeedData.ReadResource(script), cancellationToken);
            }

            await context.Database.MigrateAsync(cancellationToken);
        }

        /// <summary>
        /// Loads the embedded sample catalog: product types, brands, delivery methods, and the
        /// products that reference them. Each table is filled only when it is empty, so running
        /// it twice adds nothing. Works on any provider; the schema must already exist.
        /// </summary>
        public static Task<SeedCounts> SeedAsync(WeShopAlotContext context, CancellationToken cancellationToken = default) =>
            SeedAsync(context, SeedData.Load(), cancellationToken);

        /// <summary>Same as <see cref="SeedAsync(WeShopAlotContext, CancellationToken)"/> with data you supply.</summary>
        public static async Task<SeedCounts> SeedAsync(WeShopAlotContext context, SeedData data, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(context);
            ArgumentNullException.ThrowIfNull(data);

            var productTypes = 0;
            if (!await context.ProductTypes.AnyAsync(cancellationToken))
            {
                context.ProductTypes.AddRange(data.ProductTypes.Select(t => new ProductType
                {
                    InternalId = t.Id,
                    Name = t.Name,
                }));
                productTypes = await context.SaveChangesAsync(cancellationToken);
            }

            var productBrands = 0;
            if (!await context.ProductBrands.AnyAsync(cancellationToken))
            {
                context.ProductBrands.AddRange(data.ProductBrands.Select(b => new ProductBrand
                {
                    InternalId = b.Id,
                    Name = b.Name,
                }));
                productBrands = await context.SaveChangesAsync(cancellationToken);
            }

            var deliveryMethods = 0;
            if (!await context.DeliveryMethods.AnyAsync(cancellationToken))
            {
                context.DeliveryMethods.AddRange(data.DeliveryMethods.Select(d => new DeliveryMethod
                {
                    InternalId = d.Id,
                    ShortName = d.ShortName,
                    Description = d.Description,
                    DeliveryTime = d.DeliveryTime,
                    Price = d.Price,
                }));
                deliveryMethods = await context.SaveChangesAsync(cancellationToken);
            }

            var products = 0;
            if (!await context.Products.AnyAsync(cancellationToken))
            {
                // products.json names its brand and type; the rows above give them their Ids.
                var brandIds = await context.ProductBrands.ToDictionaryAsync(b => b.Name.Trim(), b => b.Id, cancellationToken);
                var typeIds = await context.ProductTypes.ToDictionaryAsync(t => t.Name.Trim(), t => t.Id, cancellationToken);

                foreach (var p in data.Products)
                {
                    if (!brandIds.TryGetValue(p.ProductBrand.Trim(), out var brandId))
                        throw new InvalidDataException($"products.json: product '{p.Name}' names brand '{p.ProductBrand}', which is not in productBrands.json.");
                    if (!typeIds.TryGetValue(p.ProductType.Trim(), out var typeId))
                        throw new InvalidDataException($"products.json: product '{p.Name}' names type '{p.ProductType}', which is not in productTypes.json.");

                    context.Products.Add(new Product
                    {
                        InternalId = p.InternalId,
                        Name = p.Name,
                        Description = p.Description,
                        Price = p.Price,
                        PictureUrl = p.PictureUrl,
                        ProductBrandId = brandId,
                        ProductTypeId = typeId,
                    });
                }
                products = await context.SaveChangesAsync(cancellationToken);
            }

            return new SeedCounts(productTypes, productBrands, deliveryMethods, products);
        }
    }
}
