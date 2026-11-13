[CmdletBinding()]
param(
    [string]$ServerUrl = "http://localhost:9001",
    [string]$ProjectKey = "restaurantes-key",
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
        $safeArguments = $Arguments | ForEach-Object {
            if ($_ -match "(?i)^/d:sonar\.token=") {
                "/d:sonar.token=[REDACTED]"
            } else {
                $_
            }
        }
        throw "El comando 'dotnet $($safeArguments -join ' ')' finalizó con código $LASTEXITCODE."
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
    "/d:sonar.exclusions=**/*.js,**/*.mjs",
    "/d:sonar.scm.use.blame.algorithm=GIT_NATIVE_BLAME",
    "/d:sonar.qualitygate.wait=true",
    "/d:sonar.qualitygate.timeout=$QualityGateTimeoutSeconds"
)

Invoke-DotNet $beginArguments
Invoke-DotNet @("build", "Restaurantes.slnx")
Invoke-DotNet @("tool", "run", "dotnet-sonarscanner", "--", "end", "/d:sonar.token=$Token")
