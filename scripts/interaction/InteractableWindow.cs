// =========================================================================================
// Original Author : air (Airless-KV) - Oct 2026
// Responsibilities: - Interactive cabin window with sliding offset animation and tweening
//                   - Perimeter security state tracking for BreachManager night defense
// -----------------------------------------------------------------------------------------
// Change Log:
// - Oct 24, 2026 | air : Streamlined window sliding animation and prompt formatting for Alpha build
// =========================================================================================
using Godot;

public partial class InteractableWindow : InteractableBase
{
	[Signal] public delegate void WindowStateChangedEventHandler(bool isOpen);

	[ExportGroup("Window Settings")]
	[Export] public bool IsOpen { get; set; } = false;
	[Export] public bool IsLocked { get; set; } = false;
	[Export] public Node3D WindowMesh;
	[Export] public Vector3 OpenOffset = new(0, 0.8f, 0);
	[Export] public float AnimationDuration = 0.4f;

	private Vector3 _closedPosition;
	private Tween _currentTween;

	public override void _Ready()
	{
		base._Ready();
		AddToGroup("Windows");
		AddToGroup("Entrances");
		WindowMesh ??= this;
		_closedPosition = WindowMesh.Position;
		if (IsOpen) WindowMesh.Position = _closedPosition + OpenOffset;
	}

	public override string GetPrompt() => IsLocked ? "[Locked] Window cannot be opened" : (IsOpen ? "Press [E] to Close Window" : "Press [E] to Open Window");

	protected override void OnInteract(PlayerController player)
	{
		if (IsLocked) return;
		IsOpen = !IsOpen;
		AnimateWindow();
		EmitSignal(SignalName.WindowStateChanged, IsOpen);
	}

	private void AnimateWindow()
	{
		if (WindowMesh == null) return;
		_currentTween?.Kill();
		_currentTween = CreateTween();
		Vector3 targetPos = IsOpen ? (_closedPosition + OpenOffset) : _closedPosition;
		_currentTween.TweenProperty(WindowMesh, "position", targetPos, AnimationDuration).SetTrans(Tween.TransitionType.Quad).SetEase(Tween.EaseType.Out);
	}
}
