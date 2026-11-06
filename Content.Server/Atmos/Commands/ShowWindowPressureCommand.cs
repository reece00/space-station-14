using Content.Server.Administration;
using Content.Server.Atmos.EntitySystems;
using Content.Shared.Administration;
using Robust.Shared.Console;

namespace Content.Server.Atmos.Commands;

[AdminCommand(AdminFlags.Debug)]
public sealed partial class ShowWindowPressure : IConsoleCommand
{
    [Dependency] private IEntityManager _entities = default!;

    public string Command => "showwindowpressure";
    public string Description => "Toggles a live window damage and gas pressure overlay.";
    public string Help => $"Usage: {Command}";

    public void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        if (shell.Player is not { } player)
        {
            shell.WriteLine("You must be a player to use this command.");
            return;
        }

        var enabled = _entities.System<AtmosDebugOverlaySystem>().ToggleWindowObserver(player);
        shell.WriteLine(enabled
            ? "Enabled window damage and pressure overlay."
            : "Disabled window damage and pressure overlay.");
    }
}
