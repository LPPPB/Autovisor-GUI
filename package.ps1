#requires -version 5.1
param(
    [Parameter(Mandatory = $true)]
    [string]$UpstreamArchive,
    [ValidatePattern('^[0-9]+\.[0-9]+\.[0-9]+$')]
    [string]$Version = '1.6.0'
)

$ErrorActionPreference = 'Stop'
$here = $PSScriptRoot
$dist = Join-Path $here 'dist'
$stage = Join-Path $dist ('.stage-' + [guid]::NewGuid().ToString('N'))
$zip = Join-Path $dist ("AutovisorPanel-{0}.zip" -f $Version)
$upstreamSha256 = '11cea9e9ac1d9f122041cd82c7e6befcc891487396f16e82386f06cf2f8ea32d'

if ((Get-FileHash -LiteralPath $UpstreamArchive -Algorithm SHA256).Hash -ne $upstreamSha256) {
    throw 'Expected the official Autovisor 3.18.4 Windows AMD64 ZIP with the pinned SHA-256.'
}
if (Test-Path -LiteralPath $zip) { throw "Archive already exists: $zip" }

Add-Type -AssemblyName System.IO.Compression.FileSystem
New-Item -ItemType Directory -Path $stage -Force | Out-Null
try {
    $archive = [IO.Compression.ZipFile]::OpenRead((Resolve-Path -LiteralPath $UpstreamArchive).Path)
    try {
        $executables = @($archive.Entries | Where-Object { ($_.FullName -replace '\\', '/') -match '(^|/)Autovisor\.exe$' })
        if ($executables.Count -ne 1) { throw 'Expected exactly one upstream Autovisor.exe.' }
        $exeName = $executables[0].FullName -replace '\\', '/'
        $prefix = $exeName.Substring(0, $exeName.Length - 'Autovisor.exe'.Length)
        $fixedFiles = @('Autovisor.exe', 'resources/QRcode.jpg', 'resources/stealth.min.js', 'data/mirrors.json')
        $runtimeDir = [IO.Path]::GetFullPath((Join-Path $stage 'runtime'))
        $runtimePrefix = $runtimeDir + [IO.Path]::DirectorySeparatorChar

        foreach ($entry in $archive.Entries) {
            $name = $entry.FullName -replace '\\', '/'
            if (!$name.StartsWith($prefix, [StringComparison]::Ordinal) -or $name.EndsWith('/')) { continue }
            $relative = $name.Substring($prefix.Length)
            if ($relative -notin $fixedFiles -and !$relative.StartsWith('internal/', [StringComparison]::Ordinal)) { continue }

            # Certifi ships a public CA bundle. Every other credential-shaped runtime file is rejected.
            if ($relative -ne 'internal/certifi/cacert.pem' -and $relative -match '(?i)(^|/)(?:\.env(?:\.[^/]+)?|logs?|packages|cookies?\.(?:json|txt)|cred\.bin|credentials?(?:\.[^/]+)?|config\.ini|id_(?:rsa|dsa|ecdsa|ed25519)|[^/]+\.(?:log|key|pfx|p12|pem))$') {
                throw "Unexpected runtime entry: $relative"
            }
            if ($relative -match '(^|/)\.\.?(/|$)|:') { throw "Invalid runtime entry: $relative" }
            $target = [IO.Path]::GetFullPath((Join-Path $runtimeDir $relative))
            if (!$target.StartsWith($runtimePrefix, [StringComparison]::OrdinalIgnoreCase)) {
                throw "Runtime entry escapes the staging directory: $relative"
            }
            New-Item -ItemType Directory -Path ([IO.Path]::GetDirectoryName($target)) -Force | Out-Null
            [IO.Compression.ZipFileExtensions]::ExtractToFile($entry, $target)
        }
        foreach ($required in $fixedFiles + 'internal/playwright/driver/node.exe') {
            if (!(Test-Path -LiteralPath (Join-Path $runtimeDir $required) -PathType Leaf)) {
                throw "Missing required runtime file: $required"
            }
        }
    }
    finally { $archive.Dispose() }

    $template = Join-Path $here 'config.template.ini'
    $ini = Get-Content -LiteralPath $template -Raw -Encoding UTF8
    if ($ini -match '(?im)^[ \t]*(?:username|password|URL[0-9]+)[ \t]*=[ \t]*\S' -or
        $ini -notmatch '(?im)^username[ \t]*=[ \t]*\r?$' -or
        $ini -notmatch '(?im)^password[ \t]*=[ \t]*\r?$') {
        throw 'Distribution configuration must have blank account, password and course entries.'
    }
    Copy-Item -LiteralPath $template -Destination (Join-Path $stage 'config.ini')
    Copy-Item -LiteralPath (Join-Path $here '使用说明.txt') -Destination $stage
    Copy-Item -LiteralPath (Join-Path $here 'LICENSE') -Destination (Join-Path $stage 'LICENSE.txt')
    Copy-Item -LiteralPath (Join-Path $here 'LICENSE.upstream.txt') -Destination $stage
    & (Join-Path $here 'build.ps1') -OutFile (Join-Path $stage 'AutovisorPanel.exe') -NoLocalCopy

    [IO.Compression.ZipFile]::CreateFromDirectory($stage, $zip, [IO.Compression.CompressionLevel]::Optimal, $false)
    $hash = (Get-FileHash -LiteralPath $zip -Algorithm SHA256).Hash.ToLowerInvariant()
    Set-Content -LiteralPath ($zip + '.sha256') -Encoding Ascii -Value ($hash + '  ' + [IO.Path]::GetFileName($zip))
    Write-Host "Package: $zip"
    Write-Host "SHA-256: $hash"
}
finally {
    $resolvedStage = (Resolve-Path -LiteralPath $stage).Path
    $resolvedDist = (Resolve-Path -LiteralPath $dist).Path
    if ($resolvedStage.StartsWith($resolvedDist + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase) -and
        [IO.Path]::GetFileName($resolvedStage).StartsWith('.stage-', [StringComparison]::Ordinal)) {
        Remove-Item -LiteralPath $resolvedStage -Recurse -Force
    }
}
