using Robust.Shared.Audio;

namespace Content.Server.Atmos.Components;

/// <summary>
/// Plays a warning sound when an entity is damaged by atmospheric pressure.
/// Only entities with this component opt into pressure damage audio.
/// </summary>
[RegisterComponent]
public sealed partial class PressureDamageSoundComponent : Component
{
    [DataField]
    public SoundSpecifier Sound = new SoundCollectionSpecifier("GlassCrack", AudioParams.Default.AddVolume(0f));

    /// <summary>
    /// Minimum time between warning sounds on this entity.
    /// </summary>
    [DataField]
    public TimeSpan Cooldown = TimeSpan.FromSeconds(5);

    public TimeSpan NextSoundTime;
}
