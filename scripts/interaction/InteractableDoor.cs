// =========================================================================================
// Original Author : air (Airless-KV) - Oct 2026
// Responsibilities: - Interactive cabin front door with hinge pivot rotation and tweening
//                   - Entrance security state tracking for BreachManager night defense
// -----------------------------------------------------------------------------------------
// Change Log:
// - Oct 24, 2026 | air : Streamlined door tweening and prompt formatting for Alpha build
// =========================================================================================
using Godot;

public partial class InteractableDoor : InteractableBase
{
	[Signal] public delegate void DoorStateChangedEventHandler(bool isOpen);

	[ExportGroup("Door Settings")]
	[Export] public bool IsOpen { get; set; } = false;
	[Export] public bool IsLocked { get; set; } = false;
	[Export] public Node3D DoorHinge;
	[Export] public float OpenAngleDegrees = -90.0f;
	[Export] public float AnimationDuration = 0.5f;

	private float _closedRotationY;
	private Tween _currentTween;

	public override void _Ready()
	{
		base._Ready();
		AddToGroup("Doors");
		AddToGroup("Entrances");
		DoorHinge ??= this;
		_closedRotationY = DoorHinge.RotationDegrees.Y;
		if (IsOpen) DoorHinge.RotationDegrees = new Vector3(DoorHinge.RotationDegrees.X, _closedRotationY + OpenAngleDegrees, DoorHinge.RotationDegrees.Z);
	}

	public override string GetPrompt() => IsLocked ? "[Locked] Door cannot be opened" : (IsOpen ? "Press [E] to Close Door" : "Press [E] to Open Door");

	protected override void OnInteract(PlayerController player)
	{
		if (IsLocked) return;
		IsOpen = !IsOpen;
		AnimateDoor();
		EmitSignal(SignalName.DoorStateChanged, IsOpen);
	}

	private void AnimateDoor()
	{
		if (DoorHinge == null) return;
		_currentTween?.Kill();
		_currentTween = CreateTween();
		float targetAngle = IsOpen ? (_closedRotationY + OpenAngleDegrees) : _closedRotationY;
		Vector3 targetRot = new(DoorHinge.RotationDegrees.X, targetAngle, DoorHinge.RotationDegrees.Z);
		_currentTween.TweenProperty(DoorHinge, "rotation_degrees", targetRot, AnimationDuration).SetTrans(Tween.TransitionType.Quad).SetEase(Tween.EaseType.Out);
	}
}
