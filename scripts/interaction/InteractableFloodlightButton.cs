// =========================================================================================
// Original Author : air (Airless-KV) - Nov 2026
// Responsibilities: - Directional arrow keypad button on the CCTV desk
//                   - Remote activation of perimeter floodlights (North, East, South, West)
//                   - Push-down tactile tween feedback and power/cooldown status checks
// -----------------------------------------------------------------------------------------
// Change Log:
// - Nov 15, 2026 | air : Implemented directional floodlight keypad button for CCTV console
// - Nov 17, 2026 | air : Streamlined prompt format to '[E / LMB]' convention
// - Nov 17, 2026 | air : Added FloodlightButtonPressed event for interactive tutorial verification
// =========================================================================================
using Godot;
using System;
using System.Linq;

public partial class InteractableFloodlightButton : InteractableBase
{
	public static event Action FloodlightButtonPressed;

	[Export(PropertyHint.Enum, "North,East,South,West")] 
	public int Direction = 0; // 0 = North, 1 = East, 2 = South, 3 = West

	[Export] public MeshInstance3D ButtonMesh;
	[Export] public Label3D DirectionLabel;

	private InteractableFloodlightSwitch _cachedSwitch;
	private HUDManager _hud;
	private Tween _pressTween;

	private static readonly string[] DirectionNames = { "North", "East", "South", "West" };

	public override void _Ready()
	{
		base._Ready();
		AddToGroup("FloodlightButtons");
		_hud ??= GetTree().Root.FindChild("HUD", true, false) as HUDManager;
		ButtonMesh ??= GetNodeOrNull<MeshInstance3D>("ButtonMesh");
		DirectionLabel ??= GetNodeOrNull<Label3D>("Label3D");
	}

	private InteractableFloodlightSwitch GetTargetFloodlight()
	{
		if (_cachedSwitch != null && IsInstanceValid(_cachedSwitch))
			return _cachedSwitch;

		var allFloodlights = GetTree().GetNodesInGroup("Floodlights").OfType<InteractableFloodlightSwitch>();
		_cachedSwitch = allFloodlights.FirstOrDefault(f => f.Direction == Direction);
		return _cachedSwitch;
	}

	private InteractableBatteryStation GetBatteryStation()
	{
		return InteractableBatteryStation.GetStation(PowerSystemType.Floodlights)
			?? InteractableBatteryStation.Instance 
			?? GetTree()?.GetFirstNodeInGroup("BatteryStation") as InteractableBatteryStation;
	}

	public override string GetPrompt()
	{
		string dirName = DirectionNames[Mathf.Clamp(Direction, 0, 3)];
		var station = GetBatteryStation();

		if (station == null || !station.HasBattery)
			return $"[!] {dirName} Floodlight Offline - No Battery in Floodlight Station";

		if (station.BatteryHealth <= 0)
			return $"[!] {dirName} Floodlight Offline - Battery Dead (0% HP)";

		var flood = GetTargetFloodlight();
		if (flood != null)
		{
			if (flood.IsActive)
				return $"[Active] {dirName} Floodlight Firing! [Floodlight Batt: {station.BatteryHealth}%]";
			if (flood.IsOnCooldown)
				return $"[Cooldown] {dirName} Floodlight Capacitor Recharging...";
		}

		return $"[E / LMB] Fire {dirName} Floodlight (Cost: 25% | Floodlight Batt: {station.BatteryHealth}%)";
	}

	protected override void OnInteract(PlayerController player)
	{
		// Tactile press button animation
		if (ButtonMesh != null)
		{
			if (_pressTween != null && _pressTween.IsValid()) _pressTween.Kill();
			_pressTween = CreateTween();
			_pressTween.TweenProperty(ButtonMesh, "position:y", -0.015f, 0.07f);
			_pressTween.TweenProperty(ButtonMesh, "position:y", 0.0f, 0.10f);
		}

		var flood = GetTargetFloodlight();
		if (flood == null)
		{
			_hud?.SetInteractionPrompt($"[!] No floodlight stand linked to {DirectionNames[Mathf.Clamp(Direction, 0, 3)]} perimeter.");
			return;
		}

		// Forward interaction to the floodlight switch
		flood.Interact(player);
		FloodlightButtonPressed?.Invoke();
	}
}
