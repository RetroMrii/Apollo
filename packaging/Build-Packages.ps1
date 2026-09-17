param(
    [string]$Version = '1.0.0',
    [string]$RuntimeIdentifier = 'win-x64',
    [string]$InnoCompiler
)

$ErrorActionPreference = 'Stop'
$root = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$artifacts = [System.IO.Path]::GetFullPath((Join-Path $root 'artifacts'))
$publishDirectory = Join-Path $artifacts "publish\$RuntimeIdentifier"
$portableArchive = Join-Path $artifacts "Apollo-$Version-$RuntimeIdentifier-portable.zip"

if (-not $artifacts.StartsWith("$root\", [System.StringComparison]::OrdinalIgnoreCase)) {
    throw "Unexpected artifacts path: $artifacts"
}

if (Test-Path -LiteralPath $artifacts) {
    Remove-Item -LiteralPath $artifacts -Recurse -Force
}

New-Item -ItemType Directory -Path $publishDirectory -Force | Out-Null

& dotnet publish (Join-Path $root 'src\Apollo.App\Apollo.App.csproj') `
    --configuration Release `
    --runtime $RuntimeIdentifier `
    --self-contained true `
    --output $publishDirectory `
    -p:Version=$Version `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:PublishTrimmed=false `
    -p:DebugType=None `
    -p:DebugSymbols=false

if ($LASTEXITCODE -ne 0) {
    throw "dotnet publish failed with exit code $LASTEXITCODE."
}

Copy-Item -LiteralPath (Join-Path $root 'README.md') -Destination (Join-Path $publishDirectory 'README.md')
Compress-Archive -Path (Join-Path $publishDirectory '*') -DestinationPath $portableArchive -CompressionLevel Optimal

if ([string]::IsNullOrWhiteSpace($InnoCompiler)) {
    $compilerCandidates = @(
        (Join-Path $root '.packaging-tools\Inno Setup 7\ISCC.exe'),
        (Join-Path ${env:ProgramFiles} 'Inno Setup 7\ISCC.exe'),
        (Join-Path ${env:ProgramFiles(x86)} 'Inno Setup 7\ISCC.exe'),
        (Join-Path ${env:ProgramFiles(x86)} 'Inno Setup 6\ISCC.exe')
    )
    $InnoCompiler = $compilerCandidates | Where-Object { Test-Path -LiteralPath $_ } | Select-Object -First 1
}

if ([string]::IsNullOrWhiteSpace($InnoCompiler) -or -not (Test-Path -LiteralPath $InnoCompiler)) {
    Write-Warning 'Inno Setup Compiler was not found. The portable package was created; install Inno Setup 7 to build the installer.'
}
else {
    & $InnoCompiler `
        "/DAppVersion=$Version" `
        "/DSourceDir=$publishDirectory" `
        "/DOutputDir=$artifacts" `
        (Join-Path $PSScriptRoot 'Apollo.iss')

    if ($LASTEXITCODE -ne 0) {
        throw "Inno Setup compilation failed with exit code $LASTEXITCODE."
    }
}

$packages = Get-ChildItem -LiteralPath $artifacts -File | Where-Object {
    $_.Extension -in '.zip', '.exe'
}

$checksumLines = foreach ($package in $packages) {
    $hash = Get-FileHash -LiteralPath $package.FullName -Algorithm SHA256
    "$($hash.Hash.ToLowerInvariant())  $($package.Name)"
}

if ($checksumLines.Count -gt 0) {
    Set-Content -LiteralPath (Join-Path $artifacts 'SHA256SUMS.txt') -Value $checksumLines -Encoding ascii
}

$packages | Select-Object Name, Length, LastWriteTime
