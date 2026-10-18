<#
.SYNOPSIS
    Compila, prueba y empaqueta el Gestor de Tareas para Windows (.exe) y Android (.apk / .aab).

.EXAMPLE
    ./build.ps1                # tests + Windows + Android
    ./build.ps1 -Target windows
    ./build.ps1 -Target android
    ./build.ps1 -Target test
#>
[CmdletBinding()]
param(
    [ValidateSet('all', 'test', 'windows', 'android')]
    [string]$Target = 'all',

    # Carpeta del ejecutable de Windows. Por defecto: "Cosas Portfolio/task-manager-windows".
    [string]$WindowsOutput = (Join-Path $PSScriptRoot '..\task-manager-windows'),

    # Carpeta de los paquetes de Android.
    [string]$AndroidOutput = (Join-Path $PSScriptRoot 'artifacts\android'),

    [string]$AndroidSdk = $(if ($env:ANDROID_HOME) { $env:ANDROID_HOME } else { Join-Path $env:LOCALAPPDATA 'Android\Sdk' }),

    [string]$JavaSdk = $(if ($env:JAVA_HOME) { $env:JAVA_HOME } else { (Get-ChildItem 'C:\Program Files\Microsoft' -Directory -Filter 'jdk-17*' -ErrorAction SilentlyContinue | Select-Object -First 1).FullName })
)

$ErrorActionPreference = 'Stop'
$app = Join-Path $PSScriptRoot 'src\TaskManager.App\TaskManager.App.csproj'

function Invoke-Step([string]$Title, [scriptblock]$Command) {
    Write-Host "`n==> $Title" -ForegroundColor Cyan
    & $Command
    if ($LASTEXITCODE -ne 0) { throw "Falló: $Title (código $LASTEXITCODE)" }
}

if ($Target -in 'all', 'test') {
    Invoke-Step 'Tests del núcleo' { dotnet test (Join-Path $PSScriptRoot 'tests\TaskManager.Core.Tests') -c Release }
}

if ($Target -in 'all', 'windows') {
    $out = [IO.Path]::GetFullPath($WindowsOutput)
    if (Test-Path $out) { Remove-Item $out -Recurse -Force }
    # Ejecutable sin empaquetar (sin MSIX) y autocontenido: no requiere instalar .NET ni el Windows App SDK.
    Invoke-Step 'Windows (.exe)' {
        dotnet publish $app -f net10.0-windows10.0.19041.0 -c Release -p:RuntimeIdentifierOverride=win-x64 `
            -p:WindowsPackageType=None -p:WindowsAppSDKSelfContained=true -p:SelfContained=true `
            -o $out
    }
    Copy-Item (Join-Path $PSScriptRoot 'packaging\LEEME.txt') $out
    Write-Host "Ejecutable: $(Join-Path $out 'GestorTareas.exe')" -ForegroundColor Green
}

if ($Target -in 'all', 'android') {
    if (-not $JavaSdk -or -not (Test-Path $JavaSdk)) { throw 'No se encontró un JDK 17. Instalalo con: winget install Microsoft.OpenJDK.17' }

    # Descarga el Android SDK (plataforma, build-tools) la primera vez.
    if (-not (Test-Path (Join-Path $AndroidSdk 'platform-tools'))) {
        Invoke-Step 'Android SDK (primera vez)' {
            dotnet build $app -t:InstallAndroidDependencies -f net10.0-android `
                "-p:AndroidSdkDirectory=$AndroidSdk" "-p:JavaSdkDirectory=$JavaSdk" -p:AcceptAndroidSDKLicenses=True
        }
    }

    # Firma: si están definidas las variables ANDROID_KEYSTORE* se firma con esa clave (publicación);
    # si no, con la clave de depuración (sirve para instalar y probar el APK).
    $signing = @()
    if ($env:ANDROID_KEYSTORE) {
        $signing = @('-p:AndroidKeyStore=true', "-p:AndroidSigningKeyStore=$env:ANDROID_KEYSTORE",
            "-p:AndroidSigningKeyAlias=$env:ANDROID_KEYSTORE_ALIAS",
            '-p:AndroidSigningKeyPass=env:ANDROID_KEYSTORE_PASSWORD', '-p:AndroidSigningStorePass=env:ANDROID_KEYSTORE_PASSWORD')
    }

    $tmp = Join-Path $PSScriptRoot 'artifacts\android-publish'
    Invoke-Step 'Android (.apk + .aab)' {
        dotnet publish $app -f net10.0-android -c Release `
            "-p:AndroidSdkDirectory=$AndroidSdk" "-p:JavaSdkDirectory=$JavaSdk" @signing -o $tmp
    }
    New-Item -ItemType Directory -Force $AndroidOutput | Out-Null
    Get-ChildItem $tmp -Include '*-Signed.apk', '*-Signed.aab' -Recurse | Copy-Item -Destination $AndroidOutput -Force
    Get-ChildItem $AndroidOutput | ForEach-Object { Write-Host "Paquete: $($_.FullName)" -ForegroundColor Green }
}
