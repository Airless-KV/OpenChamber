// =========================================================================================
// Original Author : air (Airless-KV) - Oct 2026
// Responsibilities: - Base abstract class for cabin security entrances (Doors, Windows)
//                   - Entrance state management (IsOpen, IsLocked) and perimeter tracking
//                   - Wooden plank barricading system for absorbing night horror breaches
//                   - Standardized interaction prompts and entrance animation dispatch
// -----------------------------------------------------------------------------------------
// Change Log:
// - Oct 31, 2026 | air : Created common entrance base class to unify door and window security logic
// - Nov 06, 2026 | air : Added wooden plank barricading ([F]), visual plank generation, and breach absorption
// =========================================================================================
using Godot;

public abstract partial class InteractableEntrance : InteractableBase
{
	[Signal] public delegate void EntranceStateChangedEventHandler(bool isOpen);
	[Signal] public delegate void BarricadeStateChangedEventHandler(bool isBarricaded);

	[ExportGroup("Entrance Settings")]
	[Export] public bool IsOpen { get; set; } = false;
	[Export] public bool IsLocked { get; set; } = false;
	[Export] public float AnimationDuration = 0.45f;

	[ExportGroup("Barricade Fortification")]
	[Export] public bool IsBarricaded { get; set; } = false;
	[Export] public Node3D BarricadeVisual;

	protected abstract string EntranceTypeName { get; }
	protected abstract string SfxName { get; }

	public override void _Ready()
	{
		base._Ready();
		AddToGroup("Entrances");
		EnsureBarricadeVisual();
	}

	public override string GetPrompt()
	{
		if (IsLocked)
			return $"[Locked] {EntranceTypeName} cannot be opened";

		if (IsBarricaded)
			return $"[Barricaded] {EntranceTypeName} reinforced with Wooden Planks | [E] Toggle";

		if (IsOpen)
			return $"Press [E] to Close {EntranceTypeName}";

		var gm = GetTree().Root.FindChild("GameManager", true, false) as GameManager;
		int planks = gm?.Planks ?? 0;
		if (planks > 0)
			return $"Press [E] to Open {EntranceTypeName} | Press [F] to Barricade (1 Plank)";

		return $"Press [E] to Open {EntranceTypeName} | [F] Barricade (Need Plank)";
	}

	protected override void OnInteract(PlayerController player)
	{
		if (IsLocked) return;
		IsOpen = !IsOpen;
		AnimateEntrance();
		if (!string.IsNullOrEmpty(SfxName)) AudioManager.Sfx(SfxName);
		EmitSignal(SignalName.EntranceStateChanged, IsOpen);
	}

	protected override void OnInteractSecondary(PlayerController player)
	{
		if (IsLocked) return;

		var hud = GetTree().Root.FindChild("HUD", true, false) as HUDManager;
		if (IsOpen)
		{
			hud?.SetInteractionPrompt($"[!] Close the {EntranceTypeName} before barricading!");
			return;
		}

		if (IsBarricaded)
		{
			hud?.SetInteractionPrompt($"[✓] {EntranceTypeName} is already fortified with Wooden Planks!");
			return;
		}

		var gm = GetTree().Root.FindChild("GameManager", true, false) as GameManager;
		if (gm != null && gm.TryUsePlank())
		{
			IsBarricaded = true;
			if (BarricadeVisual != null) BarricadeVisual.Visible = true;
			AudioManager.Sfx("door_thud");
			hud?.SetInteractionPrompt($"[✓] {EntranceTypeName} Fortified with Wooden Plank! ({gm.Planks} Planks remaining)");
			EmitSignal(SignalName.BarricadeStateChanged, true);
		}
		else
		{
			hud?.SetInteractionPrompt($"[!] Need a Wooden Plank from the Flower Shop [E] to barricade {EntranceTypeName}!");
		}
	}

	public bool AbsorbBreachWithBarricade()
	{
		if (!IsBarricaded) return false;
		IsBarricaded = false;
		if (BarricadeVisual != null) BarricadeVisual.Visible = false;
		AudioManager.Sfx("window_rattle");
		EmitSignal(SignalName.BarricadeStateChanged, false);
		return true;
	}

	private void EnsureBarricadeVisual()
	{
		if (BarricadeVisual != null)
		{
			BarricadeVisual.Visible = IsBarricaded;
			return;
		}

		BarricadeVisual = GetNodeOrNull<Node3D>("BarricadeVisual");
		if (BarricadeVisual == null)
		{
			var root = new Node3D { Name = "BarricadeVisual" };
			var mat = new StandardMaterial3D { AlbedoColor = new Color(0.45f, 0.28f, 0.14f), Roughness = 0.85f };
			
			var p1 = new MeshInstance3D
			{
				Mesh = new BoxMesh { Size = new Vector3(0.08f, 0.18f, 1.6f) },
				MaterialOverride = mat,
				RotationDegrees = new Vector3(0, 0, 32)
			};
			var p2 = new MeshInstance3D
			{
				Mesh = new BoxMesh { Size = new Vector3(0.08f, 0.18f, 1.6f) },
				MaterialOverride = mat,
				RotationDegrees = new Vector3(0, 0, -32)
			};
			root.AddChild(p1);
			root.AddChild(p2);
			AddChild(root);
			BarricadeVisual = root;
		}
		BarricadeVisual.Visible = IsBarricaded;
	}

	protected abstract void AnimateEntrance();
}
