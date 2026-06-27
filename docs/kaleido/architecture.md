# Kaleido Architecture

Kaleido is a realm orchestration layer. It owns the platform mechanics for runtime instances, but it does not decide business meaning for a world. UnifierTSL does not know that Kaleido exists.

The intended boundary is:

- UnifierTSL owns neutral server runtime primitives: `ServerContext`, connection routing, transfer execution, join resolver registration, server registration, and server lifecycle visibility.
- Kaleido owns realm orchestration: mounted system ownership, join routing, instance deduplication, host selection, transfer intent execution, occupancy, tick dispatch, lifecycle evaluation, and retirement.
- Realm systems own creation meaning: why a world is needed, what key identifies it, which players may enter, what content it installs, what external triggers transfer players, and what state it persists.
- Host implementations own hard runtime mechanics: fake projection packets or full `ServerContext` startup, attach/detach, tick, stop, and disposal.

## Core Flow

1. A realm system is mounted through `RealmOrchestrator.MountSystemAsync`.
2. During mount, the system receives a `RealmSystemScope` and registers only the entry points it actually needs.
3. On player join, Kaleido runs mounted join handlers by priority. A handler may return a `RealmJoinDecision` for either a `RealmPlan` or an existing `ServerContext`.
4. During gameplay, systems call `scope.Realms.PrepareAsync`, `scope.Realms.Hold`, `scope.Realms.TransferAsync`, or `scope.Realms.RetireAsync` from their own commands, event handlers, repositories, instance installers, or gameplay logic.
5. Kaleido deduplicates creation by `RealmKey`, selects a host by `RealmHostRequirement`, records the `RealmInstance`, and installs the plan's content.
6. Kaleido executes transfers through `ServerRuntime.TransferAsync`, which serializes each player and commits after the involved physical dispatch domains reach a safe point.
7. `RealmScheduler` ticks active handles, starts non-overlapping asynchronous maintenance, and requests retirement according to lifecycle policy. It never waits for incomplete tasks in its frame loop and does not own gameplay transfer triggers.
8. Retirement is single-flight. A retiring instance rejects new admissions, stops ticking, remains keyed in the registry until uninstall and host stop finish, and then completes every retirement waiter.

## System Scope

`RealmSystemScope` is the mounted system's platform handle:

- `Join`: register join-time routing handlers.
- `Realms`: ensure, query, transfer, and retire realm instances.
- `Maintenance`: run low-frequency platform maintenance on Kaleido's scheduler thread.
- `Events`: observe neutral platform events such as instance retirement.

The scope is not a business context. It does not store home, dungeon, lobby, shard, account, party, invite, or region-edge semantics. Those belong to the mounted system.

Per-instance gameplay logic should be installed through `IRealmContentInstaller` and its `RealmInstallScope`, so server-thread-adjacent hooks live with the concrete runtime instance instead of Kaleido's platform scheduler.

## Boundary Rules

Kaleido core only understands neutral concepts:

- `RealmPlan`
- `RealmJoinDecision`
- `RealmTransferRequest`
- `RealmHostRequirement`
- `RealmLifecyclePolicy`
- `RealmInstance`
- `RealmSystemScope`

Kaleido core must not contain dedicated concepts such as lobby, home, dungeon, shard, account, invite, party, quest, or region edge. Those are system-owned semantics.

Kaleido also does not provide a storage layer in this pass. Systems that need durable state should own their repository and may currently use SQLite under their plugin config directory. Future database-service migration should replace the system repository implementation, not Kaleido core.

## UnifierTSL Bridge

Kaleido talks to UnifierTSL through `ServerRuntime`:

- `RegisterJoinResolver`
- `RegisterTransferObserver`
- `RegisterLeaveObserver`
- `TransferAsync`
- `Register` / `Unregister`
- `GetCurrentServer`

`ServerRuntime` is deliberately neutral. Its options use server/runtime language such as `AllowTransientTarget`, not Kaleido or realm language.
