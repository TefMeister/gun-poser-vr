@echo off
rem Builds the Gun Poser window into dist\ (C#, the compiler that ships with Windows' .NET Framework 4), icon beside it.
rem shared\ is a copy of Grip Zones' window files (look, sliders, tick boxes, game folder, gun names); see shared\README.md.
rem Then install.ps1 puts the icon on the desktop. The game half is Visceral RE8 itself (its zones script), installed with that mod.
setlocal
set HERE=%~dp0
if not exist "%HERE%dist" mkdir "%HERE%dist"
"%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe" /nologo /target:winexe /optimize+ /win32icon:"%HERE%app\gun_poser.ico" ^
   /main:GunPoserProgram /out:"%HERE%dist\GunPoser.exe" "%HERE%app\*.cs" "%HERE%shared\*.cs"
if errorlevel 1 (echo BUILD FAILED & exit /b 1)
copy /y "%HERE%app\gun_poser.ico" "%HERE%dist\" >nul
echo BUILD OK: %HERE%dist
