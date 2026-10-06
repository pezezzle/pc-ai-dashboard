param([switch]$Publish,[switch]$Verify)
$ErrorActionPreference='Stop'
Push-Location $PSScriptRoot
try {
    npm ci
    if($LASTEXITCODE -ne 0) { throw 'npm ci fehlgeschlagen' }
    npm run build
    if($LASTEXITCODE -ne 0) { throw 'TypeScript-Build fehlgeschlagen' }
    dotnet build PcAiDashboard.slnx -c Release
    if($LASTEXITCODE -ne 0) { throw '.NET-Build fehlgeschlagen' }
    if($Verify) {
        dotnet run --project tests\PcAiDashboard.Checks -c Release --no-build
        if($LASTEXITCODE -ne 0) { throw 'Datenprüfungen fehlgeschlagen' }
        npx playwright test
        if($LASTEXITCODE -ne 0) { throw 'Oberflächenprüfungen fehlgeschlagen' }
    }
    if($Publish) {
        dotnet publish src\PcAiDashboard\PcAiDashboard.csproj -c Release -r win-x64 --self-contained true -o artifacts\app
        if($LASTEXITCODE -ne 0) { throw 'Publish fehlgeschlagen' }
        Write-Host "Startbereit: $PSScriptRoot\artifacts\app\PcAiDashboard.exe"
    }
} finally { Pop-Location }
