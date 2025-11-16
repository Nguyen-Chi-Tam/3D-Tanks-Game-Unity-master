<#
ClearUnitySavedData.ps1

Removes Unity PlayerPrefs (registry) and persistent data folder for a given
Company/Product combination on Windows. Use this before building or distributing
to ensure a clean test environment.

USAGE (PowerShell):
  pwsh -ExecutionPolicy Bypass -File .\BuildTools\ClearUnitySavedData.ps1
  # or with custom names
  pwsh -ExecutionPolicy Bypass -File .\BuildTools\ClearUnitySavedData.ps1 -Company "MyStudio" -Product "Tank Game"

PARAMETERS:
  -Company : Unity PlayerSettings company name (default: DefaultCompany)
  -Product : Unity PlayerSettings product name  (default: Tank Game)
  -Confirm : Add -Confirm:$false to suppress interactive prompt.

SAFEGUARDS:
  Prompts for confirmation unless -Force or -Confirm:$false is specified.

WHAT IT DELETES:
  1. HKCU:\Software\<Company>\<Product>                (Standalone PlayerPrefs)
  2. HKCU:\Software\Unity\UnityEditor\<Company>\<Product> (Editor PlayerPrefs)
  3. %USERPROFILE%\AppData\LocalLow\<Company>\<Product>   (persistentDataPath contents)

#>

param(
    [string]$Company = "DefaultCompany",
    [string]$Product = "Tank Game",
    [switch]$Force
)

function Remove-RegistryPathIfExists {
    param([string]$Path)
    if (Test-Path $Path) {
        try {
            Remove-Item -Path $Path -Recurse -Force -ErrorAction Stop
            Write-Host "Removed registry path: $Path" -ForegroundColor Green
        } catch {
            Write-Warning "Failed to remove $Path : $($_.Exception.Message)"
        }
    } else {
        Write-Host "Registry path not found (skip): $Path" -ForegroundColor DarkGray
    }
}

function Remove-FolderIfExists {
    param([string]$Path)
    if (Test-Path $Path) {
        try {
            Remove-Item -LiteralPath $Path -Recurse -Force -ErrorAction Stop
            Write-Host "Removed folder: $Path" -ForegroundColor Green
        } catch {
            Write-Warning "Failed to remove folder $Path : $($_.Exception.Message)"
        }
    } else {
        Write-Host "Folder not found (skip): $Path" -ForegroundColor DarkGray
    }
}

$standalonePrefs = "HKCU:\Software\$Company\$Product"
$editorPrefs     = "HKCU:\Software\Unity\UnityEditor\$Company\$Product"
$persistentPath  = Join-Path $env:USERPROFILE "AppData/LocalLow/$Company/$Product"

Write-Host "Unity Saved Data Cleanup" -ForegroundColor Cyan
Write-Host " Company : $Company" -ForegroundColor Cyan
Write-Host " Product : $Product" -ForegroundColor Cyan
Write-Host "----------------------------------------" -ForegroundColor Cyan
Write-Host "Targets:" -ForegroundColor Yellow
Write-Host "  PlayerPrefs (Standalone): $standalonePrefs"
Write-Host "  PlayerPrefs (Editor)    : $editorPrefs"
Write-Host "  persistentDataPath      : $persistentPath"

if (-not $Force) {
    $answer = Read-Host "Proceed with deletion? (y/N)"
    if ($answer.ToLower() -ne 'y') {
        Write-Host "Aborted by user." -ForegroundColor Red
        exit 1
    }
}

Remove-RegistryPathIfExists -Path $standalonePrefs
Remove-RegistryPathIfExists -Path $editorPrefs
Remove-FolderIfExists       -Path $persistentPath

Write-Host "Cleanup completed." -ForegroundColor Green
exit 0
