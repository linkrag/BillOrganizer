# Builds BillOrganizer.dll for RimWorld 1.6 (net472 -> Bin/1.6) and 1.7 (net10.0 -> Bin/1.7). Close the game first (it locks the DLLs).
# Then tools/deploy.ps1 mirrors the public part (About, LoadFolders.xml, 1.6, Bin) into the game's Mods folder
# (skipped with -Out or -NoDeploy). This folder (_dev/src) is the source; Mods/<mod> is only the shipped copy.
# -Out <folder>: build there instead of Bin/ (just to check the compile).
# usage: powershell -File build.ps1 [-Version all|1.6|1.7] [-Out <folder>] [-NoDeploy] [-Workshop <folder>]
param(
    [ValidateSet("all", "1.6", "1.7")][string]$Version = "all",
    [string]$Out = "",
    [switch]$NoDeploy,
    [string]$Workshop = "F:\Steam\steamapps\workshop\content\294100"
)
$ErrorActionPreference = "Stop"
$root = $PSScriptRoot
$frameworks = @{ "1.6" = "net472"; "1.7" = "net10.0" }
$versions = if ($Version -eq "all") { @("1.6", "1.7") } else { @($Version) }
if ($Out) { $Out = $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($Out) }
$props = @("-p:Workshop=$Workshop")
if ($Out) { $props += "-p:ModBin=$Out" }
foreach ($v in $versions) {
    & dotnet build "$root\Source\BillOrganizer.csproj" -c Release -f $frameworks[$v] --nologo -v minimal @props
    if ($LASTEXITCODE -ne 0) { throw "dotnet build failed ($v)" }
}
$bin = (Get-Item $(if ($Out) { $Out } else { "$root\Bin" })).FullName
Get-ChildItem $bin -Recurse -Filter *.dll | ForEach-Object {
    "{0}  {1}" -f (Get-FileHash $_.FullName -Algorithm MD5).Hash, $_.FullName.Substring($bin.Length).TrimStart([char]'\')
}
if (-not $Out -and -not $NoDeploy) {
    & powershell -NoProfile -ExecutionPolicy Bypass -File "$root\tools\deploy.ps1"
    if ($LASTEXITCODE -ne 0) { throw "deploy failed ($LASTEXITCODE)" }
}
