using Godot;

public partial class InteractableDoor : InteractableBase
{
	[Signal]
	public delegate void DoorStateChangedEventHandler(bool isOpen);

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
		if (DoorHinge == null)
		{
			DoorHinge = this;
		}
		_closedRotationY = DoorHinge.RotationDegrees.Y;

		if (IsOpen)
		{
			DoorHinge.RotationDegrees = new Vector3(
				DoorHinge.RotationDegrees.X,
				_closedRotationY + OpenAngleDegrees,
				DoorHinge.RotationDegrees.Z
			);
		}
	}

	public override string GetPrompt()
	{
		if (IsLocked)
			return "[Locked] Door cannot be opened";

		return IsOpen ? "Press [E] to Close Door" : "Press [E] to Open Door";
	}

	protected override void OnInteract(PlayerController player)
	{
		if (IsLocked)
		{
			GD.Print("Door is locked!");
			return;
		}

		IsOpen = !IsOpen;
		AnimateDoor();
		EmitSignal(SignalName.DoorStateChanged, IsOpen);
		GD.Print($"Door is now: {(IsOpen ? "OPEN" : "CLOSED")}");
	}

	private void AnimateDoor()
	{
		if (DoorHinge == null)
			return;

		_currentTween?.Kill();
		_currentTween = CreateTween();

		float targetAngle = IsOpen ? (_closedRotationY + OpenAngleDegrees) : _closedRotationY;
		Vector3 targetRot = new Vector3(
			DoorHinge.RotationDegrees.X,
			targetAngle,
			DoorHinge.RotationDegrees.Z
		);

		_currentTween.TweenProperty(DoorHinge, "rotation_degrees", targetRot, AnimationDuration)
			.SetTrans(Tween.TransitionType.Quad)
			.SetEase(Tween.EaseType.Out);
	}
}
