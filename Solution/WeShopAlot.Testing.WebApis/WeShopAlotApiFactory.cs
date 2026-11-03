using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using WeShopAlot.Data;
using WeShopAlot.Data.Interfaces;
using WeShopAlot.Data.Models;
using WeShopAlot.Data.Repositories.Interfaces;

namespace WeShopAlot.Testing.WebApis
{
    /// <summary>
    /// Runs the whole WeShopAlot API in memory for integration tests: the real
    /// pipeline, controllers, authentication, and AutoMapper, with an in-memory
    /// EF Core database and in-memory stand-ins for Redis, so no SQL Server,
    /// Redis, or user secrets are needed.
    ///
    /// This is the fast tier. The in-memory provider does not run migrations and does
    /// not enforce foreign keys, column sizes, or SQL semantics; for those, see
    /// <see cref="WeShopAlotSqlApiFactory"/>, which rebuilds a real SQL Server database
    /// with the same seeder the console app uses.
    /// </summary>
    public class WeShopAlotApiFactory : WebApplicationFactory<Program>
    {
        private readonly string databaseName = "WeShopAlotTests-" + Guid.NewGuid();

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Development");
            builder.UseSetting("Token:Key", "integration-test-signing-key-integration-test-signing-key-0123456789");
            builder.UseSetting("Token:Issuer", "https://weshopalot.test");
            builder.UseSetting("Settings:DbServerType", "SQLServer");
            builder.UseSetting("ConnectionStrings:WeShopAlotSQLConnection", "Server=unused-in-tests");
            builder.UseSetting("ConnectionStrings:Redis", "localhost:6379,abortConnect=false");

            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<DbContextOptions<WeShopAlotContext>>();
                services.RemoveAll<IDbContextOptionsConfiguration<WeShopAlotContext>>();
                services.AddDbContext<WeShopAlotContext>(o => o.UseInMemoryDatabase(databaseName));

                services.RemoveAll<IResponseCacheService>();
                services.AddSingleton<IResponseCacheService, TestDoubles.NoResponseCache>();
                services.RemoveAll<IBasketRepository>();
                services.AddSingleton<IBasketRepository, TestDoubles.InMemoryBasketRepository>();
            });
        }

        /// <summary>Creates the database and adds a small catalog: 2 brands, 2 types, 3 products, 2 delivery methods.</summary>
        public async Task SeedAsync()
        {
            using var scope = Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<WeShopAlotContext>();
            await db.Database.EnsureCreatedAsync();
            if (await db.Products.AnyAsync()) return;

            var angular = new ProductBrand { Id = 1, Name = "Angular" };
            var net = new ProductBrand { Id = 2, Name = "NetCore" };
            var boards = new ProductType { Id = 1, Name = "Boards" };
            var hats = new ProductType { Id = 2, Name = "Hats" };
            db.ProductBrands.AddRange(angular, net);
            db.ProductTypes.AddRange(boards, hats);
            db.Products.AddRange(
                new Product { Id = 1, Name = "Angular Speedster Board", Description = "Board", Price = 200m, PictureUrl = "images/products/sb-ang1.png", ProductBrand = angular, ProductType = boards },
                new Product { Id = 2, Name = "Green Angular Board", Description = "Board", Price = 150m, PictureUrl = "images/products/sb-ang2.png", ProductBrand = angular, ProductType = boards },
                new Product { Id = 3, Name = "Core Purple Hat", Description = "Hat", Price = 10m, PictureUrl = "images/products/hat-core1.png", ProductBrand = net, ProductType = hats });
            db.DeliveryMethods.AddRange(
                new DeliveryMethod { Id = 1, ShortName = "UPS1", Description = "Fastest", DeliveryTime = "1-2 Days", Price = 10m },
                new DeliveryMethod { Id = 2, ShortName = "FREE", Description = "Free", DeliveryTime = "1-2 Weeks", Price = 0m });
            await db.SaveChangesAsync();
        }
    }
}
