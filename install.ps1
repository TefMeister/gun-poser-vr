# install.ps1 -- copies Gun Poser from dist\ to %LOCALAPPDATA%\Programs\GunPoser and puts its icon on the desktop.
#   powershell -ExecutionPolicy Bypass -File install.ps1 [-From <folder with the built files>]
param([string]$From = "")
if ($From -eq "") { $From = if (Test-Path (Join-Path $PSScriptRoot "dist\GunPoser.exe")) { Join-Path $PSScriptRoot "dist" } else { $PSScriptRoot } }
$ErrorActionPreference = "Stop"
$to = Join-Path $env:LOCALAPPDATA "Programs\GunPoser"
New-Item -ItemType Directory -Force $to | Out-Null
foreach ($f in "GunPoser.exe", "gun_poser.ico") { Copy-Item (Join-Path $From $f) (Join-Path $to $f) -Force }
# until 2026-10-28 Gun Poser came inside Grip Zones' download: that old copy goes, so only one is left
$old = Join-Path $env:LOCALAPPDATA "Programs\GripZones\GunPoser.exe"
if (Test-Path $old) { Remove-Item $old -Force }
$shell = New-Object -ComObject WScript.Shell
$lnk = $shell.CreateShortcut((Join-Path ([Environment]::GetFolderPath("Desktop")) "Gun Poser.lnk"))
$lnk.TargetPath = Join-Path $to "GunPoser.exe"
$lnk.WorkingDirectory = $to
$lnk.IconLocation = (Join-Path $to "gun_poser.ico") + ",0"
$lnk.Description = "Aim and move the gun in the right hand in VR, per gun"
$lnk.Save()
Write-Output "Installed to $to, icon on the desktop."
