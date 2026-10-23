[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Release',

    [switch]$NoRestore,

    [switch]$NoBuild,

    # 只改桌面端时跳过 .NET 测试（xunit + Updater Verification），节省约 15 秒。
    [switch]$SkipDotnet,

    # 只改 C# 时跳过桌面端检查（vue-tsc / eslint / vitest / node:test），节省约 60 秒。
    [switch]$SkipDesktop,

    # 只跑匹配的 .NET 测试（dotnet test --filter），内循环建议搭配 -SkipDesktop。
    [string]$DotnetFilter
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

. (Join-Path $PSScriptRoot 'common.ps1')
Assert-SolutionExists

if ($SkipDotnet -and $SkipDesktop) {
    throw '不能同时跳过 .NET 和桌面端测试：请至少验证一侧。'
}

if (-not $SkipDotnet) {
    if (-not $NoRestore) {
        Invoke-DotNet -Arguments @(
            'restore',
            $script:SolutionPath,
            '--configfile', $script:NuGetConfigPath,
            '--verbosity', 'minimal'
        )
    }

    $resultsDirectory = Join-Path (Get-RepositoryRoot) 'artifacts\TestResults'
    New-Item -ItemType Directory -Path $resultsDirectory -Force | Out-Null

    $testArguments = @(
        'test',
        $script:SolutionPath,
        '--configuration', $Configuration,
        '--no-restore',
        '--logger', 'trx',
        '--results-directory', $resultsDirectory,
        '--nologo'
    )

    if ($NoBuild) {
        $testArguments += '--no-build'
    }

    if (-not [string]::IsNullOrWhiteSpace($DotnetFilter)) {
        $testArguments += @('--filter', $DotnetFilter)
    }

    Invoke-DotNet -Arguments $testArguments

    $verificationArguments = @(
        'run',
        '--project', (Join-Path (Get-RepositoryRoot) 'src\Loopstructor.AutoPlayer.Updater\Verification\Verification.csproj'),
        '--configuration', $Configuration,
        '--no-restore'
    )
    if ($NoBuild) {
        $verificationArguments += '--no-build'
    }

    Invoke-DotNet -Arguments $verificationArguments
}

if (-not $SkipDesktop) {
    Invoke-Pnpm -Arguments @('typecheck')
    Invoke-Pnpm -Arguments @('lint')
    Invoke-Pnpm -Arguments @('test')
}
