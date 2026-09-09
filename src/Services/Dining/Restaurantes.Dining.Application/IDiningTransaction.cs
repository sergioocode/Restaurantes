namespace Restaurantes.Dining.Application;

public interface IDiningTransaction : IAsyncDisposable
{
    Task CommitAsync(CancellationToken ct);
    Task RollbackAsync(CancellationToken ct);
}
