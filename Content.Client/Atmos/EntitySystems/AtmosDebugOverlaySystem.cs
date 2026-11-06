using Content.Client.Atmos.Overlays;
using Content.Shared.Atmos;
using Content.Shared.Atmos.EntitySystems;
using Content.Shared.GameTicking;
using JetBrains.Annotations;
using Robust.Client.Graphics;

namespace Content.Client.Atmos.EntitySystems
{
    [UsedImplicitly]
    internal sealed partial class AtmosDebugOverlaySystem : SharedAtmosDebugOverlaySystem
    {
        [Dependency] private IOverlayManager _overlayManager = default!;

        public readonly Dictionary<EntityUid, AtmosDebugOverlayMessage> TileData = [];
        public readonly Dictionary<EntityUid, WindowPressureDebugOverlayMessage> WindowData = [];

        // Configuration set by debug commands and used by AtmosDebugOverlay {
        /// <summary>Value source for display</summary>
        public AtmosDebugOverlayMode CfgMode;
        /// <summary>This is subtracted from value (applied before CfgScale)</summary>
        public float CfgBase = 0;
        /// <summary>The value is divided by this (applied after CfgBase)</summary>
        public float CfgScale = Atmospherics.MolesCellStandard * 2;
        /// <summary>Gas ID used by GasMoles mode</summary>
        public int CfgSpecificGas = 0;
        /// <summary>Uses black-to-white interpolation (as opposed to red-green-blue) for colourblind users</summary>
        public bool CfgCBM = false;
        // }

        private AtmosDebugOverlay? _overlay;
        private WindowPressureDebugOverlay? _windowOverlay;

        public override void Initialize()
        {
            base.Initialize();

            SubscribeNetworkEvent<RoundRestartCleanupEvent>(Reset);
            SubscribeNetworkEvent<AtmosDebugOverlayMessage>(HandleAtmosDebugOverlayMessage);
            SubscribeNetworkEvent<AtmosDebugOverlayDisableMessage>(HandleAtmosDebugOverlayDisableMessage);
            SubscribeNetworkEvent<WindowPressureDebugOverlayMessage>(HandleWindowPressureDebugOverlayMessage);
            SubscribeNetworkEvent<WindowPressureDebugOverlayDisableMessage>(HandleWindowPressureDebugOverlayDisableMessage);

            SubscribeLocalEvent<GridRemovalEvent>(OnGridRemoved);
        }

        private void OnGridRemoved(GridRemovalEvent ev)
        {
            TileData.Remove(ev.EntityUid);
            WindowData.Remove(ev.EntityUid);
        }

        private void HandleAtmosDebugOverlayMessage(AtmosDebugOverlayMessage message)
        {
            TileData[GetEntity(message.GridId)] = message;

            if (_overlay is not null)
                return;

            _overlay = new AtmosDebugOverlay(this);
            _overlayManager.AddOverlay(_overlay);
        }

        private void HandleAtmosDebugOverlayDisableMessage(AtmosDebugOverlayDisableMessage ev)
        {
            TileData.Clear();
            RemoveOverlay();
        }

        private void HandleWindowPressureDebugOverlayMessage(WindowPressureDebugOverlayMessage message)
        {
            WindowData[GetEntity(message.GridId)] = message;

            if (_windowOverlay is not null)
                return;

            _windowOverlay = new WindowPressureDebugOverlay(this);
            _overlayManager.AddOverlay(_windowOverlay);
        }

        private void HandleWindowPressureDebugOverlayDisableMessage(WindowPressureDebugOverlayDisableMessage message)
        {
            WindowData.Clear();
            RemoveWindowOverlay();
        }

        public override void Shutdown()
        {
            base.Shutdown();

            RemoveOverlay();
            RemoveWindowOverlay();
        }

        public void Reset(RoundRestartCleanupEvent ev)
        {
            TileData.Clear();
            WindowData.Clear();
            RemoveWindowOverlay();
        }

        public bool HasData(EntityUid gridId)
        {
            return TileData.ContainsKey(gridId);
        }

        private void RemoveWindowOverlay()
        {
            if (_windowOverlay is null)
                return;

            _overlayManager.RemoveOverlay(_windowOverlay);
            _windowOverlay = null;
        }

        private void RemoveOverlay()
        {
            if (_overlay is null)
                return;

            _overlayManager.RemoveOverlay(_overlay);
            _overlay = null;
        }
    }

    internal enum AtmosDebugOverlayMode : byte
    {
        TotalMoles,
        GasMoles,
        Temperature
    }
}
