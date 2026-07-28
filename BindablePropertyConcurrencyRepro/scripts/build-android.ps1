[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')]
    [string] $Configuration = 'Release'
)

$ErrorActionPreference = 'Stop'

$project = Join-Path $PSScriptRoot '..\BindablePropertyConcurrencyRepro.csproj' | Resolve-Path

Write-Host "Building $Configuration for net10.0-android" -ForegroundColor Cyan
& dotnet build $project -f net10.0-android -c $Configuration -v minimal
