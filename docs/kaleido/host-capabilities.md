# Kaleido Host Capabilities

Kaleido hosts are hard runtime carriers. They are not world modes and they do not encode persistence strategy.

World modes are built by registered systems. Lifecycle policy decides whether an instance is resident or elastic. World data providers decide whether terrain is ordinary `.wld`, generated, virtualized, or system-owned.

## SharedProjection

`SharedProjectionHost` is the cheapest fake-world host.

Characteristics:

- no full Terraria server thread,
- fake world handshake and section packets,
- projection tile provider,
- lightweight per-instance entity arrays,
- instance-scoped typed packet/module and frame registrations through plan content,
- no ordinary `.wld` save,
- realm transfers default to `AllowTransientTarget`.

Use it for short, controlled spaces such as login lobbies, confirmation rooms, UI-like projection spaces, and one-player private interactions.

Do not use it for gameplay that depends on full NPC, projectile, liquid, worldgen, or plugin context semantics.

`SharedProjectionContext` is the sealed neutral carrier. It does not expose a virtual method for every Terraria packet. A system installs only the handlers its content actually needs through `RealmInstallScope.Projection`; unknown inputs remain uninterpreted and are dropped by default.

## ServerContext

`ServerContextHost` starts a real `ServerContext`.

Characteristics:

- real server thread,
- real player/NPC/item/projectile context,
- normal plugin/runtime context,
- suitable for multiple players,
- terrain and save behavior come from the system's `IWorldDataProvider`.

Use it for ordinary worlds, homes, dungeons, minigames, elastic shards, generated terrain, streamed terrain, or any mode that needs complete Terraria runtime semantics.

If the system needs virtual terrain, use a provider such as `IVirtualTileWorldDataProvider`; it installs `Main.tile` through `ConfigureRuntime` and suppresses ordinary world saves by default. If the system needs ordinary world files, use a standard provider and `WorldSaveMode.Standard`.

## Retention Policy

Kaleido separates hard runtime from residency:

- `RealmLifecyclePolicy.Resident`: keep the instance running even when empty.
- `RealmLifecyclePolicy.UnloadWhenEmpty(delay)`: elastic instance, unload after it has been empty for the delay.
- `RealmLifecyclePolicy.SuspendWhenEmpty(delay, bufferTime)`: elastic instance with a future buffer/suspend intent. The current implementation retires it with a suspend reason; a later pass can preserve warm state.

Systems may also acquire a neutral `RealmHold` from an instance to keep an otherwise-empty realm warm for a bounded time or until the hold is disposed. Holds do not encode why the realm is warm; home, dungeon, shard, or queue semantics remain system-owned.

Preferred defaults:

- login lobby: `SharedProjection` + `UnloadWhenEmpty`.
- main city, spawn world, permanent activity world: `ServerContext` + `Resident`.
- player home, minigame room, dungeon run, shard unit: `ServerContext` + `UnloadWhenEmpty` or `SuspendWhenEmpty`.
