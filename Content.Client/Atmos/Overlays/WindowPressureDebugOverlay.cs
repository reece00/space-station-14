using System.Numerics;
using Content.Client.Atmos.EntitySystems;
using Content.Client.Stylesheets.Fonts;
using Robust.Client.Graphics;
using Robust.Client.ResourceManagement;
using Robust.Client.UserInterface;
using Robust.Shared.Enums;

namespace Content.Client.Atmos.Overlays;

/// <summary>
/// Screen-space, opt-in overlay for testing window pressure damage.
/// Samples come from the server, not locally predicted atmospherics.
/// </summary>
internal sealed partial class WindowPressureDebugOverlay : Overlay
{
    [Dependency] private IEntityManager _entities = default!;
    [Dependency] private IEyeManager _eye = default!;
    [Dependency] private IResourceCache _resources = default!;

    private readonly AtmosDebugOverlaySystem _system;
    private readonly SharedTransformSystem _transform;
    private readonly Font _font;

    public override OverlaySpace Space => OverlaySpace.ScreenSpace;

    public WindowPressureDebugOverlay(AtmosDebugOverlaySystem system)
    {
        IoCManager.InjectDependencies(this);

        _system = system;
        _transform = _entities.System<SharedTransformSystem>();
        _font = new NotoFontFamilyStack(_resources).GetFont(12);
    }

    protected override void Draw(in OverlayDrawArgs args)
    {
        var handle = args.ScreenHandle;
        var step = new Vector2(0, _font.GetLineHeight(1f));

        foreach (var message in _system.WindowData.Values)
        {
            foreach (var data in message.Windows)
            {
                if (!_entities.TryGetEntity(data.EntityId, out var uid) ||
                    !_entities.TryGetComponent(uid.Value, out TransformComponent? xform) ||
                    xform.MapID != args.MapId)
                    continue;

                var worldPos = _transform.GetWorldPosition(xform);
                if (!args.WorldAABB.Contains(worldPos))
                    continue;

                var pos = _eye.WorldToScreen(worldPos + new Vector2(0f, 0.65f));
                var hp = data.BreakThreshold > 0
                    ? $"HP {MathF.Max(0f, data.BreakThreshold - data.Damage):0.#}/{data.BreakThreshold:0.#}  Damage {data.Damage:0.#}"
                    : $"Damage {data.Damage:0.#} (HP threshold unknown)";

                handle.DrawString(_font, pos, hp, 1f,
                    data.IsTakingDamage ? Color.OrangeRed : Color.White, TextOutline.Default);
                pos += step;
                handle.DrawString(_font, pos,
                    $"Pmax {data.MaxPressure:0.#}/{data.MinPressure:0.#} kPa  dP {data.DeltaPressure:0.#}/{data.MinPressureDelta:0.#} kPa",
                    1f, Color.Yellow, TextOutline.Default);
                pos += step;
                handle.DrawString(_font, pos,
                    $"N {data.North:0.#} S {data.South:0.#} E {data.East:0.#} W {data.West:0.#} kPa",
                    1f, Color.White, TextOutline.Default);
            }
        }
    }
}
