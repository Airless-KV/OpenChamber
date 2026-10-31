// =========================================================================================
// Original Author : air (Airless-KV) - Oct 2026
// Responsibilities: - Base abstract class for cabin security entrances (Doors, Windows)
//                   - Entrance state management (IsOpen, IsLocked) and perimeter tracking
//                   - Standardized interaction prompts and entrance animation dispatch
// -----------------------------------------------------------------------------------------
// Change Log:
// - Oct 31, 2026 | air : Created common entrance base class to unify door and window security logic
// =========================================================================================
using Godot;

public abstract partial class InteractableEntrance : InteractableBase
{
	[Signal] public delegate void EntranceStateChangedEventHandler(bool isOpen);

	[ExportGroup("Entrance Settings")]
	[Export] public bool IsOpen { get; set; } = false;
	[Export] public bool IsLocked { get; set; } = false;
	[Export] public float AnimationDuration = 0.45f;

	protected abstract string EntranceTypeName { get; }
	protected abstract string SfxName { get; }

	public override void _Ready()
	{
		base._Ready();
		AddToGroup("Entrances");
	}

	public override string GetPrompt() => IsLocked 
		? $"[Locked] {EntranceTypeName} cannot be opened" 
		: (IsOpen ? $"Press [E] to Close {EntranceTypeName}" : $"Press [E] to Open {EntranceTypeName}");

	protected override void OnInteract(PlayerController player)
	{
		if (IsLocked) return;
		IsOpen = !IsOpen;
		AnimateEntrance();
		if (!string.IsNullOrEmpty(SfxName)) AudioManager.Sfx(SfxName);
		EmitSignal(SignalName.EntranceStateChanged, IsOpen);
	}

	protected abstract void AnimateEntrance();
}
