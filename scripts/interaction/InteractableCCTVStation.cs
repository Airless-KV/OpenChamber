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
			return "Press [E] to Exit CCTV Monitor";
		}
		return "Press [E] to Access CCTV Security Feeds";
	}

	protected override void OnInteract(PlayerController player)
	{
		if (NightCameraNode == null)
		{
			NightCameraNode = GetTree().Root.FindChild("NighttCamera", true, false) as NighttCamera;
		}

		if (NightCameraNode != null)
		{
			NightCameraNode.ApplyMode(!NightCameraNode.IsNightMode);
			GD.Print($"Toggled CCTV Security Station. IsNightMode: {NightCameraNode.IsNightMode}");
		}
	}
}
