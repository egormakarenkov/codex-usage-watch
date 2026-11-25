$ErrorActionPreference = 'Stop'
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
& $compiler /nologo /target:exe /out:"$PSScriptRoot\Tests.exe" /r:System.Web.Extensions.dll "$PSScriptRoot\Core.cs" "$PSScriptRoot\Tests.cs"
if ($LASTEXITCODE -ne 0) { throw 'Test compilation failed.' }
& "$PSScriptRoot\Tests.exe"
if ($LASTEXITCODE -ne 0) { throw 'Tests failed.' }
if (Test-Path -LiteralPath "$PSScriptRoot\App.cs") {
    & $compiler /nologo /target:winexe /optimize+ /out:"$PSScriptRoot\CodexUsageWatch.exe" /r:System.Windows.Forms.dll /r:System.Drawing.dll /r:System.Web.Extensions.dll /r:System.Security.dll /r:System.Net.Http.dll "$PSScriptRoot\Core.cs" "$PSScriptRoot\App.cs"
    if ($LASTEXITCODE -ne 0) { throw 'Application compilation failed.' }
}
