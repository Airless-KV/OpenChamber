using Godot;

public partial class InteractableWindow : InteractableBase
{
	[Signal]
	public delegate void WindowStateChangedEventHandler(bool isOpen);

	[ExportGroup("Window Settings")]
	[Export] public bool IsOpen { get; set; } = false;
	[Export] public bool IsLocked { get; set; } = false;
	[Export] public Node3D WindowMesh;
	[Export] public Vector3 OpenOffset = new Vector3(0, 0.8f, 0); // Slides up when opened
	[Export] public float AnimationDuration = 0.4f;

	private Vector3 _closedPosition;
	private Tween _currentTween;

	public override void _Ready()
	{
		base._Ready();
		AddToGroup("Windows");
		AddToGroup("Entrances");
		if (WindowMesh == null)
		{
			WindowMesh = this;
		}
		_closedPosition = WindowMesh.Position;

		if (IsOpen)
		{
			WindowMesh.Position = _closedPosition + OpenOffset;
		}
	}

	public override string GetPrompt()
	{
		if (IsLocked)
			return "[Locked] Window cannot be opened";

		return IsOpen ? "Press [E] to Close Window" : "Press [E] to Open Window";
	}

	protected override void OnInteract(PlayerController player)
	{
		if (IsLocked)
		{
			GD.Print("Window is locked!");
			return;
		}

		IsOpen = !IsOpen;
		AnimateWindow();
		EmitSignal(SignalName.WindowStateChanged, IsOpen);
		GD.Print($"Window is now: {(IsOpen ? "OPEN" : "CLOSED")}");
	}

	private void AnimateWindow()
	{
		if (WindowMesh == null)
			return;

		_currentTween?.Kill();
		_currentTween = CreateTween();

		Vector3 targetPos = IsOpen ? (_closedPosition + OpenOffset) : _closedPosition;

		_currentTween.TweenProperty(WindowMesh, "position", targetPos, AnimationDuration)
			.SetTrans(Tween.TransitionType.Quad)
			.SetEase(Tween.EaseType.Out);
	}
}
