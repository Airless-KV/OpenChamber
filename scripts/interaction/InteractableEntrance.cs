// =========================================================================================
// Original Author : air (Airless-KV) - Oct 2026
// Responsibilities: - Base class for cabin defense openings and barricades
//                   - Opening state management (IsOpen, IsBarricaded) and perimeter tracking
//                   - Wooden plank barricading system for absorbing night horror breaches
//                   - Standardized interaction prompts for barricading
// -----------------------------------------------------------------------------------------
// Change Log:
// - Oct 31, 2026 | air : Created common entrance base class to unify door and window security logic
// - Nov 06, 2026 | air : Added wooden plank barricading ([F]), visual plank generation, and breach absorption
// - Nov 06, 2026 | air : Unified prompts and actions with LockableOpening 3s hold barricade/unbarricade mechanics
// - Nov 09, 2026 | air : Converted to concrete Bare Opening Barricade class with door and window removal
// - Nov 10, 2026 | air : Added AttackDirection export and child LockableOpening synchronization
// - Nov 10, 2026 | air : Updated prompts to reflect Left Click placement and Right Click removal
// =========================================================================================
using Godot;
using System.Linq;

public partial class InteractableEntrance : InteractableBase
{
	[Signal] public delegate void EntranceStateChangedEventHandler(bool isOpen);
	[Signal] public delegate void BarricadeStateChangedEventHandler(bool isBarricaded);

	[ExportGroup("Entrance Settings")]
	[Export] public bool IsOpen { get; set; } = true;
	[Export] public bool IsLocked { get; set; } = false;
	[Export] public float AnimationDuration = 0.45f;
	[Export] public string EntranceTypeName { get; set; } = "Opening";
	[Export] public string SfxName { get; set; } = "door_thud";
	[Export(PropertyHint.Enum, "North,East,South,West")] public int AttackDirection { get; set; } = 0;

	[ExportGroup("Barricade Fortification")]
	[Export] public bool IsBarricaded { get; set; } = false;
	[Export] public Node3D BarricadeVisual;

	public override void _Ready()
	{
		base._Ready();
		AddToGroup("Entrances");
		EnsureBarricadeVisual();
		SyncAttackDirection();
	}

	public void SyncAttackDirection()
	{
		var lockable = GetChildren().OfType<LockableOpening>().FirstOrDefault();
		if (lockable != null)
		{
			lockable.AttackDirection = AttackDirection;
		}
	}

	public override string GetPrompt()
	{
		var lockable = GetChildren().OfType<LockableOpening>().FirstOrDefault();
		if (lockable != null)
		{
			if (lockable.PlanksPlaced > 0)
				return $"[Barricaded: {lockable.PlanksPlaced}/{lockable.MaxPlanks}] {EntranceTypeName} | Hold Left Click: Add | Hold Right Click: Remove";

			if (lockable.IsBroken)
				return $"[BROKEN OPENING (0 HP)] {EntranceTypeName} | Hold Left Click (3s) to Barricade";

			return $"[Bare {EntranceTypeName}] Hold Left Click (3s) to Place Plank (1 Plank)";
		}

		return $"[Bare {EntranceTypeName}]";
	}

	protected override void OnInteract(PlayerController player)
	{
		var hud = GetTree().Root.FindChild("HUD", true, false) as HUDManager;
		var lockable = GetChildren().OfType<LockableOpening>().FirstOrDefault();
		if (lockable != null && lockable.PlanksPlaced > 0)
		{
			hud?.SetInteractionPrompt($"[Barricaded {EntranceTypeName}] ({lockable.PlanksPlaced}/{lockable.MaxPlanks} Planks). Hold Left Click to add, or Right Click to remove.");
		}
		else
		{
			hud?.SetInteractionPrompt($"[Bare {EntranceTypeName}] Opening is unbarricaded. Hold Left Click (3s) to place a wooden plank!");
		}
	}

	protected override void OnInteractSecondary(PlayerController player)
	{
		// Handled via LockableOpening hold timers (Right Click hold, Left Click hold)
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
		if (BarricadeVisual != null)
		{
			BarricadeVisual.Visible = IsBarricaded;
		}
	}

	protected virtual void AnimateEntrance() { }
}
