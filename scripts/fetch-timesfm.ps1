# Downloads the TimesFM 2.5 ONNX export into App_Data. The file is gitignored.
# License: Apache-2.0 (google/timesfm-2.5-200m-transformers and pdufour/timesfm-2.5-200m-transformers-onnx).
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$dest = Join-Path $root 'TradeMonitor\App_Data\models\timesfm-2.5'
New-Item -ItemType Directory -Force -Path $dest | Out-Null
$onnx = Join-Path $dest 'model.onnx'
$url = 'https://huggingface.co/pdufour/timesfm-2.5-200m-transformers-onnx/resolve/main/onnx/model.onnx'
$hash = '4d6b58f3d21f6ffa1171f230d0b8001028d218e9b976bd60b3d7043b4033a751'
if (Test-Path $onnx) {
    $current = (Get-FileHash -Algorithm SHA256 -Path $onnx).Hash.ToLowerInvariant()
    if ($current -eq $hash) {
        Write-Host 'TimesFM 2.5 ONNX is already present.'
        exit 0
    }
}
Write-Host 'Downloading TimesFM 2.5 ONNX (about 956 MB). Apache-2.0. The file is not committed.'
Invoke-WebRequest -Uri $url -OutFile $onnx
$got = (Get-FileHash -Algorithm SHA256 -Path $onnx).Hash.ToLowerInvariant()
if ($got -ne $hash) {
    Remove-Item $onnx
    throw "Hash mismatch: $got"
}
Write-Host 'Saved TradeMonitor\App_Data\models\timesfm-2.5\model.onnx'
