$ErrorActionPreference = 'Stop'
$framework = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319'
if (!(Test-Path (Join-Path $framework 'csc.exe'))) {
    $framework = Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319'
}
$wpf = Join-Path $framework 'WPF'
$sources = @(Get-ChildItem -LiteralPath (Join-Path $PSScriptRoot 'src') -Filter '*.cs' | ForEach-Object FullName)
$theme = Join-Path $PSScriptRoot 'src\Theme.xaml'
$icon = Join-Path $PSScriptRoot 'src\TaskDesk.ico'
$out = Join-Path $PSScriptRoot 'TaskDesk.exe'
$references = @('PresentationFramework','PresentationCore','WindowsBase','System.Xaml') | ForEach-Object {
    $dll = Join-Path $wpf ($_ + '.dll')
    if (!(Test-Path $dll)) { $dll = Join-Path $framework ($_ + '.dll') }
    '/reference:' + $dll
}
& (Join-Path $framework 'csc.exe') /nologo /target:winexe /platform:anycpu /optimize+ /codepage:65001 "/win32icon:$icon" "/resource:$theme,Theme.xaml" "/out:$out" $references $sources
if ($LASTEXITCODE -ne 0) {throw 'C# compilation failed.'}
