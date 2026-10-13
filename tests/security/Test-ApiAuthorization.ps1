param(
    [string] $OidcBaseUrl = "http://localhost:8081",
    [string] $LancamentosBaseUrl = "http://localhost:5001",
    [string] $ConsolidadoBaseUrl = "http://localhost:5002"
)

$ErrorActionPreference = "Stop"

$tokenEndpoint = "$OidcBaseUrl/realms/fluxocaixa/protocol/openid-connect/token"
$consolidadoUrl = "$ConsolidadoBaseUrl/api/v1/consolidado/periodo?inicio=2099-01-01&fim=2099-01-01"
$lancamentosUrl = "$LancamentosBaseUrl/api/v1/lancamentos"

function Get-ClientToken {
    param(
        [string] $ClientId,
        [string] $ClientSecret
    )

    $result = Invoke-RestMethod -Method Post -Uri $tokenEndpoint -ContentType "application/x-www-form-urlencoded" -Body @{
        grant_type = "client_credentials"
        client_id = $ClientId
        client_secret = $ClientSecret
    }
    if ([string]::IsNullOrWhiteSpace($result.access_token)) {
        throw "O provedor OIDC não retornou access_token para $ClientId."
    }
    return $result.access_token
}

function Get-HttpStatus {
    param(
        [string] $Uri,
        [string] $Method = "GET",
        [hashtable] $Headers = @{},
        [string] $Body = ""
    )

    try {
        if ($Method -eq "POST") {
            $response = Invoke-WebRequest -UseBasicParsing -Method $Method -Uri $Uri -Headers $Headers -Body $Body
        }
        else {
            $response = Invoke-WebRequest -UseBasicParsing -Method $Method -Uri $Uri -Headers $Headers
        }
        return [int]$response.StatusCode
    }
    catch {
        if ($null -ne $_.Exception.Response) {
            return [int]$_.Exception.Response.StatusCode
        }
        throw
    }
}

$unauthenticatedRead = Get-HttpStatus -Uri $consolidadoUrl
$readOnlyToken = Get-ClientToken `
    -ClientId "fluxocaixa-readonly" `
    -ClientSecret "local-readonly-only-secret"
$authorizedRead = Get-HttpStatus -Uri $consolidadoUrl -Headers @{
    Authorization = "Bearer $readOnlyToken"
}
$forbiddenWrite = Get-HttpStatus -Uri $lancamentosUrl -Method Post -Headers @{
    Authorization = "Bearer $readOnlyToken"
    "Content-Type" = "application/json"
}
$unauthenticatedWrite = Get-HttpStatus -Uri $lancamentosUrl -Method Post
$writeToken = Get-ClientToken `
    -ClientId "fluxocaixa-loadtest" `
    -ClientSecret "local-loadtest-only-secret"
$authorizedWrite = Get-HttpStatus -Uri $lancamentosUrl -Method Post -Headers @{
    Authorization = "Bearer $writeToken"
    "Content-Type" = "application/json"
} -Body '{"tipo":'

if ($unauthenticatedRead -ne 401 -or
    $authorizedRead -ne 200 -or
    $forbiddenWrite -ne 403 -or
    $unauthenticatedWrite -ne 401 -or
    $authorizedWrite -ne 400) {
    throw "Falha de autorização: GET sem token=$unauthenticatedRead (401), GET read-only=$authorizedRead (200), POST read-only=$forbiddenWrite (403), POST sem token=$unauthenticatedWrite (401), POST com escopo write e JSON inválido=$authorizedWrite (400)."
}

Write-Host "Autorização validada: 401 sem token, 200 com escopo read, 403 sem escopo write e requisição write autorizada rejeitada por JSON inválido (400)."
