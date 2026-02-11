# UTSL.TShock

`UTSL.TShock` is a UnifierTSL migration package of TShock for Terraria server hosting.

This package is not an official Pryaxis TShock release.

## Identity

- Package ID: `UTSL.TShock`
- Repository: `https://github.com/CedaryCat/UnifierTSL`
- Upstream reference project: `https://github.com/Pryaxis/TShock.git`

## Mainline Sync Metadata

This assembly records the upstream sync baseline via `AssemblyMetadata`:

- `MainlineSyncRepo`
- `MainlineSyncBranch`
- `MainlineSyncCommit`
- `MainlineVersion`

Use these values as the authoritative "last synced baseline" when collecting upstream diffs for parallel migration.

## Versioning Model

This project intentionally keeps two independent version channels:

- Assembly version (`AssemblyVersion` / `FileVersion`) from `TShockAPI.csproj`
- Plugin display version (`PluginMetadata` in `TShock.cs`)

They are intentionally not auto-synchronized.

## Package Version Rules

- `main`-like context (no pre-release tag): `<MainlineVersion>`
- `develop` / `feature` context (with pre-release tag): `<MainlineVersion>-utsl.<tag>`
- No GitVersion context: `<MainlineVersion>-utsl.local`

## Sync Update Checklist

When updating your parallel migration baseline:

1. Update `MainlineSyncRepo` if upstream repo changes.
2. Update `MainlineSyncBranch` to the tracked upstream branch.
3. Update `MainlineSyncCommit` to the new synced upstream commit.
4. Update `MainlineVersion` if the upstream baseline version changes.
5. Update `PluginMetadata` version only when you decide plugin-facing version should change.
