#!/usr/bin/env pwsh

# SECTION: Environment validation
if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
    Write-Error "dotnet CLI is required to publish the application."
    exit 1
}

# SECTION: Restore dependencies
if (-not (Test-Path "node_modules")) {
    Write-Host "Restoring Node.js dependencies to ensure bundled assets are present..."
    npm install --ignore-scripts
}

# SECTION: Publish output
$publishRoot = Join-Path (Get-Location) "publish"
Write-Host "Creating publish output under $publishRoot..."

dotnet publish ProjectManagement.csproj `
    --configuration Release `
    --output $publishRoot `
    /p:UseAppHost=false

Write-Host "Publish folder created at $publishRoot."
