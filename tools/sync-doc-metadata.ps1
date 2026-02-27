param(
    [Parameter(Mandatory = $false)]
    [ValidateSet('apply', 'check')]
    [string]$Mode = 'check'
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Get-ProjectXml {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Path
    )

    [xml](Get-Content -Path $Path -Raw -Encoding UTF8)
}

function Get-PackageVersion {
    param(
        [Parameter(Mandatory = $true)]
        [xml]$ProjectXml,
        [Parameter(Mandatory = $true)]
        [string]$PackageId
    )

    $references = @(
        $ProjectXml.Project.ItemGroup |
            ForEach-Object {
                if ($_.PSObject.Properties.Name -contains 'PackageReference') {
                    @($_.PackageReference)
                } else {
                    @()
                }
            } |
            Where-Object { $null -ne $_ }
    )

    $node = $references |
        Where-Object { $_.Include -eq $PackageId } |
        Select-Object -First 1

    if ($null -eq $node -or [string]::IsNullOrWhiteSpace([string]$node.Version)) {
        throw "Could not locate PackageReference '$PackageId'."
    }

    return [string]$node.Version
}

function Convert-TargetFrameworkDisplay {
    param(
        [Parameter(Mandatory = $true)]
        [string]$TargetFramework
    )

    if ($TargetFramework -match '^net(?<major>\d+)\.(?<minor>\d+)$') {
        return ".NET $($Matches.major).$($Matches.minor)"
    }

    return $TargetFramework
}

function Build-EnglishVersionBlock {
    param(
        [Parameter(Mandatory = $true)]
        [hashtable]$Metadata
    )

    $lines = @(
        '<!-- BEGIN:version-matrix -->',
        'The baseline values below come straight from project files and runtime version helpers in this repository:',
        '',
        '| Component | Version | Source |',
        '|:--|:--|:--|',
        ('| Target framework | `{0}` | `src/UnifierTSL/*.csproj` |' -f $Metadata.TargetFrameworkDisplay),
        '| Terraria | `1.4.5.5` | `src/UnifierTSL/VersionHelper.cs` (assembly file version from OTAPI/Terraria runtime) |',
        ('| OTAPI USP | `{0}` | `src/UnifierTSL/UnifierTSL.csproj` |' -f $Metadata.OTAPIUSPVersion),
        '',
        '<details>',
        '<summary><strong>TShock and dependency details</strong></summary>',
        '',
        '| Item | Value |',
        '|:--|:--|',
        ('| Bundled TShock version | `{0}` |' -f $Metadata.TShockMainlineVersion),
        ('| Sync branch | `{0}` |' -f $Metadata.TShockSyncBranch),
        ('| Sync commit | `{0}` |' -f $Metadata.TShockSyncCommit),
        '| Source | `src/Plugins/TShockAPI/TShockAPI.csproj` |',
        '',
        'Additional dependency baselines:',
        '',
        '| Package | Version | Source |',
        '|:--|:--|:--|',
        ('| ModFramework | `{0}` | `src/UnifierTSL/UnifierTSL.csproj` |' -f $Metadata.ModFrameworkVersion),
        ('| MonoMod.RuntimeDetour | `{0}` | `src/UnifierTSL/UnifierTSL.csproj` |' -f $Metadata.MonoModRuntimeDetourVersion),
        ('| Tomlyn | `{0}` | `src/UnifierTSL/UnifierTSL.csproj` |' -f $Metadata.TomlynVersion),
        ('| linq2db | `{0}` | `src/UnifierTSL/UnifierTSL.csproj` |' -f $Metadata.Linq2DbVersion),
        ('| Microsoft.Data.Sqlite | `{0}` | `src/UnifierTSL/UnifierTSL.csproj` |' -f $Metadata.MicrosoftDataSqliteVersion),
        '',
        '</details>',
        '<!-- END:version-matrix -->'
    )

    return ($lines -join "`n")
}

function Build-ChineseVersionBlock {
    param(
        [Parameter(Mandatory = $true)]
        [hashtable]$Metadata
    )

    $lines = @(
        '<!-- BEGIN:version-matrix -->',
        '下面这些基线值直接来自仓库内项目文件与运行时版本辅助逻辑：',
        '',
        '| 组件 | 版本 | 来源 |',
        '|:--|:--|:--|',
        ('| 目标框架 | `{0}` | `src/UnifierTSL/*.csproj` |' -f $Metadata.TargetFrameworkDisplay),
        '| Terraria | `1.4.5.5` | `src/UnifierTSL/VersionHelper.cs`（从 OTAPI/Terraria 运行时程序集文件版本读取） |',
        ('| OTAPI USP | `{0}` | `src/UnifierTSL/UnifierTSL.csproj` |' -f $Metadata.OTAPIUSPVersion),
        '',
        '<details>',
        '<summary><strong>TShock 与依赖详情</strong></summary>',
        '',
        '| 项目 | 值 |',
        '|:--|:--|',
        ('| 内置 TShock 版本 | `{0}` |' -f $Metadata.TShockMainlineVersion),
        ('| 同步分支 | `{0}` |' -f $Metadata.TShockSyncBranch),
        ('| 同步提交 | `{0}` |' -f $Metadata.TShockSyncCommit),
        '| 来源 | `src/Plugins/TShockAPI/TShockAPI.csproj` |',
        '',
        '附加依赖版本：',
        '',
        '| 包 | 版本 | 来源 |',
        '|:--|:--|:--|',
        ('| ModFramework | `{0}` | `src/UnifierTSL/UnifierTSL.csproj` |' -f $Metadata.ModFrameworkVersion),
        ('| MonoMod.RuntimeDetour | `{0}` | `src/UnifierTSL/UnifierTSL.csproj` |' -f $Metadata.MonoModRuntimeDetourVersion),
        ('| Tomlyn | `{0}` | `src/UnifierTSL/UnifierTSL.csproj` |' -f $Metadata.TomlynVersion),
        ('| linq2db | `{0}` | `src/UnifierTSL/UnifierTSL.csproj` |' -f $Metadata.Linq2DbVersion),
        ('| Microsoft.Data.Sqlite | `{0}` | `src/UnifierTSL/UnifierTSL.csproj` |' -f $Metadata.MicrosoftDataSqliteVersion),
        '',
        '</details>',
        '<!-- END:version-matrix -->'
    )

    return ($lines -join "`n")
}

function Sync-MarkedBlock {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Path,
        [Parameter(Mandatory = $true)]
        [string]$NewBlock,
        [Parameter(Mandatory = $true)]
        [ValidateSet('apply', 'check')]
        [string]$Mode
    )

    $pattern = '(?s)<!-- BEGIN:version-matrix -->.*?<!-- END:version-matrix -->'
    $content = Get-Content -Path $Path -Raw -Encoding UTF8

    if (-not [regex]::IsMatch($content, $pattern)) {
        throw "Missing version matrix markers in '$Path'."
    }

    # Preserve the current file's newline style so check mode is stable across Windows/Linux.
    $lineEnding = if ($content -match "`r`n") { "`r`n" } else { "`n" }
    $normalizedBlock = $NewBlock -replace "`r?`n", $lineEnding

    $updated = [regex]::Replace($content, $pattern, $normalizedBlock, 1)
    if ($updated -eq $content) {
        Write-Host "Up-to-date: $Path"
        return $false
    }

    if ($Mode -eq 'apply') {
        Set-Content -Path $Path -Value $updated -Encoding UTF8
        Write-Host "Updated: $Path"
        return $true
    }

    Write-Host "Outdated: $Path"
    return $true
}

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$unifierCsproj = Join-Path $repoRoot 'src/UnifierTSL/UnifierTSL.csproj'
$tshockCsproj = Join-Path $repoRoot 'src/Plugins/TShockAPI/TShockAPI.csproj'
$readmeEn = Join-Path $repoRoot 'README.md'
$readmeZh = Join-Path $repoRoot 'docs/README.zh-cn.md'

$unifierXml = Get-ProjectXml -Path $unifierCsproj
$tshockXml = Get-ProjectXml -Path $tshockCsproj

$unifierProps = $unifierXml.Project.PropertyGroup | Select-Object -First 1
$tshockProps = $tshockXml.Project.PropertyGroup | Select-Object -First 1

$targetFramework = [string]$unifierProps.TargetFramework
if ([string]::IsNullOrWhiteSpace($targetFramework)) {
    throw "Could not read TargetFramework from '$unifierCsproj'."
}

$metadata = @{
    TargetFrameworkDisplay         = Convert-TargetFrameworkDisplay -TargetFramework $targetFramework
    OTAPIUSPVersion                = Get-PackageVersion -ProjectXml $unifierXml -PackageId 'OTAPI.USP'
    ModFrameworkVersion            = Get-PackageVersion -ProjectXml $unifierXml -PackageId 'ModFramework'
    MonoModRuntimeDetourVersion    = Get-PackageVersion -ProjectXml $unifierXml -PackageId 'MonoMod.RuntimeDetour'
    TomlynVersion                  = Get-PackageVersion -ProjectXml $unifierXml -PackageId 'Tomlyn'
    Linq2DbVersion                 = Get-PackageVersion -ProjectXml $unifierXml -PackageId 'linq2db'
    MicrosoftDataSqliteVersion     = Get-PackageVersion -ProjectXml $unifierXml -PackageId 'Microsoft.Data.Sqlite'
    TShockMainlineVersion          = [string]$tshockProps.MainlineVersion
    TShockSyncBranch               = [string]$tshockProps.MainlineSyncBranch
    TShockSyncCommit               = [string]$tshockProps.MainlineSyncCommit
}

if ([string]::IsNullOrWhiteSpace($metadata.TShockMainlineVersion) -or
    [string]::IsNullOrWhiteSpace($metadata.TShockSyncBranch) -or
    [string]::IsNullOrWhiteSpace($metadata.TShockSyncCommit)) {
    throw "Could not read TShock sync metadata from '$tshockCsproj'."
}

$englishBlock = Build-EnglishVersionBlock -Metadata $metadata
$chineseBlock = Build-ChineseVersionBlock -Metadata $metadata

$changed = $false
$changed = (Sync-MarkedBlock -Path $readmeEn -NewBlock $englishBlock -Mode $Mode) -or $changed
$changed = (Sync-MarkedBlock -Path $readmeZh -NewBlock $chineseBlock -Mode $Mode) -or $changed

if ($Mode -eq 'check' -and $changed) {
    throw "Documentation metadata is out of sync. Run: pwsh ./tools/sync-doc-metadata.ps1 -Mode apply"
}

Write-Host "Version metadata sync check completed in '$Mode' mode."



