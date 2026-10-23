using System.Diagnostics;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using Restaurantes.Catalog.Contracts.Responses;
using Restaurantes.Dining.Contracts.Requests;
using Restaurantes.Orders.Contracts.Requests;
using Restaurantes.Orders.Contracts.Responses;

namespace Restaurantes.OrderSimulator;

internal static class Program
{
    private static readonly TimeSpan DiningProjectionPollInterval = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan DiningProjectionTimeout = TimeSpan.FromMinutes(2);
    private static readonly TimeSpan TableAvailabilityPollInterval = TimeSpan.FromSeconds(1);

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true,
    };

    public static async Task<int> Main(string[] args)
    {
        try
        {
            string configPath = ReadConfigPath(args);
            SimulatorOptions options = await LoadOptions(configPath);
            Validate(options);
            using CancellationTokenSource stopCreating = new();
            Console.CancelKeyPress += (_, eventArgs) =>
            {
                eventArgs.Cancel = true;
                stopCreating.Cancel();
                WriteLog("Parando pedidos nuevos; los pedidos en curso terminarán su ciclo.");
            };

            using HttpClient client = new()
            {
                BaseAddress = new Uri(options.ApiBaseUrl.TrimEnd('/') + "/"),
                Timeout = Timeout.InfiniteTimeSpan,
            };

            SimulatorRandom sharedRandom = new(options.RandomSeed);
            SemaphoreSlim randomLock = new(1, 1);
            RandomOrderScheduler orderScheduler = new(
                options.MaxConcurrentOrders,
                options.Restaurants.Select(restaurant => restaurant.RestaurantId),
                sharedRandom
            );
            Task<Exception?>[] preflights = options
                .Restaurants.Select(restaurant =>
                    PreflightRestaurantAsync(
                        client,
                        restaurant,
                        restaurant.PosAccessToken.Trim(),
                        restaurant.KdsAccessToken.Trim()
                    )
                )
                .ToArray();
            Exception?[] preflightErrors = await Task.WhenAll(preflights);
            if (preflightErrors.Any(error => error is not null))
            {
                foreach (Exception? error in preflightErrors.Where(error => error is not null))
                {
                    WriteError(error!.Message);
                }
                WriteError(
                    "No se inició la simulación: todos los locales deben superar la validación inicial."
                );
                return 1;
            }

            WriteLog(
                $"{string.Join(", ", options.Restaurants.Select(restaurant => restaurant.Name))} superaron la validación; los locales arrancan en paralelo."
            );
            List<Task<RestaurantRunResult>> simulations = options
                .Restaurants.Select(restaurant =>
                    RunRestaurantSafelyAsync(
                        client,
                        restaurant,
                        restaurant.PosAccessToken.Trim(),
                        restaurant.KdsAccessToken.Trim(),
                        options.OrdersPerBatch,
                        orderScheduler,
                        TimeSpan.FromSeconds(options.KitchenStepDelaySeconds),
                        TimeSpan.FromMinutes(options.RunDurationMinutes),
                        sharedRandom,
                        randomLock,
                        stopCreating.Token
                    )
                )
                .ToList();
            RestaurantRunResult[] results = await Task.WhenAll(simulations);
            int failedOrders = results.Sum(result => result.FailedOrders);
            int executionErrors = results.Sum(result => result.ExecutionErrors);
            if (failedOrders + executionErrors > 0)
            {
                WriteError(
                    $"Simulación finalizada con {failedOrders} pedidos fallidos y {executionErrors} errores de ejecución."
                );
                return 1;
            }
            WriteLog("Simulación terminada.");
            return 0;
        }
        catch (Exception exception)
        {
            WriteError($"Error: {exception.Message}");
            return 1;
        }
    }

    internal static async Task<Exception?> PreflightRestaurantAsync(
        HttpClient client,
        RestaurantOptions restaurant,
        string posToken,
        string kdsToken
    )
    {
        string step = "menú TPV";
        try
        {
            List<MenuItemResponse> menu = await GetAsync<List<MenuItemResponse>>(
                client,
                $"api/catalog/restaurants/{restaurant.RestaurantId}/menu",
                posToken,
                CancellationToken.None
            );
            step = "productos disponibles del menú TPV";
            if (FilterUsableMenu(menu).Count == 0)
            {
                throw new InvalidOperationException(
                    "la carta no tiene productos disponibles para este local. Revisa la carta general y la disponibilidad de productos del local."
                );
            }
            step = "mesas TPV";
            _ = await GetAsync<List<TableInfo>>(
                client,
                $"api/dining/restaurants/{restaurant.RestaurantId}/tables",
                posToken,
                CancellationToken.None
            );
            step = "estado de caja TPV";
            _ = await GetAsync<CashRegisterStatusResponse?>(
                client,
                $"api/cash-register/restaurants/{restaurant.RestaurantId}/current",
                posToken,
                CancellationToken.None
            );
            step = "KDS principal (CHEF)";
            _ = await GetAsync<List<OrderResponse>>(
                client,
                $"api/orders/kds?restaurantId={restaurant.RestaurantId}&stationCode=CHEF",
                kdsToken,
                CancellationToken.None
            );
            WriteLog($"{restaurant.Name}: validación inicial de TPV y KDS correcta.");
            return null;
        }
        catch (Exception exception)
        {
            return new InvalidOperationException(
                $"{restaurant.Name}: falló la validación de {step}; revisa el token correspondiente y que el local esté accesible. {exception.Message}",
                exception
            );
        }
    }

    internal static List<MenuItemResponse> FilterUsableMenu(IReadOnlyList<MenuItemResponse> menu)
    {
        HashSet<Guid> availableProductIds = menu.Where(item => item.IsAvailable)
            .Select(item => item.ProductId)
            .ToHashSet();
        return menu.Where(item =>
                item.IsAvailable
                && availableProductIds.Contains(item.ProductId)
                && !item.CategoryName.Equals("Extras", StringComparison.OrdinalIgnoreCase)
                && (item.ExtraGroups ?? []).All(group =>
                    group.Options.Count(option => availableProductIds.Contains(option.ProductId))
                    >= group.MinSelections
                )
            )
            .ToList();
    }

    private static async Task<RestaurantRunResult> RunRestaurantSafelyAsync(
        HttpClient client,
        RestaurantOptions restaurant,
        string posToken,
        string kdsToken,
        int ordersPerBatch,
        RandomOrderScheduler orderScheduler,
        TimeSpan kitchenStepDelay,
        TimeSpan runDuration,
        SimulatorRandom random,
        SemaphoreSlim randomLock,
        CancellationToken stopCreating
    )
    {
        try
        {
            return await RunRestaurantAsync(
                client,
                restaurant,
                posToken,
                kdsToken,
                ordersPerBatch,
                orderScheduler,
                kitchenStepDelay,
                runDuration,
                random,
                randomLock,
                stopCreating
            );
        }
        catch (Exception exception)
        {
            orderScheduler.MarkReady(restaurant.RestaurantId);
            WriteError(
                $"{restaurant.Name}: la ejecución se detuvo por un error: {exception.Message}"
            );
            return new RestaurantRunResult(0, 1);
        }
    }

    internal static string ReadConfigPath(string[] args)
    {
        if (args.Length == 0)
        {
            return Path.Combine("tools", "order-simulator", "simulator.local.json");
        }

        if (args.Length == 2 && args[0] == "--config")
        {
            return args[1];
        }

        throw new ArgumentException(
            "Uso: dotnet run --project tools/order-simulator/Restaurantes.OrderSimulator -- [--config ruta.json]"
        );
    }

    private static async Task<SimulatorOptions> LoadOptions(string path)
    {
        if (!File.Exists(path))
        {
            throw new FileNotFoundException(
                $"No existe '{path}'. Copia simulator.sample.json como simulator.local.json y configura los locales.",
                path
            );
        }

        await using FileStream stream = File.OpenRead(path);
        return await JsonSerializer.DeserializeAsync<SimulatorOptions>(stream, JsonOptions)
            ?? throw new InvalidDataException("El archivo de configuración está vacío.");
    }

    internal static void Validate(SimulatorOptions options)
    {
        ValidateSimulatorOptions(options);
        foreach (RestaurantOptions restaurant in options.Restaurants)
        {
            ValidateRestaurantOptions(restaurant);
        }
    }

    private static void ValidateSimulatorOptions(SimulatorOptions options)
    {
        if (!Uri.TryCreate(options.ApiBaseUrl, UriKind.Absolute, out Uri? baseUri))
        {
            throw new InvalidDataException(
                "ApiBaseUrl debe ser una URL absoluta del Gateway privado."
            );
        }
        if (baseUri.Scheme is not ("http" or "https"))
        {
            throw new InvalidDataException("ApiBaseUrl debe usar HTTP o HTTPS.");
        }
        if (!baseUri.IsLoopback && !options.AllowNonLocalApi)
        {
            throw new InvalidDataException(
                "ApiBaseUrl no es local. Cambia AllowNonLocalApi a true solo si has verificado el entorno destino."
            );
        }
        if (options.Restaurants.Count == 0)
        {
            throw new InvalidDataException("Configura al menos un local para esta simulación.");
        }
        if (
            options.Restaurants.Select(item => item.RestaurantId).Distinct().Count()
            != options.Restaurants.Count
        )
        {
            throw new InvalidDataException("Cada local debe tener un RestaurantId distinto.");
        }
        if (options.OrdersPerBatch < 1)
        {
            throw new InvalidDataException("OrdersPerBatch debe ser al menos 1.");
        }
        if (options.MaxConcurrentOrders < 1)
        {
            throw new InvalidDataException("MaxConcurrentOrders debe ser al menos 1.");
        }
        if (options.RunDurationMinutes < 1)
        {
            throw new InvalidDataException("RunDurationMinutes debe ser al menos 1.");
        }
        if (options.KitchenStepDelaySeconds is < 1 or > 30)
        {
            throw new InvalidDataException(
                "KitchenStepDelaySeconds debe estar entre 1 y 30 para que los estados KDS sean observables."
            );
        }
    }

    private static void ValidateRestaurantOptions(RestaurantOptions restaurant)
    {
        if (restaurant.RestaurantId == Guid.Empty || string.IsNullOrWhiteSpace(restaurant.Name))
        {
            throw new InvalidDataException("Cada local necesita Name y RestaurantId.");
        }
        if (string.IsNullOrWhiteSpace(restaurant.PosAccessToken))
        {
            throw new InvalidDataException(
                $"{restaurant.Name}: configura PosAccessToken con una cuenta autorizada para TPV y sus operaciones."
            );
        }
        if (string.IsNullOrWhiteSpace(restaurant.KdsAccessToken))
        {
            throw new InvalidDataException(
                $"{restaurant.Name}: configura KdsAccessToken con una cuenta autorizada para KDS."
            );
        }
        if (restaurant.OpeningFloat is < 0 or > 100000)
        {
            throw new InvalidDataException(
                $"{restaurant.Name}: OpeningFloat debe estar entre 0 y 100.000 €."
            );
        }
        ValidateRange(restaurant.GuestCount, $"{restaurant.Name}.GuestCount", 2, 10);
        if (restaurant.MaxProductsPerOrder is < 1 or > 30)
        {
            throw new InvalidDataException(
                $"{restaurant.Name}: MaxProductsPerOrder debe ser 1–30."
            );
        }
        ValidatePaymentOptions(restaurant);
    }

    private static void ValidatePaymentOptions(RestaurantOptions restaurant)
    {
        if (
            restaurant.Payment.Methods.Count < 3
            || restaurant.Payment.Methods.Any(method =>
                method.Weight <= 0 || method.Name is not ("Card" or "Cash" or "Transfer")
            )
        )
        {
            throw new InvalidDataException(
                $"{restaurant.Name}: usa métodos Card, Cash o Transfer con peso positivo."
            );
        }
        if (
            restaurant.Payment.SplitProbability is < 0 or > 1
            || restaurant.Payment.CashOverpayProbability is < 0 or > 1
            || restaurant.Payment.TipPercentages.Count == 0
            || restaurant.Payment.TipPercentages.Any(percentage => percentage <= 0)
            || restaurant.Payment.TipPercentages.Any(percentage => percentage is < 0 or > 100)
        )
        {
            throw new InvalidDataException(
                $"{restaurant.Name}: probabilidades entre 0 y 1 y propinas entre 0 y 100%."
            );
        }
        if (
            restaurant
                .Payment.Methods.Select(method => method.Name)
                .Distinct(StringComparer.Ordinal)
                .Count() != restaurant.Payment.Methods.Count
        )
        {
            throw new InvalidDataException(
                $"{restaurant.Name}: no repitas métodos de pago; usa Weight para ajustar su frecuencia."
            );
        }
    }

    private static void ValidateRange(IntRange range, string name, int min, int max)
    {
        if (range.Minimum < min || range.Maximum < range.Minimum || range.Maximum > max)
        {
            throw new InvalidDataException(
                $"{name}: intervalo inválido; usa valores entre {min} y {max} segundos."
            );
        }
    }

    internal static async Task<RestaurantRunResult> RunRestaurantAsync(
        HttpClient client,
        RestaurantOptions restaurant,
        string posToken,
        string kdsToken,
        int ordersPerBatch,
        RandomOrderScheduler orderScheduler,
        TimeSpan kitchenStepDelay,
        TimeSpan runDuration,
        SimulatorRandom random,
        SemaphoreSlim randomLock,
        CancellationToken stopCreating
    )
    {
        List<MenuItemResponse> menu = await GetAsync<List<MenuItemResponse>>(
            client,
            $"api/catalog/restaurants/{restaurant.RestaurantId}/menu",
            posToken,
            stopCreating
        );
        menu = FilterUsableMenu(menu);
        HashSet<Guid> availableProductIds = menu.Where(item => item.IsAvailable)
            .Select(item => item.ProductId)
            .ToHashSet();

        if (menu.Count == 0)
        {
            throw new InvalidOperationException(
                $"{restaurant.Name}: no hay productos disponibles en el menú."
            );
        }

        await EnsureCashRegisterOpenAsync(client, restaurant, posToken, stopCreating);
        orderScheduler.MarkReady(restaurant.RestaurantId);

        WriteLog(
            $"{restaurant.Name}: menú {menu.Count} productos; carga durante {runDuration.TotalMinutes:0} minutos; hasta {ordersPerBatch} pedidos en rotación, sujetos a la selección aleatoria global."
        );
        RestaurantRunInputs inputs = new()
        {
            Client = client,
            Restaurant = restaurant,
            PosToken = posToken,
            KdsToken = kdsToken,
            Menu = menu,
            AvailableProductIds = availableProductIds,
            OrderScheduler = orderScheduler,
            KitchenStepDelay = kitchenStepDelay,
            Random = random,
            RandomLock = randomLock,
        };
        int failedOrders = 0;
        int completedOrders = 0;
        int startedOrders = 0;
        bool drainLogged = false;
        bool canCreateOrders = true;
        bool generationFailed = false;
        Dictionary<Guid, Task<bool>> activeOrders = [];
        Stopwatch runTimer = Stopwatch.StartNew();
        while (true)
        {
            (int completed, int failed) = await CollectCompletedOrdersAsync(activeOrders);
            completedOrders += completed;
            failedOrders += failed;

            bool acceptingOrders = CanCreateMoreOrders(
                canCreateOrders,
                runTimer,
                runDuration,
                stopCreating
            );
            if (acceptingOrders && activeOrders.Count < ordersPerBatch)
            {
                List<TableInfo>? tables = await TryGetAvailableTablesAsync(inputs, activeOrders);
                if (tables is null)
                {
                    canCreateOrders = false;
                    generationFailed = true;
                }
                else
                {
                    startedOrders += ScheduleAvailableOrders(
                        inputs,
                        tables,
                        activeOrders,
                        ordersPerBatch,
                        runTimer,
                        runDuration,
                        stopCreating
                    );
                }
            }

            bool acceptingMoreOrders = CanCreateMoreOrders(
                canCreateOrders,
                runTimer,
                runDuration,
                stopCreating
            );
            if (!acceptingMoreOrders && !drainLogged)
            {
                WriteLog(
                    $"{restaurant.Name}: fin de generación; esperando {activeOrders.Count} pedidos activos."
                );
                drainLogged = true;
            }
            if (!acceptingMoreOrders && activeOrders.Count == 0)
            {
                break;
            }

            await WaitForOrderActivityAsync(activeOrders, stopCreating);
        }

        WriteLog(
            $"{restaurant.Name}: carga finalizada; pedidos iniciados {startedOrders}, completados {completedOrders}, fallidos {failedOrders}{(generationFailed ? ", generación detenida por error" : "")}."
        );
        return new RestaurantRunResult(failedOrders, generationFailed ? 1 : 0);
    }

    internal static bool CanCreateMoreOrders(
        bool canCreateOrders,
        Stopwatch runTimer,
        TimeSpan runDuration,
        CancellationToken stopCreating
    )
    {
        return canCreateOrders
            && !stopCreating.IsCancellationRequested
            && runTimer.Elapsed < runDuration;
    }

    internal static async Task<(int Completed, int Failed)> CollectCompletedOrdersAsync(
        Dictionary<Guid, Task<bool>> activeOrders
    )
    {
        int completedOrders = 0;
        int failedOrders = 0;
        foreach (
            KeyValuePair<Guid, Task<bool>> completed in activeOrders
                .Where(item => item.Value.IsCompleted)
                .ToList()
        )
        {
            if (await completed.Value)
            {
                completedOrders++;
            }
            else
            {
                failedOrders++;
            }
            activeOrders.Remove(completed.Key);
        }
        return (completedOrders, failedOrders);
    }

    private static async Task<List<TableInfo>?> TryGetAvailableTablesAsync(
        RestaurantRunInputs inputs,
        Dictionary<Guid, Task<bool>> activeOrders
    )
    {
        List<TableInfo> tables;
        try
        {
            tables = await GetAsync<List<TableInfo>>(
                inputs.Client,
                $"api/dining/restaurants/{inputs.Restaurant.RestaurantId}/tables",
                inputs.PosToken,
                CancellationToken.None
            );
        }
        catch (Exception exception)
        {
            WriteError(
                $"{inputs.Restaurant.Name}: no se pudieron consultar las mesas; se detienen los pedidos nuevos y se drenarán los activos. {exception.Message}"
            );
            return null;
        }

        return InterleaveTablesByZone(
            tables
                .Where(table =>
                    table.IsActive
                    && table.Status == "Available"
                    && !activeOrders.ContainsKey(table.Id)
                )
                .ToList(),
            inputs.Random,
            inputs.RandomLock
        );
    }

    private static int ScheduleAvailableOrders(
        RestaurantRunInputs inputs,
        IReadOnlyList<TableInfo> tables,
        Dictionary<Guid, Task<bool>> activeOrders,
        int ordersPerBatch,
        Stopwatch runTimer,
        TimeSpan runDuration,
        CancellationToken stopCreating
    )
    {
        int startedOrders = 0;
        foreach (TableInfo table in tables.Take(ordersPerBatch - activeOrders.Count))
        {
            if (stopCreating.IsCancellationRequested || runTimer.Elapsed >= runDuration)
            {
                break;
            }

            activeOrders.Add(
                table.Id,
                inputs.OrderScheduler.RunAsync(
                    inputs.Restaurant.RestaurantId,
                    () =>
                        ProcessOrderAsync(
                            inputs.Client,
                            inputs.Restaurant,
                            inputs.PosToken,
                            inputs.KdsToken,
                            inputs.Menu,
                            inputs.AvailableProductIds,
                            table,
                            inputs.KitchenStepDelay,
                            inputs.Random,
                            inputs.RandomLock
                        )
                )
            );
            startedOrders++;
            WriteLog(
                $"{inputs.Restaurant.Name}: inicia {table.Label}; pedidos activos {activeOrders.Count}/{ordersPerBatch}."
            );
        }
        return startedOrders;
    }

    internal static async Task WaitForOrderActivityAsync(
        Dictionary<Guid, Task<bool>> activeOrders,
        CancellationToken stopCreating
    )
    {
        if (stopCreating.IsCancellationRequested && activeOrders.Count > 0)
        {
            await Task.WhenAny(activeOrders.Values.Cast<Task>());
        }
        else
        {
            Task pollDelay = Task.Delay(TableAvailabilityPollInterval, stopCreating);
            if (activeOrders.Count == 0)
            {
                await pollDelay;
            }
            else
            {
                await Task.WhenAny(activeOrders.Values.Cast<Task>().Append(pollDelay));
            }
        }
    }

    private sealed class RestaurantRunInputs
    {
        public required HttpClient Client { get; init; }
        public required RestaurantOptions Restaurant { get; init; }
        public required string PosToken { get; init; }
        public required string KdsToken { get; init; }
        public required IReadOnlyList<MenuItemResponse> Menu { get; init; }
        public required IReadOnlySet<Guid> AvailableProductIds { get; init; }
        public required RandomOrderScheduler OrderScheduler { get; init; }
        public required TimeSpan KitchenStepDelay { get; init; }
        public required SimulatorRandom Random { get; init; }
        public required SemaphoreSlim RandomLock { get; init; }
    }

    internal static async Task EnsureCashRegisterOpenAsync(
        HttpClient client,
        RestaurantOptions restaurant,
        string token,
        CancellationToken ct
    )
    {
        string path = $"api/cash-register/restaurants/{restaurant.RestaurantId}/current";
        CashRegisterStatusResponse? current = await GetAsync<CashRegisterStatusResponse?>(
            client,
            path,
            token,
            ct
        );
        if (current?.Status == "Open")
        {
            WriteLog($"{restaurant.Name}: caja ya abierta; se reutiliza el turno.");
            return;
        }

        try
        {
            CashRegisterStatusResponse opened = await PostAsync<CashRegisterStatusResponse>(
                client,
                $"api/cash-register/restaurants/{restaurant.RestaurantId}/open",
                new { openingFloat = restaurant.OpeningFloat },
                token,
                ct
            );
            if (opened.Status != "Open")
            {
                throw new InvalidDataException(
                    $"{restaurant.Name}: la API no confirmó que la caja quedara abierta."
                );
            }
            WriteLog(
                $"{restaurant.Name}: caja abierta automáticamente con {restaurant.OpeningFloat:0.00} € de fondo."
            );
        }
        catch (HttpRequestException exception)
            when (exception.StatusCode == System.Net.HttpStatusCode.Conflict)
        {
            current = await GetAsync<CashRegisterStatusResponse?>(client, path, token, ct);
            if (current?.Status != "Open")
            {
                throw;
            }
            WriteLog($"{restaurant.Name}: otra operación abrió la caja; se reutiliza el turno.");
        }
    }

    internal static List<TableInfo> InterleaveTablesByZone(
        IReadOnlyList<TableInfo> tables,
        SimulatorRandom random,
        SemaphoreSlim randomLock
    )
    {
        if (tables.Count == 0)
        {
            return [];
        }

        List<List<TableInfo>> zones = Shuffle(
            tables
                .GroupBy(table => table.ZoneName, StringComparer.OrdinalIgnoreCase)
                .Select(zone => Shuffle(zone.ToList(), random, randomLock))
                .ToList(),
            random,
            randomLock
        );
        return Enumerable
            .Range(0, zones.Max(zone => zone.Count))
            .SelectMany(index =>
                zones.Where(zone => index < zone.Count).Select(zone => zone[index])
            )
            .ToList();
    }

    internal static async Task<bool> ProcessOrderAsync(
        HttpClient client,
        RestaurantOptions restaurant,
        string posToken,
        string kdsToken,
        IReadOnlyList<MenuItemResponse> menu,
        IReadOnlySet<Guid> availableProductIds,
        TableInfo table,
        TimeSpan kitchenStepDelay,
        SimulatorRandom random,
        SemaphoreSlim randomLock
    )
    {
        Guid sessionId = Guid.Empty;
        Guid orderId = Guid.Empty;
        try
        {
            SessionInfo session = await PostAsync<SessionInfo>(
                client,
                $"api/dining/tables/{table.Id}/sessions",
                new OpenSessionRequest { Source = "Pos" },
                posToken,
                CancellationToken.None
            );
            sessionId = session.Id;
            int guestCount = 1;
            if (table.RequestGuestCount)
            {
                guestCount = Next(random, randomLock, restaurant.GuestCount);
                await PutAsync<SessionInfo>(
                    client,
                    $"api/dining/sessions/{sessionId}/guests",
                    new SetGuestCountRequest(guestCount),
                    posToken,
                    CancellationToken.None
                );
            }
            List<CreateOrderLineRequest> lines = CreateRandomLines(
                restaurant,
                menu,
                availableProductIds,
                random,
                randomLock
            );
            OrderResponse order = await PostAsync<OrderResponse>(
                client,
                "api/orders",
                new
                {
                    restaurantId = restaurant.RestaurantId,
                    tableId = table.Id,
                    diningSessionId = sessionId,
                    tableLabel = table.Label,
                    serviceMode = "DineIn",
                    source = "Pos",
                    paymentTiming = "OnAccount",
                    lines,
                },
                posToken,
                CancellationToken.None
            );
            orderId = order.Id;
            order = await PostAsync<OrderResponse>(
                client,
                $"api/orders/{order.Id}/submit",
                null,
                posToken,
                CancellationToken.None
            );
            WriteLog(
                $"{restaurant.Name} · {table.Label} · pedido {order.Id.ToString("N")[..8]} · {lines.Count} platos · {order.Total:0.00} €"
            );
            await Task.Delay(kitchenStepDelay);

            order = await SimulateKitchenAsync(client, kdsToken, order, kitchenStepDelay);
            OrderResponse deliveredOrder = order.Status.Equals(
                "Delivered",
                StringComparison.OrdinalIgnoreCase
            )
                ? order
                : await PostAsync<OrderResponse>(
                    client,
                    $"api/orders/{order.Id}/deliver",
                    null,
                    posToken,
                    CancellationToken.None
                );
            if (deliveredOrder.Status != "Delivered")
            {
                throw new InvalidDataException(
                    $"El pedido {order.Id.ToString("N")[..8]} no quedó entregado antes del cobro ({deliveredOrder.Status})."
                );
            }
            await WaitForDiningBillAsync(
                client,
                sessionId,
                posToken,
                bill =>
                    bill.Orders.Any(sessionOrder =>
                        sessionOrder.OrderId == order.Id && sessionOrder.OrderStatus == "Delivered"
                    ),
                "la entrega del pedido"
            );
            await SimulatePaymentsAsync(
                client,
                restaurant,
                posToken,
                sessionId,
                order,
                guestCount,
                random,
                randomLock
            );
            await ClosePaidSessionAsync(client, sessionId, posToken);
            WriteLog(
                $"{restaurant.Name} · {table.Label} · {order.Id.ToString("N")[..8]} completado; cuenta pagada y mesa liberada."
            );
            return true;
        }
        catch (Exception exception)
        {
            await ReportOrderFailureAsync(
                client,
                restaurant,
                posToken,
                table,
                sessionId,
                orderId,
                exception
            );
            return false;
        }
    }

    private static async Task ReportOrderFailureAsync(
        HttpClient client,
        RestaurantOptions restaurant,
        string posToken,
        TableInfo table,
        Guid sessionId,
        Guid orderId,
        Exception exception
    )
    {
        string sessionStatus = "";
        if (sessionId != Guid.Empty && orderId == Guid.Empty)
        {
            try
            {
                await PostAsync<SessionInfo>(
                    client,
                    $"api/dining/sessions/{sessionId}/cancel",
                    null,
                    posToken,
                    CancellationToken.None
                );
                sessionStatus = "; sesión vacía cancelada";
            }
            catch (Exception cleanupException)
            {
                sessionStatus =
                    $"; no se pudo cancelar la sesión vacía {sessionId.ToString("N")[..8]}: {cleanupException.Message}";
            }
        }

        string order =
            orderId == Guid.Empty
                ? "sin pedido confirmado"
                : $"pedido {orderId.ToString("N")[..8]}";
        string session = "";
        if (sessionId != Guid.Empty)
        {
            session =
                orderId == Guid.Empty
                    ? sessionStatus
                    : $"; sesión {sessionId.ToString("N")[..8]} queda abierta";
        }
        WriteError($"{restaurant.Name} · {table.Label} · {order}{session}: {exception.Message}");
    }

    internal static List<CreateOrderLineRequest> CreateRandomLines(
        RestaurantOptions restaurant,
        IReadOnlyList<MenuItemResponse> menu,
        IReadOnlySet<Guid> availableProductIds,
        SimulatorRandom random,
        SemaphoreSlim randomLock
    )
    {
        List<MenuItemResponse> selected = Shuffle(menu, random, randomLock)
            .Take(
                Next(
                    random,
                    randomLock,
                    new IntRange(1, Math.Min(restaurant.MaxProductsPerOrder, menu.Count))
                )
            )
            .ToList();
        List<CreateOrderLineRequest> lines = [];
        foreach (MenuItemResponse product in selected)
        {
            List<CreateOrderExtraRequest> extras = [];
            foreach (ProductExtraGroup group in product.ExtraGroups ?? [])
            {
                List<ProductExtraOption> availableOptions = group
                    .Options.Where(option => availableProductIds.Contains(option.ProductId))
                    .ToList();
                int maximumSelections = Math.Min(group.MaxSelections, availableOptions.Count);
                int count = Next(
                    random,
                    randomLock,
                    new IntRange(group.MinSelections, maximumSelections)
                );
                if (count > availableOptions.Count)
                {
                    throw new InvalidDataException(
                        $"{product.ProductName}: el grupo de extras '{group.Name}' no tiene suficientes opciones disponibles."
                    );
                }
                foreach (
                    ProductExtraOption option in Shuffle(availableOptions, random, randomLock)
                        .Take(count)
                )
                {
                    extras.Add(
                        new CreateOrderExtraRequest
                        {
                            GroupId = group.Id,
                            ProductId = option.ProductId,
                            Quantity = 1,
                        }
                    );
                }
            }
            lines.Add(
                new CreateOrderLineRequest
                {
                    ProductId = product.ProductId,
                    Quantity = Next(random, randomLock, new IntRange(1, 3)),
                    SelectedExtras = extras,
                }
            );
        }
        return lines;
    }

    internal static async Task<OrderResponse> SimulateKitchenAsync(
        HttpClient client,
        string token,
        OrderResponse order,
        TimeSpan kitchenStepDelay
    )
    {
        foreach (
            OrderLineResponse submittedLine in order.Lines.Where(line =>
                line.Status != "Cancelled"
                && !string.IsNullOrWhiteSpace(line.PreparationStationCode)
            )
        )
        {
            order = await PrepareKitchenLineAsync(
                client,
                token,
                order,
                submittedLine.Id,
                kitchenStepDelay
            );
        }

        OrderKitchenStationResponse[] primaryDispatchStations = order
            .Stations.Where(station => station.RequiresPrimaryDispatch)
            .OrderBy(station => station.Priority)
            .ToArray();
        if (primaryDispatchStations.Length > 0)
        {
            WriteLog(
                $"{order.TableLabel} · todos los platos están listos; el KDS principal comenzará los pases."
            );
            await Task.Delay(kitchenStepDelay);
        }

        foreach (OrderKitchenStationResponse station in primaryDispatchStations)
        {
            if (!station.Status.Equals("Ready", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException(
                    $"El KDS principal no puede despachar el pase {station.Name} hasta que su estación marque listo (estado actual: {station.Status})."
                );
            }
        }

        foreach (OrderKitchenStationResponse station in primaryDispatchStations)
        {
            order = await DispatchKitchenStationAsync(
                client,
                token,
                order,
                station,
                kitchenStepDelay
            );
        }

        ValidateKitchenCompletion(order);
        return order;
    }

    private static async Task<OrderResponse> PrepareKitchenLineAsync(
        HttpClient client,
        string token,
        OrderResponse order,
        Guid lineId,
        TimeSpan kitchenStepDelay
    )
    {
        OrderLineResponse line = order.Lines.Single(item => item.Id == lineId);
        if (line.KitchenStatus.Equals("Dispatched", StringComparison.OrdinalIgnoreCase))
        {
            return order;
        }
        if (line.KitchenStatus.Equals("Pending", StringComparison.OrdinalIgnoreCase))
        {
            order = await PostAsync<OrderResponse>(
                client,
                $"api/orders/{order.Id}/lines/{line.Id}/start-preparation",
                null,
                token,
                CancellationToken.None
            );
            line = order.Lines.Single(item => item.Id == lineId);
        }
        if (line.KitchenStatus.Equals("InPreparation", StringComparison.OrdinalIgnoreCase))
        {
            WriteLog(
                $"{order.TableLabel} · {line.ProductName} · {line.PreparationStationName} en preparación."
            );
            await Task.Delay(kitchenStepDelay);
            order = await PostAsync<OrderResponse>(
                client,
                $"api/orders/{order.Id}/lines/{line.Id}/ready",
                null,
                token,
                CancellationToken.None
            );
        }
        OrderLineResponse updatedLine = order.Lines.Single(item => item.Id == lineId);
        string readyMessage = updatedLine.KitchenStatus.Equals(
            "Dispatched",
            StringComparison.OrdinalIgnoreCase
        )
            ? "listo y despachado automáticamente"
            : "listo; espera el despacho del KDS principal";
        WriteLog(
            $"{order.TableLabel} · {updatedLine.ProductName} · {readyMessage} ({updatedLine.PreparationStationName})."
        );
        return order;
    }

    private static async Task<OrderResponse> DispatchKitchenStationAsync(
        HttpClient client,
        string token,
        OrderResponse order,
        OrderKitchenStationResponse station,
        TimeSpan kitchenStepDelay
    )
    {
        await Task.Delay(kitchenStepDelay);
        Guid[] lineIds = order
            .Lines.Where(line =>
                line.Status != "Cancelled"
                && line.PreparationStationCode.Equals(
                    station.Code,
                    StringComparison.OrdinalIgnoreCase
                )
            )
            .Select(line => line.Id)
            .ToArray();
        foreach (Guid lineId in lineIds)
        {
            order = await PostAsync<OrderResponse>(
                client,
                $"api/orders/{order.Id}/lines/{lineId}/dispatch",
                null,
                token,
                CancellationToken.None
            );
        }

        if (
            !order
                .Stations.Single(item =>
                    item.Code.Equals(station.Code, StringComparison.OrdinalIgnoreCase)
                )
                .Status.Equals("Dispatched", StringComparison.OrdinalIgnoreCase)
        )
        {
            throw new InvalidDataException(
                $"El KDS principal no confirmó el despacho del pase {station.Name}."
            );
        }
        WriteLog($"{order.TableLabel} · pase {station.Name} despachado por KDS principal.");
        return order;
    }

    private static void ValidateKitchenCompletion(OrderResponse order)
    {
        if (order.Status is not ("Ready" or "Delivered"))
        {
            throw new InvalidDataException(
                $"El pedido {order.Id.ToString("N")[..8]} terminó el flujo KDS en estado {order.Status}, no Ready ni Delivered."
            );
        }
        OrderLineResponse[] undispatchedLines = order
            .Lines.Where(line =>
                line.Status != "Cancelled"
                && !line.KitchenStatus.Equals("Dispatched", StringComparison.OrdinalIgnoreCase)
            )
            .ToArray();
        if (undispatchedLines.Length > 0)
        {
            string lines = string.Join(", ", undispatchedLines.Select(line => line.ProductName));
            throw new InvalidDataException(
                $"El pedido {order.Id.ToString("N")[..8]} no despachó todos los platos: {lines}."
            );
        }
    }

    private static async Task SimulatePaymentsAsync(
        HttpClient client,
        RestaurantOptions restaurant,
        string token,
        Guid sessionId,
        OrderResponse order,
        int guestCount,
        SimulatorRandom random,
        SemaphoreSlim randomLock
    )
    {
        bool splitAcrossGuests =
            guestCount > 1 && NextDouble(random, randomLock) < restaurant.Payment.SplitProbability;
        List<PaymentPart> parts = splitAcrossGuests
            ? CreateGuestPaymentParts(
                restaurant.Payment,
                order.Total,
                guestCount,
                random,
                randomLock
            )
            : CreatePaymentParts(restaurant.Payment, order.Total, random, randomLock);
        decimal totalTip = Math.Max(
            0.01m,
            RoundMoney(
                order.Total * Pick(restaurant.Payment.TipPercentages, random, randomLock) / 100m
            )
        );
        List<decimal> tips = AllocateMoney(totalTip, parts.Select(part => part.Amount).ToList());
        for (int index = 0; index < parts.Count; index++)
        {
            PaymentPart part = parts[index];
            decimal tip = tips[index];
            decimal amountDue = part.Amount + tip;
            decimal? amountReceived =
                part.Method == "Cash"
                && NextDouble(random, randomLock) < restaurant.Payment.CashOverpayProbability
                    ? decimal.Ceiling(amountDue)
                    : amountDue;
            BillResponse bill = await PostAsync<BillResponse>(
                client,
                $"api/dining/sessions/{sessionId}/checkout",
                new CheckoutSessionRequest
                {
                    IdempotencyKey = Guid.NewGuid(),
                    Method = part.Method,
                    Allocations = [new CheckoutOrderAllocation(order.Id, part.Amount)],
                    TipAmount = tip,
                    AmountReceived = amountReceived,
                    ExternalReference =
                        $"SIM-{restaurant.Name}-{order.Id.ToString("N")[..8]}-{index + 1}"
                        + (part.GuestNumber > 0 ? $"-C{part.GuestNumber}" : ""),
                },
                token,
                CancellationToken.None
            );
            if (index == parts.Count - 1 && bill.Outstanding != 0)
            {
                throw new InvalidDataException(
                    $"El último pago dejó {bill.Outstanding:0.00} € pendientes."
                );
            }
            WriteLog(
                $"{restaurant.Name} · pago {index + 1}/{parts.Count}{(part.GuestNumber > 0 ? $" · comensal {part.GuestNumber}" : "")}: {part.Method} {part.Amount:0.00} € + {tip:0.00} € propina{(amountReceived > amountDue ? $" · cambio {amountReceived - amountDue:0.00} €" : "")}"
            );
        }
    }

    private static async Task<BillResponse> WaitForDiningBillAsync(
        HttpClient client,
        Guid sessionId,
        string token,
        Func<BillResponse, bool> condition,
        string description
    )
    {
        Stopwatch timeout = Stopwatch.StartNew();
        while (true)
        {
            BillResponse bill = await GetAsync<BillResponse>(
                client,
                $"api/dining/sessions/{sessionId}/bill",
                token,
                CancellationToken.None
            );
            if (condition(bill))
            {
                return bill;
            }
            if (timeout.Elapsed >= DiningProjectionTimeout)
            {
                string orders = string.Join(
                    "; ",
                    bill.Orders.Select(order =>
                        $"{order.OrderId}: estado={order.OrderStatus}, pago={order.PaymentStatus}, abonado={order.AmountPaid:0.00}/{order.Amount:0.00} €"
                    )
                );
                throw new TimeoutException(
                    $"Se agotaron 2 minutos esperando {description}. "
                        + $"Pendiente={bill.Outstanding:0.00} €, CanRelease={bill.CanRelease}. Pedidos: {orders}"
                );
            }
            await Task.Delay(DiningProjectionPollInterval);
        }
    }

    internal static bool IsReadyToClose(BillResponse bill) =>
        bill.Outstanding == 0
        && bill.CanRelease
        && bill.Orders.Count > 0
        && bill.Orders.All(order =>
            order.OrderStatus.Equals("Cancelled", StringComparison.OrdinalIgnoreCase)
            || order.PaymentStatus.Equals("Paid", StringComparison.OrdinalIgnoreCase)
        );

    internal static async Task ClosePaidSessionAsync(
        HttpClient client,
        Guid sessionId,
        string token,
        TimeSpan? retryInterval = null,
        TimeSpan? timeout = null
    )
    {
        TimeSpan interval = retryInterval ?? DiningProjectionPollInterval;
        TimeSpan maximumWait = timeout ?? DiningProjectionTimeout;
        Stopwatch elapsed = Stopwatch.StartNew();
        BillResponse? lastBill = null;
        string billPath = $"api/dining/sessions/{sessionId}/bill";

        while (elapsed.Elapsed < maximumWait)
        {
            lastBill = await GetAsync<BillResponse>(
                client,
                billPath,
                token,
                CancellationToken.None
            );
            if (!IsReadyToClose(lastBill))
            {
                await Task.Delay(interval);
                continue;
            }

            try
            {
                await PostAsync<SessionInfo>(
                    client,
                    $"api/dining/sessions/{sessionId}/close",
                    null,
                    token,
                    CancellationToken.None
                );
                return;
            }
            catch (HttpRequestException exception)
                when (exception.StatusCode == System.Net.HttpStatusCode.Conflict
                    && exception.Message.Contains("unpaidOrderIds", StringComparison.Ordinal)
                )
            {
                await Task.Delay(interval);
            }
        }

        lastBill ??= await GetAsync<BillResponse>(client, billPath, token, CancellationToken.None);
        string orders = string.Join(
            "; ",
            lastBill.Orders.Select(order =>
                $"{order.OrderId}: estado={order.OrderStatus}, pago={order.PaymentStatus}, abonado={order.AmountPaid:0.00}/{order.Amount:0.00} €"
            )
        );
        throw new TimeoutException(
            $"Se agotó el tiempo esperando que Dining confirmara el cierre de la mesa. "
                + $"Pendiente={lastBill.Outstanding:0.00} €, CanRelease={lastBill.CanRelease}. Pedidos: {orders}"
        );
    }

    internal static List<PaymentPart> CreateGuestPaymentParts(
        PaymentOptions options,
        decimal amount,
        int guestCount,
        SimulatorRandom random,
        SemaphoreSlim randomLock
    )
    {
        List<decimal> shares = Allocate(amount, Enumerable.Repeat(1m, guestCount).ToList());
        List<PaymentMethodOptions> methods = options.Methods.ToList();
        List<string> selectedMethods = [];
        while (selectedMethods.Count < 3)
        {
            selectedMethods.Add(PickWeightedMethods(methods, 1, random, randomLock)[0].Name);
            methods.RemoveAll(method => method.Name == selectedMethods[^1]);
        }

        List<PaymentPart> parts = [];
        if (guestCount >= 3)
        {
            List<string> shuffledMethods = Shuffle(selectedMethods, random, randomLock);
            for (int index = 0; index < shares.Count; index++)
            {
                string method =
                    index < shuffledMethods.Count
                        ? shuffledMethods[index]
                        : PickWeightedMethods(options.Methods, 1, random, randomLock)[0].Name;
                parts.Add(new PaymentPart(method, shares[index], index + 1));
            }
        }
        else
        {
            decimal firstGuestFirstPart = RoundMoney(shares[0] / 2m);
            decimal firstGuestSecondPart = shares[0] - firstGuestFirstPart;
            if (firstGuestFirstPart <= 0 || firstGuestSecondPart <= 0)
            {
                throw new InvalidDataException(
                    "El importe del pedido es demasiado bajo para repartirlo entre tres medios de pago."
                );
            }
            List<string> shuffledMethods = Shuffle(selectedMethods, random, randomLock);
            parts.Add(new PaymentPart(shuffledMethods[0], firstGuestFirstPart, 1));
            parts.Add(new PaymentPart(shuffledMethods[1], firstGuestSecondPart, 1));
            parts.Add(new PaymentPart(shuffledMethods[2], shares[1], 2));
        }
        return parts;
    }

    internal static List<PaymentPart> CreatePaymentParts(
        PaymentOptions options,
        decimal amount,
        SimulatorRandom random,
        SemaphoreSlim? randomLock = null
    )
    {
        if (amount <= 0 || amount != decimal.Round(amount, 2))
        {
            throw new ArgumentOutOfRangeException(nameof(amount));
        }
        bool split =
            options.Methods.Count > 1 && NextDouble(random, randomLock) < options.SplitProbability;
        int count = split ? Next(random, randomLock, new IntRange(2, options.Methods.Count)) : 1;
        List<PaymentMethodOptions> methods = PickWeightedMethods(
            options.Methods,
            count,
            random,
            randomLock
        );
        if (count == 1)
        {
            return [new PaymentPart(methods[0].Name, amount)];
        }

        int[] weights = methods
            .Select(_ => Next(random, randomLock, new IntRange(1, 10)))
            .ToArray();
        int totalWeight = weights.Sum();
        List<decimal> amounts = [];
        decimal allocated = 0;
        for (int index = 0; index < methods.Count; index++)
        {
            decimal part =
                index == methods.Count - 1
                    ? amount - allocated
                    : RoundMoney(amount * weights[index] / totalWeight);
            amounts.Add(part);
            allocated += part;
        }
        for (int index = amounts.Count - 1; index >= 0; index--)
        {
            if (amounts[index] <= 0)
            {
                amounts.RemoveAt(index);
                methods.RemoveAt(index);
            }
        }
        return methods
            .Select((method, index) => new PaymentPart(method.Name, amounts[index]))
            .ToList();
    }

    private static List<PaymentMethodOptions> PickWeightedMethods(
        IReadOnlyList<PaymentMethodOptions> methods,
        int count,
        SimulatorRandom random,
        SemaphoreSlim? randomLock
    )
    {
        List<PaymentMethodOptions> candidates = methods.ToList();
        List<PaymentMethodOptions> selected = [];
        while (selected.Count < count)
        {
            int draw = Next(
                random,
                randomLock,
                new IntRange(1, candidates.Sum(method => method.Weight))
            );
            foreach (PaymentMethodOptions candidate in candidates)
            {
                draw -= candidate.Weight;
                if (draw <= 0)
                {
                    selected.Add(candidate);
                    candidates.Remove(candidate);
                    break;
                }
            }
        }
        return selected;
    }

    internal static List<decimal> Allocate(decimal total, IReadOnlyList<decimal> weights)
    {
        if (weights.Count == 0 || weights.Any(weight => weight <= 0))
        {
            throw new ArgumentException(
                "Se necesitan importes positivos para repartir la propina.",
                nameof(weights)
            );
        }
        decimal weightTotal = weights.Sum();
        List<decimal> result = [];
        decimal allocated = 0;
        for (int index = 0; index < weights.Count; index++)
        {
            decimal value =
                index == weights.Count - 1
                    ? total - allocated
                    : RoundMoney(total * weights[index] / weightTotal);
            result.Add(value);
            allocated += value;
        }
        return result;
    }

    internal static List<decimal> AllocateMoney(decimal total, IReadOnlyList<decimal> weights)
    {
        if (total < 0 || decimal.Round(total, 2) != total)
        {
            throw new ArgumentOutOfRangeException(nameof(total));
        }
        if (weights.Count == 0 || weights.Any(weight => weight <= 0))
        {
            throw new ArgumentException(
                "Se necesitan importes positivos para repartir la propina.",
                nameof(weights)
            );
        }

        decimal totalWeight = weights.Sum();
        long totalCents = decimal.ToInt64(total * 100m);
        List<long> allocatedCents = [];
        List<(int Index, decimal Remainder)> remainders = [];
        long allocatedTotal = 0;
        for (int index = 0; index < weights.Count; index++)
        {
            decimal exactCents = totalCents * weights[index] / totalWeight;
            long wholeCents = decimal.ToInt64(decimal.Floor(exactCents));
            allocatedCents.Add(wholeCents);
            allocatedTotal += wholeCents;
            remainders.Add((index, exactCents - wholeCents));
        }

        long remainingCents = totalCents - allocatedTotal;
        foreach (
            var remainder in remainders
                .OrderByDescending(item => item.Remainder)
                .Take((int)remainingCents)
        )
        {
            allocatedCents[remainder.Index]++;
        }

        return allocatedCents.Select(cents => cents / 100m).ToList();
    }

    private static async Task<T> GetAsync<T>(
        HttpClient client,
        string path,
        string token,
        CancellationToken ct
    )
    {
        using HttpRequestMessage request = new(HttpMethod.Get, path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using HttpResponseMessage response = await client.SendAsync(request, ct);
        return await ReadResponse<T>(response);
    }

    private static async Task<T> PostAsync<T>(
        HttpClient client,
        string path,
        object? body,
        string token,
        CancellationToken ct
    )
    {
        using HttpRequestMessage request = new(HttpMethod.Post, path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        if (body is not null)
        {
            request.Content = JsonContent.Create(body, options: JsonOptions);
        }
        using HttpResponseMessage response = await client.SendAsync(request, ct);
        return await ReadResponse<T>(response);
    }

    private static async Task<T> PutAsync<T>(
        HttpClient client,
        string path,
        object body,
        string token,
        CancellationToken ct
    )
    {
        using HttpRequestMessage request = new(HttpMethod.Put, path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Content = JsonContent.Create(body, options: JsonOptions);
        using HttpResponseMessage response = await client.SendAsync(request, ct);
        return await ReadResponse<T>(response);
    }

    private static async Task<T> ReadResponse<T>(HttpResponseMessage response)
    {
        if (!response.IsSuccessStatusCode)
        {
            string body = await response.Content.ReadAsStringAsync();
            throw new HttpRequestException(
                $"HTTP {(int)response.StatusCode} {response.ReasonPhrase}: {body}",
                null,
                response.StatusCode
            );
        }
        if (response.StatusCode == System.Net.HttpStatusCode.NoContent)
        {
            return default!;
        }
        return await response.Content.ReadFromJsonAsync<T>(JsonOptions)
            ?? throw new InvalidDataException("El servicio respondió sin contenido JSON.");
    }

    private static int Next(SimulatorRandom random, SemaphoreSlim? randomLock, IntRange range)
    {
        if (randomLock is null)
        {
            return random.NextInteger(range.Minimum, range.Maximum + 1);
        }
        randomLock.Wait();
        try
        {
            return random.NextInteger(range.Minimum, range.Maximum + 1);
        }
        finally
        {
            randomLock.Release();
        }
    }

    private static double NextDouble(SimulatorRandom random, SemaphoreSlim? randomLock)
    {
        if (randomLock is null)
        {
            return random.NextFraction();
        }
        randomLock.Wait();
        try
        {
            return random.NextFraction();
        }
        finally
        {
            randomLock.Release();
        }
    }

    private static int Pick(List<int> items, SimulatorRandom random, SemaphoreSlim? randomLock)
    {
        return items[Next(random, randomLock, new IntRange(0, items.Count - 1))];
    }

    private static List<T> Shuffle<T>(
        IReadOnlyList<T> items,
        SimulatorRandom random,
        SemaphoreSlim? randomLock
    )
    {
        List<T> result = items.ToList();
        for (int i = result.Count - 1; i > 0; i--)
        {
            int j = Next(random, randomLock, new IntRange(0, i));
            (result[i], result[j]) = (result[j], result[i]);
        }
        return result;
    }

    private static void WriteLog(string message) =>
        Console.WriteLine($"[{DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss.fff zzz}] {message}");

    private static void WriteError(string message) =>
        Console.Error.WriteLine($"[{DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss.fff zzz}] {message}");

    private static decimal RoundMoney(decimal value) =>
        decimal.Round(value, 2, MidpointRounding.AwayFromZero);

    internal sealed class SimulatorRandom(int? seed)
    {
        private readonly object gate = new();
        private ulong state = seed is int value ? unchecked((ulong)(long)value) : 0;

        public int NextInteger(int minimum, int maximum)
        {
            lock (gate)
            {
                if (minimum >= maximum)
                {
                    return minimum;
                }

                if (seed is null)
                {
                    return RandomNumberGenerator.GetInt32(minimum, maximum);
                }

                ulong range = (ulong)((long)maximum - minimum);
                return (int)(minimum + (long)(NextSeededUInt64() % range));
            }
        }

        public double NextFraction()
        {
            lock (gate)
            {
                if (seed is null)
                {
                    return RandomNumberGenerator.GetInt32(0, 1 << 24) / (double)(1 << 24);
                }

                return (NextSeededUInt64() >> 11) * (1.0 / (1UL << 53));
            }
        }

        private ulong NextSeededUInt64()
        {
            state += 0x9E3779B97F4A7C15;
            ulong value = state;
            value = (value ^ (value >> 30)) * 0xBF58476D1CE4E5B9;
            value = (value ^ (value >> 27)) * 0x94D049BB133111EB;
            return value ^ (value >> 31);
        }
    }

    internal sealed class RandomOrderScheduler(
        int maxConcurrentOrders,
        IEnumerable<Guid> restaurantIds,
        SimulatorRandom random
    )
    {
        private readonly object gate = new();
        private readonly Dictionary<Guid, Queue<PendingOrder>> waitingOrders = [];
        private readonly HashSet<Guid> restaurantsNotReady = restaurantIds.ToHashSet();
        private int activeOrders;

        public void MarkReady(Guid restaurantId)
        {
            lock (gate)
            {
                restaurantsNotReady.Remove(restaurantId);
                StartAvailableOrders();
            }
        }

        public Task<bool> RunAsync(Guid restaurantId, Func<Task<bool>> processOrder)
        {
            TaskCompletionSource<bool> completion = new(
                TaskCreationOptions.RunContinuationsAsynchronously
            );
            lock (gate)
            {
                if (!waitingOrders.TryGetValue(restaurantId, out Queue<PendingOrder>? queue))
                {
                    queue = new Queue<PendingOrder>();
                    waitingOrders.Add(restaurantId, queue);
                }
                queue.Enqueue(new PendingOrder(processOrder, completion));
                StartAvailableOrders();
            }
            return completion.Task;
        }

        private void StartAvailableOrders()
        {
            while (
                restaurantsNotReady.Count == 0
                && activeOrders < maxConcurrentOrders
                && waitingOrders.Count > 0
            )
            {
                KeyValuePair<Guid, Queue<PendingOrder>>[] eligibleRestaurants = waitingOrders
                    .Where(item => item.Value.Count > 0)
                    .ToArray();
                if (eligibleRestaurants.Length == 0)
                {
                    return;
                }

                int selectedIndex = random.NextInteger(0, eligibleRestaurants.Length);
                KeyValuePair<Guid, Queue<PendingOrder>> selected = eligibleRestaurants[
                    selectedIndex
                ];
                PendingOrder order = selected.Value.Dequeue();
                if (selected.Value.Count == 0)
                {
                    waitingOrders.Remove(selected.Key);
                }

                activeOrders++;
                _ = ExecuteOrderAsync(order);
            }
        }

        private async Task ExecuteOrderAsync(PendingOrder order)
        {
            try
            {
                order.Completion.TrySetResult(await order.ProcessOrder());
            }
            catch (Exception exception)
            {
                order.Completion.TrySetException(exception);
            }
            finally
            {
                lock (gate)
                {
                    activeOrders--;
                    StartAvailableOrders();
                }
            }
        }

        private sealed record PendingOrder(
            Func<Task<bool>> ProcessOrder,
            TaskCompletionSource<bool> Completion
        );
    }
}

internal sealed class SimulatorOptions
{
    public string ApiBaseUrl { get; init; } = "http://localhost:5001";
    public bool AllowNonLocalApi { get; init; }
    public int? RandomSeed { get; init; }
    public int OrdersPerBatch { get; init; } = 20;
    public int MaxConcurrentOrders { get; init; } = 10;
    public int KitchenStepDelaySeconds { get; init; } = 2;
    public int RunDurationMinutes { get; init; } = 10;
    public List<RestaurantOptions> Restaurants { get; init; } = [];
}

internal sealed class RestaurantOptions
{
    public string Name { get; init; } = "";
    public Guid RestaurantId { get; init; }
    public string PosAccessToken { get; init; } = "";
    public string KdsAccessToken { get; init; } = "";
    public decimal OpeningFloat { get; init; }
    public IntRange GuestCount { get; init; } = new(2, 10);
    public int MaxProductsPerOrder { get; init; } = 4;
    public PaymentOptions Payment { get; init; } = new();
}

internal sealed class PaymentOptions
{
    public double SplitProbability { get; init; } = 0.35;
    public double CashOverpayProbability { get; init; } = 0.4;
    public List<int> TipPercentages { get; init; } = [5, 8, 10, 15];
    public List<PaymentMethodOptions> Methods { get; init; } =
    [
        new() { Name = "Card", Weight = 50 },
        new() { Name = "Cash", Weight = 35 },
        new() { Name = "Transfer", Weight = 15 },
    ];
}

internal sealed class PaymentMethodOptions
{
    public string Name { get; init; } = "Card";
    public int Weight { get; init; } = 1;
}

internal sealed record IntRange(int Minimum, int Maximum);

internal sealed record PaymentPart(string Method, decimal Amount, int GuestNumber = 0);

internal sealed record RestaurantRunResult(int FailedOrders, int ExecutionErrors);

internal sealed record TableInfo(
    Guid Id,
    string Label,
    string Status,
    bool IsActive,
    Guid? ActiveSessionId,
    bool RequestGuestCount,
    string ZoneName
);

internal sealed record SessionInfo(Guid Id, Guid RestaurantId, Guid TableId, string Status);

internal sealed record CashRegisterStatusResponse(string Status);

internal sealed record BillResponse(
    Guid Id,
    decimal Total,
    decimal TipTotal,
    decimal Outstanding,
    bool CanRelease,
    IReadOnlyList<BillOrderInfo> Orders
);

internal sealed record BillOrderInfo(
    Guid OrderId,
    string OrderStatus,
    string PaymentStatus,
    decimal Amount,
    decimal AmountPaid,
    decimal TipPaid,
    DateTime AddedAtUtc
);
