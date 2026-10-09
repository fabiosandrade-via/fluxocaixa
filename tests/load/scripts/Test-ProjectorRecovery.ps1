param(
    [int] $Rate = 50,
    [string] $Duration = "5m",
    [decimal] $CreditAmount = 0.01
)

$ErrorActionPreference = "Stop"
$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..\..\..")).Path
$composeDirectory = Join-Path $repoRoot "iac\docker"
$envFile = Join-Path $composeDirectory ".env.local"
$evidenceDirectory = Join-Path $repoRoot "docs\evidencias"
$runId = "projector-recovery-{0}" -f (Get-Date -Format "yyyyMMdd-HHmmss")

if (-not (Test-Path $envFile)) {
    throw "Arquivo de configuração ausente: $envFile. Configure-o conforme .env.example sem sobrescrever segredos existentes."
}
if ($Rate -ne 50 -or $Duration -ne "5m") {
    throw "Este cenário de aceitação exige 50 req/s por 5 minutos."
}
if ($CreditAmount -le 0 -or [decimal]::Round($CreditAmount, 2) -ne $CreditAmount) {
    throw "CreditAmount deve ser maior que zero e ter no máximo duas casas decimais."
}

New-Item -ItemType Directory -Force -Path $evidenceDirectory | Out-Null
$testDate = (Get-Date).Date.AddDays(365)
$summaryName = "$runId-summary.json"
$summaryPath = Join-Path $evidenceDirectory $summaryName
$compose = @("--env-file", ".env.local", "-f", "docker-compose.yml")

function Invoke-ComposeText {
    param([string[]] $ComposeArguments)
    $output = & docker compose @compose @ComposeArguments
    if ($LASTEXITCODE -ne 0) {
        throw "docker compose falhou ($LASTEXITCODE): $($ComposeArguments -join ' ')"
    }
    return ($output | Out-String).Trim()
}

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

function Get-StoreTotals {
    param([string] $Date)
    $query = "SELECT COUNT(*), COALESCE(SUM((dados->>'Valor')::numeric) FILTER (WHERE dados->>'Tipo' = 'Credito'), 0) FROM public.eventos WHERE dados->>'Data' = '$Date';"
    $raw = Invoke-ComposeText @("exec", "-T", "db-lancamentos", "psql", "-U", "lancamentos_user", "-d", "lancamentos_db", "-At", "-F", ",", "-c", $query)
    $parts = $raw -split ","
    if ($parts.Length -ne 2) {
        throw "Não foi possível interpretar a reconciliação do Event Store: $raw"
    }
    return [pscustomobject]@{
        Count = [long]$parts[0]
        Credits = [decimal]::Parse($parts[1], [Globalization.CultureInfo]::InvariantCulture)
    }
}

function Get-ProjectedCredits {
    param([string] $Date)
    $url = "http://localhost:5002/api/v1/consolidado/periodo?inicio=$Date&fim=$Date"
    try {
        $response = Invoke-RestMethod -Method Get -Uri $url -TimeoutSec 5
    }
    catch [System.Net.Http.HttpRequestException] {
        return $null
    }
    catch [System.Net.WebException] {
        return $null
    }
    $balance = @($response) | Where-Object { $_.data -eq $Date } | Select-Object -First 1
    if ($null -eq $balance) {
        return [decimal]0
    }
    return [decimal]$balance.totalCreditos
}

Push-Location $composeDirectory
$projectorStopped = $false
try {
    $dateEvents = 0
    for ($dateAttempt = 0; $dateAttempt -lt 60; $dateAttempt++) {
        $testDateText = $testDate.ToString("yyyy-MM-dd")
        $dateEvents = Invoke-ComposeText @(
            "exec", "-T", "db-lancamentos", "psql", "-U", "lancamentos_user",
            "-d", "lancamentos_db", "-At", "-c",
            "SELECT COUNT(*) FROM public.eventos WHERE dados->>'Data' = '$testDateText';"
        )
        if ([long]$dateEvents -ne 0) {
            $testDate = $testDate.AddDays(1)
        }
        else {
            break
        }
    }
    if ([long]$dateEvents -ne 0) {
        throw "Não foi possível encontrar uma data futura vazia para o teste."
    }
    $testDate = $testDate.ToString("yyyy-MM-dd")

    Invoke-ComposeText @("stop", "projetor") | Out-Null
    $projectorStopped = $true

    $creditAmountText = $CreditAmount.ToString([Globalization.CultureInfo]::InvariantCulture)
    $loadArguments = @(
        "--env-file", ".env.local", "-f", "docker-compose.yml",
        "--profile", "loadtest", "run", "--rm",
        "-e", "RUN_ID=$runId",
        "-e", "RATE=$Rate",
        "-e", "DURATION=$Duration",
        "-e", "NFR_TEST_DATE=$testDate",
        "-e", "CREDIT_AMOUNT=$creditAmountText",
        "k6", "run", "--out", "experimental-prometheus-rw",
        "--summary-export=/results/$summaryName",
        "/scripts/lancamentos-write.js"
    )
    & docker compose @loadArguments
    if ($LASTEXITCODE -ne 0) {
        throw "Carga k6 não passou; o Projetor será iniciado no bloco de limpeza."
    }

    if (-not (Test-Path $summaryPath)) {
        throw "Resumo k6 não foi gravado em $summaryPath."
    }
    $summary = Get-Content -Raw $summaryPath | ConvertFrom-Json
    $failedRate = [double](Get-SummaryMetricValue $summary.metrics "http_req_failed" "value")
    $accepted = [long](Get-SummaryMetricValue $summary.metrics "accepted_requests" "count")
    $dropped = [long](Get-SummaryMetricValue $summary.metrics "dropped_iterations" "count")
    if ($failedRate -gt 0.05 -or $dropped -ne 0 -or $accepted -lt 1) {
        throw "NFR não atendido: falhas HTTP=$failedRate; iterações descartadas=$dropped; HTTP 201=$accepted."
    }

    $storeTotals = Get-StoreTotals $testDate
    if ($storeTotals.Count -lt $accepted -or $storeTotals.Credits -lt ($accepted * $CreditAmount)) {
        throw "Event Store contém menos eventos/créditos que os HTTP 201: eventos=$($storeTotals.Count), créditos=$($storeTotals.Credits), aceitos=$accepted."
    }

    Invoke-ComposeText @("start", "projetor") | Out-Null
    $projectorStopped = $false

    $deadline = (Get-Date).AddMinutes(5)
    $projectedCredits = $null
    do {
        Start-Sleep -Seconds 5
        $projectedCredits = Get-ProjectedCredits $testDate
        if ((Get-Date) -ge $deadline) {
            throw "Projetor não reconciliou em 5 minutos. Event Store=$($storeTotals.Credits), projeção=$projectedCredits."
        }
    } while ($null -eq $projectedCredits -or $projectedCredits -ne $storeTotals.Credits)

    $report = [pscustomobject]@{
        runId = $runId
        scenario = "projector-recovery"
        ratePerSecond = $Rate
        duration = $Duration
        date = $testDate
        eventStoreEventCount = $storeTotals.Count
        eventStoreCreditTotal = $storeTotals.Credits
        projectedCreditTotal = $projectedCredits
        acceptedHttp201 = $accepted
        failedHttpRate = $failedRate
        droppedIterations = $dropped
        recoveredWithinMinutes = 5
    }
    $reportPath = Join-Path $evidenceDirectory "$runId-recovery.json"
    $report | ConvertTo-Json -Depth 5 | Set-Content -Encoding utf8 $reportPath
    Write-Host "Cenário passou; eventos Event Store=$($storeTotals.Count), créditos reconciliados=$projectedCredits."
    Write-Host "Relatório: $reportPath"
}
finally {
    if ($projectorStopped) {
        Invoke-ComposeText @("start", "projetor") | Out-Null
    }
    Pop-Location
}
