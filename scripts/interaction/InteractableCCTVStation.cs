// =========================================================================================
// Original Author : air (Airless-KV) - Oct 2026
// Responsibilities: - Interactive cabin CCTV security desk station
//                   - Toggle CCTV monitoring mode on NighttCamera
// Change Log:
// - Nov 10, 2026 | air : Updated prompt to Left Click to access/exit CCTV
// - Nov 14, 2026 | air : Added battery power gating preventing CCTV access without charged Treehouse battery
// =========================================================================================
using Godot;

public partial class InteractableCCTVStation : InteractableBase
{
	[Export] public NighttCamera NightCameraNode;

	public override void _Ready()
	{
		base._Ready();
		if (NightCameraNode == null)
		{
			NightCameraNode = GetTree().Root.FindChild("NighttCamera", true, false) as NighttCamera;
		}
	}

	public override string GetPrompt()
	{
		if (NightCameraNode != null && NightCameraNode.IsNightMode)
		{
			return "Left Click (or Right Click) to Exit CCTV Monitor";
		}

		var station = InteractableBatteryStation.Instance;
		if (station == null || !station.HasPower)
		{
			return "[!] CCTV Offline - Insert Car Battery in Treehouse Power Station";
		}

		return "Left Click to Access CCTV Security Feeds";
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
			var station = InteractableBatteryStation.Instance;
			if (station == null || !station.HasPower)
			{
				AudioManager.Sfx("window_rattle", -2f);
				var hud = GetTree().Root.FindChild("HUD", true, false) as HUDManager;
				hud?.SetInteractionPrompt("[!] CCTV UNPOWERED! Install a charged Car Battery into the Treehouse Power Station.");
				return;
			}
		}

		NightCameraNode.ApplyMode(!NightCameraNode.IsNightMode);
		GD.Print($"Toggled CCTV Security Station. IsNightMode: {NightCameraNode.IsNightMode}");
	}
}
