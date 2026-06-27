# Registering Kaleido Systems

A Kaleido system is a mounted runtime entity, not a bundle of split hook objects. The system owns a coherent gameplay or platform feature, registers the small entry points it needs, and then uses Kaleido's neutral API to keep that feature running.

```csharp
public sealed class MyRealmSystem : IRealmSystem, IDisposable
{
    public string Id => "Example.MyRealmSystem";

    public Task MountAsync(RealmSystemScope scope, CancellationToken cancellationToken) {
        scope.Join.Use(RouteJoin, priority: 10);
        scope.Maintenance.Every(TimeSpan.FromSeconds(30), CleanupAsync);
        scope.Events.InstanceRetiring.Register(OnInstanceRetiringAsync);
        return Task.CompletedTask;
    }

    private RealmJoinDecision? RouteJoin(RealmJoin join) => null;

    private ValueTask CleanupAsync(CancellationToken cancellationToken) {
        return ValueTask.CompletedTask;
    }

    private ValueTask OnInstanceRetiringAsync(RealmInstanceRetiring evt, CancellationToken cancellationToken) {
        return ValueTask.CompletedTask;
    }

    public void Dispose() {
    }
}
```

Mounting returns a `RealmSystemLease`. Awaiting `DisposeAsync` unregisters all scope registrations and disposes the system through `IAsyncDisposable` when available, otherwise through `IDisposable`.

## Join Routing

Join handlers run before the default server resolver. They are ordered by descending priority, then registration order.

A handler may:

- return `null` to let the next handler or default server selection continue,
- return `RealmJoinDecision.ToRealm(plan)` to route the joining player into a planned realm,
- return `RealmJoinDecision.ToServer(server)` to route the joining player to an existing server.

Join handlers should be narrow and decisive. They should not mutate global server bookkeeping or call `ServerRuntime.TransferAsync` directly. Join routing is synchronous; cold realm startup belongs in an earlier prepare flow or a later transfer unless a deferred-join protocol is introduced explicitly.

## Runtime Operations

`scope.Realms` is the system's platform API:

- `EnsureAsync(plan)`: create or reuse the instance for `plan.Key`.
- `PrepareAsync(plan, options)`: ensure the instance, optionally wait until the host is ready, and optionally return a temporary `RealmHold`.
- `Hold(instance, duration)`: keep an existing instance from empty-retirement without explaining the system-specific reason.
- `TransferAsync(request)`: transfer a player to a planned realm or concrete server.
- `RetireAsync(instance, reason)`: stop an instance through Kaleido's retirement path.
- `Instances` / `TryGet(...)`: inspect the in-memory runtime ledger.

This is where commands, party queues, repository restore decisions, and external plugin events connect to Kaleido. Instance-local tile/NPC/player-position logic should usually be installed through plan content, because it belongs beside the concrete `ServerContext` or projection instance. Those triggers remain system logic. Kaleido only receives neutral plans and transfer requests.

## Planning

Planning is no longer a separate framework hook. The mounted system creates a `RealmPlan` at the point where its own logic knows what should exist.

The plan must define:

- `RealmKey`: identity used for deduplication.
- `DisplayName`: human-facing runtime name.
- `RealmHostRequirement`: required hard runtime carrier.
- `RealmLifecyclePolicy`: what happens when the instance becomes empty.
- `ContextFactory`: how the concrete `ServerContext` or `SharedProjectionContext` is created.
- `Content`: optional per-instance installers for event subscriptions, rules, generated structures, projection state, or runtime hooks.

For virtual-tile contexts, prefer an `IWorldDataProvider` that implements `IVirtualTileWorldDataProvider`; it binds the tile provider through the neutral `ConfigureRuntime` hook and defaults ordinary world saves off.

## Lifecycle

Lifecycle is Kaleido's residency policy, not a host type.

- `Resident` keeps the instance running while empty.
- `UnloadWhenEmpty` unloads an elastic instance after its empty delay.
- `SuspendWhenEmpty` expresses a future buffered elastic mode; current runtime retires it with a suspend reason.

For short-lived warm instances, use `scope.Realms.PrepareAsync(..., new(HoldFor: ...))`, `scope.Realms.Hold(...)`, or `install.Lifetime.Hold(...)`. A hold delays empty-instance retirement without explaining why the instance is warm, so system concepts such as nearby chunks, invitations, parties, or queues stay outside Kaleido core.

## Events And Maintenance

Maintenance callbacks are asynchronous, non-overlapping tasks for low-frequency platform work: expiring reservations, pruning system caches, refreshing external repository state, or compensating after an external failure. The scheduler observes them without waiting inside its frame loop. They are not a gameplay update loop and should not poll player movement at frame-like intervals.

Events expose neutral lifecycle signals. `InstanceRetiring` is awaited before content uninstall and host stop, so a system may detach instance-specific state or save its own repository without blocking the scheduler thread.

## Content

Content installers attach runtime behavior to an instance after Kaleido starts it and detach that behavior before retirement. `InstallAsync` receives a `RealmInstallScope`, which exposes the `RealmInstance`, its concrete `ServerContext`, realm operations, server runtime hooks, neutral transfer hooks, shared-projection hooks when applicable, and a small lifetime tracker for disposables or holds owned by that instance.

`install.Transfers.OnEntering(...)` and `OnLeaving(...)` run through the corresponding realm dispatcher after the core transfer commit. They receive open `RealmEntry` or `RealmExit` data; the installed system may interpret its own anchors and metadata, while Kaleido does not reserve or interpret those values. These hooks are synchronous safe-point work and must remain bounded.

Installation may honor its cancellation token while the instance is still being created. Uninstallation is a committed cleanup phase: Kaleido invokes successfully installed content in reverse order without a cancellation token, then disposes tracked lifetime registrations and stops the host. Uninstallers must therefore remain bounded and apply their own explicit timeouts to external I/O when necessary.

Use content installers for:

- local event subscriptions,
- instance-specific NPC or projection state,
- virtual tile provider binding,
- world rules,
- generated structures,
- server-thread-adjacent runtime hooks, such as player-position checks or tile/NPC interaction handlers.

Example:

```csharp
public sealed class EdgeWatcher : IRealmContentInstaller {
    public Task InstallAsync(RealmInstallScope install, CancellationToken cancellationToken) {
        install.Runtime.OnPostUpdate(update => CheckEdges(update.Instance, update.Server));
        return Task.CompletedTask;
    }

    public Task UninstallAsync(RealmInstallScope install) {
        return Task.CompletedTask;
    }
}
```

Do not use installers as a hidden lifecycle manager. Lifecycle belongs to Kaleido policy plus the host handle.

For a `SharedProjection` realm, `install.Projection` exposes instance-scoped registrations for exact packet types, exact network-module types, player synchronization/entry, and player frames. Only registered protocol shapes are parsed. A handler returns `Unhandled`, `Handled`, or `Forward`; unhandled projection input is dropped unless the host explicitly classifies it as safe baseline player state. Registrations are owned by the install lifetime and are removed automatically.

```csharp
public Task InstallAsync(RealmInstallScope install, CancellationToken cancellationToken) {
    var projection = install.Projection
        ?? throw new InvalidOperationException("This content requires SharedProjection.");
    projection.OnPacket<TileChange>(MessageID.TileChange, input => {
        HandleTile(input.Remote, input.Packet);
        return ProjectionInputResult.Handled;
    });
    return Task.CompletedTask;
}
```

Projection infrastructure owns handshake, validation, ping, section synchronization, and the narrow baseline forwarding policy. Gameplay meaning remains in the installer; do not subclass `SharedProjectionContext` to accumulate scenario hooks.
