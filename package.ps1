#requires -version 5.1
# 打包分发：面板 EXE + 上游 runtime 目录 -> 单个 zip
param(
    [string]$Version = '1.0.0',
    [switch]$SkipBuild
)

$ErrorActionPreference = 'Stop'

$here = Split-Path -Parent $MyInvocation.MyCommand.Path
$dist = Join-Path $here 'dist'
$runtime = Join-Path $here 'runtime'
$stage = Join-Path $dist 'stage'

if (-not $SkipBuild) {
    & (Join-Path $here 'build.ps1')
}

$panelExe = Join-Path $dist 'AutovisorPanel.exe'
if (-not (Test-Path $panelExe)) { throw "未找到编译产物: $panelExe" }

if (-not (Test-Path (Join-Path $runtime 'Autovisor.exe'))) {
    throw "runtime 目录不完整，请把上游发行包解压到 $runtime"
}
foreach ($need in 'internal', 'resources', 'data') {
    if (-not (Test-Path (Join-Path $runtime $need))) {
        throw "runtime 缺少 $need 目录"
    }
}

if (Test-Path $stage) { Remove-Item -LiteralPath $stage -Recurse -Force }
New-Item -ItemType Directory -Path $stage -Force | Out-Null
New-Item -ItemType Directory -Path (Join-Path $stage 'runtime') -Force | Out-Null

# 1. 面板
Copy-Item -LiteralPath $panelExe -Destination $stage -Force

# 2. 配置模板
$tpl = Join-Path $here 'config.template.ini'
if (Test-Path $tpl) {
    Copy-Item -LiteralPath $tpl -Destination (Join-Path $stage 'config.ini') -Force
}

# 3. 上游 runtime（排除日志、依赖缓存、凭据）
$excluded = @('logs', 'packages')
Get-ChildItem -LiteralPath $runtime -Force | Where-Object { $excluded -notcontains $_.Name } | ForEach-Object {
    Copy-Item -LiteralPath $_.FullName -Destination (Join-Path $stage 'runtime') -Recurse -Force
}
foreach ($bad in 'data\cookies.json', 'data\cred.bin') {
    $p = Join-Path $stage ('runtime\' + $bad)
    if (Test-Path $p) { Remove-Item -LiteralPath $p -Force }
}

# 4. 说明文档与许可证
$doc = Join-Path $here '使用说明.txt'
if (-not (Test-Path $doc)) { throw "缺少 使用说明.txt" }
Copy-Item -LiteralPath $doc -Destination $stage -Force
Copy-Item -LiteralPath (Join-Path $here 'LICENSE') -Destination (Join-Path $stage 'LICENSE.txt') -Force
# 5. 压缩
$zip = Join-Path $dist ("AutovisorPanel-{0}.zip" -f $Version)
if (Test-Path $zip) { Remove-Item -LiteralPath $zip -Force }
Compress-Archive -Path (Join-Path $stage '*') -DestinationPath $zip -CompressionLevel Optimal
Remove-Item -LiteralPath $stage -Recurse -Force

$info = Get-Item -LiteralPath $zip
Write-Host ("打包完成: {0}  ({1:N1} MB)" -f $info.Name, ($info.Length / 1MB))
