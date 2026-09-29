using System.Diagnostics;
using System.Net;
using System.Reflection;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace Restaurantes.Hosts.Tests;

public sealed class HostStartupTests
{
    [Theory]
    [InlineData("Restaurantes.Gateway.Private")]
    [InlineData("Restaurantes.Gateway.Public")]
    [InlineData("Restaurantes.Clients.Dashboard.Web")]
    [InlineData("Restaurantes.Clients.Kds.Web")]
    [InlineData("Restaurantes.Integrations.Api")]
    public async Task Host_starts_serves_health_and_stops(string assemblyName)
    {
        TaskCompletionSource<IHost> built = new(TaskCreationOptions.RunContinuationsAsynchronously);
        using HostBuiltObserver observer = new(built);
        Assembly assembly = Assembly.Load(assemblyName);
        MethodInfo entryPoint =
            assembly.EntryPoint
            ?? throw new InvalidOperationException($"{assemblyName} has no entry point.");
        string[] arguments = ["--urls", "http://127.0.0.1:0"];
        Task run = Task.Run(
            () =>
            {
                object? result = entryPoint.Invoke(null, [arguments]);
                if (result is Task task)
                {
                    task.GetAwaiter().GetResult();
                }
            },
            TestContext.Current.CancellationToken
        );
        IHost host = await built.Task.WaitAsync(
            TimeSpan.FromSeconds(20),
            TestContext.Current.CancellationToken
        );
        IHostApplicationLifetime lifetime =
            host.Services.GetRequiredService<IHostApplicationLifetime>();

        try
        {
            TaskCompletionSource<bool> started = new(
                TaskCreationOptions.RunContinuationsAsynchronously
            );
            using CancellationTokenRegistration registration = lifetime.ApplicationStarted.Register(
                () =>
                    started.TrySetResult(true)
            );
            await started.Task.WaitAsync(
                TimeSpan.FromSeconds(20),
                TestContext.Current.CancellationToken
            );

            IServer server = host.Services.GetRequiredService<IServer>();
            IServerAddressesFeature addresses =
                server.Features.Get<IServerAddressesFeature>()
                ?? throw new InvalidOperationException("The host has no HTTP address.");
            string address = Assert.Single(addresses.Addresses);
            using HttpClient client = new() { BaseAddress = new Uri(address) };
            using HttpResponseMessage response = await client.GetAsync(
                "/health",
                TestContext.Current.CancellationToken
            );

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }
        finally
        {
            lifetime.StopApplication();
            await run.WaitAsync(TimeSpan.FromSeconds(20), TestContext.Current.CancellationToken);
        }
    }

    private sealed class HostBuiltObserver
        : IObserver<DiagnosticListener>,
            IObserver<KeyValuePair<string, object?>>,
            IDisposable
    {
        private readonly TaskCompletionSource<IHost> built;
        private readonly IDisposable listeners;
        private IDisposable? events;

        public HostBuiltObserver(TaskCompletionSource<IHost> built)
        {
            this.built = built;
            listeners = DiagnosticListener.AllListeners.Subscribe(this);
        }

        public void OnNext(DiagnosticListener listener)
        {
            if (listener.Name == "Microsoft.Extensions.Hosting")
            {
                events = listener.Subscribe(
                    (IObserver<KeyValuePair<string, object?>>)this,
                    name => name == "HostBuilt"
                );
            }
        }

        public void OnNext(KeyValuePair<string, object?> value)
        {
            if (value.Key == "HostBuilt" && value.Value is IHost host)
            {
                built.TrySetResult(host);
            }
        }

        public void OnCompleted() { }

        public void OnError(Exception error) => built.TrySetException(error);

        public void Dispose()
        {
            events?.Dispose();
            listeners.Dispose();
        }
    }
}
