param(
    [ValidateSet("Development", "Release")]
    [string]$Configuration = "Development",
    [string]$UnityPath = $env:NAZIKI_UNITY_PATH
)

$ErrorActionPreference = "Stop"
$requiredVersion = "6000.0.80f1"
$workspaceRoot = Split-Path -Parent $PSScriptRoot
$projectPath = Join-Path $workspaceRoot "External\original_player\engines\unity"

if ([string]::IsNullOrWhiteSpace($UnityPath)) {
    $UnityPath = "C:\Program Files\Unity\Hub\Editor\$requiredVersion\Editor\Unity.exe"
}
if (-not (Test-Path -LiteralPath $UnityPath)) {
    throw "Unity $requiredVersion was not found. Install Unity Editor and Windows Build Support, or pass -UnityPath / set NAZIKI_UNITY_PATH."
}

$method = if ($Configuration -eq "Release") {
    "CytoidCoreBuild.BuildWindowsEditorPreviewRelease"
} else {
    "CytoidCoreBuild.BuildWindowsEditorPreview"
}
$logDirectory = Join-Path $workspaceRoot "Runtime\OriginalPlayer"
New-Item -ItemType Directory -Path $logDirectory -Force | Out-Null
$logPath = Join-Path $logDirectory "unity-build.log"

# Do not add -quit. CytoidCoreBuild survives target-switch domain reloads through
# EditorPrefs and calls EditorApplication.Exit itself after the build completes.
# Unity.exe is a GUI-subsystem executable, so invoking it directly does not
# reliably populate $LASTEXITCODE. Wait on the process and inspect ExitCode.
$unityProcess = Start-Process `
    -FilePath $UnityPath `
    -ArgumentList @(
        "-batchmode",
        "-projectPath", "`"$projectPath`"",
        "-executeMethod", $method,
        "-logFile", "`"$logPath`"") `
    -WindowStyle Hidden `
    -Wait `
    -PassThru

if ($unityProcess.ExitCode -ne 0) {
    throw "Unity Preview build failed with exit code $($unityProcess.ExitCode). See $logPath."
}

$player = Join-Path $logDirectory "NazikiOriginalPlayer.exe"
if (-not (Test-Path -LiteralPath $player)) {
    throw "Unity reported success but did not create $player."
}
Write-Host "Unity Original Player built successfully: $player"
