// =========================================================================================
// Original Author : air (Airless-KV) - Oct 2026
// Responsibilities: - Interactive cabin CCTV security desk station
//                   - Toggle CCTV monitoring mode on NighttCamera
//                   - 3D world interaction and power status label for CCTV station
// Change Log:
// - Nov 10, 2026 | air : Updated prompt to Left Click to access/exit CCTV
// - Nov 14, 2026 | air : Added battery power gating preventing CCTV access without charged Treehouse battery
// - Nov 15, 2026 | air : Added dynamic 3D world interaction status label above CRT monitor
// - Nov 17, 2026 | air : Streamlined prompt format to '[E / LMB]' convention
// =========================================================================================
using Godot;

public partial class InteractableCCTVStation : InteractableBase
{
	[Export] public NighttCamera NightCameraNode;
	[Export] public Label3D InteractLabel;

	private float _labelUpdateTimer = 0f;

	public override void _Ready()
	{
		base._Ready();
		if (NightCameraNode == null)
		{
			NightCameraNode = GetTree().Root.FindChild("NighttCamera", true, false) as NighttCamera;
		}

		InteractLabel ??= GetNodeOrNull<Label3D>("InteractLabel");
		Update3DLabel();
	}

	public override void _Process(double delta)
	{
		_labelUpdateTimer += (float)delta;
		if (_labelUpdateTimer >= 0.5f)
		{
			_labelUpdateTimer = 0f;
			Update3DLabel();
		}
	}

	public void Update3DLabel()
	{
		if (InteractLabel == null) return;

		if (NightCameraNode != null && NightCameraNode.IsNightMode)
		{
			InteractLabel.Text = "CCTV MONITOR ACTIVE\nExit Monitor";
			InteractLabel.Modulate = new Color(0.2f, 0.95f, 0.8f);
			return;
		}

		var station = InteractableBatteryStation.GetStation(PowerSystemType.CCTV)
			?? InteractableBatteryStation.Instance;
		if (station == null || !station.HasPower)
		{
			InteractLabel.Text = "[!] CCTV OFFLINE\nInsert Car Battery Under Desk";
			InteractLabel.Modulate = new Color(0.95f, 0.45f, 0.2f);
		}
		else
		{
			InteractLabel.Text = $"CCTV SECURITY FEED ({station.BatteryHealth}%)\nAccess Feeds";
			InteractLabel.Modulate = new Color(0.2f, 0.95f, 0.8f);
		}
	}

	public override string GetPrompt()
	{
		if (NightCameraNode != null && NightCameraNode.IsNightMode)
		{
			return "[E / ESC / RMB] Exit CCTV Monitor";
		}

		var station = InteractableBatteryStation.GetStation(PowerSystemType.CCTV)
			?? InteractableBatteryStation.Instance;
		if (station == null || !station.HasPower)
		{
			return "[!] CCTV Offline - Insert Car Battery in CCTV Station under desk";
		}

		return $"[E / LMB] Access CCTV Security Feeds [CCTV Batt: {station.BatteryHealth}%]";
	}

	protected override void OnInteract(PlayerController player)
	{
		if (NightCameraNode == null)
		{
			NightCameraNode = GetTree().Root.FindChild("NighttCamera", true, false) as NighttCamera;
		}

		if (NightCameraNode == null) return;

		if (!NightCameraNode.IsNightMode)
		{
			var station = InteractableBatteryStation.GetStation(PowerSystemType.CCTV)
				?? InteractableBatteryStation.Instance;
			if (station == null || !station.HasPower)
			{
				AudioManager.Sfx("window_rattle", -2f);
				var hud = GetTree().Root.FindChild("HUD", true, false) as HUDManager;
				hud?.SetInteractionPrompt("[!] CCTV UNPOWERED! Install a charged Car Battery into the CCTV PC Station under the desk.");
				return;
			}
		}

		NightCameraNode.ApplyMode(!NightCameraNode.IsNightMode);
		Update3DLabel();
		GD.Print($"Toggled CCTV Security Station. IsNightMode: {NightCameraNode.IsNightMode}");
	}
}
