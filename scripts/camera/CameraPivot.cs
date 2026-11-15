// =========================================================================================
// Original Author : air (Airless-KV) - Oct 2026
// Responsibilities: - First-person horror camera look (pitch & yaw controls via captured mouse)
//                   - Target eye-level following (Player) with smooth interpolation
//                   - Procedural walking wobble (headbob, sway, roll/pitch nod)
//                   - House attack structural shudder and trauma-based camera shake
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
// - Nov 14, 2026 | air : Added procedural walking wobble (headbob, sway, tilt) and house attack trauma shake to Camera3D
// - Nov 15, 2026 | air : Added InvertY, FOV scaling, and ApplySavedSettings integration
// - Nov 15, 2026 | air : Added PlayDeathSequence and camera collapse animation for 3D death scenes
// - Nov 15, 2026 | air : Implemented face-targeted look, upward pitch tumble to +52 deg, and 5.5s death sequence
// - Nov 15, 2026 | air : Added FarPlane configuration (extended to 350m) to preserve mountain perimeter
// - Nov 15, 2026 | air : Clamped FarPlane to 210m to match outer forest perimeter and reduce frustum depth churn
// =========================================================================================
using Godot;
using System;

public partial class CameraPivot : Node3D
{
	public static CameraPivot Instance { get; private set; }

	[ExportGroup("First-Person Settings")]
	[Export] public Vector3 FollowOffset = new(0, 1.45f, 0);
	[Export] public float MouseSensitivity = 0.0035f;
	[Export] public bool InvertY = false;
	[Export] public float MinPitchDegrees = -85.0f;
	[Export] public float MaxPitchDegrees = 85.0f;
	[Export] public float FarPlane = 210.0f;

	[ExportGroup("Target Follow")]
	[Export] public Node3D Target;
	[Export] public float FollowSpeed = 25.0f;

	[ExportGroup("Walk Wobble (Headbob)")]
	[Export] public bool EnableWalkWobble = true;
	[Export] public float WalkBobFrequency = 8.5f;
	[Export] public float WalkBobHorizontal = 0.035f;   // Horizontal sway displacement in meters
	[Export] public float WalkBobVertical = 0.045f;     // Vertical step dip in meters
	[Export] public float WalkBobRollDegrees = 0.75f;   // Lateral head roll tilt in degrees
	[Export] public float WalkBobPitchDegrees = 0.35f;  // Forward step nod in degrees
	[Export] public float SprintBobMultiplier = 1.35f;  // Multiplier when sprinting
	[Export] public float BobDampingSpeed = 8.0f;       // Smoothing speed for transitions

	[ExportGroup("House Attack Wobble (Trauma & Shake)")]
	[Export] public bool EnableAttackWobble = true;
	[Export] public float TraumaDecayRate = 1.35f;        // How fast trauma dissipates per second
	[Export] public float MaxShakeOffset = 0.12f;         // Max positional displacement in meters (12cm)
	[Export] public float MaxShakeRollDegrees = 2.8f;     // Max roll shake in degrees
	[Export] public float MaxShakePitchDegrees = 2.2f;    // Max pitch shake in degrees
	[Export] public float MaxShakeYawDegrees = 2.0f;      // Max yaw shake in degrees
	[Export] public float AttackContinuousTrauma = 0.32f; // Base shudder floor while house is under attack

	public bool IsDeathAnimationActive { get; private set; } = false;

	private Camera3D _camera;
	private Control _crosshair;
	private HUDManager _hud;
	private BreachManager _breachManager;
	private float _pitch;
	private float _yaw;
	private bool _isCCTVActive;
	private bool _isShopActive;

	private float _walkBobTimer;
	private Vector3 _currentBobPos = Vector3.Zero;
	private Vector3 _currentBobRot = Vector3.Zero;

	private float _trauma;
	private float _shakeTime;

	private float _deathHeightOffset = 0f;
	private float _deathRollOffset = 0f;
	private Tween _deathTween;
	private Tween _deathImpactTween;

	public override void _Ready()
	{
		Instance = this;
		_camera = GetNodeOrNull<Camera3D>("Camera3D");
		if (_camera != null)
		{
			_camera.Position = Vector3.Zero;
			_camera.Rotation = Vector3.Zero;
			_camera.Far = FarPlane;
		}

		Target ??= GetTree().Root.FindChild("Player", true, false) as Node3D;
		if (Target is PlayerController { VisualRoot: { } vis }) vis.Visible = false;

		_hud ??= GetTree().Root.FindChild("HUD", true, false) as HUDManager;
		_breachManager ??= GetTree().Root.FindChild("BreachManager", true, false) as BreachManager;

		_yaw = Rotation.Y;
		_pitch = Rotation.X;
		Input.MouseMode = Input.MouseModeEnum.Captured;
		UpdateCrosshair();
		ApplySavedSettings();
	}

	public void ApplySavedSettings()
	{
		var settings = SaveManager.CurrentData?.Audio;
		if (settings == null) return;

		MouseSensitivity = settings.MouseSensitivity;
		InvertY = settings.InvertY;
		EnableWalkWobble = settings.Headbob;
		EnableAttackWobble = settings.ScreenShake;
		if (_camera != null)
		{
			_camera.Fov = settings.Fov;
			_camera.Far = FarPlane;
		}
	}

	public override void _ExitTree()
	{
		if (Instance == this) Instance = null;
	}

	public void AddTrauma(float amount)
	{
		_trauma = Mathf.Clamp(_trauma + amount, 0.0f, 1.0f);
	}

	public void SetCCTVActive(bool active)
	{
		_isCCTVActive = active;
		SetProcessInput(!_isCCTVActive && !_isShopActive);
		Input.MouseMode = _isCCTVActive ? Input.MouseModeEnum.Visible : (_isShopActive ? Input.MouseModeEnum.Visible : Input.MouseModeEnum.Captured);
		if (_camera != null && active)
		{
			_camera.Position = Vector3.Zero;
			_camera.Rotation = Vector3.Zero;
		}
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
		if (IsDeathAnimationActive || _isCCTVActive || _isShopActive || GetTree().Paused) return;

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
			float pitchDelta = motion.Relative.Y * MouseSensitivity * (InvertY ? -1.0f : 1.0f);
			_pitch = Mathf.Clamp(_pitch - pitchDelta, Mathf.DegToRad(MinPitchDegrees), Mathf.DegToRad(MaxPitchDegrees));
			Rotation = new Vector3(_pitch, _yaw, 0f);
		}
	}

	public override void _Process(double delta)
	{
		float dt = (float)delta;

		if (Target != null)
		{
			Vector3 targetFollow = Target.GlobalPosition + FollowOffset + new Vector3(0, _deathHeightOffset, 0);
			GlobalPosition = GlobalPosition.Lerp(targetFollow, FollowSpeed * dt);
		}

		if (IsDeathAnimationActive)
		{
			Rotation = new Vector3(_pitch, _yaw, _deathRollOffset);
		}

		if (_hud != null && _hud.IsShopOpen != _isShopActive)
		{
			SetShopActive(_hud.IsShopOpen);
		}

		UpdateWobble(dt);
	}

	public void PlayDeathSequence(Vector3 lookAtTarget, float targetRollDegrees, float targetHeightDrop, float snapDuration, Action onLungeImpact, Action onComplete)
	{
		IsDeathAnimationActive = true;
		_deathTween?.Kill();
		_deathImpactTween?.Kill();

		// Calculate target yaw and pitch to face the threat's head
		Vector3 toTarget = (lookAtTarget - GlobalPosition).Normalized();
		float targetYaw = Mathf.Atan2(-toTarget.X, -toTarget.Z);
		float targetPitch = Mathf.Clamp(Mathf.Asin(toTarget.Y), Mathf.DegToRad(MinPitchDegrees), Mathf.DegToRad(MaxPitchDegrees));

		// Wrap shortest angular distance to prevent spinning 360 degrees the wrong way
		float diffYaw = Mathf.Wrap(targetYaw - _yaw, -Mathf.Pi, Mathf.Pi);
		float endYaw = _yaw + diffYaw;

		_deathTween = CreateTween().SetParallel(true);
		_deathTween.TweenMethod(Callable.From<float>(v => _yaw = v), _yaw, endYaw, snapDuration)
			.SetTrans(Tween.TransitionType.Quad).SetEase(Tween.EaseType.Out);
		_deathTween.TweenMethod(Callable.From<float>(v => _pitch = v), _pitch, targetPitch, snapDuration)
			.SetTrans(Tween.TransitionType.Quad).SetEase(Tween.EaseType.Out);

		// Delayed impact and collapse: monster lunges for 1.35s, strikes, and player is tackled to the floor
		float impactDelay = snapDuration + 0.95f;
		float fallDuration = 0.55f;

		_deathImpactTween = CreateTween().SetParallel(false);
		_deathImpactTween.TweenInterval(impactDelay);
		_deathImpactTween.TweenCallback(Callable.From(() =>
		{
			AddTrauma(1.0f);
			onLungeImpact?.Invoke();
		}));

		// Tumble to floor: height drops to 0.25m, roll tilts, and pitch angles UP to +52 deg to stare up at the monster's face!
		var fallTween = _deathImpactTween.Parallel();
		fallTween.TweenMethod(Callable.From<float>(v => _deathHeightOffset = v), 0f, -targetHeightDrop, fallDuration)
			.SetTrans(Tween.TransitionType.Bounce).SetEase(Tween.EaseType.Out);
		fallTween.TweenMethod(Callable.From<float>(v => _deathRollOffset = v), 0f, Mathf.DegToRad(targetRollDegrees), fallDuration)
			.SetTrans(Tween.TransitionType.Back).SetEase(Tween.EaseType.Out);
		fallTween.TweenMethod(Callable.From<float>(v => _pitch = v), targetPitch, Mathf.DegToRad(52.0f), fallDuration)
			.SetTrans(Tween.TransitionType.Back).SetEase(Tween.EaseType.Out);

		// Linger on the floor for 2.2 seconds while the creature looms directly overhead
		_deathImpactTween.TweenInterval(2.2f);
		_deathImpactTween.TweenCallback(Callable.From(() =>
		{
			onComplete?.Invoke();
		}));
	}

	private void UpdateWobble(float dt)
	{
		if (_camera == null || _isCCTVActive) return;

		Vector3 targetBobPos = Vector3.Zero;
		Vector3 targetBobRot = Vector3.Zero;

		// 1. Walk Wobble (Headbob)
		if (EnableWalkWobble && Target is PlayerController player)
		{
			float speed = new Vector2(player.Velocity.X, player.Velocity.Z).Length();
			bool isSprinting = Input.IsKeyPressed(Key.Shift);
			bool isMoving = speed > 0.2f && player.IsOnFloor();

			if (isMoving)
			{
				float speedNorm = Mathf.Clamp(speed / (player.WalkSpeed > 0f ? player.WalkSpeed : 6.0f), 0.2f, 2.0f);
				float sprintMultiplier = isSprinting ? SprintBobMultiplier : 1.0f;
				float freq = WalkBobFrequency * speedNorm * (isSprinting ? 1.15f : 1.0f);

				_walkBobTimer += dt * freq;

				// Horizontal side-to-side sway (1 full sway cycle per 2 steps)
				float swayX = Mathf.Sin(_walkBobTimer * 0.5f) * WalkBobHorizontal * sprintMultiplier;
				// Vertical step dip (dips down on each step)
				float dipY = (Mathf.Cos(_walkBobTimer) - 1.0f) * 0.5f * WalkBobVertical * sprintMultiplier;

				targetBobPos = new Vector3(swayX, dipY, 0f);

				// Subtle camera roll and pitch nod matching footsteps
				float rollRad = Mathf.DegToRad(Mathf.Sin(_walkBobTimer * 0.5f) * WalkBobRollDegrees * sprintMultiplier);
				float pitchRad = Mathf.DegToRad(Mathf.Sin(_walkBobTimer) * WalkBobPitchDegrees * sprintMultiplier);

				targetBobRot = new Vector3(pitchRad, 0f, rollRad);
			}
		}

		_currentBobPos = _currentBobPos.Lerp(targetBobPos, dt * BobDampingSpeed);
		_currentBobRot = _currentBobRot.Lerp(targetBobRot, dt * BobDampingSpeed);

		// 2. House Attack Wobble (Trauma Shake)
		Vector3 shakePos = Vector3.Zero;
		Vector3 shakeRot = Vector3.Zero;

		if (EnableAttackWobble)
		{
			_breachManager ??= GetTree().Root.FindChild("BreachManager", true, false) as BreachManager;
			if (_breachManager != null && _breachManager.IsMonsterAttacking)
			{
				// Sustain continuous base tremor while the house is under assault
				float sustain = AttackContinuousTrauma;
				if (Target is PlayerController p && !p.IsIndoors)
				{
					// Attenuate if player is outside away from house center
					float dist = (p.GlobalPosition - p.HouseCenter).Length();
					sustain *= Mathf.Clamp(1.0f - (dist - 4.0f) / 12.0f, 0.2f, 1.0f);
				}
				_trauma = Mathf.Max(_trauma, sustain);
			}

			if (_trauma > 0.001f)
			{
				_trauma = Mathf.Max(0.0f, _trauma - dt * TraumaDecayRate);
				_shakeTime += dt * 32.0f;
				float shake = _trauma * _trauma; // Non-linear quadratic falloff

				// Multi-harmonic non-repeating organic shake
				float sX = (Mathf.Sin(_shakeTime * 1.0f) * 0.6f + Mathf.Sin(_shakeTime * 2.3f) * 0.4f) * MaxShakeOffset * shake;
				float sY = (Mathf.Cos(_shakeTime * 1.2f) * 0.6f + Mathf.Cos(_shakeTime * 2.7f) * 0.4f) * MaxShakeOffset * shake;
				float sZ = (Mathf.Sin(_shakeTime * 0.9f) * 0.5f + Mathf.Cos(_shakeTime * 1.8f) * 0.5f) * (MaxShakeOffset * 0.5f) * shake;

				float rPitch = Mathf.DegToRad((Mathf.Sin(_shakeTime * 1.4f) * 0.7f + Mathf.Cos(_shakeTime * 2.1f) * 0.3f) * MaxShakePitchDegrees * shake);
				float rYaw = Mathf.DegToRad((Mathf.Cos(_shakeTime * 1.1f) * 0.7f + Mathf.Sin(_shakeTime * 1.9f) * 0.3f) * MaxShakeYawDegrees * shake);
				float rRoll = Mathf.DegToRad((Mathf.Sin(_shakeTime * 1.7f) * 0.8f + Mathf.Cos(_shakeTime * 2.5f) * 0.2f) * MaxShakeRollDegrees * shake);

				shakePos = new Vector3(sX, sY, sZ);
				shakeRot = new Vector3(rPitch, rYaw, rRoll);
			}
			else
			{
				_trauma = 0.0f;
			}
		}

		_camera.Position = _currentBobPos + shakePos;
		_camera.Rotation = _currentBobRot + shakeRot;
	}

	private void UpdateCrosshair()
	{
		_crosshair ??= GetTree().Root.FindChild("Crosshair", true, false) as Control;
		if (_crosshair != null) _crosshair.Visible = !_isCCTVActive && !_isShopActive;
	}
}
