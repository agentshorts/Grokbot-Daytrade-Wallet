# Builds TradeMonitor in Release and starts it on 127.0.0.1 only.
# A non-loopback URL stops the script before it writes config or starts the host.
# The MCP API key is written to .env and is not printed in full.

[CmdletBinding()]
param(
    [string]$Urls = "http://127.0.0.1:5050",
    [string]$Root = "",
    [switch]$PrepareOnly,
    [switch]$SkipBuild
)

$ErrorActionPreference = "Stop"
. (Join-Path $PSScriptRoot "host-lib.ps1")

try {
    $resolved = Resolve-HostUrls -Urls $Urls
} catch {
    [Console]::Error.WriteLine($_.Exception.Message)
    exit 1
}

$env:ASPNETCORE_URLS = $resolved
$env:ASPNETCORE_ENVIRONMENT = "Production"

if ([string]::IsNullOrWhiteSpace($Root)) {
    $Root = Split-Path -Parent $PSScriptRoot
}

try {
    Initialize-LocalConfig -Root $Root
} catch {
    [Console]::Error.WriteLine($_.Exception.Message)
    exit 1
}

if (-not (Test-McpKey $env:MCP_API_KEY)) {
    $fromFile = Get-DotEnvValue -Path (Join-Path $Root ".env") -Name "MCP_API_KEY"
    if (Test-McpKey $fromFile) {
        $env:MCP_API_KEY = $fromFile.Trim()
    }
}

if ($PrepareOnly) {
    Write-Host "Local config is ready."
    exit 0
}

if (-not $SkipBuild) {
    $client = Join-Path $Root "TradeMonitor.Client"
    $lock = Join-Path $client "package-lock.json"
    if (-not (Test-Path -LiteralPath $lock)) {
        [Console]::Error.WriteLine("TradeMonitor.Client/package-lock.json was not found.")
        exit 1
    }

    Push-Location $client
    try {
        $modules = Join-Path $client "node_modules"
        if (-not (Test-Path -LiteralPath $modules)) {
            & npm ci
            if ($LASTEXITCODE -ne 0) {
                [Console]::Error.WriteLine("npm ci failed ($LASTEXITCODE).")
                exit 1
            }
        }
        & npm run build
        if ($LASTEXITCODE -ne 0) {
            [Console]::Error.WriteLine("Client build failed ($LASTEXITCODE).")
            exit 1
        }
    } finally {
        Pop-Location
    }

    $dist = Join-Path $client "dist"
    $www = Join-Path $Root "TradeMonitor/wwwroot"
    if (Test-Path -LiteralPath $www) {
        Remove-Item -LiteralPath $www -Recurse -Force
    }
    New-Item -ItemType Directory -Path $www | Out-Null
    Copy-Item -Path (Join-Path $dist "*") -Destination $www -Recurse -Force

    & dotnet build (Join-Path $Root "TradeMonitor/TradeMonitor.csproj") -c Release --nologo
    if ($LASTEXITCODE -ne 0) {
        [Console]::Error.WriteLine("dotnet build failed ($LASTEXITCODE).")
        exit 1
    }
}

Write-Host "Starting TradeMonitor at $env:ASPNETCORE_URLS"
Push-Location $Root
try {
    & dotnet run --project (Join-Path $Root "TradeMonitor/TradeMonitor.csproj") -c Release --no-launch-profile --no-build --nologo
    if ($LASTEXITCODE -ne 0) {
        [Console]::Error.WriteLine("dotnet run failed ($LASTEXITCODE).")
        exit $LASTEXITCODE
    }
} finally {
    Pop-Location
}
