// =========================================================================================
// Original Author : air (Airless-KV) - Oct 2026
// Responsibilities: - Interactive cabin window with sliding offset animation and tweening
//                   - Perimeter security state tracking for BreachManager night defense
// -----------------------------------------------------------------------------------------
// Change Log:
// - Oct 24, 2026 | air : Streamlined window sliding animation and prompt formatting for Alpha build
// - Oct 31, 2026 | air : Added window_rattle SFX trigger via AudioManager
// - Oct 31, 2026 | air : Derived from InteractableEntrance to eliminate duplicated entrance logic
// =========================================================================================
using Godot;

public partial class InteractableWindow : InteractableEntrance
{
	[Signal] public delegate void WindowStateChangedEventHandler(bool isOpen);

	[ExportGroup("Window Slide Settings")]
	[Export] public Node3D WindowMesh;
	[Export] public Vector3 OpenOffset = new(0, 0.8f, 0);

	protected override string EntranceTypeName => "Window";
	protected override string SfxName => "window_rattle";

	private Vector3 _closedPosition;
	private Tween _currentTween;

	public override void _Ready()
	{
		base._Ready();
		AddToGroup("Windows");
		WindowMesh ??= this;
		_closedPosition = WindowMesh.Position;
		if (IsOpen) WindowMesh.Position = _closedPosition + OpenOffset;
	}

	protected override void AnimateEntrance()
	{
		if (WindowMesh == null) return;
		_currentTween?.Kill();
		_currentTween = CreateTween();
		Vector3 targetPos = IsOpen ? (_closedPosition + OpenOffset) : _closedPosition;
		_currentTween.TweenProperty(WindowMesh, "position", targetPos, AnimationDuration).SetTrans(Tween.TransitionType.Quad).SetEase(Tween.EaseType.Out);
		EmitSignal(SignalName.WindowStateChanged, IsOpen);
	}
}
