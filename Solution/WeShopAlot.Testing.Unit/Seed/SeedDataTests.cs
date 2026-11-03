using System.Text.RegularExpressions;
using WeShopAlot.Data.Seed;

namespace WeShopAlot.Testing.Unit.Seed
{
    /// <summary>
    /// The sample catalog as data, before any database sees it. These catch a bad edit to the
    /// JSON files in WeShopAlot.Data/Seed/Content the moment it is made, rather than as a
    /// confusing failure in the seeder or a missing picture in the shop.
    /// </summary>
    [TestClass]
    public class SeedDataTests
    {
        private static readonly SeedData Seed = SeedData.Load();

        [TestMethod]
        public void Load_ReadsTheFourEmbeddedFiles()
        {
            Assert.AreEqual(7, Seed.ProductTypes.Count, "productTypes.json");
            Assert.AreEqual(10, Seed.ProductBrands.Count, "productBrands.json");
            Assert.AreEqual(4, Seed.DeliveryMethods.Count, "deliveryMethods.json");
            Assert.AreEqual(16, Seed.Products.Count, "products.json");
        }

        [TestMethod]
        public void EveryProduct_NamesAKnownBrandAndType()
        {
            // DatabaseSeeder.SeedAsync looks these up by name; a typo here would throw halfway through seeding.
            var brands = Seed.ProductBrands.Select(b => b.Name.Trim()).ToHashSet();
            var types = Seed.ProductTypes.Select(t => t.Name.Trim()).ToHashSet();

            foreach (var product in Seed.Products)
            {
                Assert.IsTrue(brands.Contains(product.ProductBrand.Trim()), $"{product.Name}: brand '{product.ProductBrand}' is not in productBrands.json");
                Assert.IsTrue(types.Contains(product.ProductType.Trim()), $"{product.Name}: type '{product.ProductType}' is not in productTypes.json");
            }
        }

        [TestMethod]
        public void InternalIds_AreUniqueAndSequential()
        {
            CollectionAssert.AreEqual(Enumerable.Range(1, 16).ToList(), Seed.Products.Select(p => p.InternalId).ToList());
            CollectionAssert.AllItemsAreUnique(Seed.ProductBrands.Select(b => b.Id).ToList());
            CollectionAssert.AllItemsAreUnique(Seed.ProductTypes.Select(t => t.Id).ToList());
            CollectionAssert.AllItemsAreUnique(Seed.DeliveryMethods.Select(d => d.Id).ToList());
        }

        [TestMethod]
        public void Prices_FitTheDecimal5_2Columns()
        {
            // Every money column in the schema is decimal(5,2): at most 999.99. SQL Server rejects a larger
            // value at SaveChanges; the in-memory provider does not, so this is checked on the data itself.
            foreach (var product in Seed.Products)
            {
                Assert.IsTrue(product.Price > 0 && product.Price < 1000m, $"{product.Name}: {product.Price}");
                Assert.AreEqual(product.Price, decimal.Round(product.Price, 2), $"{product.Name}: more than two decimals");
            }
            foreach (var method in Seed.DeliveryMethods)
            {
                Assert.IsTrue(method.Price >= 0 && method.Price < 1000m, $"{method.ShortName}: {method.Price}");
            }
        }

        [TestMethod]
        public void PictureUrls_FollowTheImageNamingScheme()
        {
            // images/products/<InternalId as 8 digits>-001.png: the files in WeShopAlot..WebAPI/Content.
            var scheme = new Regex(@"^images/products/(\d{8})-001\.png$");
            foreach (var product in Seed.Products)
            {
                var match = scheme.Match(product.PictureUrl);
                Assert.IsTrue(match.Success, $"{product.Name}: '{product.PictureUrl}'");
                Assert.AreEqual(product.InternalId, int.Parse(match.Groups[1].Value), $"{product.Name}: picture number differs from InternalId");
            }
        }

        [TestMethod]
        public void RequiredText_IsPresentAndWithinColumnLengths()
        {
            foreach (var product in Seed.Products)
            {
                Assert.IsFalse(string.IsNullOrWhiteSpace(product.Name), "Product.Name is required");
                Assert.IsTrue(product.Name.Length <= 150, $"{product.Name}: Name over 150 characters");
                Assert.IsFalse(string.IsNullOrWhiteSpace(product.Description), $"{product.Name}: Description is required");
                Assert.IsTrue(product.Description.Length <= 500, $"{product.Name}: Description over 500 characters");
            }
        }

        [TestMethod]
        public void DropScripts_AreEmbeddedForBothProviders()
        {
            StringAssert.Contains(SeedData.ReadResource("DropSqlServerTables.sql"), "WSA");
            StringAssert.Contains(SeedData.ReadResource("DropPostgresTables.sql"), "WSA");
        }
    }
}
