#!/usr/bin/env pwsh
#Requires -Version 7

[CmdletBinding()]
param (
    [Parameter()]
    [switch] $All,
    [Parameter()]
    [ValidateSet("UnifierTSL", "TShockAPI", "Atelier")]
    [string[]] $Domain,
    [Parameter()]
    [switch] $NoExtract,
    [Parameter()]
    [switch] $NoPo,
    [Parameter()]
    [switch] $NoMo,
    [Parameter()]
    [switch] $NoTShockSeed,
    [Parameter()]
    [string] $FallbackI18nPath,
    [Parameter()]
    [string] $TShockUpstreamPath = $env:TSHOCK_UPSTREAM_PATH
)

$ErrorActionPreference = "Stop"
$PSDefaultParameterValues["*:Encoding"] = "utf8"

$RepoRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot ".."))
Set-Location $RepoRoot

$DomainConfigs = [ordered]@{
    UnifierTSL = [pscustomobject]@{
        Name = "UnifierTSL"
        Project = "src/UnifierTSL/UnifierTSL.csproj"
        Template = "i18n/UnifierTSL.template.pot"
    }
    TShockAPI = [pscustomobject]@{
        Name = "TShockAPI"
        Project = "src/Plugins/TShockAPI/TShockAPI.csproj"
        Template = "i18n/TShockAPI.template.pot"
    }
    Atelier = [pscustomobject]@{
        Name = "Atelier"
        Project = "src/Plugins/Atelier/Atelier.csproj"
        Template = "i18n/Atelier.template.pot"
    }
}

function Resolve-RepoPath {
    param ([string] $Path)
    [System.IO.Path]::GetFullPath((Join-Path $RepoRoot $Path))
}

function Invoke-NativeCommand {
    param (
        [Parameter(Mandatory = $true)]
        [string] $FileName,
        [string[]] $ArgumentList
    )

    & $FileName @ArgumentList
    if ($LASTEXITCODE -ne 0) {
        throw "Native command '$FileName' failed with exit code $LASTEXITCODE."
    }
}

function Get-CoreAutocrlf {
    try {
        $value = git config --get core.autocrlf 2>$null
        if ($LASTEXITCODE -ne 0) {
            $global:LASTEXITCODE = 0
            return ""
        }

        return $value
    }
    catch {
        $global:LASTEXITCODE = 0
        return ""
    }
}

function Format-GettextFile {
    param ([string] $FilePath)

    if (!(Test-Path -Path $FilePath -PathType Leaf)) {
        return
    }

    $regex = [regex]::new("^#:.*", [System.Text.RegularExpressions.RegexOptions]::Multiline)
    $content = Get-Content -Path $FilePath -Raw
    $formatted = $regex.Replace($content, { $args[0].ToString().Replace("\", "/") })
    $headerNoiseRegex = [regex]::new('^"(POT-Creation-Date|PO-Revision-Date|Last-Translator): .*\\n"$', [System.Text.RegularExpressions.RegexOptions]::Multiline)
    $formatted = $headerNoiseRegex.Replace($formatted, { "`"$($args[0].Groups[1].Value): \n`"" })

    if ((Get-CoreAutocrlf) -eq "true") {
        $formatted = $formatted -replace "((?<!\r)\n|\r(?!\n))", "`r`n"
    }
    else {
        $formatted = $formatted -replace "\r\n", "`n"
    }

    $formatted | Out-File -FilePath $FilePath -NoNewline
}

function Ensure-PotFile {
    param (
        [string] $TemplatePath,
        [string] $ProjectId
    )

    if (Test-Path -Path $TemplatePath -PathType Leaf) {
        return
    }

    $header = @'
msgid ""
msgstr ""
"Project-Id-Version: __PROJECT_ID__\n"
"POT-Creation-Date: \n"
"PO-Revision-Date: \n"
"Last-Translator: \n"
"Language-Team: \n"
"MIME-Version: 1.0\n"
"Content-Type: text/plain; charset=utf-8\n"
"Content-Transfer-Encoding: 8bit\n"
"X-Generator: UnifierTSL i18n script\n"
'@.Replace("__PROJECT_ID__", $ProjectId)

    New-Item -Path (Split-Path $TemplatePath) -ItemType Directory -Force | Out-Null
    $header | Out-File -FilePath $TemplatePath -NoNewline
}

function Update-Template {
    param ($Config)

    $projectPath = Resolve-RepoPath $Config.Project
    $templatePath = Resolve-RepoPath $Config.Template
    New-Item -Path (Split-Path $templatePath) -ItemType Directory -Force | Out-Null

    if (!$NoExtract) {
        Write-Output "[$($Config.Name)] extracting $($Config.Template)..."
        $extractArgs = @("tool", "run", "GetText.Extractor", "-u", "-o", "-s", $projectPath, "-t", $templatePath)
        Invoke-NativeCommand -FileName dotnet -ArgumentList $extractArgs
    }

    Ensure-PotFile $templatePath $Config.Name
    Format-GettextFile $templatePath
}

function Get-TShockUpstreamI18nPath {
    if (!$TShockUpstreamPath) {
        $defaultPath = Resolve-Path -Path (Join-Path $RepoRoot "..\TShock") -ErrorAction SilentlyContinue
        if ($defaultPath) {
            return (Join-Path $defaultPath.Path "i18n")
        }

        return $null
    }

    $resolved = Resolve-Path -Path $TShockUpstreamPath -ErrorAction SilentlyContinue
    if (!$resolved) {
        return $null
    }

    return (Join-Path $resolved.Path "i18n")
}

function Get-FallbackI18nPath {
    if (!$FallbackI18nPath) {
        return $null
    }

    $resolved = Resolve-Path -Path $FallbackI18nPath -ErrorAction SilentlyContinue
    if (!$resolved) {
        return $null
    }

    return $resolved.Path
}

function Get-DomainLocales {
    param (
        $Config,
        [string] $TShockUpstreamI18n,
        [string] $FallbackI18n
    )

    $locales = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::OrdinalIgnoreCase)
    $i18nRoot = Resolve-RepoPath "i18n"

    if (Test-Path -Path $i18nRoot -PathType Container) {
        foreach ($localeDir in Get-ChildItem -Path $i18nRoot -Directory) {
            $poPath = Join-Path $localeDir.FullName "$($Config.Name).po"
            if (Test-Path -Path $poPath -PathType Leaf) {
                [void] $locales.Add($localeDir.Name)
            }
        }
    }

    if ($FallbackI18n -and (Test-Path -Path $FallbackI18n -PathType Container)) {
        foreach ($localeDir in Get-ChildItem -Path $FallbackI18n -Directory) {
            $poPath = Join-Path $localeDir.FullName "$($Config.Name).po"
            if (Test-Path -Path $poPath -PathType Leaf) {
                [void] $locales.Add($localeDir.Name)
            }
        }
    }

    if ($Config.Name -eq "TShockAPI" -and $TShockUpstreamI18n -and (Test-Path -Path $TShockUpstreamI18n -PathType Container)) {
        foreach ($localeDir in Get-ChildItem -Path $TShockUpstreamI18n -Directory) {
            $poPath = Join-Path $localeDir.FullName "TShockAPI.po"
            if (Test-Path -Path $poPath -PathType Leaf) {
                [void] $locales.Add($localeDir.Name)
            }
        }
    }

    return @($locales | Sort-Object)
}

function Merge-PoWithFallback {
    param (
        [string] $PrimaryPoPath,
        [string] $FallbackPoPath
    )

    if (!(Test-Path -Path $PrimaryPoPath -PathType Leaf) -or !(Test-Path -Path $FallbackPoPath -PathType Leaf)) {
        return
    }

    $tempDir = Resolve-RepoPath ".local/i18n-merge"
    New-Item -Path $tempDir -ItemType Directory -Force | Out-Null
    $tempName = [System.Guid]::NewGuid().ToString("N")
    $translatedPath = Join-Path $tempDir "$tempName.translated.po"
    $mergedPath = Join-Path $tempDir "$tempName.merged.po"

    try {
        Invoke-NativeCommand -FileName msgattrib -ArgumentList @("--translated", "--no-obsolete", "-o", $translatedPath, $PrimaryPoPath)
        if (!(Test-Path -Path $translatedPath -PathType Leaf)) {
            Copy-Item -Path $FallbackPoPath -Destination $PrimaryPoPath -Force
            return
        }

        Invoke-NativeCommand -FileName msgcat -ArgumentList @("--use-first", "-o", $mergedPath, $translatedPath, $FallbackPoPath)
        Copy-Item -Path $mergedPath -Destination $PrimaryPoPath -Force
    }
    finally {
        Remove-Item -Path $translatedPath, $mergedPath -Force -ErrorAction SilentlyContinue
    }
}

function Update-PoFiles {
    param ($Config)

    if ($NoPo) {
        return
    }

    $templatePath = Resolve-RepoPath $Config.Template
    if (!(Test-Path -Path $templatePath -PathType Leaf)) {
        return
    }

    $tshockUpstreamI18n = Get-TShockUpstreamI18nPath
    $fallbackI18n = Get-FallbackI18nPath
    foreach ($locale in Get-DomainLocales $Config $tshockUpstreamI18n $fallbackI18n) {
        $localLocaleDir = Resolve-RepoPath (Join-Path "i18n" $locale)
        $poPath = Join-Path $localLocaleDir "$($Config.Name).po"
        $fallbackPoPath = $null
        if ($fallbackI18n) {
            $fallbackPoPath = Join-Path (Join-Path $fallbackI18n $locale) "$($Config.Name).po"
        }

        $compendium = $null

        if ($Config.Name -eq "TShockAPI" -and !$NoTShockSeed -and $tshockUpstreamI18n) {
            $candidate = Join-Path (Join-Path $tshockUpstreamI18n $locale) "TShockAPI.po"
            if (Test-Path -Path $candidate -PathType Leaf) {
                $compendium = $candidate
            }
        }

        if (!(Test-Path -Path $poPath -PathType Leaf)) {
            if (!$compendium) {
                if (!$fallbackPoPath -or !(Test-Path -Path $fallbackPoPath -PathType Leaf)) {
                    continue
                }
            }

            New-Item -Path $localLocaleDir -ItemType Directory -Force | Out-Null
            $sourcePoPath = $compendium
            if ($fallbackPoPath -and (Test-Path -Path $fallbackPoPath -PathType Leaf)) {
                $sourcePoPath = $fallbackPoPath
            }

            Copy-Item -Path $sourcePoPath -Destination $poPath -Force
        }
        elseif ($fallbackPoPath -and (Test-Path -Path $fallbackPoPath -PathType Leaf)) {
            Merge-PoWithFallback $poPath $fallbackPoPath
        }

        $mergeArgs = @("--previous", "--no-fuzzy-matching", "--backup=off", "--update")
        if ($compendium) {
            $mergeArgs += "--compendium=$compendium"
        }

        $mergeArgs += @($poPath, $templatePath)
        Write-Output "[$($Config.Name)] [$locale] merging..."
        Invoke-NativeCommand -FileName msgmerge -ArgumentList $mergeArgs
        Format-GettextFile $poPath
    }
}

function Update-MoFiles {
    param ($Config)

    if ($NoMo) {
        return
    }

    $i18nRoot = Resolve-RepoPath "i18n"
    foreach ($poFile in Get-ChildItem -Path $i18nRoot -Filter "$($Config.Name).po" -Recurse) {
        $moPath = [System.IO.Path]::ChangeExtension($poFile.FullName, ".mo")
        Write-Output "[$($Config.Name)] [$($poFile.Directory.Name)] generating mo..."
        $formatArgs = @("-o", $moPath, $poFile.FullName)
        Invoke-NativeCommand -FileName msgfmt -ArgumentList $formatArgs
    }
}

$selectedDomains = if ($Domain) { $Domain } else { @("UnifierTSL", "TShockAPI", "Atelier") }
foreach ($domainName in $selectedDomains) {
    $config = $DomainConfigs[$domainName]
    Update-Template $config
    Update-PoFiles $config
    Update-MoFiles $config
}
