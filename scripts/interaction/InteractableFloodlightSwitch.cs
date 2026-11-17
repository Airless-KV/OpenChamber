// =========================================================================================
// Original Author : air (Airless-KV) - Nov 2026
// Responsibilities: - Interior cabin floodlight interactive switch
//                   - Enforces open door/window requirement before garden illumination
//                   - Interior halogen spotlight activation aimed through entrance opening
//                   - Dispatches blinding repel signal to CropDevourer and attacking Ani-Ani
// -----------------------------------------------------------------------------------------
// Change Log:
// - Nov 06, 2026 | air : Implemented physical cabin switch, open-entrance check, and devourer repel
// - Nov 06, 2026 | air : Added directional aiming, Ani-Ani repel check, and interior stand support
// - Nov 06, 2026 | air : Gated floodlight beam on unbarricaded opening (0 planks) requiring player tactical trade-off
// - Nov 10, 2026 | air : Updated prompt to Left Click to fire and Right Click to clear planks
// - Nov 13, 2026 | air : Gated spotlights on treehouse Car Battery power and integrated 25% health drain per activation
// - Nov 14, 2026 | air : Synchronized Direction with RequiredEntrance.AttackDirection in _Ready
// - Nov 15, 2026 | air : Removed barricade obstruction requirements from floodlight beam
// - Nov 17, 2026 | air : Increased floodlight battery consumption to 35% per activation
// - Nov 17, 2026 | air : Streamlined prompt format to '[E / LMB]' convention
// =========================================================================================
using Godot;
using System.Linq;

public partial class InteractableFloodlightSwitch : InteractableBase
{
	[ExportGroup("Floodlight Setup")]
	[Export] public SpotLight3D ExteriorFloodlight;
	[Export] public InteractableEntrance RequiredEntrance;
	[Export(PropertyHint.Enum, "North,East,South,West")] public int Direction = 1;
	[Export] public float ActiveDuration = 6.0f;
	[Export] public float CooldownDuration = 10.0f;

	[ExportGroup("Switch Visuals")]
	[Export] public MeshInstance3D SwitchLever;
	[Export] public MeshInstance3D LampLensMesh;

	public bool IsActive { get; private set; }
	public bool IsOnCooldown => _cooldownTimer > 0f;

	private float _activeTimer;
	private float _cooldownTimer;
	private HUDManager _hud;
	private StandardMaterial3D _lensMat;

	public override void _Ready()
	{
		base._Ready();
		AddToGroup("Floodlights");
		_hud ??= GetTree().Root.FindChild("HUD", true, false) as HUDManager;
		if (ExteriorFloodlight != null) ExteriorFloodlight.Visible = false;

		LampLensMesh ??= GetNodeOrNull<MeshInstance3D>("LampHead/LensMesh");
		if (LampLensMesh != null && LampLensMesh.MaterialOverride is StandardMaterial3D sm)
		{
			_lensMat = sm.Duplicate() as StandardMaterial3D;
			LampLensMesh.MaterialOverride = _lensMat;
			SetLensActive(false);
		}

		if (RequiredEntrance != null)
		{
			Direction = RequiredEntrance.AttackDirection;
		}
	}

	public override void _Process(double delta)
	{
		float dt = (float)delta;
		if (IsActive)
		{
			_activeTimer -= dt;
			if (_activeTimer <= 0f)
			{
				TurnOff();
			}
		}
		else if (_cooldownTimer > 0f)
		{
			_cooldownTimer -= dt;
		}
	}

	private LockableOpening GetOpeningLockable()
	{
		if (RequiredEntrance == null) return null;
		return RequiredEntrance.GetChildren().OfType<LockableOpening>().FirstOrDefault()
			?? GetTree().GetNodesInGroup("Lockables").OfType<LockableOpening>().FirstOrDefault(o => o.Entrance == RequiredEntrance);
	}

	private InteractableBatteryStation GetBatteryStation()
	{
		return InteractableBatteryStation.GetStation(PowerSystemType.Floodlights)
			?? InteractableBatteryStation.Instance 
			?? GetTree()?.GetFirstNodeInGroup("BatteryStation") as InteractableBatteryStation;
	}

	public override string GetPrompt()
	{
		var station = GetBatteryStation();
		if (station == null || !station.HasBattery)
			return "[!] NO POWER! Insert Car Battery at Floodlight Power Station!";

		if (station.BatteryHealth <= 0)
			return "[!] BATTERY DEAD (0% HP)! Replace battery at Floodlight Power Station!";

		if (IsActive)
			return $"[Active] Floodlight beam active! ({Mathf.CeilToInt(_activeTimer)}s) [Floodlight Batt: {station.BatteryHealth}%]";

		if (IsOnCooldown)
			return $"[Cooldown] Recharging floodlight capacitor ({Mathf.CeilToInt(_cooldownTimer)}s)...";

		if (RequiredEntrance != null && !RequiredEntrance.IsOpen)
			return $"[!] {RequiredEntrance.Name} is CLOSED! Open it to cast floodlight beam outside!";

		return $"[E / LMB] Fire Floodlight through {RequiredEntrance?.Name ?? "Opening"}! [Batt: {station.BatteryHealth}%]";
	}

	protected override void OnInteract(PlayerController player)
	{
		if (IsActive || IsOnCooldown) return;

		var station = GetBatteryStation();
		if (station == null || !station.HasPower)
		{
			AudioManager.Sfx("window_rattle", 1f);
			if (station == null || !station.HasBattery)
				_hud?.SetInteractionPrompt("[!] NO POWER! Floodlights offline - insert a Car Battery into the Treehouse Power Station!");
			else
				_hud?.SetInteractionPrompt("[!] NO POWER! Car Battery is dead (0% HP) - replace it at the Power Station!");
			return;
		}

		if (RequiredEntrance != null && !RequiredEntrance.IsOpen)
		{
			_hud?.SetInteractionPrompt($"[!] Open {RequiredEntrance.Name} first to aim the beam outside!");
			return;
		}

		TurnOn();
	}

	private void TurnOn()
	{
		IsActive = true;
		_activeTimer = ActiveDuration;

		// Consume 35% battery health from treehouse power station
		var station = GetBatteryStation();
		station?.ConsumePower(35);

		if (ExteriorFloodlight != null)
		{
			ExteriorFloodlight.Visible = true;
			ExteriorFloodlight.LightEnergy = 9.0f;
		}

		SetLensActive(true);

		if (SwitchLever != null)
			SwitchLever.RotationDegrees = new Vector3(-35f, 0, 0);

		AudioManager.Sfx("warning_stinger");

		bool repelledTarget = false;

		// 1. Blinding repel against Crop Devourer stalking crops outside
		var devourer = GetTree().GetNodesInGroup("CropDevourers").OfType<CropDevourer>().FirstOrDefault()
			?? GetTree().Root.FindChild("CropDevourer", true, false) as CropDevourer;

		if (devourer != null && devourer.RepelByFloodlight())
		{
			repelledTarget = true;
			_hud?.SetInteractionPrompt("[✓] FLOODLIGHT BLINDED THE CROP DEVOURER! Crops protected!");
		}

		// 2. Blinding repel against Ani-Ani if attacking from this direction
		var breach = GetTree().Root.FindChild("BreachManager", true, false) as BreachManager;
		if (breach != null && breach.IsMonsterAttacking && (int)breach.CurrentApproachDir == Direction)
		{
			breach.RepelByFloodlight();
			repelledTarget = true;
			_hud?.SetInteractionPrompt($"[✓] FLOODLIGHT BLASTED ANI-ANI AT {breach.CurrentApproachDir.ToString().ToUpper()}! MONSTER REPELLED!");
		}

		if (!repelledTarget)
		{
			_hud?.SetInteractionPrompt("[!] Floodlight beam active! High-power halogen beam piercing the perimeter.");
		}
	}

	private void TurnOff()
	{
		IsActive = false;
		_cooldownTimer = CooldownDuration;

		if (ExteriorFloodlight != null)
			ExteriorFloodlight.Visible = false;

		SetLensActive(false);

		if (SwitchLever != null)
			SwitchLever.RotationDegrees = Vector3.Zero;
	}

	private void SetLensActive(bool active)
	{
		if (_lensMat != null)
		{
			_lensMat.EmissionEnabled = true;
			_lensMat.Emission = active ? new Color(1f, 0.95f, 0.8f) : new Color(0.12f, 0.12f, 0.12f);
			_lensMat.EmissionEnergyMultiplier = active ? 4.0f : 0.05f;
		}
	}
}
