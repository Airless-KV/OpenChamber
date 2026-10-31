// =========================================================================================
// Original Author : air (Airless-KV) - Oct 2026
// Responsibilities: - Dynamic Day/Night lighting and atmospheric transitions
//                   - Sun/Moon directional light rotation, color, and energy lerping
//                   - Cold, desaturated nighttime fog and procedural sky management
//                   - House interior sanctuary lighting contrast
// -----------------------------------------------------------------------------------------
// Change Log:
// - Oct 30, 2026 | air : Implemented dynamic Day/Night lighting and atmosphere system
// =========================================================================================
using Godot;
using System;

public partial class AtmosphereManager : Node3D
{
	[ExportGroup("Dependencies")]
	[Export] public GameManager GameManagerNode;
	[Export] public DirectionalLight3D SunLightNode;
	[Export] public WorldEnvironment WorldEnvironmentNode;
	[Export] public Light3D InteriorLightNode;

	[ExportGroup("Transition Settings")]
	[Export] public float TransitionSpeed = 2.5f;
	[Export] public bool EnableDynamicSunMovement = true;

	[ExportGroup("Daytime Sunlight Settings")]
	[Export] public Color DaySunColor = new(1.0f, 0.94f, 0.82f);
	[Export] public float DaySunEnergy = 1.30f;
	[Export] public Vector3 DaySunRotationDegrees = new(-52.0f, 35.0f, 0.0f);
	[Export] public Color DaySkyTopColor = new(0.35f, 0.62f, 0.95f);
	[Export] public Color DaySkyHorizonColor = new(0.72f, 0.82f, 0.92f);
	[Export] public Color DayAmbientColor = new(0.85f, 0.82f, 0.75f);
	[Export] public float DayAmbientEnergy = 0.35f;
	[Export] public Color DayFogColor = new(0.82f, 0.86f, 0.92f);
	[Export] public float DayFogDensity = 0.0015f;
	[Export] public float DaySaturation = 1.05f;
	[Export] public float DayContrast = 1.02f;
	[Export] public float DayInteriorLightEnergy = 0.25f;

	[ExportGroup("Dusk Sunset Settings")]
	[Export] public Color DuskSunColor = new(1.0f, 0.58f, 0.28f);
	[Export] public float DuskSunEnergy = 0.85f;
	[Export] public Vector3 DuskSunRotationDegrees = new(-8.0f, 75.0f, 0.0f);
	[Export] public Color DuskSkyTopColor = new(0.18f, 0.20f, 0.42f);
	[Export] public Color DuskSkyHorizonColor = new(0.92f, 0.48f, 0.28f);
	[Export] public Color DuskAmbientColor = new(0.55f, 0.42f, 0.42f);
	[Export] public float DuskAmbientEnergy = 0.24f;
	[Export] public Color DuskFogColor = new(0.42f, 0.35f, 0.45f);
	[Export] public float DuskFogDensity = 0.015f;
	[Export] public float DuskSaturation = 0.92f;
	[Export] public float DuskContrast = 1.08f;
	[Export] public float DuskInteriorLightEnergy = 0.80f;

	[ExportGroup("Nighttime Horror Settings")]
	[Export] public Color NightMoonColor = new(0.32f, 0.45f, 0.70f);
	[Export] public float NightMoonEnergy = 0.18f;
	[Export] public Vector3 NightMoonRotationDegrees = new(-30.0f, -145.0f, 0.0f);
	[Export] public Color NightSkyTopColor = new(0.015f, 0.020f, 0.040f);
	[Export] public Color NightSkyHorizonColor = new(0.050f, 0.080f, 0.130f);
	[Export] public Color NightAmbientColor = new(0.07f, 0.10f, 0.17f);
	[Export] public float NightAmbientEnergy = 0.10f;
	[Export] public Color NightFogColor = new(0.10f, 0.14f, 0.20f);
	[Export] public float NightFogDensity = 0.045f;
	[Export] public float NightSaturation = 0.42f;
	[Export] public float NightContrast = 1.18f;
	[Export] public float NightInteriorLightEnergy = 1.40f;

	private Godot.Environment _environment;
	private ProceduralSkyMaterial _skyMaterial;
	private bool _isNight = false;
	private float _dayProgress = 0.0f;
	private float _initialPhaseDuration = 300.0f;

	// Current interpolated values
	private Color _currentSunColor;
	private float _currentSunEnergy;
	private Vector3 _currentSunRotationDeg;
	private Color _currentSkyTop;
	private Color _currentSkyHorizon;
	private Color _currentAmbientColor;
	private float _currentAmbientEnergy;
	private Color _currentFogColor;
	private float _currentFogDensity;
	private float _currentSaturation;
	private float _currentContrast;
	private float _currentInteriorEnergy;

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

		// Initialize current values to daytime or nighttime directly
		InitializeCurrentValues(_isNight ? 1.0f : 0.0f);
		ApplyAtmosphereImmediate();
	}

	private void SetupEnvironment()
	{
		if (WorldEnvironmentNode == null)
			return;

		if (WorldEnvironmentNode.Environment == null)
			WorldEnvironmentNode.Environment = new Godot.Environment();

		// Duplicate environment instance to avoid modifying shared scene resources
		_environment = (Godot.Environment)WorldEnvironmentNode.Environment.Duplicate();
		WorldEnvironmentNode.Environment = _environment;

		// Ensure background mode is Sky
		_environment.BackgroundMode = Godot.Environment.BGMode.Sky;

		if (_environment.Sky == null)
			_environment.Sky = new Sky();

		if (_environment.Sky.SkyMaterial is ProceduralSkyMaterial existingPsm)
		{
			_skyMaterial = (ProceduralSkyMaterial)existingPsm.Duplicate();
		}
		else
		{
			_skyMaterial = new ProceduralSkyMaterial();
		}
		_environment.Sky.SkyMaterial = _skyMaterial;

		// Ambient lighting configuration
		_environment.AmbientLightSource = Godot.Environment.AmbientSource.Color;

		// Fog configuration
		_environment.FogEnabled = true;
		_environment.FogAerialPerspective = 0.6f;
		_environment.FogSkyAffect = 0.85f;

		// Post-processing adjustment configuration
		_environment.AdjustmentEnabled = true;
		_environment.AdjustmentBrightness = 0.98f;
		_environment.TonemapMode = Godot.Environment.ToneMapper.Filmic;
	}

	private void SetupLight()
	{
		if (SunLightNode != null)
		{
			SunLightNode.ShadowEnabled = true;
		}

		if (InteriorLightNode != null)
		{
			InteriorLightNode.LightColor = new Color(1.0f, 0.88f, 0.70f);
		}
	}

	private void InitializeCurrentValues(float nightWeight)
	{
		if (nightWeight > 0.5f)
		{
			_currentSunColor = NightMoonColor;
			_currentSunEnergy = NightMoonEnergy;
			_currentSunRotationDeg = NightMoonRotationDegrees;
			_currentSkyTop = NightSkyTopColor;
			_currentSkyHorizon = NightSkyHorizonColor;
			_currentAmbientColor = NightAmbientColor;
			_currentAmbientEnergy = NightAmbientEnergy;
			_currentFogColor = NightFogColor;
			_currentFogDensity = NightFogDensity;
			_currentSaturation = NightSaturation;
			_currentContrast = NightContrast;
			_currentInteriorEnergy = NightInteriorLightEnergy;
		}
		else
		{
			_currentSunColor = DaySunColor;
			_currentSunEnergy = DaySunEnergy;
			_currentSunRotationDeg = DaySunRotationDegrees;
			_currentSkyTop = DaySkyTopColor;
			_currentSkyHorizon = DaySkyHorizonColor;
			_currentAmbientColor = DayAmbientColor;
			_currentAmbientEnergy = DayAmbientEnergy;
			_currentFogColor = DayFogColor;
			_currentFogDensity = DayFogDensity;
			_currentSaturation = DaySaturation;
			_currentContrast = DayContrast;
			_currentInteriorEnergy = DayInteriorLightEnergy;
		}
	}

	public override void _Process(double delta)
	{
		float dt = (float)delta;
		UpdateAtmosphereTargets(out var targetSunColor, out var targetSunEnergy, out var targetSunRot,
			out var targetSkyTop, out var targetSkyHorizon, out var targetAmbientColor, out var targetAmbientEnergy,
			out var targetFogColor, out var targetFogDensity, out var targetSaturation, out var targetContrast,
			out var targetInteriorEnergy);

		float lerpFactor = Mathf.Clamp(dt * TransitionSpeed, 0.0f, 1.0f);

		_currentSunColor = _currentSunColor.Lerp(targetSunColor, lerpFactor);
		_currentSunEnergy = Mathf.Lerp(_currentSunEnergy, targetSunEnergy, lerpFactor);
		_currentSunRotationDeg = LerpDegreesVector(_currentSunRotationDeg, targetSunRot, lerpFactor);
		_currentSkyTop = _currentSkyTop.Lerp(targetSkyTop, lerpFactor);
		_currentSkyHorizon = _currentSkyHorizon.Lerp(targetSkyHorizon, lerpFactor);
		_currentAmbientColor = _currentAmbientColor.Lerp(targetAmbientColor, lerpFactor);
		_currentAmbientEnergy = Mathf.Lerp(_currentAmbientEnergy, targetAmbientEnergy, lerpFactor);
		_currentFogColor = _currentFogColor.Lerp(targetFogColor, lerpFactor);
		_currentFogDensity = Mathf.Lerp(_currentFogDensity, targetFogDensity, lerpFactor);
		_currentSaturation = Mathf.Lerp(_currentSaturation, targetSaturation, lerpFactor);
		_currentContrast = Mathf.Lerp(_currentContrast, targetContrast, lerpFactor);
		_currentInteriorEnergy = Mathf.Lerp(_currentInteriorEnergy, targetInteriorEnergy, lerpFactor);

		ApplyAtmosphereImmediate();
	}

	private void UpdateAtmosphereTargets(
		out Color sunColor, out float sunEnergy, out Vector3 sunRot,
		out Color skyTop, out Color skyHorizon, out Color ambientColor, out float ambientEnergy,
		out Color fogColor, out float fogDensity, out float saturation, out float contrast,
		out float interiorEnergy)
	{
		if (_isNight)
		{
			sunColor = NightMoonColor;
			sunEnergy = NightMoonEnergy;
			sunRot = NightMoonRotationDegrees;
			skyTop = NightSkyTopColor;
			skyHorizon = NightSkyHorizonColor;
			ambientColor = NightAmbientColor;
			ambientEnergy = NightAmbientEnergy;
			fogColor = NightFogColor;
			fogDensity = NightFogDensity;
			saturation = NightSaturation;
			contrast = NightContrast;
			interiorEnergy = NightInteriorLightEnergy;
			return;
		}

		// Daytime progression: shifts from noon warmth into dusk sunset as countdown runs down
		float t = EnableDynamicSunMovement ? Mathf.Clamp((_dayProgress - 0.55f) / 0.45f, 0.0f, 1.0f) : 0.0f;
		float smoothT = Mathf.SmoothStep(0.0f, 1.0f, t);

		sunColor = DaySunColor.Lerp(DuskSunColor, smoothT);
		sunEnergy = Mathf.Lerp(DaySunEnergy, DuskSunEnergy, smoothT);
		sunRot = DaySunRotationDegrees.Lerp(DuskSunRotationDegrees, smoothT);
		skyTop = DaySkyTopColor.Lerp(DuskSkyTopColor, smoothT);
		skyHorizon = DaySkyHorizonColor.Lerp(DuskSkyHorizonColor, smoothT);
		ambientColor = DayAmbientColor.Lerp(DuskAmbientColor, smoothT);
		ambientEnergy = Mathf.Lerp(DayAmbientEnergy, DuskAmbientEnergy, smoothT);
		fogColor = DayFogColor.Lerp(DuskFogColor, smoothT);
		fogDensity = Mathf.Lerp(DayFogDensity, DuskFogDensity, smoothT);
		saturation = Mathf.Lerp(DaySaturation, DuskSaturation, smoothT);
		contrast = Mathf.Lerp(DayContrast, DuskContrast, smoothT);
		interiorEnergy = Mathf.Lerp(DayInteriorLightEnergy, DuskInteriorLightEnergy, smoothT);
	}

	private void ApplyAtmosphereImmediate()
	{
		if (SunLightNode != null)
		{
			SunLightNode.LightColor = _currentSunColor;
			SunLightNode.LightEnergy = _currentSunEnergy;
			SunLightNode.RotationDegrees = _currentSunRotationDeg;
		}

		if (_skyMaterial != null)
		{
			_skyMaterial.SkyTopColor = _currentSkyTop;
			_skyMaterial.SkyHorizonColor = _currentSkyHorizon;
			_skyMaterial.GroundBottomColor = _currentSkyTop.Darkened(0.6f);
			_skyMaterial.GroundHorizonColor = _currentSkyHorizon.Darkened(0.2f);
		}

		if (_environment != null)
		{
			_environment.AmbientLightColor = _currentAmbientColor;
			_environment.AmbientLightEnergy = _currentAmbientEnergy;
			_environment.FogLightColor = _currentFogColor;
			_environment.FogDensity = _currentFogDensity;
			_environment.AdjustmentSaturation = _currentSaturation;
			_environment.AdjustmentContrast = _currentContrast;
		}

		if (InteriorLightNode != null)
		{
			InteriorLightNode.LightEnergy = _currentInteriorEnergy;
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

	private static Vector3 LerpDegreesVector(Vector3 from, Vector3 to, float weight) => new(
		Mathf.RadToDeg(Mathf.LerpAngle(Mathf.DegToRad(from.X), Mathf.DegToRad(to.X), weight)),
		Mathf.RadToDeg(Mathf.LerpAngle(Mathf.DegToRad(from.Y), Mathf.DegToRad(to.Y), weight)),
		Mathf.RadToDeg(Mathf.LerpAngle(Mathf.DegToRad(from.Z), Mathf.DegToRad(to.Z), weight))
	);

	public override void _UnhandledInput(InputEvent @event)
	{
		// Developer hotkey to toggle between day and night atmosphere preview in debug mode
		if (OS.IsDebugBuild() && @event is InputEventKey { Pressed: true, Echo: false, Keycode: Key.F6 })
		{
			_isNight = !_isNight;
			_dayProgress = _isNight ? 0.0f : 0.0f;
			GD.Print($"[AtmosphereManager] Toggled Atmosphere Preview: {(_isNight ? "NIGHT (Cold Fog)" : "DAY (Warm Sunlight)")}");
		}
	}
}
