// =========================================================================================
// Original Author : air (Airless-KV) - Nov 2026
// Responsibilities: - Autonomous airborne crow flight controller circling forest edges & perimeter
//                   - Side-profile flight silhouette with velocity-aligned directional heading
//                   - Dynamic eye-mesh and light tracking synchronized with horizontal sprite flip
//                   - Sinusoidal altitude undulation at Ani-Ani eye height (~6.2m)
//                   - Procedural flapping bursts, wing banking, and rhythmic flight dynamics
//                   - Atmospheric glowing red eye illumination matching Ani-Ani's emission
//                   - Persistent flock presence across both morning/daytime and nighttime phases
//                   - CCTV night stealth mechanic matching cows/sheep (body hidden in direct view, revealed on CCTV)
// -----------------------------------------------------------------------------------------
// Change Log:
// - Nov 15, 2026 | air : Created SpookyCrow entity with Ani-Ani matching eye glow and twitch behaviors
// - Nov 15, 2026 | air : Converted to fully airborne flying entity with wide orbit paths and flapping dynamics
// - Nov 15, 2026 | air : Redesigned to side-profile flight silhouette with velocity-aligned FlipH
// - Nov 15, 2026 | air : Integrated CCTV stealth visibility for crows (silhouette revealed on CCTV, glowing eye in fog)
// - Nov 15, 2026 | air : Added NightEyes emissive mesh and synced stealth visibility with AmbientCreature standards
// - Nov 15, 2026 | air : Configured twin glowing NightEyes and CCTV-conditional night flight freeze (halt in direct view, move on CCTV)
// - Nov 15, 2026 | air : Added HasGlowingEyes flag to disable glowing eye meshes on inner/mid crows while preserving distant forest eyes
// - Nov 15, 2026 | air : Swapped daytime crow sprite texture to natural dark eye version so eyes never glow during day
// =========================================================================================
using Godot;
using System;

public partial class SpookyCrow : Node3D
{
	[ExportGroup("Visual Components")]
	[Export] public Sprite3D BillboardSprite;
	[Export] public OmniLight3D EyeLight;
	[Export] public Node3D NightEyes;
	[Export] public Node3D VisualRoot;
	[Export] public Texture2D DayTexture;
	[Export] public Texture2D NightTexture;

	[ExportGroup("Flight Trajectory")]
	[Export] public Vector3 AnchorPosition = Vector3.Zero;
	[Export] public float OrbitRadiusX = 14.0f;
	[Export] public float OrbitRadiusZ = 14.0f;
	[Export] public float FlightSpeed = 4.5f;
	[Export] public float BaseHeight = 6.20f; // Exactly Ani-Ani eye height
	[Export] public float AltitudeAmplitude = 0.35f;
	[Export] public bool OrbitClockwise = true;
	[Export] public float InitialAngleDegrees = 0.0f;

	[ExportGroup("Flight Dynamics")]
	[Export] public bool HasGlowingEyes { get; set; } = true;
	[Export] public float FlapFrequency = 6.5f;
	[Export] public float BankAngle = 8.0f;
	[Export] public float BaseEyeEnergy = 0.85f;
	[Export] public float MinBlinkInterval = 4.0f;
	[Export] public float MaxBlinkInterval = 8.5f;

	private readonly Random _rng = new();
	private double _animTime;
	private float _orbitAngle;
	private float _blinkIntervalTimer;
	private float _blinkDurationTimer;
	private bool _isBlinking;
	private float _glideTimer;
	private bool _isGliding;
	private bool _isNight = false;
	private bool _isCCTV = false;
	private bool _nightCamConnected = false;

	private const float EyeBaseX = 0.43f;
	private const float EyeBaseY = 0.15f;

	public override void _Ready()
	{
		BillboardSprite ??= GetNodeOrNull<Sprite3D>("VisualRoot/Sprite3D") ?? FindChild("Sprite3D", true, false) as Sprite3D;
		EyeLight ??= GetNodeOrNull<OmniLight3D>("VisualRoot/EyeLight") ?? FindChild("EyeLight", true, false) as OmniLight3D;
		NightEyes ??= GetNodeOrNull<Node3D>("VisualRoot/NightEyes") ?? FindChild("NightEyes", true, false) as Node3D;
		VisualRoot ??= GetNodeOrNull<Node3D>("VisualRoot") ?? this;

		DayTexture ??= GD.Load<Texture2D>("res://assets/textures/environment/creature_spooky_crow_day.png");
		NightTexture ??= GD.Load<Texture2D>("res://assets/textures/environment/creature_spooky_crow.png");

		if (AnchorPosition == Vector3.Zero)
		{
			AnchorPosition = IsInsideTree() ? GlobalPosition : Position;
		}

		if (BillboardSprite != null)
		{
			BillboardSprite.Layers = 4; // Visual Layer 3 (exempt from local point light self-illumination)
			BillboardSprite.FlipH = false;
			UpdateEyeFlip(false);
		}

		if (EyeLight != null)
		{
			EyeLight.LightEnergy = BaseEyeEnergy;
			EyeLight.LightCullMask = 1; // Illuminates trees/world
		}

		// Stagger angles and timers so flock individuals don't sync
		_orbitAngle = Mathf.DegToRad(InitialAngleDegrees + (float)(_rng.NextDouble() * 30.0 - 15.0));
		_animTime = _rng.NextDouble() * 20.0;
		_blinkIntervalTimer = (float)(MinBlinkInterval + _rng.NextDouble() * (MaxBlinkInterval - MinBlinkInterval));
		_glideTimer = (float)(2.0 + _rng.NextDouble() * 3.0);

		var gm = GetTree().Root.FindChild("GameManager", true, false) as GameManager;
		if (gm != null)
		{
			gm.PhaseChanged += (isDay, _) =>
			{
				_isNight = !isDay;
				UpdateVisibility();
			};
			_isNight = gm.CurrentPhase == GameManager.GamePhase.Night;
		}

		if (NighttCamera.Instance != null)
		{
			NighttCamera.Instance.CCTVModeChanged += isCCTV =>
			{
				_isCCTV = isCCTV;
				UpdateVisibility();
			};
			_isCCTV = NighttCamera.Instance.IsNightMode;
			_nightCamConnected = true;
		}

		UpdateVisibility();

		// Position immediately at starting orbit coordinate
		UpdateFlightPosition(0.01f);
	}

	public void UpdateVisibility()
	{
		if (!_isNight)
		{
			// Daytime: normal peaceful crow flight, natural dark eye texture, night eyes & eye lights disabled
			if (BillboardSprite != null)
			{
				BillboardSprite.Visible = true;
				if (DayTexture != null && BillboardSprite.Texture != DayTexture)
				{
					BillboardSprite.Texture = DayTexture;
				}
			}
			if (NightEyes != null) NightEyes.Visible = false;
			if (EyeLight != null) EyeLight.Visible = false;
			return;
		}

		// Nighttime:
		// Body is ONLY visible on CCTV!
		// In first-person direct view (looking through doors/windows/porch), body is OFF so only glowing red eye appears in fog!
		bool showBody = _isCCTV;
		if (BillboardSprite != null)
		{
			BillboardSprite.Visible = showBody;
			if (NightTexture != null && BillboardSprite.Texture != NightTexture)
			{
				BillboardSprite.Texture = NightTexture;
			}
		}
		if (NightEyes != null) NightEyes.Visible = HasGlowingEyes;
		if (EyeLight != null) EyeLight.Visible = HasGlowingEyes && _isCCTV;
	}

	public override void _Process(double delta)
	{
		float dt = (float)delta;
		_animTime += delta;

		if (!_nightCamConnected && NighttCamera.Instance != null)
		{
			NighttCamera.Instance.CCTVModeChanged += isCCTV =>
			{
				_isCCTV = isCCTV;
				UpdateVisibility();
			};
			_isCCTV = NighttCamera.Instance.IsNightMode;
			_nightCamConnected = true;
			UpdateVisibility();
		}

		// At night, crows stop flying when player is in direct view and only move when viewing CCTV
		bool shouldFly = !_isNight || _isCCTV;

		if (shouldFly)
		{
			// 1. Orbital flight positioning along perimeter
			UpdateFlightPosition(dt);

			// 2. Wing flap vs glide state cycle
			_glideTimer -= dt;
			if (_glideTimer <= 0f)
			{
				_isGliding = !_isGliding;
				_glideTimer = _isGliding ? (float)(1.5 + _rng.NextDouble() * 2.5) : (float)(2.5 + _rng.NextDouble() * 4.0);
			}

			if (VisualRoot != null && VisualRoot != this)
			{
				float flapCycle = _isGliding ? 0f : Mathf.Sin((float)_animTime * FlapFrequency);
				// Procedural wing flap squash & stretch on Y scale
				float flapScaleY = 1.0f + flapCycle * 0.16f;
				VisualRoot.Scale = new Vector3(1.0f, flapScaleY, 1.0f);

				// Dynamic banking tilt into the turn
				float targetRoll = (OrbitClockwise ? -1f : 1f) * BankAngle;
				VisualRoot.RotationDegrees = new Vector3(0, 0, targetRoll);
			}
		}
		else
		{
			// Frozen watching posture: wings level and stationary in mid-air
			if (VisualRoot != null && VisualRoot != this)
			{
				VisualRoot.Scale = Vector3.One;
				VisualRoot.RotationDegrees = Vector3.Zero;
			}
		}

		// Rotate NightEyes to face the player/active camera so the glowing eye is always clearly visible
		if (NightEyes != null && NightEyes.Visible)
		{
			Camera3D cam = GetViewport()?.GetCamera3D();
			if (cam != null)
			{
				Vector3 toCam = cam.GlobalPosition - NightEyes.GlobalPosition;
				toCam.Y = 0;
				if (toCam.LengthSquared() > 0.01f)
				{
					NightEyes.LookAt(NightEyes.GlobalPosition - toCam, Vector3.Up);
				}
			}
		}

		// 3. Eye blink mechanics (only for crows with glowing eyes)
		if (HasGlowingEyes)
		{
			if (_isBlinking)
			{
				_blinkDurationTimer -= dt;
				if (_blinkDurationTimer <= 0f)
				{
					_isBlinking = false;
					if (NightEyes != null && _isNight) NightEyes.Visible = true;
				}
			}
			else
			{
				_blinkIntervalTimer -= dt;
				if (_blinkIntervalTimer <= 0f)
				{
					_isBlinking = true;
					_blinkDurationTimer = 0.08f;
					_blinkIntervalTimer = (float)(MinBlinkInterval + _rng.NextDouble() * (MaxBlinkInterval - MinBlinkInterval));
					if (NightEyes != null && _isNight) NightEyes.Visible = false;
				}
			}
		}

		// 4. Sinusoidal eye glow breathing pulse matching Ani-Ani
		if (HasGlowingEyes && EyeLight != null && EyeLight.Visible)
		{
			if (_isBlinking)
			{
				EyeLight.LightEnergy = 0.02f;
			}
			else
			{
				float pulse = Mathf.Sin((float)_animTime * 2.5f) * 0.15f;
				EyeLight.LightEnergy = BaseEyeEnergy + pulse;
			}
		}
	}

	private void UpdateFlightPosition(float dt)
	{
		float angularSpeed = (FlightSpeed / Mathf.Max(3.0f, (OrbitRadiusX + OrbitRadiusZ) * 0.5f));
		_orbitAngle += (OrbitClockwise ? 1f : -1f) * angularSpeed * dt;

		float x = AnchorPosition.X + Mathf.Cos(_orbitAngle) * OrbitRadiusX;
		float z = AnchorPosition.Z + Mathf.Sin(_orbitAngle) * OrbitRadiusZ;

		// Directional velocity tangent: determines whether crow flies toward +X or -X
		float vx = -Mathf.Sin(_orbitAngle) * OrbitRadiusX * (OrbitClockwise ? 1f : -1f);
		if (BillboardSprite != null)
		{
			bool shouldFaceRight = vx > 0.05f;
			bool shouldFaceLeft = vx < -0.05f;
			if (shouldFaceRight && !BillboardSprite.FlipH)
			{
				BillboardSprite.FlipH = true;
				UpdateEyeFlip(true);
			}
			else if (shouldFaceLeft && BillboardSprite.FlipH)
			{
				BillboardSprite.FlipH = false;
				UpdateEyeFlip(false);
			}
		}

		// Gentle vertical undulation hovering around Ani-Ani eye line
		float altWave = Mathf.Sin(_orbitAngle * 2.0f + (float)_animTime * 0.8f) * AltitudeAmplitude;
		float y = BaseHeight + altWave;

		if (IsInsideTree())
		{
			GlobalPosition = new Vector3(x, y, z);
		}
		else
		{
			Position = new Vector3(x, y, z);
		}
	}

	private void UpdateEyeFlip(bool flipH)
	{
		float eyeX = flipH ? EyeBaseX : -EyeBaseX;
		if (EyeLight != null)
		{
			EyeLight.Position = new Vector3(eyeX, EyeBaseY, EyeLight.Position.Z);
		}
		if (NightEyes != null)
		{
			NightEyes.Position = new Vector3(eyeX, EyeBaseY, NightEyes.Position.Z);
		}
	}
}
