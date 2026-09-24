[CmdletBinding()]
param(
    [string]$ServerUrl = "http://localhost:9001",
    [string]$ProjectKey = "restaurantes",
    [string]$ProjectName = "Restaurantes",
    [string]$Token = $env:SONAR_TOKEN,
    [ValidateRange(1, 900)]
    [int]$QualityGateTimeoutSeconds = 300
)

$ErrorActionPreference = "Stop"

if ([string]::IsNullOrWhiteSpace($Token)) {
    throw "Define SONAR_TOKEN o proporciona el parámetro -Token antes de ejecutar el análisis."
}

function Invoke-DotNet {
    param([string[]]$Arguments)

    & dotnet @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "El comando 'dotnet $($Arguments -join ' ')' finalizó con código $LASTEXITCODE."
    }
}

Invoke-DotNet @("tool", "restore")

$beginArguments = @(
    "tool",
    "run",
    "dotnet-sonarscanner",
    "--",
    "begin",
    "/k:$ProjectKey",
    "/n:$ProjectName",
    "/d:sonar.host.url=$ServerUrl",
    "/d:sonar.token=$Token",
    "/d:sonar.qualitygate.wait=true",
    "/d:sonar.qualitygate.timeout=$QualityGateTimeoutSeconds"
)

Invoke-DotNet $beginArguments
Invoke-DotNet @("build", "Restaurantes.slnx", "--no-incremental")
Invoke-DotNet @("tool", "run", "dotnet-sonarscanner", "--", "end", "/d:sonar.token=$Token")
