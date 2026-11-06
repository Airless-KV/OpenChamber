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
// - Nov 06, 2026 | air : Unified prompts and actions with LockableOpening 3s hold barricade/unbarricade mechanics
// =========================================================================================
using Godot;
using System.Linq;

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
		var lockable = GetChildren().OfType<LockableOpening>().FirstOrDefault();
		if (lockable != null)
		{
			if (lockable.PlanksPlaced > 0)
				return $"[Barricaded: {lockable.PlanksPlaced}/{lockable.MaxPlanks}] {EntranceTypeName} | [F] hold (3s): remove | [G] hold (3s): add";

			if (IsLocked)
				return $"[Locked] {EntranceTypeName} | Hold [F] (3s) to Unlock";

			if (IsOpen)
				return $"Press [E] to Close {EntranceTypeName} | [G] hold (3s): Barricade";

			return $"Press [E] to Open {EntranceTypeName} | [F] hold (3s): Lock | [G] hold (3s): Barricade";
		}

		if (IsLocked)
			return $"[Locked] {EntranceTypeName} cannot be opened";

		if (IsOpen)
			return $"Press [E] to Close {EntranceTypeName}";

		return $"Press [E] to Open {EntranceTypeName}";
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
		// Handled via LockableOpening hold timers ([F] hold, [G] hold)
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
