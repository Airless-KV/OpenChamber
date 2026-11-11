// =========================================================================================
// Original Author : air (Airless-KV) - Oct 2026
// Responsibilities: - First-person horror camera look (pitch & yaw controls via captured mouse)
//                   - Target eye-level following (Player) with smooth interpolation
//                   - CCTV camera mode synchronization and crosshair management
//                   - Flower Shop UI cursor unlocking and input decoupling
// -----------------------------------------------------------------------------------------
// Change Log:
// - Oct 24, 2026 | air : Implemented dual-mode camera system (1st-person & 3rd-person toggle)
// - Oct 30, 2026 | air : Transitioned camera exclusively to First-Person horror perspective
//                        Removed 3rd-person orbit, wheel zoom, and perspective toggle keys
//                        Streamlined mouse look clamping and CCTV feed switching
// - Oct 31, 2026 | air : Removed duplicate Escape key intercept to prevent conflict with PauseMenu
// - Oct 31, 2026 | air : Compacted mouse look and target follow interpolation
// - Nov 06, 2026 | air : Added shop cursor synchronization to release mouse and pause look during shopping
// - Nov 07, 2026 | air : Guarded _Input against cursor re-capture when end screens or pause are active
// =========================================================================================
using Godot;

public partial class CameraPivot : Node3D
{
	[ExportGroup("First-Person Settings")]
	[Export] public Vector3 FollowOffset = new(0, 1.45f, 0);
	[Export] public float MouseSensitivity = 0.0035f;
	[Export] public float MinPitchDegrees = -85.0f;
	[Export] public float MaxPitchDegrees = 85.0f;

	[ExportGroup("Target Follow")]
	[Export] public Node3D Target;
	[Export] public float FollowSpeed = 25.0f;

	private Control _crosshair;
	private HUDManager _hud;
	private float _pitch;
	private float _yaw;
	private bool _isCCTVActive;
	private bool _isShopActive;

	public override void _Ready()
	{
		if (GetNodeOrNull<Camera3D>("Camera3D") is { } cam) cam.Position = Vector3.Zero;
		Target ??= GetTree().Root.FindChild("Player", true, false) as Node3D;
		if (Target is PlayerController { VisualRoot: { } vis }) vis.Visible = false;

		_hud ??= GetTree().Root.FindChild("HUD", true, false) as HUDManager;

		_yaw = Rotation.Y;
		_pitch = Rotation.X;
		Input.MouseMode = Input.MouseModeEnum.Captured;
		UpdateCrosshair();
	}

	public void SetCCTVActive(bool active)
	{
		_isCCTVActive = active;
		SetProcessInput(!_isCCTVActive && !_isShopActive);
		Input.MouseMode = _isCCTVActive ? Input.MouseModeEnum.Visible : (_isShopActive ? Input.MouseModeEnum.Visible : Input.MouseModeEnum.Captured);
		UpdateCrosshair();
	}

	public void SetShopActive(bool active)
	{
		if (_isShopActive == active) return;
		_isShopActive = active;
		SetProcessInput(!_isCCTVActive && !_isShopActive);
		Input.MouseMode = _isShopActive ? Input.MouseModeEnum.Visible : (_isCCTVActive ? Input.MouseModeEnum.Visible : Input.MouseModeEnum.Captured);
		UpdateCrosshair();
	}

	public override void _Input(InputEvent @event)
	{
		if (_isCCTVActive || _isShopActive || GetTree().Paused) return;

		if (GetTree().Root.FindChild("JumpScareScreen", true, false) is CanvasLayer { Visible: true } ||
			GetTree().Root.FindChild("VictoryScreen", true, false) is CanvasLayer { Visible: true } ||
			GetTree().Root.FindChild("PauseMenu", true, false) is CanvasLayer { Visible: true })
		{
			return;
		}

		if (@event is InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left } && Input.MouseMode == Input.MouseModeEnum.Visible)
		{
			Input.MouseMode = Input.MouseModeEnum.Captured;
			return;
		}

		if (@event is InputEventMouseMotion motion && Input.MouseMode == Input.MouseModeEnum.Captured)
		{
			_yaw -= motion.Relative.X * MouseSensitivity;
			_pitch = Mathf.Clamp(_pitch - motion.Relative.Y * MouseSensitivity, Mathf.DegToRad(MinPitchDegrees), Mathf.DegToRad(MaxPitchDegrees));
			Rotation = new Vector3(_pitch, _yaw, 0f);
		}
	}

	public override void _Process(double delta)
	{
		if (Target != null)
			GlobalPosition = GlobalPosition.Lerp(Target.GlobalPosition + FollowOffset, FollowSpeed * (float)delta);

		if (_hud != null && _hud.IsShopOpen != _isShopActive)
		{
			SetShopActive(_hud.IsShopOpen);
		}
	}

	private void UpdateCrosshair()
	{
		_crosshair ??= GetTree().Root.FindChild("Crosshair", true, false) as Control;
		if (_crosshair != null) _crosshair.Visible = !_isCCTVActive && !_isShopActive;
	}
}
