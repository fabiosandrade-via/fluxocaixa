param(
    [decimal] $CreditAmount = 0.01
)

$ErrorActionPreference = "Stop"
$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..\..")).Path
$composeDirectory = Join-Path $repoRoot "iac\docker"
$envFile = Join-Path $composeDirectory ".env.local"
$evidenceDirectory = Join-Path $repoRoot "docs\evidencias"

if (-not (Test-Path $envFile)) {
    throw "Arquivo de configuração ausente: $envFile. Configure-o conforme .env.example sem sobrescrever segredos existentes."
}
if ($CreditAmount -le 0 -or [decimal]::Round($CreditAmount, 2) -ne $CreditAmount) {
    throw "CreditAmount deve ser maior que zero e ter no máximo duas casas decimais."
}

New-Item -ItemType Directory -Force -Path $evidenceDirectory | Out-Null
$runId = "idempotency-{0}" -f (Get-Date -Format "yyyyMMdd-HHmmss")
$idempotencyKey = "$runId-$([guid]::NewGuid().ToString('N'))"
$date = (Get-Date).Date.AddDays(366).ToString("yyyy-MM-dd")
$creditAmountText = $CreditAmount.ToString([Globalization.CultureInfo]::InvariantCulture)
$tokenResponse = Invoke-RestMethod -Method Post `
    -Uri "http://localhost:8081/realms/fluxocaixa/protocol/openid-connect/token" `
    -ContentType "application/x-www-form-urlencoded" `
    -Body @{
        grant_type = "client_credentials"
        client_id = "fluxocaixa-loadtest"
        client_secret = "local-loadtest-only-secret"
    }
if ([string]::IsNullOrWhiteSpace($tokenResponse.access_token)) {
    throw "Keycloak não retornou access token para o teste de idempotência."
}

$headers = @{
    Authorization = "Bearer $($tokenResponse.access_token)"
    "Idempotency-Key" = $idempotencyKey
}
$requestBody = @{
    tipo = "Credito"
    valor = $CreditAmount
    data = $date
    descricao = "Verificacao idempotencia $runId"
} | ConvertTo-Json -Compress
$url = "http://localhost:5001/api/v1/lancamentos"
$first = Invoke-RestMethod -Method Post -Uri $url -Headers $headers `
    -ContentType "application/json" -Body $requestBody
$duplicate = Invoke-RestMethod -Method Post -Uri $url -Headers $headers `
    -ContentType "application/json" -Body $requestBody
if ($first.id -ne $duplicate.id) {
    throw "A repetição da requisição retornou um lançamento diferente."
}

$conflictingBody = @{
    tipo = "Credito"
    valor = $CreditAmount + 0.01
    data = $date
    descricao = "Verificacao idempotencia conflitante $runId"
} | ConvertTo-Json -Compress
$conflictStatus = 0
try {
    $null = Invoke-RestMethod -Method Post -Uri $url -Headers $headers `
        -ContentType "application/json" -Body $conflictingBody
}
catch {
    if ($null -ne $_.Exception.Response -and $null -ne $_.Exception.Response.StatusCode) {
        $conflictStatus = [int]$_.Exception.Response.StatusCode
    }
}
if ($conflictStatus -ne 409) {
    throw "O uso da mesma chave com outro corpo deveria retornar HTTP 409; recebido=$conflictStatus."
}

Push-Location $composeDirectory
try {
    $query = @"
SELECT COUNT(*), COUNT(DISTINCT c.evento_id), COUNT(e.evento_id)
FROM public.chaves_idempotencia c
LEFT JOIN public.eventos e ON e.evento_id = c.evento_id
WHERE c.chave = '$idempotencyKey';
"@
    $raw = & docker compose --env-file .env.local -f docker-compose.yml `
        exec -T db-lancamentos psql -U lancamentos_user -d lancamentos_db `
        -At -F "," -c $query
    if ($LASTEXITCODE -ne 0) {
        throw "Não foi possível conferir a chave no Event Store."
    }
}
finally {
    Pop-Location
}

$counts = $raw -split ","
if ($counts.Length -ne 3 -or [long]$counts[0] -ne 1 -or
    [long]$counts[1] -ne 1 -or [long]$counts[2] -ne 1) {
    throw "A chave deveria estar associada a um único evento. Resultado SQL: $raw"
}

$report = [pscustomobject]@{
    runId = $runId
    scenario = "api-idempotency"
    status = "passed"
    repeatedResponseId = $first.id
    firstStatus = 201
    duplicateStatus = 201
    conflictingPayloadStatus = $conflictStatus
    idempotencyRecords = [long]$counts[0]
    distinctEventIds = [long]$counts[1]
    eventStoreEvents = [long]$counts[2]
}
$reportPath = Join-Path $evidenceDirectory "$runId.json"
$report | ConvertTo-Json -Depth 5 | Set-Content -Encoding utf8 $reportPath
Write-Host "Idempotência confirmada: repetição sem duplicação e conflito HTTP 409."
Write-Host "Relatório: $reportPath"
