param(
    [ValidateSet("read", "write")]
    [string] $Scenario = "read",
    [int] $Rate = 50,
    [string] $Duration = "5m",
    [string] $TestDate = (Get-Date).Date.ToString("yyyy-MM-dd")
)

$ErrorActionPreference = "Stop"
$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..\..\..")).Path
$composeDirectory = Join-Path $repoRoot "iac\docker"
$envFile = Join-Path $composeDirectory ".env.local"
$evidenceDirectory = Join-Path $repoRoot "docs\evidencias"

if (-not (Test-Path $envFile)) {
    throw "Arquivo de configuração ausente: $envFile. Configure-o conforme .env.example sem sobrescrever segredos existentes."
}
if ($Rate -lt 1 -or $Rate -gt 50) {
    throw "Rate deve estar entre 1 e 50; use 50 para validar o requisito."
}
if ($Duration -notmatch '^[1-9]\d*[smh]$') {
    throw "Duration deve usar um valor como 30s, 5m ou 1h."
}

New-Item -ItemType Directory -Force -Path $evidenceDirectory | Out-Null
$runId = "{0}-{1}" -f $Scenario, (Get-Date -Format "yyyyMMdd-HHmmss")
$scriptName = if ($Scenario -eq "read") { "consolidado-read.js" } else { "lancamentos-write.js" }
$summaryName = "$runId-summary.json"
$summaryPath = Join-Path $evidenceDirectory $summaryName
$resultPath = "/results/$summaryName"

function Get-SummaryMetricValue {
    param(
        [object] $Metrics,
        [string] $MetricName,
        [string] $ValueName
    )

    $metric = $Metrics.PSObject.Properties[$MetricName]
    if ($null -eq $metric) {
        throw "Métrica '$MetricName' ausente no resumo k6."
    }

    $value = $metric.Value.PSObject.Properties[$ValueName]
    if ($null -ne $value) {
        return $value.Value
    }

    $legacyValues = $metric.Value.PSObject.Properties["values"]
    if ($null -ne $legacyValues) {
        $legacyValue = $legacyValues.Value.PSObject.Properties[$ValueName]
        if ($null -ne $legacyValue) {
            return $legacyValue.Value
        }
    }

    throw "Valor '$ValueName' ausente na métrica '$MetricName' do resumo k6."
}

Push-Location $composeDirectory
try {
    $arguments = @(
        "--env-file", ".env.local",
        "-f", "docker-compose.yml",
        "--profile", "loadtest",
        "run", "--rm",
        "-e", "RUN_ID=$runId",
        "-e", "RATE=$Rate",
        "-e", "DURATION=$Duration",
        "-e", "NFR_TEST_DATE=$TestDate",
        "k6", "run",
        "--out", "experimental-prometheus-rw",
        "--summary-export=$resultPath",
        "/scripts/$scriptName"
    )
    & docker compose @arguments
    if ($LASTEXITCODE -ne 0) {
        throw "k6 terminou com código $LASTEXITCODE. Consulte os thresholds e os logs acima."
    }
}
finally {
    Pop-Location
}

if (-not (Test-Path $summaryPath)) {
    throw "Resumo k6 não foi gravado em $summaryPath."
}

$summary = Get-Content -Raw $summaryPath | ConvertFrom-Json
$requests = [long](Get-SummaryMetricValue $summary.metrics "http_reqs" "count")
$failedRate = [double](Get-SummaryMetricValue $summary.metrics "http_req_failed" "value")
$dropped = [long](Get-SummaryMetricValue $summary.metrics "dropped_iterations" "count")
Write-Host "Execução: $runId"
Write-Host "Requisições: $requests; falhas HTTP: $([math]::Round($failedRate * 100, 2))%; iterações descartadas: $dropped"
Write-Host "Resumo: $summaryPath"

if ($Rate -eq 50 -and $Duration -eq "5m" -and $Scenario -eq "write") {
    $accepted = [long](Get-SummaryMetricValue $summary.metrics "accepted_requests" "count")
    Write-Host "Lançamentos aceitos (201): $accepted"
}
