#requires -version 5.1
# 编译面板为单一 EXE（.NET Framework 4.8 内建，无需安装运行时）
param(
    [string]$OutFile = ''
)

$ErrorActionPreference = 'Stop'

$here = Split-Path -Parent $MyInvocation.MyCommand.Path
$src = Join-Path $here 'src'
$csc = Join-Path $env:SystemRoot 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'

if (-not (Test-Path $csc)) {
    throw "未找到 csc.exe，请确认 .NET Framework 4.x 已安装"
}
if (-not (Test-Path $src)) {
    throw "未找到源码目录: $src"
}
if ($OutFile -eq '') {
    $OutFile = Join-Path $here 'dist\AutovisorPanel.exe'
}

$outDir = Split-Path -Parent $OutFile
if (-not (Test-Path $outDir)) { New-Item -ItemType Directory -Path $outDir -Force | Out-Null }

$files = Get-ChildItem -LiteralPath $src -Filter '*.cs' -Recurse | ForEach-Object { $_.FullName }

$cscArgs = @(
    '/target:winexe'
    '/platform:anycpu'
    '/optimize+'
    '/nologo'
    '/win32manifest:' + (Join-Path $src 'app.manifest')
    '/out:' + $OutFile
    '/r:System.dll'
    '/r:System.Core.dll'
    '/r:System.Drawing.dll'
    '/r:System.Windows.Forms.dll'
    '/r:System.Security.dll'
) + $files

Write-Host "编译 $($files.Count) 个源文件 -> $OutFile"
& $csc $cscArgs

if ($LASTEXITCODE -ne 0) {
    throw "编译失败，退出码 $LASTEXITCODE"
}

$info = Get-Item -LiteralPath $OutFile
Write-Host ("编译成功: {0}  ({1:N0} 字节)" -f $info.Name, $info.Length)

# 本地开发：runtime 目录就绪时，把 EXE 放到仓库根，可直接双击运行
$localExe = Join-Path $here 'AutovisorPanel.exe'
if ((Test-Path (Join-Path $here 'runtime\Autovisor.exe')) -and ($localExe -ne $OutFile)) {
    try {
        Copy-Item -LiteralPath $OutFile -Destination $localExe -Force
        Write-Host ("已放置到仓库根: " + $localExe)
    } catch {
        Write-Host ("跳过放置（文件被占用？）: " + $_.Exception.Message)
    }
}
