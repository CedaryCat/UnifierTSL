# Kaleido Casebook

This document sketches how promised realm modes should attach to Kaleido without adding their semantics to Kaleido core.

## Login Lobby

Implemented by `Kaleido.LoginLobby`.

Shape:

- `LoginLobbyService` mounts as an `IRealmSystem`.
- It registers one high-priority join handler through `scope.Join.Use(...)`.
- The join handler checks identity state and returns `null` for pass-through players.
- When the player must be intercepted, the handler creates a per-player `RealmPlan` and returns `RealmJoinDecision.ToRealm(plan)`.
- Host requirement is `SharedProjection`.
- Lifecycle is `UnloadWhenEmpty`.
- A lobby content installer registers only its required typed inputs and frame callback through `install.Projection`.
- The content installer owns chat, crystal interaction, timeout, and final transfer semantics; the shared-projection carrier does not name them.
- Final transfer goes through `install.Realms.TransferAsync` and is observed asynchronously from later frames.

The identity adapter remains outside Kaleido core. TShock behavior lives in `TShockLoginLobbyIdentityService`.

## Player Home

Suggested system responsibilities:

- mount a home system that owns home repositories, invite checks, commands, and join restore behavior,
- optionally register a join handler that routes a reconnecting player into their last home when the system's repository says so,
- handle explicit commands or invitations by creating a home `RealmPlan` and calling `scope.Realms.TransferAsync`,
- key the instance by owner id or home id,
- keep access logic inside the home system,
- choose `UnloadWhenEmpty` or `SuspendWhenEmpty` for idle save/unload behavior,
- use `ServerContext` as the hard host,
- choose tile and save shape through the home system's world data provider.

Persistence:

- the home system owns its repository,
- SQLite can be used inside `config/<HomePlugin>/`,
- Kaleido only receives neutral plans and transfer requests.

## Dungeon Instance

Suggested system responsibilities:

- mount a dungeon system that owns party/run/session state,
- trigger entry from a party command, tile interaction, NPC interaction, quest state, or another system event,
- create a `RealmPlan` keyed by run id, party id, seed, or scenario id,
- install dungeon rules and generated content through plan content installers,
- call `scope.Realms.TransferAsync` when the run starts, completes, fails, or ejects a player,
- retire the instance on completion or after a short empty delay.

Recommended host:

- `ServerContext` for ordinary, generated, or streamed dungeon gameplay,
- `SharedProjection` only for pre-entry confirmation or non-combat scenes.

## Elastic Infinite World

Suggested system responsibilities:

- mount an infinite-world system that owns topology and shard repositories,
- map world coordinates to realm keys,
- install system-owned player position checks or tile hooks into each shard instance, for example through `RealmInstallScope.Runtime.OnPostUpdate`,
- create or reuse destination shard plans by coordinate,
- prewarm nearby shards with `scope.Realms.PrepareAsync` and short `RealmHold` instances,
- transfer across shard boundaries with `scope.Realms.TransferAsync`,
- let far empty shards retire by lifecycle policy,
- persist shard data through the infinite-world system's own repository.

For overlapping shards, the system can prepare the possible destination shard before the commit line, hold both the current and neighboring shard while the player is inside the overlap band, and only transfer when the player crosses the system-defined commit edge. Kaleido only sees prepare, hold, transfer, and release operations.

The system may retain the `Task<RealmPreparation>` while the player remains in the overlap band and inspect its completed, faulted, or canceled state on later updates; it must never synchronously wait in a gameplay hook. Once preparation succeeds, the returned hold owns the warm lifetime. Coordinate or anchor conversion belongs in `install.Transfers.OnEntering(...)` / `OnLeaving(...)`, using system-owned `RealmEntry` and `RealmExit` metadata.

Recommended host:

- `ServerContext` with a streamed/generated provider for virtual terrain,
- `ServerContext` with a standard provider only for shards that must be ordinary world files.

Kaleido should not know the words "north edge", "chunk", "shard coordinate", or "infinite world" in core code. The system turns those semantics into neutral `RealmPlan` and `RealmTransferRequest` values.
