[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Release',

    [string]$Version,

    [switch]$NoRestore,

    [switch]$SkipPnpmInstall,

    # 只改桌面端（Electron / Vue / 测试）时跳过 .NET 构建，节省 15-20 秒。
    [switch]$SkipDotnetBuild,

    # 只改 C# 时跳过桌面端构建，节省 Vite/tsc 时间。
    [switch]$SkipDesktopBuild
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

. (Join-Path $PSScriptRoot 'common.ps1')
Assert-SolutionExists

if ($SkipDotnetBuild -and $SkipDesktopBuild) {
    throw '不能同时跳过 .NET 和桌面端构建：请至少构建一侧。'
}

if (-not $SkipDotnetBuild) {
    if (-not $NoRestore) {
        Invoke-DotNet -Arguments @(
            'restore',
            $script:SolutionPath,
            '--configfile', $script:NuGetConfigPath,
            '--verbosity', 'minimal'
        )
    }

    $buildArguments = @(
        'build',
        $script:SolutionPath,
        '--configuration', $Configuration,
        '--no-restore',
        '--nologo'
    )

    if (-not [string]::IsNullOrWhiteSpace($Version)) {
        $buildArguments += "-p:Version=$Version"
    }

    Invoke-DotNet -Arguments $buildArguments
}

if (-not $SkipDesktopBuild) {
    if (-not $SkipPnpmInstall) {
        Invoke-Pnpm -Arguments @('install', '--frozen-lockfile')
    }
    Invoke-Pnpm -Arguments @('build')
}
