# Builds the Parallel Edit plugin and copies it into Paratext 9's plugins folder.
# Usage:  pwsh -File install.ps1            (build + install)
#         pwsh -File install.ps1 -Uninstall
# Paratext must be closed. Asks for administrator rights (Program Files is protected).
param(
    [string]$ParatextDir = "C:\Program Files\Paratext 9",
    [switch]$Uninstall,
    [switch]$NoBuild,
    [string]$Built  # internal: path of the built DLL, passed to the elevated copy step
)
$ErrorActionPreference = "Stop"
$target = Join-Path $ParatextDir "plugins\ParallelEdit"

function Test-Admin {
    ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
}

if (-not (Test-Path (Join-Path $ParatextDir "Paratext.exe"))) { throw "Paratext 9 not found in $ParatextDir (use -ParatextDir)." }
if (Get-Process Paratext -ErrorAction SilentlyContinue) { throw "Please close Paratext first, then run this again." }

if (-not $Uninstall -and -not $Built) {
    $dotnet = (Get-Command dotnet -ErrorAction SilentlyContinue).Source
    $userDotnet = Join-Path $env:USERPROFILE ".dotnet\dotnet.exe"
    if (Test-Path $userDotnet) { $dotnet = $userDotnet }
    $project = Join-Path $PSScriptRoot "src\ParallelEdit\ParallelEdit.csproj"
    if (-not $NoBuild) {
        & $dotnet build $project -c Release -nologo -v q "-p:ParatextInstallDir=$ParatextDir"
        if ($LASTEXITCODE -ne 0) { throw "Build failed." }
    }
    $Built = Join-Path $PSScriptRoot "src\ParallelEdit\bin\Release\net48\ParallelEdit.dll"
    if (-not (Test-Path $Built)) { throw "Built plugin not found: $Built" }
}

if (-not (Test-Admin)) {
    $shell = (Get-Process -Id $PID).Path
    $argList = @("-NoProfile", "-ExecutionPolicy", "Bypass", "-File", "`"$PSCommandPath`"", "-ParatextDir", "`"$ParatextDir`"")
    if ($Uninstall) { $argList += "-Uninstall" } else { $argList += @("-Built", "`"$Built`"") }
    $p = Start-Process $shell -ArgumentList $argList -Verb RunAs -Wait -PassThru
    if ($p.ExitCode -ne 0) { throw "The administrator step failed (exit code $($p.ExitCode))." }
} elseif ($Uninstall) {
    if (Test-Path $target) { Remove-Item $target -Recurse -Force }
} else {
    New-Item -ItemType Directory -Force $target | Out-Null
    Copy-Item $Built (Join-Path $target "ParallelEdit.ptxplg") -Force
    $pdb = [IO.Path]::ChangeExtension($Built, ".pdb")
    if (Test-Path $pdb) { Copy-Item $pdb (Join-Path $target "ParallelEdit.pdb") -Force }
}

if ($Uninstall) {
    if (Test-Path $target) { throw "Uninstall did not remove $target" }
    Write-Host "Parallel Edit removed."
} else {
    if (-not (Test-Path (Join-Path $target "ParallelEdit.ptxplg"))) { throw "Install did not complete." }
    Write-Host "Parallel Edit installed to $target"
    Write-Host "Start Paratext, open a project, then choose Tools > Parallel Edit... in that project's menu."
}
