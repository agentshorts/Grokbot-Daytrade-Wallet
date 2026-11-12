# Calls one MCP tool on the loopback host.
# The API key is read from MCP_API_KEY or from .env. It is not accepted as an
# argument and it is not put in the URL.
#
#   powershell -NoProfile -ExecutionPolicy Bypass -File scripts\mcp-call.ps1 wallet_balances '{"wallet":"paper"}'
#   powershell -NoProfile -ExecutionPolicy Bypass -File scripts\mcp-call.ps1 wallet_create -JsonFile docs\examples\wallet.json

[CmdletBinding()]
param(
    [Parameter(Mandatory = $true, Position = 0)]
    [string]$Tool,
    [Parameter(Position = 1)]
    [string]$Json = "{}",
    [string]$JsonFile = "",
    [string]$Url = "",
    [string]$Root = ""
)

$ErrorActionPreference = "Stop"
. (Join-Path $PSScriptRoot "host-lib.ps1")

if ([string]::IsNullOrWhiteSpace($Root)) {
    $Root = Split-Path -Parent $PSScriptRoot
}
Import-DotEnvFile (Join-Path $Root ".env")

if ([string]::IsNullOrWhiteSpace($Url)) {
    if (-not [string]::IsNullOrWhiteSpace($env:MCP_URL)) {
        $Url = $env:MCP_URL
    } else {
        $Url = "http://127.0.0.1:5050/mcp"
    }
}

if ($Tool -notmatch "^[A-Za-z0-9_-]+$") {
    [Console]::Error.WriteLine("The tool name must be letters, numbers, underscores, or hyphens.")
    exit 1
}

try {
    $uri = [Uri]$Url
} catch {
    [Console]::Error.WriteLine("Refusing to call MCP. The URL is not a loopback address.")
    exit 1
}

if (-not (Test-LoopbackUrl $Url)) {
    [Console]::Error.WriteLine("Refusing to call '$Url'. The host listens on 127.0.0.1 only.")
    exit 1
}

if (-not [string]::IsNullOrEmpty($uri.Query)) {
    [Console]::Error.WriteLine("Refusing to call MCP with a query string. Put the API key in MCP_API_KEY, not in the URL.")
    exit 1
}

if (-not [string]::IsNullOrWhiteSpace($JsonFile)) {
    if (-not (Test-Path -LiteralPath $JsonFile)) {
        [Console]::Error.WriteLine("The JSON file was not found.")
        exit 1
    }
    $Json = [System.IO.File]::ReadAllText($JsonFile)
}

$trimmed = $Json.Trim()
if (-not $trimmed.StartsWith("{")) {
    [Console]::Error.WriteLine("The tool arguments must be a JSON object.")
    exit 1
}
try {
    $null = $trimmed | ConvertFrom-Json
} catch {
    [Console]::Error.WriteLine("The tool arguments are not JSON.")
    exit 1
}

$key = $env:MCP_API_KEY
if (-not (Test-McpKey $key)) {
    [Console]::Error.WriteLine("MCP_API_KEY is missing or shorter than 8 characters. Set it in the environment or in .env. The key is not accepted on the command line or in the URL.")
    exit 1
}
$key = $key.Trim()

function Invoke-McpRequest([string]$Body, [string]$Session, [string]$Protocol) {
    $headers = @{
        "Accept" = "application/json, text/event-stream"
        "Authorization" = "Bearer $key"
    }
    if (-not [string]::IsNullOrEmpty($Session)) { $headers["Mcp-Session-Id"] = $Session }
    if (-not [string]::IsNullOrEmpty($Protocol)) { $headers["MCP-Protocol-Version"] = $Protocol }

    $params = @{
        Uri = $Url
        Method = "POST"
        Headers = $headers
        Body = $Body
        ContentType = "application/json"
        UseBasicParsing = $true
    }
    if ($PSVersionTable.PSVersion.Major -ge 7) {
        $params["SkipHttpErrorCheck"] = $true
    }

    try {
        return Invoke-WebRequest @params
    } catch {
        $web = $_.Exception.Response
        if ($null -eq $web) { throw }
        $status = [int]$web.StatusCode
        $text = ""
        try {
            $stream = $web.GetResponseStream()
            if ($null -ne $stream) {
                $reader = New-Object System.IO.StreamReader($stream)
                $text = $reader.ReadToEnd()
                $reader.Dispose()
            }
        } catch {
            $text = ""
        }
        return [pscustomobject]@{ StatusCode = $status; Content = $text; Headers = @{} }
    }
}

function Get-ResponseHeader($Response, [string]$Name) {
    if ($null -eq $Response.Headers) { return $null }
    $headers = $Response.Headers
    foreach ($headerName in @($headers.Keys)) {
        if ([string]$headerName -eq $Name) {
            $value = $headers[$headerName]
            if ($value -is [System.Array]) { return [string]$value[0] }
            return [string]$value
        }
    }
    return $null
}

function Get-ResponseBody($Response) {
    if ($null -eq $Response.Content) { return "" }
    return [string]$Response.Content
}

function ConvertFrom-McpBody([string]$Body) {
    if ([string]::IsNullOrWhiteSpace($Body)) { return $null }
    $trim = $Body.Trim()
    if ($trim.StartsWith("{") -or $trim.StartsWith("[")) {
        return $trim | ConvertFrom-Json
    }
    $last = $null
    foreach ($line in ($Body -split "`r?`n")) {
        $line = $line.Trim()
        if ($line.StartsWith("data:")) {
            $payload = $line.Substring(5).Trim()
            if ($payload.StartsWith("{")) {
                $last = $payload | ConvertFrom-Json
            }
        }
    }
    return $last
}

function Write-McpFailure([int]$Status, [string]$Body) {
    [Console]::Error.WriteLine("MCP call failed with HTTP $Status.")
    if (-not [string]::IsNullOrWhiteSpace($Body) -and -not $Body.Contains($key)) {
        [Console]::Error.WriteLine($Body)
    }
}

$initialize = '{"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":"2025-11-25","capabilities":{},"clientInfo":{"name":"mcp-call","version":"1.0"}}}'
$initialized = Invoke-McpRequest -Body $initialize -Session "" -Protocol ""
$initStatus = [int]$initialized.StatusCode
$initBody = Get-ResponseBody $initialized
if ($initStatus -ge 400) {
    Write-McpFailure $initStatus $initBody
    exit 1
}

$initMessage = ConvertFrom-McpBody $initBody
if ($null -eq $initMessage -or $null -eq $initMessage.result) {
    [Console]::Error.WriteLine("MCP initialize did not return a result.")
    exit 1
}

$protocol = [string]$initMessage.result.protocolVersion
if ([string]::IsNullOrWhiteSpace($protocol)) { $protocol = "2025-11-25" }
$session = Get-ResponseHeader $initialized "Mcp-Session-Id"

$notice = Invoke-McpRequest -Body '{"jsonrpc":"2.0","method":"notifications/initialized"}' -Session $session -Protocol $protocol
$noticeStatus = [int]$notice.StatusCode
if ($noticeStatus -eq 401 -or $noticeStatus -eq 403) {
    Write-McpFailure $noticeStatus (Get-ResponseBody $notice)
    exit 1
}

$toolJson = ($Tool | ConvertTo-Json -Compress)
$callBody = '{"jsonrpc":"2.0","id":2,"method":"tools/call","params":{"name":' + $toolJson + ',"arguments":' + $trimmed + '}}'
$called = Invoke-McpRequest -Body $callBody -Session $session -Protocol $protocol
$callStatus = [int]$called.StatusCode
$callRaw = Get-ResponseBody $called
if ($callStatus -ge 400) {
    Write-McpFailure $callStatus $callRaw
    exit 1
}

$message = ConvertFrom-McpBody $callRaw
if ($null -eq $message) {
    [Console]::Error.WriteLine("MCP tool call did not return JSON.")
    exit 1
}
if ($null -ne $message.error) {
    $errorJson = $message.error | ConvertTo-Json -Compress -Depth 8
    if ($errorJson.Contains($key)) { $errorJson = "MCP returned an error." }
    [Console]::Error.WriteLine($errorJson)
    exit 1
}
if ($null -eq $message.result) {
    [Console]::Error.WriteLine("MCP tool call did not return a result.")
    exit 1
}

$chunks = @()
foreach ($block in @($message.result.content)) {
    if ($null -eq $block) { continue }
    if ([string]$block.type -eq "text" -and $null -ne $block.text) {
        $chunks += [string]$block.text
    }
}
$text = $chunks -join "`n"
if ($message.result.isError) {
    if (-not $text.Contains($key)) { [Console]::Error.WriteLine($text) }
    exit 1
}
if ([string]::IsNullOrEmpty($text)) {
    $text = ($message.result | ConvertTo-Json -Compress -Depth 8)
    if ($text.Contains($key)) { $text = "" }
}
Write-Output $text
exit 0
