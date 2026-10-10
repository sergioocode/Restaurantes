param(
    [Parameter(Mandatory = $true)]
    [ValidateSet("BORRAR")]
    [string]$ConfirmReset
)

$ErrorActionPreference = "Stop"
$repositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot "../..")).Path
$sqlPath = Join-Path $PSScriptRoot "reset-operational-data.sql"

function Invoke-Docker {
    param([Parameter(Mandatory = $true)][string[]]$DockerArguments)

    & docker @DockerArguments
    if ($LASTEXITCODE -ne 0) {
        throw "Fallo docker $($DockerArguments -join ' ')."
    }
}

Set-Location $repositoryRoot

$containers = @(
    "restaurantes-postgres",
    "restaurantes-postgres-analytics",
    "restaurantes-rabbitmq"
)
foreach ($container in $containers) {
    $state = (& docker inspect --format "{{.State.Running}}" $container 2>$null).Trim()
    if ($LASTEXITCODE -ne 0 -or $state -ne "true") {
        throw "El contenedor '$container' debe existir y estar en ejecucion."
    }
}

foreach (
    $target in @(
        @{ Container = "restaurantes-postgres"; Database = "restaurants_operational"; Mode = "operational" },
        @{ Container = "restaurantes-postgres-analytics"; Database = "restaurants_analytics"; Mode = "analytics" }
    )
) {
    Invoke-Docker -DockerArguments @(
        "cp",
        $sqlPath,
        "$($target.Container):/tmp/reset-operational-data.sql"
    )
    Invoke-Docker -DockerArguments @(
        "exec",
        $target.Container,
        "psql",
        "-U",
        "restaurants",
        "-d",
        $target.Database,
        "-v",
        "confirm_reset=$ConfirmReset",
        "-v",
        "reset_target=$($target.Mode)",
        "-f",
        "/tmp/reset-operational-data.sql"
    )
}

Invoke-Docker -DockerArguments @(
    "exec",
    "restaurantes-rabbitmq",
    "rabbitmq-diagnostics",
    "-q",
    "ping"
)

$mainQueues = @(
    "dining.table-sessions",
    "orders.kds.read-model",
    "orders.kds.signalr",
    "payments.orders-integration",
    "payments.payable-orders",
    "payments.cash-register",
    "sales.checkout-process",
    "sales.read-model",
    "reporting.dashboard.read-model",
    "reporting.dashboard.signalr.projection-updated"
)
$deadLetterQueues = @(
    "dining.dead-letter.messages",
    "orders.kds.read-model.dead-letter",
    "payments.dead-letter.messages",
    "sales.dead-letter.messages",
    "reporting.dead-letter.messages"
)
$queues = @(
    $mainQueues
    foreach ($queue in $mainQueues) {
        foreach ($retry in 1..3) {
            "$queue.retry.$retry"
        }
    }
    $deadLetterQueues
)
$failedQueues = [System.Collections.Generic.List[string]]::new()
foreach ($queue in $queues) {
    $output = & docker exec restaurantes-rabbitmq rabbitmqctl -q purge_queue -p / $queue 2>&1
    if ($LASTEXITCODE -eq 0) {
        Write-Host "Purgada: $queue"
    } else {
        $details = $output -join " "
        if ($details -match "(?i)not found|not_found|does not exist") {
            Write-Host "No existe: $queue"
        } else {
            $failedQueues.Add("$queue - $details")
        }
    }
}

if ($failedQueues.Count -gt 0) {
    throw "Fallo la purga de algunas colas:`n$($failedQueues -join "`n")"
}

Write-Host "Reset PostgreSQL y purga de RabbitMQ completados."
