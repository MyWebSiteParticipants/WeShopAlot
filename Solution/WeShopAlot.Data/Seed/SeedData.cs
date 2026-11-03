using System.Text.Json;
using WeShopAlot.Data.Import.Models;

namespace WeShopAlot.Data.Seed
{
    /// <summary>
    /// The sample catalog WeShopAlot ships with, read from the JSON files embedded in this
    /// assembly (Seed/Content). It is plain data: no database is involved, so unit tests can
    /// check it, and <see cref="DatabaseSeeder"/> loads it into whichever database it is given.
    /// </summary>
    public sealed class SeedData
    {
        public IReadOnlyList<ImportedProductType> ProductTypes { get; init; } = Array.Empty<ImportedProductType>();
        public IReadOnlyList<ImportedProductBrand> ProductBrands { get; init; } = Array.Empty<ImportedProductBrand>();
        public IReadOnlyList<ImportedDeliveryMethod> DeliveryMethods { get; init; } = Array.Empty<ImportedDeliveryMethod>();
        public IReadOnlyList<ImportedProduct> Products { get; init; } = Array.Empty<ImportedProduct>();

        /// <summary>Reads the four embedded JSON files.</summary>
        public static SeedData Load()
        {
            return new SeedData
            {
                ProductTypes = ReadJson<List<ImportedProductType>>("productTypes.json"),
                ProductBrands = ReadJson<List<ImportedProductBrand>>("productBrands.json"),
                DeliveryMethods = ReadJson<List<ImportedDeliveryMethod>>("deliveryMethods.json"),
                Products = ReadJson<List<ImportedProduct>>("products.json"),
            };
        }

        /// <summary>
        /// Returns the text of an embedded seed resource: one of the JSON files, or a script
        /// such as <c>DropSqlServerTables.sql</c>. Resource names are the bare file names
        /// (see the EmbeddedResource items in WeShopAlot.Data.csproj).
        /// </summary>
        public static string ReadResource(string fileName)
        {
            var assembly = typeof(SeedData).Assembly;
            using var stream = assembly.GetManifestResourceStream("Seed." + fileName)
                ?? throw new FileNotFoundException(
                    $"Embedded seed resource 'Seed.{fileName}' was not found in {assembly.GetName().Name}. " +
                    $"Available: {string.Join(", ", assembly.GetManifestResourceNames())}");
            using var reader = new StreamReader(stream);
            return reader.ReadToEnd();
        }

        private static T ReadJson<T>(string fileName)
        {
            return JsonSerializer.Deserialize<T>(ReadResource(fileName))
                ?? throw new InvalidDataException($"Embedded seed file '{fileName}' is empty.");
        }
    }
}
