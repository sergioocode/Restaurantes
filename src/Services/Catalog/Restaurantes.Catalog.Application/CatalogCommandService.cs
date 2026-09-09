namespace Restaurantes.Catalog.Application;

public sealed partial class CatalogCommandService(
    ICatalogWriteStore store,
    TimeProvider timeProvider
) { }
