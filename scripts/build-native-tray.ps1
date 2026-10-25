param([string]$Compiler)
$ErrorActionPreference='Stop'
$root=Split-Path -Parent $PSScriptRoot
if(!$Compiler){$Compiler=$env:LAN_TRAY_CXX}
if(!$Compiler){
    $shared=Join-Path ([IO.Directory]::GetParent($root).FullName) '【Common】\LLVM-MinGW\llvm-mingw-20260922-ucrt-x86_64\bin\clang++.exe'
    if(Test-Path -LiteralPath $shared){$Compiler=$shared}
    else {$Compiler=(Get-Command clang++.exe -ErrorAction Stop).Source}
}
$source=Join-Path $root 'src\LanDeviceMonitor.App\Native\TrayRuntime.cpp'
$output=Join-Path $root '.tools\native-tray'
New-Item -ItemType Directory -Path $output -Force | Out-Null
& $Compiler '-std=c++20' '-O2' '-shared' '-static' '-DUNICODE' '-D_UNICODE' '-D_WIN32_WINNT=0x0A00' $source '-o' (Join-Path $output 'LanDeviceMonitor.TrayRuntime.dll') '-luser32' '-lshell32'
if($LASTEXITCODE -ne 0){throw "Native tray build failed ($LASTEXITCODE)"}
