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
$runId = "kafka-recovery-{0}" -f (Get-Date -Format "yyyyMMdd-HHmmss")

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
    $query = @"
SELECT COUNT(*),
       COALESCE(SUM((e.dados->>'Valor')::numeric) FILTER (WHERE e.dados->>'Tipo' = 'Credito'), 0),
       COUNT(*) FILTER (WHERE e.posicao_global > c.ultima_posicao)
FROM public.eventos e
CROSS JOIN public.checkpoints_publicacao c
WHERE c.publicador = 'lancamentos-kafka' AND e.dados->>'Data' = '$Date';
"@
    $raw = Invoke-ComposeText @(
        "exec", "-T", "db-lancamentos", "psql", "-U", "lancamentos_user",
        "-d", "lancamentos_db", "-At", "-F", ",", "-c", $query
    )
    $parts = $raw -split ","
    if ($parts.Length -ne 3) {
        throw "Não foi possível interpretar a reconciliação do Event Store: $raw"
    }
    return [pscustomobject]@{
        Count = [long]$parts[0]
        Credits = [decimal]::Parse($parts[1], [Globalization.CultureInfo]::InvariantCulture)
        Pending = [long]$parts[2]
    }
}

function Get-PendingOutboxMetric {
    $response = Invoke-RestMethod -Uri `
        "http://localhost:9090/api/v1/query?query=fluxocaixa_eventpublisher_outbox_pending_events" `
        -TimeoutSec 10
    $result = @($response.data.result) | Select-Object -First 1
    if ($null -eq $result) {
        throw "Prometheus ainda não coletou a métrica de backlog do Event Publisher."
    }
    return [long]$result.value[1]
}

function Get-ProjectorConsumerLag {
    $output = Invoke-ComposeText @(
        "exec", "-T", "kafka", "kafka-consumer-groups",
        "--bootstrap-server", "kafka:9092",
        "--describe", "--group", "consolidado-consumer-group"
    )
    $partitionLines = @($output -split "`r?`n" | Where-Object {
        $_ -match '^\s*consolidado-consumer-group\s+lancamentos-events\s+\d+\s+'
    })
    if ($partitionLines.Count -eq 0) {
        throw "O grupo consolidado-consumer-group não retornou offsets de partição."
    }
    $lag = 0L
    foreach ($line in $partitionLines) {
        $columns = @($line -split '\s+')
        if ($columns.Count -lt 6 -or $columns[5] -notmatch '^\d+$') {
            throw "Não foi possível interpretar o lag do Projetor: $line"
        }
        $lag += [long]$columns[5]
    }
    return $lag
}

function Get-LoadTestToken {
    $response = Invoke-RestMethod -Method Post `
        -Uri "http://localhost:8081/realms/fluxocaixa/protocol/openid-connect/token" `
        -ContentType "application/x-www-form-urlencoded" `
        -Body @{
            grant_type = "client_credentials"
            client_id = "fluxocaixa-loadtest"
            client_secret = "local-loadtest-only-secret"
        }
    if ([string]::IsNullOrWhiteSpace($response.access_token)) {
        throw "Keycloak não retornou access token para o cenário de recuperação."
    }
    return $response.access_token
}

function Get-ProjectedCredits {
    param(
        [string] $Date,
        [string] $AccessToken
    )
    $url = "http://localhost:5002/api/v1/consolidado/periodo?inicio=$Date&fim=$Date"
    $response = Invoke-RestMethod -Method Get -Uri $url -Headers @{
        Authorization = "Bearer $AccessToken"
    } -TimeoutSec 5
    $balance = @($response) | Where-Object { $_.data -eq $Date } | Select-Object -First 1
    if ($null -eq $balance) {
        return [decimal]0
    }
    return [decimal]$balance.totalCreditos
}

Push-Location $composeDirectory
$kafkaStopped = $false
try {
    for ($dateAttempt = 0; $dateAttempt -lt 60; $dateAttempt++) {
        $dateText = $testDate.ToString("yyyy-MM-dd")
        $dateEvents = Invoke-ComposeText @(
            "exec", "-T", "db-lancamentos", "psql", "-U", "lancamentos_user",
            "-d", "lancamentos_db", "-At", "-c",
            "SELECT COUNT(*) FROM public.eventos WHERE dados->>'Data' = '$dateText';"
        )
        if ([long]$dateEvents -eq 0) {
            break
        }
        $testDate = $testDate.AddDays(1)
    }
    if ([long]$dateEvents -ne 0) {
        throw "Não foi possível encontrar uma data futura vazia para o teste."
    }
    $testDate = $testDate.ToString("yyyy-MM-dd")
    $null = Get-LoadTestToken
    $projectorLagBeforeTest = Get-ProjectorConsumerLag
    if ($projectorLagBeforeTest -ne 0) {
        throw "O Projetor precisa estar sem backlog antes do teste. Lag atual=$projectorLagBeforeTest; aguarde a projeção terminar e tente novamente."
    }

    Invoke-ComposeText @("stop", "kafka") | Out-Null
    $kafkaStopped = $true
    $kafkaStoppedAt = Get-Date

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
    $k6ExitCode = $LASTEXITCODE
    if (-not (Test-Path $summaryPath)) {
        throw "Resumo k6 não foi gravado em $summaryPath (código de saída: $k6ExitCode)."
    }

    $summary = Get-Content -Raw $summaryPath | ConvertFrom-Json
    $requestRate = [double](Get-SummaryMetricValue $summary.metrics "http_reqs" "rate")
    $failedRate = [double](Get-SummaryMetricValue $summary.metrics "http_req_failed" "value")
    $accepted = [long](Get-SummaryMetricValue $summary.metrics "accepted_requests" "count")
    $dropped = [long](Get-SummaryMetricValue $summary.metrics "dropped_iterations" "count")
    $p95Milliseconds = [double](Get-SummaryMetricValue $summary.metrics "http_req_duration" "p(95)")
    if ($k6ExitCode -ne 0 -or $failedRate -gt 0.05 -or $dropped -ne 0 -or $accepted -lt 15000) {
        throw "NFR não atendido durante falha do Kafka (código k6=$k6ExitCode): taxa=$requestRate req/s; p95=$p95Milliseconds ms; falhas HTTP=$failedRate; descartadas=$dropped; HTTP 201=$accepted. Resumo: $summaryPath"
    }

    $pendingMetricDuringOutage = Get-PendingOutboxMetric
    if ($pendingMetricDuringOutage -lt 1) {
        throw "Prometheus não registrou backlog durante a indisponibilidade do Kafka."
    }
    $storeTotals = Get-StoreTotals $testDate
    if ($storeTotals.Count -lt $accepted -or
        $storeTotals.Credits -lt ($accepted * $CreditAmount) -or
        $storeTotals.Pending -lt 1) {
        throw "Event Store não comprovou escrita durável e backlog: eventos=$($storeTotals.Count), pendentes=$($storeTotals.Pending), aceitos=$accepted."
    }

    Invoke-ComposeText @("up", "-d", "kafka") | Out-Null
    $kafkaRestoredAt = Get-Date
    $kafkaStopped = $false

    $kafkaReady = $false
    $deadline = (Get-Date).AddMinutes(3)
    do {
        try {
            $null = Invoke-ComposeText @(
                "exec", "-T", "kafka", "kafka-topics",
                "--bootstrap-server", "kafka:9092", "--list"
            )
            $kafkaReady = $true
        }
        catch {
            Start-Sleep -Seconds 5
        }
    } while (-not $kafkaReady -and (Get-Date) -lt $deadline)
    if (-not $kafkaReady) {
        throw "Kafka não voltou a aceitar conexões em até três minutos."
    }

    $token = Get-LoadTestToken
    $projectedCredits = $null
    $recoveryDeadline = (Get-Date).AddMinutes(10)
    $recoveryStartedAt = Get-Date
    do {
        Start-Sleep -Seconds 5
        $storeTotals = Get-StoreTotals $testDate
        $projectedCredits = Get-ProjectedCredits $testDate $token
        if ((Get-Date) -ge $recoveryDeadline) {
            $reportPath = Join-Path $evidenceDirectory "$runId-recovery.json"
            [pscustomobject]@{
                runId = $runId
                scenario = "kafka-recovery"
                status = "recovery-timeout"
                ratePerSecond = $Rate
                measuredRequestRate = $requestRate
                duration = $Duration
                date = $testDate
                projectorLagBeforeTest = $projectorLagBeforeTest
                projectorLagAtTimeout = Get-ProjectorConsumerLag
                eventStoreEventCount = $storeTotals.Count
                eventStoreCreditTotal = $storeTotals.Credits
                pendingEventsAfterRecovery = $storeTotals.Pending
                projectedCreditTotal = $projectedCredits
                acceptedHttp201 = $accepted
                p95Milliseconds = $p95Milliseconds
                failedHttpRate = $failedRate
                droppedIterations = $dropped
                recoveryWaitMinutes = [math]::Round(((Get-Date) - $recoveryStartedAt).TotalMinutes, 1)
            } | ConvertTo-Json -Depth 5 | Set-Content -Encoding utf8 $reportPath
            throw "Publisher/Projetor não reconciliou em dez minutos. Pendentes=$($storeTotals.Pending); Event Store=$($storeTotals.Credits); projeção=$projectedCredits; relatório=$reportPath."
        }
    } while ($storeTotals.Pending -ne 0 -or $projectedCredits -ne $storeTotals.Credits)

    $report = [pscustomobject]@{
        runId = $runId
        scenario = "kafka-recovery"
        status = "recovered"
        ratePerSecond = $Rate
        duration = $Duration
        date = $testDate
        projectorLagBeforeTest = $projectorLagBeforeTest
        projectorLagAtRecovery = Get-ProjectorConsumerLag
        kafkaUnavailableSeconds = [math]::Round(($kafkaRestoredAt - $kafkaStoppedAt).TotalSeconds, 1)
        eventStoreEventCount = $storeTotals.Count
        eventStoreCreditTotal = $storeTotals.Credits
        pendingEventsObservedDuringOutage = $pendingMetricDuringOutage
        pendingEventsAfterRecovery = $storeTotals.Pending
        projectedCreditTotal = $projectedCredits
        acceptedHttp201 = $accepted
        measuredRequestRate = $requestRate
        p95Milliseconds = $p95Milliseconds
        failedHttpRate = $failedRate
        droppedIterations = $dropped
        recoveredWithinMinutes = 10
    }
    $reportPath = Join-Path $evidenceDirectory "$runId-recovery.json"
    $report | ConvertTo-Json -Depth 5 | Set-Content -Encoding utf8 $reportPath
    Write-Host "Kafka foi restaurado; $accepted lançamentos aceitos e saldo reconciliado: $projectedCredits."
    Write-Host "Relatório: $reportPath"
}
finally {
    if ($kafkaStopped) {
        Invoke-ComposeText @("up", "-d", "kafka") | Out-Null
    }
    Pop-Location
}
