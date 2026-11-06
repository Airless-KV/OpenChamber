// =========================================================================================
// Original Author : air (Airless-KV) - Nov 06, 2026
// Responsibilities: - Perimeter Garden Barrier deployment station
//                   - Consumes Garden Barrier from GameManager inventory
//                   - Spawns visual perimeter fence and slows Ani-Ani attack progression
// -----------------------------------------------------------------------------------------
// Change Log:
// - Nov 06, 2026 | air : Created InteractableBarrierSlot with directional defense registration
// =========================================================================================
using Godot;

public partial class InteractableBarrierSlot : InteractableBase
{
	[Signal] public delegate void BarrierDeployedEventHandler(int direction);

	[ExportGroup("Perimeter Direction")]
	[Export] public BreachManager.AttackDirection Direction = BreachManager.AttackDirection.North;

	[ExportGroup("Barrier State")]
	[Export] public bool IsDeployed { get; set; } = false;
	[Export] public Node3D BarrierVisual;

	public override void _Ready()
	{
		base._Ready();
		AddToGroup("BarrierSlots");
		EnsureVisual();
	}

	public override string GetPrompt()
	{
		if (IsDeployed)
			return $"[✓] {Direction} Perimeter Fortified with Garden Barrier";

		var gm = GetTree().Root.FindChild("GameManager", true, false) as GameManager;
		int barriers = gm?.Barriers ?? 0;
		if (barriers > 0)
			return $"Press [E] to Deploy Garden Barrier on {Direction} Perimeter (1 Barrier)";

		return $"[Locked] {Direction} Perimeter (Requires Garden Barrier from Flower Shop [E])";
	}

	protected override void OnInteract(PlayerController player)
	{
		var hud = GetTree().Root.FindChild("HUD", true, false) as HUDManager;
		if (IsDeployed)
		{
			hud?.SetInteractionPrompt($"[✓] {Direction} Perimeter is already secured by a Garden Barrier!");
			return;
		}

		var gm = GetTree().Root.FindChild("GameManager", true, false) as GameManager;
		if (gm != null && gm.TryUseBarrier())
		{
			IsDeployed = true;
			if (BarrierVisual != null) BarrierVisual.Visible = true;
			AudioManager.Sfx("door_thud");
			hud?.SetInteractionPrompt($"[✓] Deployed Garden Barrier on {Direction} perimeter! (+5s CCTV Reaction Window)");
			EmitSignal(SignalName.BarrierDeployed, (int)Direction);
		}
		else
		{
			hud?.SetInteractionPrompt($"[!] Need a Garden Barrier from the Flower Shop [E] to deploy on {Direction} perimeter!");
		}
	}

	private void EnsureVisual()
	{
		if (BarrierVisual != null)
		{
			BarrierVisual.Visible = IsDeployed;
			return;
		}

		BarrierVisual = GetNodeOrNull<Node3D>("BarrierVisual");
		if (BarrierVisual == null)
		{
			var root = new Node3D { Name = "BarrierVisual" };
			var postMat = new StandardMaterial3D { AlbedoColor = new Color(0.35f, 0.22f, 0.12f), Roughness = 0.8f };
			var wireMat = new StandardMaterial3D { AlbedoColor = new Color(0.72f, 0.65f, 0.55f), Metallic = 0.6f, Roughness = 0.4f };

			var post1 = new MeshInstance3D { Mesh = new BoxMesh { Size = new Vector3(0.2f, 1.4f, 0.2f) }, MaterialOverride = postMat, Position = new Vector3(-1.2f, 0.7f, 0) };
			var post2 = new MeshInstance3D { Mesh = new BoxMesh { Size = new Vector3(0.2f, 1.4f, 0.2f) }, MaterialOverride = postMat, Position = new Vector3(1.2f, 0.7f, 0) };
			var beam1 = new MeshInstance3D { Mesh = new BoxMesh { Size = new Vector3(2.6f, 0.15f, 0.12f) }, MaterialOverride = wireMat, Position = new Vector3(0, 0.9f, 0) };
			var beam2 = new MeshInstance3D { Mesh = new BoxMesh { Size = new Vector3(2.6f, 0.15f, 0.12f) }, MaterialOverride = wireMat, Position = new Vector3(0, 0.45f, 0) };

			root.AddChild(post1);
			root.AddChild(post2);
			root.AddChild(beam1);
			root.AddChild(beam2);
			AddChild(root);
			BarrierVisual = root;
		}
		BarrierVisual.Visible = IsDeployed;
	}
}
