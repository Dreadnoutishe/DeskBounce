[CmdletBinding()]
param(
    [string]$OutFile
)

$ErrorActionPreference = 'Stop'
if (!$OutFile) {
    $OutFile = Join-Path $PSScriptRoot 'DeskBounce.exe'
}

$compiler = @(
    Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
    Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319\csc.exe'
) | Where-Object { Test-Path -LiteralPath $_ -PathType Leaf } | Select-Object -First 1

if (!$compiler) {
    throw 'The Windows .NET Framework 4 C# compiler was not found.'
}

$destination = $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($OutFile)
$manifest = Join-Path $PSScriptRoot 'DeskBounce.manifest'
$icon = Join-Path $PSScriptRoot 'DeskBounce.ico'
$sample = Join-Path $PSScriptRoot 'Bounce.wav'
$sources = @('Program.cs', 'SingleInstance.cs', 'BallOverlay.cs', 'BallArt.cs', 'BallPhysics.cs',
    'WindowSurfaces.cs', 'BounceAudio.cs') |
    ForEach-Object { Join-Path $PSScriptRoot $_ }

& $compiler /nologo /target:winexe /optimize+ /warn:4 /reference:System.Drawing.dll `
    /reference:System.Windows.Forms.dll "/out:$destination" "/win32manifest:$manifest" "/win32icon:$icon" `
    "/resource:$sample,DeskBounce.Bounce.wav" $sources

if ($LASTEXITCODE -ne 0) {
    throw "Desk Bounce build failed with compiler exit code $LASTEXITCODE."
}

Write-Output $destination
