param(
    [ValidateSet("Development", "Release")]
    [string]$Configuration = "Development",
    [string]$UnityPath = $env:NAZIKI_UNITY_PATH
)

$ErrorActionPreference = "Stop"
$requiredVersion = "6000.0.75f1"
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
& $UnityPath `
    -batchmode `
    -projectPath $projectPath `
    -executeMethod $method `
    -logFile $logPath

if ($LASTEXITCODE -ne 0) {
    throw "Unity Preview build failed with exit code $LASTEXITCODE. See $logPath."
}

$player = Join-Path $logDirectory "NazikiOriginalPlayer.exe"
if (-not (Test-Path -LiteralPath $player)) {
    throw "Unity reported success but did not create $player."
}
Write-Host "Unity Original Player built successfully: $player"
