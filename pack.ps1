param(
    [switch]$Push,
    [string]$ApiKey
)

$ErrorActionPreference = 'Stop'

$projects = @(
    "src/McpProxy.Sdk/McpProxy.Sdk.csproj",
    "src/McpProxy/McpProxy.csproj"
    "src/McpProxy.Abstractions/McpProxy.Abstractions.csproj"
)

foreach ($projectPath in $projects) {
    $projectName = [System.IO.Path]::GetFileNameWithoutExtension($projectPath)
    Write-Host "Packing $projectName..." -ForegroundColor Cyan
    dotnet pack $projectPath -c Release -o ./artifacts

    if ($LASTEXITCODE -ne 0) {
        Write-Host "Pack failed for $projectName." -ForegroundColor Red
        exit 1
    }

    Write-Host "Pack succeeded for $projectName." -ForegroundColor Green
}

if ($Push) {
    $key = $ApiKey

    if (-not $key) {
        $key = $env:NUGET_API_KEY
    }

    if (-not $key) {
        Write-Host "No API key provided. Use -ApiKey or set the NUGET_API_KEY environment variable." -ForegroundColor Red
        exit 1
    }

    $packages = Get-ChildItem -Path ./artifacts -Filter "*.nupkg" | Sort-Object LastWriteTime -Descending

    if (-not $packages) {
        Write-Host "No .nupkg files found in ./artifacts." -ForegroundColor Red
        exit 1
    }

    foreach ($package in $packages) {
        Write-Host "Pushing $($package.Name) to nuget.org..." -ForegroundColor Cyan
        dotnet nuget push $package.FullName --api-key $key --source https://api.nuget.org/v3/index.json --skip-duplicate

        if ($LASTEXITCODE -ne 0) {
            Write-Host "Push failed for $($package.Name)." -ForegroundColor Red
            exit 1
        }

        Write-Host "Push succeeded for $($package.Name)." -ForegroundColor Green
    }
}
