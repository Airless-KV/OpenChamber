// =========================================================================================
// Original Author : air (Airless-KV) - Nov 2026
// Responsibilities: - Physical indoor cabin siren button and emergency alarm switch
//                   - Interfaces with BreachManager to activate perimeter roof siren
//                   - Provides real-time battery status prompt and press feedback
// -----------------------------------------------------------------------------------------
// Change Log:
// - Nov 15, 2026 | air : Created physical cabin emergency siren push-button interactable
// =========================================================================================
using Godot;

public partial class InteractableSirenSwitch : InteractableBase
{
	[ExportGroup("Dependencies")]
	[Export] public BreachManager BreachManagerNode;
	[Export] public MeshInstance3D ButtonMesh;

	private HUDManager _hud;
	private Tween _pressTween;

	public override void _Ready()
	{
		base._Ready();
		AddToGroup("SirenSwitches");
		BreachManagerNode ??= GetTree().Root.FindChild("BreachManager", true, false) as BreachManager;
		_hud ??= GetTree().Root.FindChild("HUD", true, false) as HUDManager;
		ButtonMesh ??= GetNodeOrNull<MeshInstance3D>("ButtonMesh");
	}

	public override string GetPrompt()
	{
		var station = InteractableBatteryStation.GetStation(PowerSystemType.Siren)
			?? InteractableBatteryStation.Instance;
		int hp = station != null && station.HasBattery ? station.BatteryHealth : 0;

		if (station == null || !station.HasPower || hp < 30)
		{
			return $"[!] Siren Offline (Requires 30% Siren Battery | Current: {hp}%)";
		}

		return $"Left Click to Sound Perimeter Siren (Cost: 30% | Siren Batt: {hp}%)";
	}

	protected override void OnInteract(PlayerController player)
	{
		BreachManagerNode ??= GetTree().Root.FindChild("BreachManager", true, false) as BreachManager;
		if (BreachManagerNode == null) return;

		// Button press animation
		if (ButtonMesh != null)
		{
			if (_pressTween != null && _pressTween.IsValid()) _pressTween.Kill();
			_pressTween = CreateTween();
			_pressTween.TweenProperty(ButtonMesh, "position:y", -0.02f, 0.08f);
			_pressTween.TweenProperty(ButtonMesh, "position:y", 0.0f, 0.12f);
		}

		BreachManagerNode.ActivateSiren(out string message);
		_hud?.SetInteractionPrompt(message);
	}
}
