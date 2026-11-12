# Shared helpers for the host scripts. Do not print API keys.

function Test-LoopbackUrl([string]$Url) {
    if ([string]::IsNullOrWhiteSpace($Url)) { return $false }
    try { $uri = [Uri]$Url } catch { return $false }
    if (-not $uri.IsAbsoluteUri) { return $false }
    if ($uri.Scheme -ne "http" -and $uri.Scheme -ne "https") { return $false }
    $hostName = $uri.Host
    if ([string]::IsNullOrWhiteSpace($hostName)) { return $false }
    if ($hostName -eq "localhost") { return $true }
    $parsed = $null
    if ([System.Net.IPAddress]::TryParse($hostName, [ref]$parsed)) {
        return [System.Net.IPAddress]::IsLoopback($parsed)
    }
    return $false
}

function ConvertTo-Ipv4LoopbackUrl([string]$Url) {
    if (-not (Test-LoopbackUrl $Url)) {
        throw "Refusing to bind '$Url'. The host listens on 127.0.0.1 only."
    }
    $uri = [Uri]$Url
    $builder = New-Object System.UriBuilder $uri
    $builder.Host = "127.0.0.1"
    return $builder.Uri.GetComponents([System.UriComponents]::SchemeAndServer, [System.UriFormat]::UriEscaped)
}

function ConvertTo-LoopbackUrls([string]$Urls) {
    $parts = @($Urls -split ';' | ForEach-Object { $_.Trim() } | Where-Object { $_.Length -gt 0 })
    if ($parts.Count -eq 0) {
        throw "Refusing to bind an empty URL. The host listens on 127.0.0.1 only."
    }
    $normalized = @()
    foreach ($part in $parts) {
        $normalized += (ConvertTo-Ipv4LoopbackUrl $part)
    }
    return ($normalized -join ";")
}

function Resolve-HostUrls([string]$Urls) {
    if ($env:ASPNETCORE_URLS) {
        foreach ($part in ($env:ASPNETCORE_URLS -split ';')) {
            $part = $part.Trim()
            if ($part.Length -eq 0) { continue }
            if (-not (Test-LoopbackUrl $part)) {
                throw "Refusing to bind '$part'. The host listens on 127.0.0.1 only."
            }
        }
    }
    return (ConvertTo-LoopbackUrls $Urls)
}

function Test-McpKey([string]$Key) {
    if ([string]::IsNullOrWhiteSpace($Key)) { return $false }
    return $Key.Trim().Length -ge 8
}

function Get-KeyPrefix([string]$Key) {
    if ([string]::IsNullOrEmpty($Key)) { return "" }
    $count = 4
    if ($Key.Length -lt 4) { $count = $Key.Length }
    return $Key.Substring(0, $count)
}

function New-McpApiKey {
    $bytes = New-Object byte[] 32
    $rng = [System.Security.Cryptography.RandomNumberGenerator]::Create()
    try {
        $rng.GetBytes($bytes)
    } finally {
        $rng.Dispose()
    }
    $encoded = [Convert]::ToBase64String($bytes).TrimEnd("=").Replace("+", "-").Replace("/", "_")
    return $encoded
}

function Get-DotEnvValue([string]$Path, [string]$Name) {
    if (-not (Test-Path -LiteralPath $Path)) { return $null }
    foreach ($raw in @(Get-Content -LiteralPath $Path)) {
        $line = $raw.Trim()
        if ($line.Length -eq 0 -or $line.StartsWith("#")) { continue }
        $split = $line.IndexOf("=")
        if ($split -le 0) { continue }
        $keyName = $line.Substring(0, $split).Trim()
        if ($keyName -ne $Name) { continue }
        $value = $line.Substring($split + 1).Trim()
        if ($value.Length -ge 2 -and $value.StartsWith('"') -and $value.EndsWith('"')) {
            $value = $value.Substring(1, $value.Length - 2)
        }
        return $value
    }
    return $null
}

function Set-DotEnvValue([string]$Path, [string]$Name, [string]$Value) {
    $lines = @()
    if (Test-Path -LiteralPath $Path) {
        $lines = @(Get-Content -LiteralPath $Path)
    }
    $found = $false
    $updated = New-Object System.Collections.Generic.List[string]
    foreach ($raw in $lines) {
        $trim = $raw.Trim()
        $replaced = $false
        if (-not $trim.StartsWith("#")) {
            $split = $trim.IndexOf("=")
            if ($split -gt 0) {
                $keyName = $trim.Substring(0, $split).Trim()
                if ($keyName -eq $Name) {
                    $updated.Add("$Name=$Value")
                    $found = $true
                    $replaced = $true
                }
            }
        }
        if (-not $replaced) { $updated.Add($raw) }
    }
    if (-not $found) { $updated.Add("$Name=$Value") }
    $text = [string]::Join("`n", $updated.ToArray())
    if (-not $text.EndsWith("`n")) { $text = $text + "`n" }
    [System.IO.File]::WriteAllText($Path, $text)
}

function Import-DotEnvFile([string]$Path) {
    if (-not (Test-Path -LiteralPath $Path)) { return }
    foreach ($raw in @(Get-Content -LiteralPath $Path)) {
        $line = $raw.Trim()
        if ($line.Length -eq 0 -or $line.StartsWith("#")) { continue }
        $split = $line.IndexOf("=")
        if ($split -le 0) { continue }
        $name = $line.Substring(0, $split).Trim()
        if ($name.Length -eq 0) { continue }
        $existing = [Environment]::GetEnvironmentVariable($name)
        if (-not [string]::IsNullOrEmpty($existing)) { continue }
        $value = $line.Substring($split + 1).Trim()
        if ($value.Length -ge 2 -and $value.StartsWith('"') -and $value.EndsWith('"')) {
            $value = $value.Substring(1, $value.Length - 2)
        }
        Set-Item -Path "Env:$name" -Value $value
    }
}

function Initialize-LocalConfig([string]$Root) {
    $example = Join-Path $Root ".env.example"
    $envFile = Join-Path $Root ".env"
    if (-not (Test-Path -LiteralPath $example)) {
        throw ".env.example was not found at $example"
    }
    if (-not (Test-Path -LiteralPath $envFile)) {
        Copy-Item -LiteralPath $example -Destination $envFile
        Write-Host "Created .env from .env.example"
    }

    $envKey = [Environment]::GetEnvironmentVariable("MCP_API_KEY")
    if (Test-McpKey $envKey) {
        Write-Host ("Using MCP_API_KEY from the environment (starts with {0})." -f (Get-KeyPrefix $envKey.Trim()))
        return
    }

    $fileKey = Get-DotEnvValue -Path $envFile -Name "MCP_API_KEY"
    if (Test-McpKey $fileKey) {
        Write-Host ("Using the MCP API key already in .env (starts with {0})." -f (Get-KeyPrefix $fileKey.Trim()))
        return
    }

    $generated = New-McpApiKey
    Set-DotEnvValue -Path $envFile -Name "MCP_API_KEY" -Value $generated
    Write-Host ("Generated a local MCP API key (starts with {0}, {1} characters). The full key is in .env only." -f (Get-KeyPrefix $generated), $generated.Length)
}

# npm ci runs when node_modules is missing or when package-lock.json differs from
# node_modules/.trademonitor-lock.sha256. A missing stamp counts as a change.
# Get-NpmCiReason returns the reason to reinstall, or $null when the install is current.

function Get-FileSha256Hex([string]$Path) {
    $sha = [System.Security.Cryptography.SHA256]::Create()
    try {
        $stream = [System.IO.File]::Open($Path, [System.IO.FileMode]::Open, [System.IO.FileAccess]::Read, [System.IO.FileShare]::ReadWrite)
        try {
            $bytes = $sha.ComputeHash($stream)
        } finally {
            $stream.Dispose()
        }
    } finally {
        $sha.Dispose()
    }
    return ([System.BitConverter]::ToString($bytes)).Replace("-", "").ToLowerInvariant()
}

function Get-NpmCiReason([string]$ClientRoot) {
    $modules = Join-Path $ClientRoot "node_modules"
    if (-not (Test-Path -LiteralPath $modules -PathType Container)) {
        return "node_modules is missing"
    }
    $lock = Join-Path $ClientRoot "package-lock.json"
    if (-not (Test-Path -LiteralPath $lock -PathType Leaf)) {
        throw "TradeMonitor.Client/package-lock.json was not found."
    }
    $current = Get-FileSha256Hex -Path $lock
    $stamp = Join-Path $modules ".trademonitor-lock.sha256"
    if (-not (Test-Path -LiteralPath $stamp -PathType Leaf)) {
        return "package-lock.json changed since the last install"
    }
    $stored = ([System.IO.File]::ReadAllText($stamp)).Trim()
    if ([string]::IsNullOrWhiteSpace($stored)) {
        return "package-lock.json changed since the last install"
    }
    if (-not $stored.Equals($current, [System.StringComparison]::OrdinalIgnoreCase)) {
        return "package-lock.json changed since the last install"
    }
    return $null
}

function Set-NpmLockStamp([string]$ClientRoot) {
    $modules = Join-Path $ClientRoot "node_modules"
    if (-not (Test-Path -LiteralPath $modules -PathType Container)) {
        throw "node_modules is missing, so the lock stamp was not written."
    }
    $lock = Join-Path $ClientRoot "package-lock.json"
    if (-not (Test-Path -LiteralPath $lock -PathType Leaf)) {
        throw "TradeMonitor.Client/package-lock.json was not found."
    }
    $hex = Get-FileSha256Hex -Path $lock
    $stamp = Join-Path $modules ".trademonitor-lock.sha256"
    $utf8 = New-Object System.Text.UTF8Encoding $false
    [System.IO.File]::WriteAllText($stamp, ($hex + "`n"), $utf8)
}
