using System.Reflection;
using Microsoft.JSInterop;
using Xunit;
using CommanderHome = Restaurantes.Clients.Commander.Pwa.Pages.Home;
using PosHome = Restaurantes.Clients.Pos.Pwa.Pages.Home;

namespace Restaurantes.Clients.Pwa.Tests;

public sealed class PrintRequestsTests
{
    [Theory]
    [InlineData(typeof(PosHome), "PrintAccountPreview")]
    [InlineData(typeof(PosHome), "PrintSalesTicket")]
    [InlineData(typeof(CommanderHome), "PrintSalesTicket")]
    public async Task Print_requests_reach_the_browser_independently_of_realtime_refresh(
        Type componentType,
        string action
    )
    {
        object component = Activator.CreateInstance(componentType)!;
        RecordingPrintRuntime js = new();
        const BindingFlags flags =
            BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
        componentType.GetProperty("Js", flags)!.SetValue(component, js);
        using CancellationTokenSource refresh = new();
        await refresh.CancelAsync();
        componentType.GetField("realtimeRefresh", flags)!.SetValue(component, refresh);

        await (Task)componentType.GetMethod(action, flags)!.Invoke(component, null)!;

        Assert.Equal("window.print", Assert.Single(js.Calls));
        Assert.False(js.Token.CanBeCanceled);
    }

    private sealed class RecordingPrintRuntime : IJSRuntime
    {
        public List<string> Calls { get; } = [];
        public CancellationToken Token { get; private set; }

        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args) =>
            InvokeAsync<TValue>(identifier, CancellationToken.None, args);

        public ValueTask<TValue> InvokeAsync<TValue>(
            string identifier,
            CancellationToken cancellationToken,
            object?[]? args
        )
        {
            Calls.Add(identifier);
            Token = cancellationToken;
            return ValueTask.FromResult(default(TValue)!);
        }
    }
}
