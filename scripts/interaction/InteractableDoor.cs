// =========================================================================================
// Original Author : air (Airless-KV) - Oct 2026
// Responsibilities: - Interactive cabin front door with hinge pivot rotation and tweening
//                   - Entrance security state tracking for BreachManager night defense
// -----------------------------------------------------------------------------------------
// Change Log:
// - Oct 24, 2026 | air : Streamlined door tweening and prompt formatting for Alpha build
// - Oct 31, 2026 | air : Added door_thud SFX trigger via AudioManager
// - Oct 31, 2026 | air : Derived from InteractableEntrance to eliminate duplicated entrance logic
// =========================================================================================
using Godot;

public partial class InteractableDoor : InteractableEntrance
{
	[Signal] public delegate void DoorStateChangedEventHandler(bool isOpen);

	[ExportGroup("Door Rotation Settings")]
	[Export] public Node3D DoorHinge;
	[Export] public float OpenAngleDegrees = -90.0f;

	protected override string EntranceTypeName => "Door";
	protected override string SfxName => "door_thud";

	private float _closedRotationY;
	private Tween _currentTween;

	public override void _Ready()
	{
		base._Ready();
		AddToGroup("Doors");
		DoorHinge ??= this;
		_closedRotationY = DoorHinge.RotationDegrees.Y;
		if (IsOpen) DoorHinge.RotationDegrees = new Vector3(DoorHinge.RotationDegrees.X, _closedRotationY + OpenAngleDegrees, DoorHinge.RotationDegrees.Z);
	}

	protected override void AnimateEntrance()
	{
		if (DoorHinge == null) return;
		_currentTween?.Kill();
		_currentTween = CreateTween();
		float targetAngle = IsOpen ? (_closedRotationY + OpenAngleDegrees) : _closedRotationY;
		Vector3 targetRot = new(DoorHinge.RotationDegrees.X, targetAngle, DoorHinge.RotationDegrees.Z);
		_currentTween.TweenProperty(DoorHinge, "rotation_degrees", targetRot, AnimationDuration).SetTrans(Tween.TransitionType.Quad).SetEase(Tween.EaseType.Out);
		EmitSignal(SignalName.DoorStateChanged, IsOpen);
	}
}
