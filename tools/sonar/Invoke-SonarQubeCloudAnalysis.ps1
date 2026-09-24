[CmdletBinding()]
param(
    [string]$ProjectKey = "sergioocode_Restaurantes",
    [string]$Organization = "sergioocode",
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
        $displayArguments = $Arguments | ForEach-Object {
            if ($_ -like "/d:sonar.token=*") {
                "/d:sonar.token=***"
            }
            else {
                $_
            }
        }

        throw "El comando 'dotnet $($displayArguments -join ' ')' finalizó con código $LASTEXITCODE."
    }
}

$repositoryRoot = Resolve-Path (Join-Path $PSScriptRoot "..\..")
$coverageDirectory = Join-Path $repositoryRoot "TestResults"
if (Test-Path -LiteralPath $coverageDirectory) {
    Remove-Item -LiteralPath $coverageDirectory -Recurse -Force
}
New-Item -ItemType Directory -Path $coverageDirectory -Force | Out-Null
$coverageReportPattern = Join-Path $coverageDirectory "**\coverage.opencover.xml"

Invoke-DotNet @("tool", "restore")

$beginArguments = @(
    "tool",
    "run",
    "dotnet-sonarscanner",
    "--",
    "begin",
    "/k:$ProjectKey",
    "/o:$Organization",
    "/n:$ProjectName",
    "/d:sonar.token=$Token",
    "/d:sonar.cs.opencover.reportsPaths=$coverageReportPattern",
    "/d:sonar.qualitygate.wait=true",
    "/d:sonar.qualitygate.timeout=$QualityGateTimeoutSeconds"
)

Invoke-DotNet $beginArguments
Invoke-DotNet @("build", "Restaurantes.slnx", "--no-incremental")
Invoke-DotNet @(
    "test",
    "Restaurantes.slnx",
    "--no-build",
    "--collect:XPlat Code Coverage",
    "--results-directory",
    $coverageDirectory,
    "--",
    "DataCollectionRunSettings.DataCollectors.DataCollector.Configuration.Format=opencover"
)
Invoke-DotNet @("tool", "run", "dotnet-sonarscanner", "--", "end", "/d:sonar.token=$Token")
