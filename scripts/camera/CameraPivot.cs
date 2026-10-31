// =========================================================================================
// Original Author : air (Airless-KV) - Oct 2026
// Responsibilities: - First-person horror camera look (pitch & yaw controls via captured mouse)
//                   - Target eye-level following (Player) with smooth interpolation
//                   - CCTV camera mode synchronization and crosshair management
// -----------------------------------------------------------------------------------------
// Change Log:
// - Oct 24, 2026 | air : Implemented dual-mode camera system (1st-person & 3rd-person toggle)
// - Oct 30, 2026 | air : Transitioned camera exclusively to First-Person horror perspective
//                        Removed 3rd-person orbit, wheel zoom, and perspective toggle keys
//                        Streamlined mouse look clamping and CCTV feed switching
// =========================================================================================
using Godot;

public partial class CameraPivot : Node3D
{
	[ExportGroup("First-Person Settings")]
	[Export] public Vector3 FollowOffset = new(0, 1.45f, 0); // Eye level height
	[Export] public float MouseSensitivity = 0.0035f;
	[Export] public float MinPitchDegrees = -85.0f;
	[Export] public float MaxPitchDegrees = 85.0f;

	[ExportGroup("Target Follow")]
	[Export] public Node3D Target;
	[Export] public float FollowSpeed = 25.0f;

	private Camera3D _camera;
	private PlayerController _playerController;
	private Control _crosshair;
	private float _pitch = 0f;
	private float _yaw = 0f;
	private bool _isCCTVActive = false;

	public override void _Ready()
	{
		_camera = GetNodeOrNull<Camera3D>("Camera3D");
		if (_camera != null)
			_camera.Position = Vector3.Zero;

		Target ??= GetTree().Root.FindChild("Player", true, false) as Node3D;
		_playerController = Target as PlayerController;

		if (_playerController?.VisualRoot != null)
			_playerController.VisualRoot.Visible = false;

		_yaw = Rotation.Y;
		_pitch = Rotation.X;

		Input.MouseMode = Input.MouseModeEnum.Captured;
		UpdateCrosshairVisibility();
	}

	public void SetCCTVActive(bool active)
	{
		_isCCTVActive = active;
		SetProcess(!_isCCTVActive);
		SetProcessInput(!_isCCTVActive);
		Input.MouseMode = _isCCTVActive ? Input.MouseModeEnum.Visible : Input.MouseModeEnum.Captured;
		UpdateCrosshairVisibility();
	}

	public override void _Input(InputEvent @event)
	{
		if (_isCCTVActive) return;

		// Escape to toggle mouse capture during gameplay
		if (@event is InputEventKey { Pressed: true, Echo: false, Keycode: Key.Escape })
		{
			Input.MouseMode = Input.MouseMode == Input.MouseModeEnum.Captured 
				? Input.MouseModeEnum.Visible 
				: Input.MouseModeEnum.Captured;
			return;
		}

		// Click to recapture mouse if freed
		if (@event is InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left } && Input.MouseMode == Input.MouseModeEnum.Visible)
		{
			Input.MouseMode = Input.MouseModeEnum.Captured;
			return;
		}

		// First-Person Mouse Look
		if (@event is InputEventMouseMotion motion && Input.MouseMode == Input.MouseModeEnum.Captured)
		{
			_yaw -= motion.Relative.X * MouseSensitivity;
			_pitch = Mathf.Clamp(
				_pitch - motion.Relative.Y * MouseSensitivity, 
				Mathf.DegToRad(MinPitchDegrees), 
				Mathf.DegToRad(MaxPitchDegrees)
			);
			Rotation = new Vector3(_pitch, _yaw, 0f);
		}
	}

	public override void _Process(double delta)
	{
		if (Target != null)
		{
			Vector3 targetPos = Target.GlobalPosition + FollowOffset;
			GlobalPosition = GlobalPosition.Lerp(targetPos, FollowSpeed * (float)delta);
		}
	}

	private void UpdateCrosshairVisibility()
	{
		_crosshair ??= GetTree().Root.FindChild("Crosshair", true, false) as Control;
		if (_crosshair != null)
			_crosshair.Visible = !_isCCTVActive;
	}
}
