namespace Restaurantes.Clients.Shared.Operations;

public static class AsyncEventDispatcher
{
    public static async Task InvokeAsync<T>(Func<T, Task>? handlers, T value)
    {
        if (handlers is null)
        {
            return;
        }

        foreach (Delegate handler in handlers.GetInvocationList())
        {
            await ((Func<T, Task>)handler)(value);
        }
    }
}
