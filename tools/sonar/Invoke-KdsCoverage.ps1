[CmdletBinding()]
param(
    [string]$ResultsDirectory = (Join-Path $PSScriptRoot "../../TestResults")
)

$ErrorActionPreference = "Stop"
$repositoryRoot = Resolve-Path (Join-Path $PSScriptRoot "../..")
New-Item -ItemType Directory -Path $ResultsDirectory -Force | Out-Null
$reportPath = Join-Path (Resolve-Path $ResultsDirectory) "kds.lcov.info"

Push-Location $repositoryRoot
try {
    & node --test --experimental-test-coverage `
        --test-reporter=spec --test-reporter-destination=stdout `
        --test-reporter=lcov "--test-reporter-destination=$reportPath" `
        'tests/Restaurantes.Hosts.Tests/Kds/*.test.mjs' `
        'tests/Restaurantes.Hosts.Tests/Dashboard/*.test.mjs'
    if ($LASTEXITCODE -ne 0) {
        throw "Las pruebas de KDS fallaron con código $LASTEXITCODE."
    }
    if (!(Test-Path -LiteralPath $reportPath) -or (Get-Item -LiteralPath $reportPath).Length -eq 0) {
        throw "Las pruebas de KDS no generaron el informe LCOV esperado."
    }
    $reportLines = [System.IO.File]::ReadAllLines($reportPath)
    for ($index = 0; $index -lt $reportLines.Length; $index++) {
        if (!$reportLines[$index].StartsWith("SF:")) {
            continue
        }
        $relativePath = $reportLines[$index].Substring(3).Replace('\', '/')
        $sourcePath = [System.IO.Path]::GetFullPath((Join-Path $repositoryRoot $relativePath))
        if (!(Test-Path -LiteralPath $sourcePath -PathType Leaf)) {
            throw "El informe LCOV referencia un archivo inexistente: $relativePath"
        }
        $reportLines[$index] = "SF:$($sourcePath.Replace('\', '/'))"
    }
    [System.IO.File]::WriteAllLines(
        $reportPath,
        $reportLines,
        [System.Text.UTF8Encoding]::new($false)
    )
}
finally {
    Pop-Location
}
