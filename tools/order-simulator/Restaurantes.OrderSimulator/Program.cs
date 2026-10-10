using System.Diagnostics;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Restaurantes.Catalog.Contracts.Responses;
using Restaurantes.Dining.Contracts.Requests;
using Restaurantes.Orders.Contracts.Requests;
using Restaurantes.Orders.Contracts.Responses;

namespace Restaurantes.OrderSimulator;

internal static class Program
{
    private static readonly TimeSpan DiningProjectionPollInterval = TimeSpan.FromSeconds(2);
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
            Dictionary<string, string> tokens = ReadTokens(options.Restaurants);

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

            Random sharedRandom = options.RandomSeed is int seed ? new Random(seed) : Random.Shared;
            SemaphoreSlim randomLock = new(1, 1);
            Task<Exception?>[] preflights = options
                .Restaurants.Select(restaurant =>
                    PreflightRestaurantAsync(
                        client,
                        restaurant,
                        tokens[$"{restaurant.RestaurantId}:Pos"],
                        tokens[$"{restaurant.RestaurantId}:Kds"]
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
                $"{string.Join(" y ", options.Restaurants.Select(restaurant => restaurant.Name))} superaron la validación; los dos locales arrancan en paralelo."
            );
            List<Task<RestaurantRunResult>> simulations = options
                .Restaurants.Select(restaurant =>
                    RunRestaurantSafelyAsync(
                        client,
                        restaurant,
                        tokens[$"{restaurant.RestaurantId}:Pos"],
                        tokens[$"{restaurant.RestaurantId}:Kds"],
                        options.OrdersPerBatch,
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

    private static async Task<Exception?> PreflightRestaurantAsync(
        HttpClient client,
        RestaurantOptions restaurant,
        string posToken,
        string kdsToken
    )
    {
        string step = "menú TPV";
        try
        {
            _ = await GetAsync<List<MenuItemResponse>>(
                client,
                $"api/catalog/restaurants/{restaurant.RestaurantId}/menu",
                posToken,
                CancellationToken.None
            );
            step = "disponibilidad del menú TPV";
            _ = await GetAsync<List<MenuItemResponse>>(
                client,
                $"api/catalog/restaurants/{restaurant.RestaurantId}/menu/configuration",
                posToken,
                CancellationToken.None
            );
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

    private static async Task<RestaurantRunResult> RunRestaurantSafelyAsync(
        HttpClient client,
        RestaurantOptions restaurant,
        string posToken,
        string kdsToken,
        int ordersPerBatch,
        TimeSpan runDuration,
        Random random,
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
                runDuration,
                random,
                randomLock,
                stopCreating
            );
        }
        catch (Exception exception)
        {
            WriteError(
                $"{restaurant.Name}: la ejecución se detuvo por un error: {exception.Message}"
            );
            return new RestaurantRunResult(0, 1);
        }
    }

    private static string ReadConfigPath(string[] args)
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

    private static void Validate(SimulatorOptions options)
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
        if (options.Restaurants.Count != 2)
        {
            throw new InvalidDataException(
                "Configura exactamente dos locales para esta simulación."
            );
        }
        if (options.Restaurants.Select(item => item.RestaurantId).Distinct().Count() != 2)
        {
            throw new InvalidDataException("Los dos locales deben tener RestaurantId distintos.");
        }
        if (options.OrdersPerBatch < 1)
        {
            throw new InvalidDataException("OrdersPerBatch debe ser al menos 1.");
        }
        if (options.RunDurationMinutes < 1)
        {
            throw new InvalidDataException("RunDurationMinutes debe ser al menos 1.");
        }

        foreach (RestaurantOptions restaurant in options.Restaurants)
        {
            if (restaurant.RestaurantId == Guid.Empty || string.IsNullOrWhiteSpace(restaurant.Name))
            {
                throw new InvalidDataException("Cada local necesita Name y RestaurantId.");
            }
            if (restaurant.OpeningFloat is < 0 or > 100000)
            {
                throw new InvalidDataException(
                    $"{restaurant.Name}: OpeningFloat debe estar entre 0 y 100.000 €."
                );
            }
            if (
                string.IsNullOrWhiteSpace(restaurant.PosAccessToken)
                || string.IsNullOrWhiteSpace(restaurant.KdsAccessToken)
            )
            {
                throw new InvalidDataException(
                    $"{restaurant.Name}: configura PosAccessToken y KdsAccessToken en el JSON."
                );
            }
            ValidateRange(restaurant.GuestCount, $"{restaurant.Name}.GuestCount", 2, 10);
            if (restaurant.MaxProductsPerOrder is < 1 or > 30)
            {
                throw new InvalidDataException(
                    $"{restaurant.Name}: MaxProductsPerOrder debe ser 1–30."
                );
            }
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

    private static Dictionary<string, string> ReadTokens(
        IReadOnlyList<RestaurantOptions> restaurants
    )
    {
        Dictionary<string, string> tokens = new(StringComparer.OrdinalIgnoreCase);
        foreach (RestaurantOptions restaurant in restaurants)
        {
            AddConfiguredToken(
                tokens,
                $"{restaurant.RestaurantId}:Pos",
                restaurant.PosAccessToken,
                $"{restaurant.Name}.PosAccessToken"
            );
            AddConfiguredToken(
                tokens,
                $"{restaurant.RestaurantId}:Kds",
                restaurant.KdsAccessToken,
                $"{restaurant.Name}.KdsAccessToken"
            );
        }
        return tokens;
    }

    private static void AddConfiguredToken(
        Dictionary<string, string> tokens,
        string key,
        string configuredToken,
        string settingName
    )
    {
        if (string.IsNullOrWhiteSpace(configuredToken))
        {
            throw new InvalidDataException(
                $"Falta {settingName} en el archivo JSON de configuración."
            );
        }
        tokens[key] = configuredToken.Trim();
    }

    private static async Task<RestaurantRunResult> RunRestaurantAsync(
        HttpClient client,
        RestaurantOptions restaurant,
        string posToken,
        string kdsToken,
        int ordersPerBatch,
        TimeSpan runDuration,
        Random random,
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
        List<MenuItemResponse> configuredMenu = await GetAsync<List<MenuItemResponse>>(
            client,
            $"api/catalog/restaurants/{restaurant.RestaurantId}/menu/configuration",
            posToken,
            stopCreating
        );
        HashSet<Guid> availableProductIds = configuredMenu
            .Where(item => item.IsAvailable)
            .Select(item => item.ProductId)
            .ToHashSet();
        menu = menu.Where(item =>
                item.IsAvailable
                && availableProductIds.Contains(item.ProductId)
                && !item.CategoryName.Equals("Extras", StringComparison.OrdinalIgnoreCase)
                && (item.ExtraGroups ?? []).All(group =>
                    group.Options.Count(option => availableProductIds.Contains(option.ProductId))
                    >= group.MinSelections
                )
            )
            .ToList();

        if (menu.Count == 0)
        {
            throw new InvalidOperationException(
                $"{restaurant.Name}: no hay productos disponibles en el menú."
            );
        }

        await EnsureCashRegisterOpenAsync(client, restaurant, posToken, stopCreating);

        WriteLog(
            $"{restaurant.Name}: menú {menu.Count} productos; carga durante {runDuration.TotalMinutes:0} minutos; hasta {ordersPerBatch} pedidos simultáneos."
        );
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

            bool acceptingOrders =
                canCreateOrders
                && !stopCreating.IsCancellationRequested
                && runTimer.Elapsed < runDuration;
            if (acceptingOrders && activeOrders.Count < ordersPerBatch)
            {
                List<TableInfo> tables = [];
                try
                {
                    tables = await GetAsync<List<TableInfo>>(
                        client,
                        $"api/dining/restaurants/{restaurant.RestaurantId}/tables",
                        posToken,
                        CancellationToken.None
                    );
                }
                catch (Exception exception)
                {
                    canCreateOrders = false;
                    generationFailed = true;
                    WriteError(
                        $"{restaurant.Name}: no se pudieron consultar las mesas; se detienen los pedidos nuevos y se drenarán los activos. {exception.Message}"
                    );
                }

                if (canCreateOrders)
                {
                    tables = InterleaveTablesByZone(
                        tables
                            .Where(table =>
                                table.IsActive
                                && table.Status == "Available"
                                && !activeOrders.ContainsKey(table.Id)
                            )
                            .ToList(),
                        random,
                        randomLock
                    );
                }

                foreach (TableInfo table in tables.Take(ordersPerBatch - activeOrders.Count))
                {
                    if (stopCreating.IsCancellationRequested || runTimer.Elapsed >= runDuration)
                    {
                        break;
                    }

                    activeOrders.Add(
                        table.Id,
                        ProcessOrderAsync(
                            client,
                            restaurant,
                            posToken,
                            kdsToken,
                            menu,
                            availableProductIds,
                            table,
                            random,
                            randomLock
                        )
                    );
                    startedOrders++;
                    WriteLog(
                        $"{restaurant.Name}: inicia {table.Label}; pedidos activos {activeOrders.Count}/{ordersPerBatch}."
                    );
                }
            }

            bool acceptingMoreOrders =
                canCreateOrders
                && !stopCreating.IsCancellationRequested
                && runTimer.Elapsed < runDuration;
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

            Task pollDelay = Task.Delay(TableAvailabilityPollInterval);
            if (activeOrders.Count == 0)
            {
                await pollDelay;
            }
            else
            {
                await Task.WhenAny(activeOrders.Values.Cast<Task>().Append(pollDelay));
            }
        }

        WriteLog(
            $"{restaurant.Name}: carga finalizada; pedidos iniciados {startedOrders}, completados {completedOrders}, fallidos {failedOrders}{(generationFailed ? ", generación detenida por error" : "")}."
        );
        return new RestaurantRunResult(failedOrders, generationFailed ? 1 : 0);
    }

    private static async Task EnsureCashRegisterOpenAsync(
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

    private static List<TableInfo> InterleaveTablesByZone(
        IReadOnlyList<TableInfo> tables,
        Random random,
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

    private static async Task<bool> ProcessOrderAsync(
        HttpClient client,
        RestaurantOptions restaurant,
        string posToken,
        string kdsToken,
        IReadOnlyList<MenuItemResponse> menu,
        IReadOnlySet<Guid> availableProductIds,
        TableInfo table,
        Random random,
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

            order = await SimulateKitchenAsync(client, kdsToken, order);
            OrderResponse deliveredOrder = await PostAsync<OrderResponse>(
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
                    )
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
            await WaitForDiningBillAsync(
                client,
                sessionId,
                posToken,
                bill => bill.Outstanding == 0 && bill.CanRelease
            );
            await PostAsync<SessionInfo>(
                client,
                $"api/dining/sessions/{sessionId}/close",
                null,
                posToken,
                CancellationToken.None
            );
            WriteLog(
                $"{restaurant.Name} · {table.Label} · {order.Id.ToString("N")[..8]} completado; cuenta pagada y mesa liberada."
            );
            return true;
        }
        catch (Exception exception)
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
            string session =
                sessionId == Guid.Empty ? ""
                : orderId == Guid.Empty ? sessionStatus
                : $"; sesión {sessionId.ToString("N")[..8]} queda abierta";
            WriteError(
                $"{restaurant.Name} · {table.Label} · {order}{session}: {exception.Message}"
            );
            return false;
        }
    }

    private static List<CreateOrderLineRequest> CreateRandomLines(
        RestaurantOptions restaurant,
        IReadOnlyList<MenuItemResponse> menu,
        IReadOnlySet<Guid> availableProductIds,
        Random random,
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

    private static async Task<OrderResponse> SimulateKitchenAsync(
        HttpClient client,
        string token,
        OrderResponse order
    )
    {
        foreach (OrderLineResponse line in order.Lines.Where(line => line.Status != "Cancelled"))
        {
            order = await PostAsync<OrderResponse>(
                client,
                $"api/orders/{order.Id}/lines/{line.Id}/start-preparation",
                null,
                token,
                CancellationToken.None
            );
            order = await PostAsync<OrderResponse>(
                client,
                $"api/orders/{order.Id}/lines/{line.Id}/ready",
                null,
                token,
                CancellationToken.None
            );
        }

        foreach (
            OrderKitchenStationResponse station in order
                .Stations.Where(station => station.RequiresPrimaryDispatch)
                .OrderBy(station => station.Priority)
        )
        {
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
        }

        if (order.Status != "Ready")
        {
            throw new InvalidDataException(
                $"El pedido {order.Id.ToString("N")[..8]} terminó el flujo KDS en estado {order.Status}, no Ready."
            );
        }
        return order;
    }

    private static async Task SimulatePaymentsAsync(
        HttpClient client,
        RestaurantOptions restaurant,
        string token,
        Guid sessionId,
        OrderResponse order,
        int guestCount,
        Random random,
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
        List<decimal> tips = Allocate(totalTip, parts.Select(part => part.Amount).ToList());
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
        Func<BillResponse, bool> condition
    )
    {
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
            await Task.Delay(DiningProjectionPollInterval);
        }
    }

    private static List<PaymentPart> CreateGuestPaymentParts(
        PaymentOptions options,
        decimal amount,
        int guestCount,
        Random random,
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
        Random random,
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
        Random random,
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

    private static int Next(Random random, SemaphoreSlim? randomLock, IntRange range)
    {
        if (randomLock is null)
        {
            return random.Next(range.Minimum, range.Maximum + 1);
        }
        randomLock.Wait();
        try
        {
            return random.Next(range.Minimum, range.Maximum + 1);
        }
        finally
        {
            randomLock.Release();
        }
    }

    private static double NextDouble(Random random, SemaphoreSlim? randomLock)
    {
        if (randomLock is null)
        {
            return random.NextDouble();
        }
        randomLock.Wait();
        try
        {
            return random.NextDouble();
        }
        finally
        {
            randomLock.Release();
        }
    }

    private static T Pick<T>(IReadOnlyList<T> items, Random random, SemaphoreSlim? randomLock)
    {
        return items[Next(random, randomLock, new IntRange(0, items.Count - 1))];
    }

    private static List<T> Shuffle<T>(
        IReadOnlyList<T> items,
        Random random,
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
}

internal sealed class SimulatorOptions
{
    public string ApiBaseUrl { get; init; } = "http://localhost:5001";
    public bool AllowNonLocalApi { get; init; }
    public int? RandomSeed { get; init; }
    public int OrdersPerBatch { get; init; } = 20;
    public int RunDurationMinutes { get; init; } = 10;
    public List<RestaurantOptions> Restaurants { get; init; } = [];
}

internal sealed class RestaurantOptions
{
    public string Name { get; init; } = "";
    public Guid RestaurantId { get; init; }
    public decimal OpeningFloat { get; init; }
    public string PosAccessToken { get; init; } = "";
    public string KdsAccessToken { get; init; } = "";
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
