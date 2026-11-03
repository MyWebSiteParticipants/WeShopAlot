using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using WeShopAlot.Data.Seed;
using WeShopAlot.Data.Shared.Enumerations;
using WeShopAlot.Shared.Dtos;

namespace WeShopAlot.Testing.WebApis
{
    /// <summary>
    /// The API against a real SQL Server database, rebuilt for this run from the migrations and
    /// the shipped seed data (see <see cref="WeShopAlotSqlApiFactory"/>). These tests ask the
    /// questions the in-memory suite cannot: did every migration apply, is the catalog the one
    /// in products.json, does a checkout survive the foreign keys, does a reseed or a rebuild
    /// leave the database where we expect.
    ///
    /// Category "SqlServer": run only this suite with
    ///   dotnet test WeShopAlot.Testing.WebApis --filter TestCategory=SqlServer
    /// or leave it out (CI does) with --filter TestCategory!=SqlServer. Without a reachable
    /// SQL Server every test here is Inconclusive, not failed.
    /// </summary>
    [TestClass]
    [TestCategory("SqlServer")]
    [DoNotParallelize] // RecreateAsync_ThenSeed_StartsFromAKnownState rebuilds the shared database
    public class SqlApiTests
    {
        private static WeShopAlotSqlApiFactory factory;
        private static HttpClient client;
        private static string unavailable;
        private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

        [ClassInitialize]
        public static async Task ClassInitialize(TestContext context)
        {
            factory = new WeShopAlotSqlApiFactory();
            if (!await factory.TryRecreateAndSeedAsync())
            {
                unavailable = factory.UnavailableReason;
                return;
            }
            client = factory.CreateClient();
        }

        [ClassCleanup]
        public static async Task ClassCleanup()
        {
            client?.Dispose();
            if (factory != null) await factory.DisposeAsync();
        }

        [TestInitialize]
        public void RequireSqlServer()
        {
            if (unavailable != null)
            {
                Assert.Inconclusive(
                    $"No SQL Server for the integration tests: {unavailable}. " +
                    $"Install SQL Server LocalDB, or set {WeShopAlotSqlApiFactory.ServerVariable} to a connection string without a database name.");
            }
        }

        // ------------------------------------------------------------- schema

        [TestMethod]
        public async Task Schema_EveryMigrationApplied_NonePending()
        {
            await using var db = factory.CreateDbContext();

            var applied = (await db.Database.GetAppliedMigrationsAsync()).ToList();
            var pending = (await db.Database.GetPendingMigrationsAsync()).ToList();

            Assert.IsTrue(applied.Count >= 2, $"expected the Initial-001 and AddressNames migrations at least; applied: {string.Join(", ", applied)}");
            Assert.AreEqual(0, pending.Count, $"pending: {string.Join(", ", pending)} — a model change needs a migration (dotnet ef migrations add)");
        }

        [TestMethod]
        public async Task OrderStatuses_ComeFromTheMigration_NotTheSeeder()
        {
            // Seeding.SeedMasterData puts the OrderStatus rows into the model (HasData), so the migration
            // inserts them. The console app and DatabaseSeeder.SeedAsync never touch this table.
            await using var db = factory.CreateDbContext();

            var statuses = await db.OrderStatuses.OrderBy(s => s.Id).ToListAsync();

            CollectionAssert.AreEqual(
                new[] { OrderStatusEnum.PaymentFailed, OrderStatusEnum.PaymentReceived, OrderStatusEnum.Pending },
                statuses.Select(s => s.Name).ToArray());
            Assert.AreEqual("Payment Received", statuses.Single(s => s.Name == OrderStatusEnum.PaymentReceived).Description);
        }

        // ------------------------------------------------------------ catalog

        [TestMethod]
        public async Task Catalog_IsTheShippedOne()
        {
            var seed = SeedData.Load();

            var page = await ReadAsync(await client.GetAsync("/api/product?pageSize=50"));
            Assert.AreEqual(seed.Products.Count, page.GetProperty("count").GetInt32(), "products.json");
            Assert.AreEqual(16, page.GetProperty("data").GetArrayLength());

            Assert.AreEqual(seed.ProductBrands.Count, (await ReadAsync(await client.GetAsync("/api/product/brands"))).GetArrayLength(), "productBrands.json");
            Assert.AreEqual(seed.ProductTypes.Count, (await ReadAsync(await client.GetAsync("/api/product/types"))).GetArrayLength(), "productTypes.json");
        }

        [TestMethod]
        public async Task Products_SortedByPriceAsc_SqlServerPutsTheCheapestFirst()
        {
            var page = await ReadAsync(await client.GetAsync("/api/product?sort=priceAsc&pageSize=3"));

            var data = page.GetProperty("data");
            Assert.AreEqual("Woodchopper", data[0].GetProperty("name").GetString(), "the $18 cigar box guitar");
            Assert.AreEqual(18m, data[0].GetProperty("price").GetDecimal());
            Assert.IsTrue(data[0].GetProperty("price").GetDecimal() <= data[1].GetProperty("price").GetDecimal());
            Assert.IsTrue(data[1].GetProperty("price").GetDecimal() <= data[2].GetProperty("price").GetDecimal());
        }

        [TestMethod]
        public async Task Product_MapsTheSeededBrandAndType()
        {
            var id = await ProductIdAsync("Blondie");

            var product = await client.GetFromJsonAsync<ProductToReturnDto>("/api/product/" + id, Json);

            Assert.AreEqual("Carvin", product.ProductBrand);
            Assert.AreEqual("Electric Guitar", product.ProductType);
            Assert.AreEqual(500m, product.Price);
            StringAssert.EndsWith(product.PictureUrl, "images/products/00000001-001.png");
        }

        [TestMethod]
        public async Task DeliveryMethods_FourShipped_FreeIsSlowest()
        {
            var token = await RegisterAsync("delivery@sql.test");

            var methods = await ReadAsync(await SendAsync(HttpMethod.Get, "/api/order/deliveryMethods", token));

            Assert.AreEqual(4, methods.GetArrayLength());
            var free = methods.EnumerateArray().Single(m => m.GetProperty("shortName").GetString() == "FREE");
            Assert.AreEqual(0m, free.GetProperty("price").GetDecimal());
            Assert.AreEqual("1-2 Weeks", free.GetProperty("deliveryTime").GetString());
        }

        // ----------------------------------------------------------- checkout

        [TestMethod]
        public async Task Checkout_WritesAnOrder_ThatSatisfiesTheForeignKeys()
        {
            // The in-memory suite proves the prices; only a real database proves the row can be written:
            // Order.StatusId must point at a seeded OrderStatus, OrderItem at the order, the address at a user.
            var email = "checkout@sql.test";
            var token = await RegisterAsync(email);
            var blondieId = await ProductIdAsync("Blondie");
            var ups1 = (await ReadAsync(await SendAsync(HttpMethod.Get, "/api/order/deliveryMethods", token)))
                .EnumerateArray().Single(m => m.GetProperty("shortName").GetString() == "UPS1").GetProperty("id").GetInt32();

            var basket = new CustomerBasketDto
            {
                Id = "sql-basket-" + Guid.NewGuid(),
                Items = new List<BasketItemDto>
                {
                    new() { Id = blondieId, ProductName = "Blondie", Price = 1m, Quantity = 1, PictureUrl = "x", Brand = "Carvin", Type = "Electric Guitar" }
                }
            };
            Assert.AreEqual(HttpStatusCode.OK, (await client.PostAsJsonAsync("/api/basket", basket)).StatusCode);

            var orderDto = new OrderDto
            {
                BasketId = basket.Id,
                DeliveryMethodId = ups1,
                ShipToAddress = new AddressDto { FirstName = "Ada", LastName = "Lovelace", Street = "1 Main St", City = "Girard", State = "OH", Zipcode = "44420" }
            };
            var created = await SendAsync(HttpMethod.Post, "/api/order", token, orderDto);
            Assert.AreEqual(HttpStatusCode.OK, created.StatusCode, await created.Content.ReadAsStringAsync());
            var createdJson = await ReadAsync(created);
            Assert.AreEqual(500m, createdJson.GetProperty("subtotal").GetDecimal(), "the catalog price, not the client's 1");

            // Look at the rows themselves.
            var orderId = createdJson.GetProperty("id").GetInt32();
            await using var db = factory.CreateDbContext();
            var order = await db.Orders
                .Include(o => o.Status)
                .Include(o => o.DeliveryMethod)
                .Include(o => o.OrderItems)
                .SingleAsync(o => o.Id == orderId);
            Assert.AreEqual(email, order.BuyerEmail);
            Assert.AreEqual(OrderStatusEnum.Pending, order.Status.Name, "StatusId must resolve through the FK to the seeded Pending row");
            Assert.AreEqual(1, order.OrderItems.Count);
            Assert.AreEqual(10m, order.DeliveryMethod.Price, "UPS1 from deliveryMethods.json");
        }

        // -------------------------------------------------- seeder behaviour

        [TestMethod]
        public async Task SeedAsync_RunTwice_AddsNothing()
        {
            await using var db = factory.CreateDbContext();
            var before = await db.Products.CountAsync();

            var counts = await DatabaseSeeder.SeedAsync(db);

            Assert.AreEqual(0, counts.Total, $"a second seed must be a no-op, but it added {counts}");
            Assert.AreEqual(before, await db.Products.CountAsync());
        }

        [TestMethod]
        public async Task RecreateAsync_ThenSeed_StartsFromAKnownState()
        {
            // What a developer's "reset" does, and what every test run starts from.
            var email = "wiped@sql.test";
            await RegisterAsync(email);
            await using var db = factory.CreateDbContext();
            Assert.IsTrue(await db.AppUsers.AnyAsync(u => u.EmailAddress == email), "the user was written before the rebuild");

            await DatabaseSeeder.RecreateAsync(db);
            var counts = await DatabaseSeeder.SeedAsync(db);

            Assert.IsFalse(await db.AppUsers.AnyAsync(), "RecreateAsync drops every WSA table, users included");
            Assert.AreEqual(0, await db.Orders.CountAsync());
            Assert.AreEqual(16, counts.Products, "the catalog is loaded again from products.json");
            Assert.AreEqual(3, await db.OrderStatuses.CountAsync(), "and the migration seeded the statuses again");
        }

        // ------------------------------------------------------------ helpers

        private static async Task<int> ProductIdAsync(string name)
        {
            var page = await ReadAsync(await client.GetAsync("/api/product?search=" + Uri.EscapeDataString(name)));
            var match = page.GetProperty("data").EnumerateArray().Single(p => p.GetProperty("name").GetString() == name);
            return match.GetProperty("id").GetInt32();
        }

        private static async Task<string> RegisterAsync(string email)
        {
            var response = await client.PostAsJsonAsync("/api/account/register",
                new RegisterDto { DisplayName = email.Split("@")[0], Email = email, Password = "Pa$$w0rd" });
            Assert.AreEqual(HttpStatusCode.OK, response.StatusCode, await response.Content.ReadAsStringAsync());
            return (await response.Content.ReadFromJsonAsync<UserDto>(Json)).Token;
        }

        private static async Task<HttpResponseMessage> SendAsync(HttpMethod method, string route, string token, object body = null)
        {
            using var request = new HttpRequestMessage(method, route);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            if (body != null) request.Content = JsonContent.Create(body);
            return await client.SendAsync(request);
        }

        private static async Task<JsonElement> ReadAsync(HttpResponseMessage response) =>
            JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
    }
}
