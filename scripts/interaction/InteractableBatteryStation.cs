// =========================================================================================
// Original Author : air (Airless-KV) - Nov 13, 2026
// Responsibilities: - Power station and car battery docking station
//                   - Manages battery insertion, ejection, and physical dock visuals
//                   - Subsystem-specific power station routing (Floodlights, Siren, CCTV)
//                   - Per-activation battery health degradation and LED status indicators
// -----------------------------------------------------------------------------------------
// Change Log:
// - Nov 13, 2026 | air : Created physical Treehouse Car Battery Power Station
// - Nov 14, 2026 | air : Added in-scene visual binding support for editor visibility
// - Nov 15, 2026 | air : Added PowerSystemType support (Floodlights, Siren, CCTV) and multi-station registry
// =========================================================================================
using Godot;
using System;
using System.Collections.Generic;

public enum PowerSystemType
{
	Floodlights,
	Siren,
	CCTV
}

public partial class InteractableBatteryStation : InteractableBase
{
	public static InteractableBatteryStation Instance { get; private set; }

	private static readonly Dictionary<PowerSystemType, InteractableBatteryStation> _stationRegistry = new();

	public static InteractableBatteryStation GetStation(PowerSystemType type)
	{
		if (_stationRegistry.TryGetValue(type, out var station) && IsInstanceValid(station))
			return station;
		return null;
	}

	[ExportGroup("Station Configuration")]
	[Export] public PowerSystemType StationType { get; set; } = PowerSystemType.Floodlights;

	[ExportGroup("Battery State")]
	[Export] public bool HasBattery { get; set; } = false;
	[Export] public int BatteryHealth { get; set; } = 100;
	[Export] public int DrainPerUsage { get; set; } = 25;

	[ExportGroup("Visual References")]
	[Export] public Node3D BatteryVisual;
	[Export] public MeshInstance3D LedIndicator;
	[Export] public Label3D StatusLabel;
	[Export] public OmniLight3D IndicatorLight;
	[Export] public MeshInstance3D LevelBarFill;

	public bool HasPower => HasBattery && BatteryHealth > 0;

	private HUDManager _hud;
	private StandardMaterial3D _ledMat;
	private StandardMaterial3D _barMat;

	public string SystemDisplayName => StationType switch
	{
		PowerSystemType.Floodlights => "Floodlights",
		PowerSystemType.Siren => "Siren",
		PowerSystemType.CCTV => "CCTV",
		_ => "Station"
	};

	public override void _EnterTree()
	{
		_stationRegistry[StationType] = this;
		if (StationType == PowerSystemType.Floodlights || Instance == null)
		{
			Instance = this;
		}
	}

	public override void _Ready()
	{
		base._Ready();
		AddToGroup("BatteryStation");
		AddToGroup("PowerStations");

		_hud ??= GetTree().Root.FindChild("HUD", true, false) as HUDManager;

		BatteryVisual ??= GetNodeOrNull<Node3D>("InstalledBatteryVisual");
		LedIndicator ??= GetNodeOrNull<MeshInstance3D>("LedIndicator");
		IndicatorLight ??= GetNodeOrNull<OmniLight3D>("IndicatorLight");
		StatusLabel ??= GetNodeOrNull<Label3D>("StatusLabel");
		LevelBarFill ??= GetNodeOrNull<MeshInstance3D>("LevelBarFill");

		if (LedIndicator != null)
		{
			_ledMat = LedIndicator.MaterialOverride as StandardMaterial3D;
			if (_ledMat == null)
			{
				_ledMat = new StandardMaterial3D { EmissionEnabled = true };
				LedIndicator.MaterialOverride = _ledMat;
			}
		}

		if (LevelBarFill != null)
		{
			_barMat = LevelBarFill.MaterialOverride as StandardMaterial3D;
			if (_barMat == null)
			{
				_barMat = new StandardMaterial3D { EmissionEnabled = true };
				LevelBarFill.MaterialOverride = _barMat;
			}
		}

		BuildStationVisuals();
		UpdateVisuals();
	}

	public override void _ExitTree()
	{
		if (_stationRegistry.TryGetValue(StationType, out var st) && st == this)
		{
			_stationRegistry.Remove(StationType);
		}
		if (Instance == this) Instance = null;
	}

	public override string GetPrompt()
	{
		var player = GetTree()?.GetFirstNodeInGroup("Player") as PlayerController;
		var inv = player?.GetNodeOrNull<CarriedInventory>("CarriedInventory");

		if (!HasBattery)
		{
			if (inv != null && inv.HasAnyBattery(out _, out int hp))
			{
				return $"[LMB] Insert Car Battery ({hp}% HP) into {SystemDisplayName} Station";
			}
			return $"[!] {SystemDisplayName} Power Station Empty - Insert Car Battery";
		}

		if (BatteryHealth <= 0)
		{
			return $"[!] DEAD BATTERY (0% HP) - [LMB] Eject {SystemDisplayName} Battery";
		}

		return $"[LMB] Eject {SystemDisplayName} Battery ({BatteryHealth}% HP) | Powered";
	}

	protected override void OnInteract(PlayerController player)
	{
		var inv = player?.GetNodeOrNull<CarriedInventory>("CarriedInventory");

		if (!HasBattery)
		{
			// Try to insert from player's inventory
			if (inv != null && (inv.ConsumeActiveBattery(out int hp) || inv.ConsumeBattery(out hp)))
			{
				HasBattery = true;
				BatteryHealth = hp;
				AudioManager.Sfx("door_thud", 2f);
				_hud?.SetInteractionPrompt($"[✓] Car Battery Installed ({BatteryHealth}% HP)! {SystemDisplayName} powered.");
				UpdateVisuals();
			}
			else
			{
				_hud?.SetInteractionPrompt($"[!] You need a Car Battery to power {SystemDisplayName}! (Found in cabin or bought at Shop)");
				AudioManager.Sfx("window_rattle", -2f);
			}
		}
		else
		{
			// Eject currently installed battery back to player
			int ejectedHp = BatteryHealth;
			HasBattery = false;
			BatteryHealth = 0;

			if (inv != null && inv.AddItem(ItemType.CarBattery, 1, ejectedHp))
			{
				_hud?.SetInteractionPrompt($"[✓] Ejected {SystemDisplayName} Battery ({ejectedHp}% HP) to carried inventory.");
			}
			else
			{
				// Carried inventory is full (3/3): spawn on floor near station
				Node sceneRoot = GetTree()?.CurrentScene ?? (Node)this;
				Vector3 dropPos = GlobalPosition + new Vector3(0, 0.35f, 0.6f);
				WorldItem.Spawn(sceneRoot, dropPos, ItemType.CarBattery, 1, ejectedHp);
				_hud?.SetInteractionPrompt($"[✓] Ejected {SystemDisplayName} Battery ({ejectedHp}% HP) onto floor (inventory full).");
			}

			AudioManager.Sfx("door_thud", -2f);
			UpdateVisuals();
		}
	}

	public bool ConsumePower(int drain = -1)
	{
		if (drain < 0) drain = DrainPerUsage;

		if (!HasPower) return false;

		BatteryHealth = Mathf.Max(0, BatteryHealth - drain);
		UpdateVisuals();

		if (BatteryHealth <= 0)
		{
			AudioManager.Sfx("banner_sting");
			_hud?.SetInteractionPrompt($"[!] BATTERY DIED! {SystemDisplayName} has consumed all power!");
		}

		return true;
	}

	public void BuildStationVisuals()
	{
		// If custom authored meshes exist in the scene (e.g. HousingMesh or Chassis), do not generate fallback dock
		if (GetNodeOrNull<MeshInstance3D>("BaseBox") != null ||
		    GetNodeOrNull<MeshInstance3D>("HousingMesh") != null ||
		    GetNodeOrNull<MeshInstance3D>("Chassis") != null)
		{
			return;
		}

		// 1. Heavy Metal Dock Stand (fallback if node was placed empty in scene)
		var baseMesh = new MeshInstance3D();
		baseMesh.Name = "BaseBox";
		baseMesh.Mesh = new BoxMesh { Size = new Vector3(0.9f, 0.7f, 0.6f) };
		baseMesh.MaterialOverride = new StandardMaterial3D
		{
			AlbedoColor = new Color(0.2f, 0.22f, 0.25f),
			Metallic = 0.8f,
			Roughness = 0.35f
		};
		AddChild(baseMesh);

		if (GetNodeOrNull<CollisionShape3D>("CollisionShape3D") == null)
		{
			var col = new CollisionShape3D();
			col.Name = "CollisionShape3D";
			col.Shape = new BoxShape3D { Size = new Vector3(0.9f, 0.7f, 0.6f) };
			AddChild(col);
		}

		// 2. Battery Tray Cutout / Dock
		if (BatteryVisual == null)
		{
			var battNode = new Node3D { Name = "InstalledBatteryVisual" };
			battNode.Position = new Vector3(0, 0.34f, 0);

			var body = new MeshInstance3D();
			body.Mesh = new BoxMesh { Size = new Vector3(0.55f, 0.36f, 0.32f) };
			body.MaterialOverride = new StandardMaterial3D { AlbedoColor = new Color(0.14f, 0.15f, 0.17f), Roughness = 0.6f };
			battNode.AddChild(body);

			var posTerm = new MeshInstance3D();
			posTerm.Mesh = new CylinderMesh { TopRadius = 0.04f, BottomRadius = 0.04f, Height = 0.08f };
			posTerm.MaterialOverride = new StandardMaterial3D { AlbedoColor = new Color(0.88f, 0.2f, 0.2f) };
			posTerm.Position = new Vector3(-0.16f, 0.21f, 0f);
			battNode.AddChild(posTerm);

			var negTerm = new MeshInstance3D();
			negTerm.Mesh = new CylinderMesh { TopRadius = 0.04f, BottomRadius = 0.04f, Height = 0.08f };
			negTerm.MaterialOverride = new StandardMaterial3D { AlbedoColor = new Color(0.18f, 0.38f, 0.88f) };
			negTerm.Position = new Vector3(0.16f, 0.21f, 0f);
			battNode.AddChild(negTerm);

			AddChild(battNode);
			BatteryVisual = battNode;
		}

		// 3. Status LED Light and Indicator
		if (LedIndicator == null)
		{
			var led = new MeshInstance3D();
			led.Name = "LedIndicator";
			led.Mesh = new SphereMesh { Radius = 0.04f, Height = 0.08f };
			_ledMat = new StandardMaterial3D { EmissionEnabled = true };
			led.MaterialOverride = _ledMat;
			led.Position = new Vector3(0.32f, 0.22f, 0.28f);
			AddChild(led);
			LedIndicator = led;
		}

		if (IndicatorLight == null)
		{
			var light = new OmniLight3D();
			light.Name = "IndicatorLight";
			light.OmniRange = 1.5f;
			light.LightEnergy = 0.5f;
			light.Position = new Vector3(0.32f, 0.22f, 0.32f);
			AddChild(light);
			IndicatorLight = light;
		}

		if (StatusLabel == null)
		{
			var lbl = new Label3D();
			lbl.Name = "StatusLabel";
			lbl.Billboard = BaseMaterial3D.BillboardModeEnum.Disabled;
			lbl.Position = new Vector3(0, 0.62f, 0);
			lbl.FontSize = 20;
			AddChild(lbl);
			StatusLabel = lbl;
		}
	}

	public void UpdateVisuals()
	{
		if (BatteryVisual != null)
		{
			BatteryVisual.Visible = HasBattery;
		}

		Color ledColor;
		float energy = 0.8f;

		if (!HasBattery)
		{
			ledColor = new Color(0.2f, 0.05f, 0.05f); // Dim off red
			energy = 0.1f;
			if (StatusLabel != null)
			{
				StatusLabel.Text = $"[{SystemDisplayName.ToUpper()} POWER: EMPTY]\nInsert Car Battery";
				StatusLabel.Modulate = new Color(0.9f, 0.4f, 0.4f);
			}
		}
		else if (BatteryHealth > 50)
		{
			ledColor = new Color(0.15f, 0.95f, 0.35f); // Vibrant Green
			energy = 1.2f;
			if (StatusLabel != null)
			{
				StatusLabel.Text = $"[{SystemDisplayName.ToUpper()} POWER: ONLINE]\nBattery: {BatteryHealth}% HP";
				StatusLabel.Modulate = new Color(0.3f, 0.95f, 0.4f);
			}
		}
		else if (BatteryHealth > 0)
		{
			ledColor = new Color(0.95f, 0.7f, 0.15f); // Amber / Orange
			energy = 1.0f;
			if (StatusLabel != null)
			{
				StatusLabel.Text = $"[{SystemDisplayName.ToUpper()} POWER: LOW]\nBattery: {BatteryHealth}% HP";
				StatusLabel.Modulate = new Color(0.95f, 0.75f, 0.2f);
			}
		}
		else
		{
			ledColor = new Color(0.95f, 0.15f, 0.15f); // Depleted Red
			energy = 1.0f;
			if (StatusLabel != null)
			{
				StatusLabel.Text = $"[{SystemDisplayName.ToUpper()} POWER: DEAD]\nBattery: 0% (Replace)";
				StatusLabel.Modulate = new Color(0.95f, 0.2f, 0.2f);
			}
		}

		if (_ledMat != null)
		{
			_ledMat.AlbedoColor = ledColor;
			_ledMat.Emission = ledColor;
			_ledMat.EmissionEnergyMultiplier = energy * 2.0f;
		}

		if (IndicatorLight != null)
		{
			IndicatorLight.LightColor = ledColor;
			IndicatorLight.LightEnergy = energy;
		}

		// Real-time physical battery level bar fill update
		if (LevelBarFill != null)
		{
			float ratio = HasBattery ? Mathf.Clamp(BatteryHealth / 100f, 0f, 1f) : 0f;
			LevelBarFill.Visible = HasBattery && ratio > 0.01f;

			// Scale bar horizontally (scale.x) and offset pivot to fill left-to-right
			LevelBarFill.Scale = new Vector3(ratio, 1f, 1f);

			Color barColor;
			if (BatteryHealth > 50)
				barColor = new Color(0.2f, 0.95f, 0.3f);
			else if (BatteryHealth > 20)
				barColor = new Color(0.95f, 0.75f, 0.15f);
			else
				barColor = new Color(0.95f, 0.2f, 0.15f);

			if (_barMat == null && LevelBarFill.MaterialOverride is StandardMaterial3D sm)
			{
				_barMat = sm;
			}

			if (_barMat != null)
			{
				_barMat.AlbedoColor = barColor;
				_barMat.Emission = barColor;
				_barMat.EmissionEnergyMultiplier = HasBattery && BatteryHealth > 0 ? 2.5f : 0f;
			}
		}
	}
}
