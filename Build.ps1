param([switch]$Publish,[switch]$Verify)
$ErrorActionPreference='Stop'
Push-Location $PSScriptRoot
try {
    npm ci
    if($LASTEXITCODE -ne 0) { throw 'npm ci failed' }
    npm run build
    if($LASTEXITCODE -ne 0) { throw 'TypeScript build failed' }
    dotnet build PcAiDashboard.slnx -c Release
    if($LASTEXITCODE -ne 0) { throw '.NET build failed' }
    if($Verify) {
        dotnet run --project tests\PcAiDashboard.Checks -c Release --no-build
        if($LASTEXITCODE -ne 0) { throw 'Data checks failed' }
        npx playwright test
        if($LASTEXITCODE -ne 0) { throw 'Interface tests failed' }
    }
    if($Publish) {
        dotnet publish src\PcAiDashboard\PcAiDashboard.csproj -c Release -r win-x64 --self-contained true -o artifacts\app
        if($LASTEXITCODE -ne 0) { throw 'Publish failed' }
        Write-Host "Ready to start: $PSScriptRoot\artifacts\app\PcAiDashboard.exe"
    }
} finally { Pop-Location }
