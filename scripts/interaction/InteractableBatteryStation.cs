// =========================================================================================
// Original Author : air (Airless-KV) - Nov 13, 2026
// Responsibilities: - Treehouse power station and car battery docking station
//                   - Manages battery insertion, ejection, and physical dock visuals
//                   - Powers interior halogen spotlights and supplies power queries
//                   - Per-activation battery health degradation and LED status indicators
// -----------------------------------------------------------------------------------------
// Change Log:
// - Nov 13, 2026 | air : Created physical Treehouse Car Battery Power Station
// - Nov 14, 2026 | air : Added in-scene visual binding support for editor visibility
// =========================================================================================
using Godot;
using System;

public partial class InteractableBatteryStation : InteractableBase
{
	public static InteractableBatteryStation Instance { get; private set; }

	[ExportGroup("Battery State")]
	[Export] public bool HasBattery { get; set; } = false;
	[Export] public int BatteryHealth { get; set; } = 100;
	[Export] public int DrainPerUsage { get; set; } = 25;

	[ExportGroup("Visual References")]
	[Export] public Node3D BatteryVisual;
	[Export] public MeshInstance3D LedIndicator;
	[Export] public Label3D StatusLabel;
	[Export] public OmniLight3D IndicatorLight;

	public bool HasPower => HasBattery && BatteryHealth > 0;

	private HUDManager _hud;
	private StandardMaterial3D _ledMat;

	public override void _EnterTree()
	{
		Instance = this;
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

		if (LedIndicator != null)
		{
			_ledMat = LedIndicator.MaterialOverride as StandardMaterial3D;
			if (_ledMat == null)
			{
				_ledMat = new StandardMaterial3D { EmissionEnabled = true };
				LedIndicator.MaterialOverride = _ledMat;
			}
		}

		BuildStationVisuals();
		UpdateVisuals();
	}

	public override void _ExitTree()
	{
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
				return $"[LMB] Insert Car Battery ({hp}% HP) into Power Station";
			}
			return "[!] Power Station Empty - Insert Car Battery to power spotlights";
		}

		if (BatteryHealth <= 0)
		{
			return "[!] DEAD BATTERY (0% HP) - [LMB] Eject Battery to replace";
		}

		return $"[LMB] Eject Car Battery ({BatteryHealth}% HP) | Spotlights Powered";
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
				_hud?.SetInteractionPrompt($"[✓] Car Battery Installed ({BatteryHealth}% HP)! Treehouse spotlights powered.");
				UpdateVisuals();
			}
			else
			{
				_hud?.SetInteractionPrompt("[!] You need a Car Battery to power this station! (Found in cabin or bought at Shop)");
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
				_hud?.SetInteractionPrompt($"[✓] Ejected Car Battery ({ejectedHp}% HP) to carried inventory.");
			}
			else
			{
				// Carried inventory is full (3/3): spawn on floor near station
				Node sceneRoot = GetTree()?.CurrentScene ?? (Node)this;
				Vector3 dropPos = GlobalPosition + new Vector3(0, 0.35f, 0.6f);
				WorldItem.Spawn(sceneRoot, dropPos, ItemType.CarBattery, 1, ejectedHp);
				_hud?.SetInteractionPrompt($"[✓] Ejected Car Battery ({ejectedHp}% HP) onto cabin floor (inventory full).");
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
			_hud?.SetInteractionPrompt("[!] BATTERY DIED! The spotlights consumed all power from the battery!");
		}

		return true;
	}

	private void BuildStationVisuals()
	{
		// 1. Station Cradle / Metal Base Box
		if (GetNodeOrNull<MeshInstance3D>("BaseBox") == null)
		{
			var baseBox = new MeshInstance3D { Name = "BaseBox" };
			baseBox.Mesh = new BoxMesh { Size = new Vector3(0.85f, 0.65f, 0.55f) };
			baseBox.MaterialOverride = new StandardMaterial3D
			{
				AlbedoColor = new Color(0.24f, 0.26f, 0.30f),
				Roughness = 0.5f,
				Metallic = 0.4f
			};
			AddChild(baseBox);

			var col = GetNodeOrNull<CollisionShape3D>("CollisionShape3D");
			if (col == null)
			{
				col = new CollisionShape3D { Name = "CollisionShape3D" };
				col.Shape = new BoxShape3D { Size = new Vector3(0.9f, 0.7f, 0.6f) };
				AddChild(col);
			}
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
			posTerm.Mesh = new CylinderMesh { TopRadius = 0.045f, BottomRadius = 0.05f, Height = 0.09f };
			posTerm.MaterialOverride = new StandardMaterial3D { AlbedoColor = new Color(0.85f, 0.15f, 0.15f) };
			posTerm.Position = new Vector3(-0.16f, 0.21f, 0f);
			battNode.AddChild(posTerm);

			var negTerm = new MeshInstance3D();
			negTerm.Mesh = new CylinderMesh { TopRadius = 0.045f, BottomRadius = 0.05f, Height = 0.09f };
			negTerm.MaterialOverride = new StandardMaterial3D { AlbedoColor = new Color(0.18f, 0.38f, 0.88f) };
			negTerm.Position = new Vector3(0.16f, 0.21f, 0f);
			battNode.AddChild(negTerm);

			AddChild(battNode);
			BatteryVisual = battNode;
		}

		// 3. Status LED Light and Indicator
		if (LedIndicator == null)
		{
			_ledMat = new StandardMaterial3D { EmissionEnabled = true };
			var led = new MeshInstance3D { Name = "LedIndicator" };
			led.Mesh = new SphereMesh { Radius = 0.05f, Height = 0.1f };
			led.Position = new Vector3(0.32f, 0.22f, 0.28f);
			led.MaterialOverride = _ledMat;
			AddChild(led);
			LedIndicator = led;
		}

		if (IndicatorLight == null)
		{
			var light = new OmniLight3D { Name = "IndicatorLight" };
			light.Position = new Vector3(0.32f, 0.22f, 0.32f);
			light.OmniRange = 1.2f;
			light.LightEnergy = 0.8f;
			AddChild(light);
			IndicatorLight = light;
		}

		// 4. Status Text Label
		if (StatusLabel == null)
		{
			var lbl = new Label3D { Name = "StatusLabel" };
			lbl.Position = new Vector3(0, 0.62f, 0);
			lbl.Billboard = BaseMaterial3D.BillboardModeEnum.FixedY;
			lbl.FontSize = 26;
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
				StatusLabel.Text = "[POWER STATION: EMPTY]\nInsert Car Battery";
				StatusLabel.Modulate = new Color(0.9f, 0.4f, 0.4f);
			}
		}
		else if (BatteryHealth > 50)
		{
			ledColor = new Color(0.15f, 0.95f, 0.35f); // Vibrant Green
			energy = 1.2f;
			if (StatusLabel != null)
			{
				StatusLabel.Text = $"[POWER STATION: ONLINE]\nBattery: {BatteryHealth}% HP";
				StatusLabel.Modulate = new Color(0.3f, 0.95f, 0.4f);
			}
		}
		else if (BatteryHealth > 0)
		{
			ledColor = new Color(0.95f, 0.7f, 0.15f); // Amber / Orange
			energy = 1.0f;
			if (StatusLabel != null)
			{
				StatusLabel.Text = $"[POWER STATION: LOW]\nBattery: {BatteryHealth}% HP";
				StatusLabel.Modulate = new Color(0.95f, 0.75f, 0.2f);
			}
		}
		else
		{
			ledColor = new Color(0.95f, 0.15f, 0.15f); // Depleted Red
			energy = 1.0f;
			if (StatusLabel != null)
			{
				StatusLabel.Text = "[POWER STATION: DEAD]\nBattery: 0% (Replace)";
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
	}
}
