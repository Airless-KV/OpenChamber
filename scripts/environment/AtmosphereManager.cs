// =========================================================================================
// Original Author : air (Airless-KV) - Oct 2026
// Responsibilities: - Dynamic Day/Night lighting and atmospheric transitions
//                   - Sun/Moon directional light rotation, color, and energy lerping
//                   - Cold, desaturated nighttime fog and procedural sky management
//                   - House interior sanctuary lighting contrast
// -----------------------------------------------------------------------------------------
// Change Log:
// - Oct 30, 2026 | air : Implemented dynamic Day/Night lighting and atmosphere system
// - Oct 31, 2026 | air : Streamlined atmosphere profiles into encapsulated struct, eliminated
//                        redundant lerp fields and multi-parameter target resolution
// =========================================================================================
using Godot;
using System;

public partial class AtmosphereManager : Node3D
{
	public struct AtmosphereProfile
	{
		public Color SunColor;
		public float SunEnergy;
		public Vector3 SunRotation;
		public Color SkyTop;
		public Color SkyHorizon;
		public Color AmbientColor;
		public float AmbientEnergy;
		public Color FogColor;
		public float FogDensity;
		public float Saturation;
		public float Contrast;
		public float InteriorEnergy;

		public static AtmosphereProfile Lerp(AtmosphereProfile a, AtmosphereProfile b, float t) => new()
		{
			SunColor = a.SunColor.Lerp(b.SunColor, t),
			SunEnergy = Mathf.Lerp(a.SunEnergy, b.SunEnergy, t),
			SunRotation = new Vector3(
				Mathf.RadToDeg(Mathf.LerpAngle(Mathf.DegToRad(a.SunRotation.X), Mathf.DegToRad(b.SunRotation.X), t)),
				Mathf.RadToDeg(Mathf.LerpAngle(Mathf.DegToRad(a.SunRotation.Y), Mathf.DegToRad(b.SunRotation.Y), t)),
				Mathf.RadToDeg(Mathf.LerpAngle(Mathf.DegToRad(a.SunRotation.Z), Mathf.DegToRad(b.SunRotation.Z), t))
			),
			SkyTop = a.SkyTop.Lerp(b.SkyTop, t),
			SkyHorizon = a.SkyHorizon.Lerp(b.SkyHorizon, t),
			AmbientColor = a.AmbientColor.Lerp(b.AmbientColor, t),
			AmbientEnergy = Mathf.Lerp(a.AmbientEnergy, b.AmbientEnergy, t),
			FogColor = a.FogColor.Lerp(b.FogColor, t),
			FogDensity = Mathf.Lerp(a.FogDensity, b.FogDensity, t),
			Saturation = Mathf.Lerp(a.Saturation, b.Saturation, t),
			Contrast = Mathf.Lerp(a.Contrast, b.Contrast, t),
			InteriorEnergy = Mathf.Lerp(a.InteriorEnergy, b.InteriorEnergy, t)
		};
	}

	public static readonly AtmosphereProfile DayProfile = new()
	{
		SunColor = new(1.0f, 0.94f, 0.82f),
		SunEnergy = 1.30f,
		SunRotation = new(-52.0f, 35.0f, 0.0f),
		SkyTop = new(0.35f, 0.62f, 0.95f),
		SkyHorizon = new(0.72f, 0.82f, 0.92f),
		AmbientColor = new(0.85f, 0.82f, 0.75f),
		AmbientEnergy = 0.35f,
		FogColor = new(0.82f, 0.86f, 0.92f),
		FogDensity = 0.0015f,
		Saturation = 1.05f,
		Contrast = 1.02f,
		InteriorEnergy = 0.25f
	};

	public static readonly AtmosphereProfile DuskProfile = new()
	{
		SunColor = new(1.0f, 0.58f, 0.28f),
		SunEnergy = 0.85f,
		SunRotation = new(-8.0f, 75.0f, 0.0f),
		SkyTop = new(0.18f, 0.20f, 0.42f),
		SkyHorizon = new(0.92f, 0.48f, 0.28f),
		AmbientColor = new(0.55f, 0.42f, 0.42f),
		AmbientEnergy = 0.24f,
		FogColor = new(0.42f, 0.35f, 0.45f),
		FogDensity = 0.015f,
		Saturation = 0.92f,
		Contrast = 1.08f,
		InteriorEnergy = 0.80f
	};

	public static readonly AtmosphereProfile NightProfile = new()
	{
		SunColor = new(0.32f, 0.45f, 0.70f),
		SunEnergy = 0.18f,
		SunRotation = new(-30.0f, -145.0f, 0.0f),
		SkyTop = new(0.015f, 0.020f, 0.040f),
		SkyHorizon = new(0.050f, 0.080f, 0.130f),
		AmbientColor = new(0.07f, 0.10f, 0.17f),
		AmbientEnergy = 0.10f,
		FogColor = new(0.10f, 0.14f, 0.20f),
		FogDensity = 0.045f,
		Saturation = 0.42f,
		Contrast = 1.18f,
		InteriorEnergy = 1.40f
	};

	[ExportGroup("Dependencies")]
	[Export] public GameManager GameManagerNode;
	[Export] public DirectionalLight3D SunLightNode;
	[Export] public WorldEnvironment WorldEnvironmentNode;
	[Export] public Light3D InteriorLightNode;

	[ExportGroup("Transition Settings")]
	[Export] public float TransitionSpeed = 2.5f;
	[Export] public bool EnableDynamicSunMovement = true;

	private Godot.Environment _environment;
	private ProceduralSkyMaterial _skyMaterial;
	private AtmosphereProfile _current;
	private bool _isNight = false;
	private float _dayProgress = 0.0f;
	private float _initialPhaseDuration = 300.0f;

	public override void _Ready()
	{
		GameManagerNode ??= GetTree().Root.FindChild("GameManager", true, false) as GameManager;
		SunLightNode ??= GetTree().Root.FindChild("DirectionalLight3D", true, false) as DirectionalLight3D;
		WorldEnvironmentNode ??= GetTree().Root.FindChild("WorldEnvironment", true, false) as WorldEnvironment;
		InteriorLightNode ??= GetTree().Root.FindChild("CeilingLight", true, false) as Light3D;

		SetupEnvironment();
		SetupLight();

		if (GameManagerNode != null)
		{
			GameManagerNode.PhaseChanged += OnPhaseChanged;
			GameManagerNode.TimeUpdated += OnTimeUpdated;

			_isNight = GameManagerNode.CurrentPhase == GameManager.GamePhase.Night;
			_initialPhaseDuration = Mathf.Max(1.0f, GameManagerNode.TimeRemaining);
		}

		_current = _isNight ? NightProfile : DayProfile;
		ApplyAtmosphereImmediate();
	}

	private void SetupEnvironment()
	{
		if (WorldEnvironmentNode == null) return;

		WorldEnvironmentNode.Environment = (Godot.Environment)(WorldEnvironmentNode.Environment?.Duplicate() ?? new Godot.Environment());
		_environment = WorldEnvironmentNode.Environment;
		_environment.BackgroundMode = Godot.Environment.BGMode.Sky;

		if (_environment.Sky == null) _environment.Sky = new Sky();
		_skyMaterial = _environment.Sky.SkyMaterial is ProceduralSkyMaterial psm ? (ProceduralSkyMaterial)psm.Duplicate() : new ProceduralSkyMaterial();
		_environment.Sky.SkyMaterial = _skyMaterial;

		_environment.AmbientLightSource = Godot.Environment.AmbientSource.Color;
		_environment.FogEnabled = true;
		_environment.FogAerialPerspective = 0.6f;
		_environment.FogSkyAffect = 0.85f;
		_environment.AdjustmentEnabled = true;
		_environment.AdjustmentBrightness = 0.98f;
		_environment.TonemapMode = Godot.Environment.ToneMapper.Filmic;
	}

	private void SetupLight()
	{
		if (SunLightNode != null) SunLightNode.ShadowEnabled = true;
		if (InteriorLightNode != null) InteriorLightNode.LightColor = new Color(1.0f, 0.88f, 0.70f);
	}

	public override void _Process(double delta)
	{
		AtmosphereProfile target = GetTargetProfile();
		float lerpFactor = Mathf.Clamp((float)delta * TransitionSpeed, 0.0f, 1.0f);
		_current = AtmosphereProfile.Lerp(_current, target, lerpFactor);
		ApplyAtmosphereImmediate();
	}

	private AtmosphereProfile GetTargetProfile()
	{
		if (_isNight) return NightProfile;

		float t = EnableDynamicSunMovement ? Mathf.Clamp((_dayProgress - 0.55f) / 0.45f, 0.0f, 1.0f) : 0.0f;
		return AtmosphereProfile.Lerp(DayProfile, DuskProfile, Mathf.SmoothStep(0.0f, 1.0f, t));
	}

	private void ApplyAtmosphereImmediate()
	{
		if (SunLightNode != null)
		{
			SunLightNode.LightColor = _current.SunColor;
			SunLightNode.LightEnergy = _current.SunEnergy;
			SunLightNode.RotationDegrees = _current.SunRotation;
		}

		if (_skyMaterial != null)
		{
			_skyMaterial.SkyTopColor = _current.SkyTop;
			_skyMaterial.SkyHorizonColor = _current.SkyHorizon;
			_skyMaterial.GroundBottomColor = _current.SkyTop.Darkened(0.6f);
			_skyMaterial.GroundHorizonColor = _current.SkyHorizon.Darkened(0.2f);
		}

		if (_environment != null)
		{
			_environment.AmbientLightColor = _current.AmbientColor;
			_environment.AmbientLightEnergy = _current.AmbientEnergy;
			_environment.FogLightColor = _current.FogColor;
			_environment.FogDensity = _current.FogDensity;
			_environment.AdjustmentSaturation = _current.Saturation;
			_environment.AdjustmentContrast = _current.Contrast;
		}

		if (InteriorLightNode != null)
		{
			InteriorLightNode.LightEnergy = _current.InteriorEnergy;
		}
	}

	private void OnPhaseChanged(bool isDay, int currentDay)
	{
		_isNight = !isDay;
		if (GameManagerNode != null)
		{
			_initialPhaseDuration = Mathf.Max(1.0f, GameManagerNode.TimeRemaining);
		}
		_dayProgress = 0.0f;
	}

	private void OnTimeUpdated(float timeRemaining)
	{
		if (!_isNight && _initialPhaseDuration > 0.0f)
		{
			_dayProgress = Mathf.Clamp(1.0f - (timeRemaining / _initialPhaseDuration), 0.0f, 1.0f);
		}
	}

	public override void _UnhandledInput(InputEvent @event)
	{
		if (OS.IsDebugBuild() && @event is InputEventKey { Pressed: true, Echo: false, Keycode: Key.F6 })
		{
			_isNight = !_isNight;
			_dayProgress = 0.0f;
			GD.Print($"[AtmosphereManager] Toggled Atmosphere Preview: {(_isNight ? "NIGHT (Cold Fog)" : "DAY (Warm Sunlight)")}");
		}
	}
}
