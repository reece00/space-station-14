using System.Numerics;
using Content.Shared.Atmos;
using Content.Shared.Atmos.Components;
using Content.Server.Atmos.Components;
using Content.Server.Destructible;
using Content.Shared.Damage.Components;
using Content.Shared.Damage.Systems;
using Content.Shared.FixedPoint;
using Content.Shared.Atmos.EntitySystems;
using Content.Shared.CCVar;
using JetBrains.Annotations;
using Robust.Server.GameObjects;
using Robust.Server.Player;
using Robust.Shared.Configuration;
using Robust.Shared.Enums;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Player;

namespace Content.Server.Atmos.EntitySystems
{
    [UsedImplicitly]
    public sealed partial class AtmosDebugOverlaySystem : SharedAtmosDebugOverlaySystem
    {
        [Dependency] private IPlayerManager _playerManager = default!;
        [Dependency] private IConfigurationManager _configManager = default!;
        [Dependency] private SharedTransformSystem _transform = default!;
        [Dependency] private MapSystem _mapSystem = default!;
        [Dependency] private DamageableSystem _damageable = default!;
        [Dependency] private DestructibleSystem _destructible = default!;

        /// <summary>
        ///     Players allowed to see the atmos debug overlay.
        ///     To modify it see <see cref="AddObserver"/> and
        ///     <see cref="RemoveObserver"/>.
        /// </summary>
        private readonly HashSet<ICommonSession> _playerObservers = new();
        private readonly HashSet<ICommonSession> _windowObservers = new();

        /// <summary>
        ///     Overlay update ticks per second.
        /// </summary>
        private float _updateCooldown;

        private List<Entity<MapGridComponent>> _grids = new();

        public override void Initialize()
        {
            base.Initialize();
            _playerManager.PlayerStatusChanged += OnPlayerStatusChanged;
        }

        public override void Shutdown()
        {
            base.Shutdown();
            _playerManager.PlayerStatusChanged -= OnPlayerStatusChanged;
        }

        public bool AddObserver(ICommonSession observer)
        {
            return _playerObservers.Add(observer);
        }

        public bool HasObserver(ICommonSession observer)
        {
            return _playerObservers.Contains(observer);
        }

        public bool RemoveObserver(ICommonSession observer)
        {
            if (!_playerObservers.Remove(observer))
            {
                return false;
            }

            var message = new AtmosDebugOverlayDisableMessage();
            RaiseNetworkEvent(message, observer.Channel);

            return true;
        }

        /// <summary>
        ///     Adds the given observer if it doesn't exist, removes it otherwise.
        /// </summary>
        /// <param name="observer">The observer to toggle.</param>
        /// <returns>true if added, false if removed.</returns>
        public bool ToggleObserver(ICommonSession observer)
        {
            if (HasObserver(observer))
            {
                RemoveObserver(observer);
                return false;
            }

            AddObserver(observer);
            return true;
        }

        public bool ToggleWindowObserver(ICommonSession observer)
        {
            if (_windowObservers.Remove(observer))
            {
                RaiseNetworkEvent(new WindowPressureDebugOverlayDisableMessage(), observer.Channel);
                return false;
            }

            _windowObservers.Add(observer);
            return true;
        }

        private void OnPlayerStatusChanged(object? sender, SessionStatusEventArgs e)
        {
            if (e.NewStatus != SessionStatus.InGame)
            {
                RemoveObserver(e.Session);
                if (_windowObservers.Remove(e.Session))
                    RaiseNetworkEvent(new WindowPressureDebugOverlayDisableMessage(), e.Session.Channel);
            }
        }

        private AtmosDebugOverlayData? ConvertTileToData(TileAtmosphere tile)
        {
            return new AtmosDebugOverlayData(
                tile.GridIndices,
                tile.Air?.Temperature ?? default,
                tile.Air?.Moles,
                tile.PressureDirection,
                tile.LastPressureDirection,
                tile.AirtightData.BlockedDirections,
                tile.ExcitedGroup?.GetHashCode(),
                tile.Space,
                tile.MapAtmosphere,
                tile.NoGridTile,
                tile.Air?.Immutable ?? false);
        }

        public override void Update(float frameTime)
        {
            if (_playerObservers.Count == 0 && _windowObservers.Count == 0)
                return;

            AccumulatedFrameTime += frameTime;
            _updateCooldown = 1 / _configManager.GetCVar(CCVars.NetAtmosDebugOverlayTickRate);

            if (AccumulatedFrameTime < _updateCooldown)
            {
                return;
            }

            // This is the timer from GasTileOverlaySystem
            AccumulatedFrameTime -= _updateCooldown;

            // Now we'll go through each player, then through each chunk in range of that player checking if the player is still in range
            // If they are, check if they need the new data to send (i.e. if there's an overlay for the gas).
            // Afterwards we reset all the chunk data for the next time we tick.
            foreach (var session in _playerObservers)
            {
                if (session.AttachedEntity is not {Valid: true} entity)
                    continue;

                var transform = Transform(entity);
                var pos = _transform.GetWorldPosition(transform);
                var worldBounds = Box2.CenteredAround(pos,
                    new Vector2(LocalViewRange, LocalViewRange));

                _grids.Clear();
                _mapSystem.FindGridsIntersecting(transform.MapID, worldBounds, ref _grids);

                foreach (var grid in _grids)
                {
                    var uid = grid.Owner;

                    if (!Exists(uid))
                        continue;

                    if (!TryComp(uid, out GridAtmosphereComponent? gridAtmos))
                        continue;

                    var entityTile = _mapSystem.GetTileRef(grid, grid, transform.Coordinates).GridIndices;
                    var baseTile = new Vector2i(entityTile.X - LocalViewRange / 2, entityTile.Y - LocalViewRange / 2);
                    var debugOverlayContent = new AtmosDebugOverlayData?[LocalViewRange * LocalViewRange];

                    var index = 0;
                    for (var y = 0; y < LocalViewRange; y++)
                    {
                        for (var x = 0; x < LocalViewRange; x++)
                        {
                            var vector = new Vector2i(baseTile.X + x, baseTile.Y + y);
                            gridAtmos.Tiles.TryGetValue(vector, out var tile);
                            debugOverlayContent[index++] = tile == null ? null : ConvertTileToData(tile);
                        }
                    }

                    var msg = new AtmosDebugOverlayMessage(GetNetEntity(grid), baseTile, debugOverlayContent);
                    RaiseNetworkEvent(msg, session.Channel);
                }
            }

            SendWindowPressureDebugData();
        }

        private static float PressureAt(GridAtmosphereComponent atmosphere, Vector2i tile)
        {
            return atmosphere.Tiles.TryGetValue(tile, out var airTile)
                ? airTile.Air?.Pressure ?? 0f
                : 0f;
        }

        private void SendWindowPressureDebugData()
        {
            foreach (var session in _windowObservers)
            {
                if (session.AttachedEntity is not { Valid: true } player)
                    continue;

                var playerTransform = Transform(player);
                var playerPos = _transform.GetWorldPosition(playerTransform);
                var bounds = Box2.CenteredAround(playerPos, new Vector2(LocalViewRange, LocalViewRange));

                _grids.Clear();
                _mapSystem.FindGridsIntersecting(playerTransform.MapID, bounds, ref _grids);

                foreach (var grid in _grids)
                {
                    if (!Exists(grid.Owner) || !TryComp(grid.Owner, out GridAtmosphereComponent? atmosphere))
                        continue;

                    var center = _mapSystem.GetTileRef(grid, grid, playerTransform.Coordinates).GridIndices;
                    var windows = new List<WindowPressureDebugData>();

                    foreach (var entity in atmosphere.DeltaPressureEntities)
                    {
                        var uid = entity.Owner;
                        if (!Exists(uid) ||
                            !TryComp(uid, out AirtightComponent? airtight) ||
                            !TryComp(uid, out DamageableComponent? damageable) ||
                            !TryComp(uid, out DestructibleComponent? destructible))
                            continue;

                        var tile = airtight.LastPosition.Tile;
                        if (Math.Abs(tile.X - center.X) > LocalViewRange / 2 ||
                            Math.Abs(tile.Y - center.Y) > LocalViewRange / 2)
                            continue;

                        var north = PressureAt(atmosphere, tile.Offset(AtmosDirection.North));
                        var south = PressureAt(atmosphere, tile.Offset(AtmosDirection.South));
                        var east = PressureAt(atmosphere, tile.Offset(AtmosDirection.East));
                        var west = PressureAt(atmosphere, tile.Offset(AtmosDirection.West));

                        // Match the directional-window substitution in ProcessDeltaPressureEntityBulk.
                        if (!airtight.NoAirWhenFullyAirBlocked)
                        {
                            var local = PressureAt(atmosphere, tile);
                            var blocked = airtight.AirBlockedDirection;
                            if (!blocked.IsFlagSet(AtmosDirection.North)) north = local;
                            if (!blocked.IsFlagSet(AtmosDirection.South)) south = local;
                            if (!blocked.IsFlagSet(AtmosDirection.East)) east = local;
                            if (!blocked.IsFlagSet(AtmosDirection.West)) west = local;
                        }

                        var maxPressure = MathF.Max(MathF.Max(north, south), MathF.Max(east, west));
                        var delta = MathF.Max(MathF.Abs(north - south), MathF.Abs(east - west));
                        var threshold = _destructible.DestroyedAt(uid, destructible);
                        var breakThreshold = threshold == FixedPoint2.MaxValue ? -1f : threshold.Float();

                        windows.Add(new WindowPressureDebugData(
                            GetNetEntity(uid),
                            _damageable.GetTotalDamage((uid, damageable)).Float(),
                            breakThreshold,
                            north,
                            south,
                            east,
                            west,
                            maxPressure,
                            delta,
                            entity.Comp.MinPressure,
                            entity.Comp.MinPressureDelta,
                            entity.Comp.IsTakingDamage));
                    }

                    RaiseNetworkEvent(
                        new WindowPressureDebugOverlayMessage(GetNetEntity(grid), windows.ToArray()),
                        session.Channel);
                }
            }
        }
    }
}
