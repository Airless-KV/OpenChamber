// =========================================================================================
// Original Author : air (Airless-KV) - Oct 2026
// Responsibilities: - 3D Ani-Ani horror entity model and silhouette generation
//                   - Directional stalking positioning across North, East, South, West feeds
//                   - Passive lurking state positioning across perimeter woods and ridges
//                   - Stalking stage state transitions (Distant, Approaching, Imminent)
//                   - Sinister eye glow emission, jitter twitches, and repel retreat animation
// -----------------------------------------------------------------------------------------
// Change Log:
// - Oct 30, 2026 | air : Implemented 3D Ani-Ani stalking entity with directional CCTV stages
// - Oct 30, 2026 | air : Added scene-tree node binding support for standalone ani_ani_monster.tscn
// - Oct 31, 2026 | air : Streamlined procedural geometry generation and symmetry instantiation
// - Oct 31, 2026 | air : Migrated Ani-Ani to pure scene asset (res://scenes/security/ani_ani_monster.tscn)
// - Nov 06, 2026 | air : Added peripheral lurking vantage states and passive non-attack presence
// - Nov 06, 2026 | air : Ensured persistent CCTV lurking visibility during night downtime and repel retreats
// - Nov 06, 2026 | air : Added callback-driven 12m woods retreat sprint and tween protection
// - Nov 10, 2026 | air : Deactivate on ready and suppress daytime lurking to prevent daytime visibility
// - Nov 11, 2026 | air : Scaled imminent breach stage coordinates for elevated treehouse windows
// =========================================================================================
using Godot;
using System;

public partial class AniAniMonster : Node3D
{
	[Export] public PackedScene ModelPrefab;
	[Export] public bool AutoHideOnReady = true;

	public int CurrentStage { get; private set; }
	public int CurrentDirection { get; private set; }
	public bool IsLurking { get; private set; }
	public bool IsTargeting { get; private set; }
	public int CurrentLurkingSpot { get; private set; } = -1;

	private Node3D _visualRoot;
	private Node3D _headNode;
	private OmniLight3D _eyeLight;
	private double _animTime;
	private readonly Random _rng = new();
	private float _nextTwitchTime;

	private readonly Vector3[,] _stagePositions =
	{
		{ new(-1.66f, -100f, 0), new(-1.66f, 0, -28f), new(-1.66f, 0, -14f), new(-1.66f, 0, -4.2f) },
		{ new(-1.66f, -100f, 0), new(26f, 0, 0), new(13f, 0, 0), new(2.2f, 0, 0) },
		{ new(-1.66f, -100f, 0), new(-1.66f, 0, 28f), new(-1.66f, 0, 14f), new(-1.66f, 0, 4.2f) },
		{ new(-1.66f, -100f, 0), new(-28f, 0, 0), new(-14f, 0, 0), new(-5.4f, 0, 0) }
	};

	private readonly float[] _directionYaws = { 180f, -90f, 0f, 90f };

	// Peripheral lurking vantage points in the woods and ridges outside cardinal entrances
	private readonly (Vector3 pos, float yaw)[] _lurkingVantages =
	{
		(new Vector3(18f, 0, -20f), -135f),   // North-East Tree Line (CAM 1 & 2)
		(new Vector3(18f, 0, 20f), -45f),     // South-East Ridge (CAM 2 & 3)
		(new Vector3(-20f, 0, 20f), 45f),     // South-West Hill (CAM 3 & 4)
		(new Vector3(-20f, 0, -20f), 135f),   // North-West Thicket (CAM 4 & 1)
		(new Vector3(-1.66f, 0, -32f), 180f), // Deep North Woods (CAM 1 background)
		(new Vector3(-1.66f, 0, 32f), 0f)     // Deep South Woods (CAM 3 background)
	};

	public override void _Ready()
	{
		InitVisuals();
		if (AutoHideOnReady) Deactivate();
		else { Visible = true; CurrentStage = 3; }
	}

	private void InitVisuals()
	{
		_visualRoot = GetNodeOrNull<Node3D>("VisualRoot");
		if (_visualRoot == null && ModelPrefab != null)
		{
			var instance = ModelPrefab.Instantiate<Node3D>();
			AddChild(instance);
			_visualRoot = instance.Name == "VisualRoot" ? instance : instance.GetNodeOrNull<Node3D>("VisualRoot") ?? instance;
		}

		_headNode = _visualRoot?.GetNodeOrNull<Node3D>("HeadNode");
		_eyeLight = _headNode?.GetNodeOrNull<OmniLight3D>("EyeGlowLight");
	}

	public override void _Process(double delta)
	{
		if (!Visible || _visualRoot == null) return;
		_animTime += delta;

		if (IsLurking)
		{
			_visualRoot.Position = new Vector3(Mathf.Sin((float)_animTime * 1.2f) * 0.02f, Mathf.Cos((float)_animTime * 1.5f) * 0.02f, 0);
			if (_eyeLight != null)
				_eyeLight.LightEnergy = 0.40f + Mathf.Sin((float)_animTime * 2.5f) * 0.15f;
			if (_headNode != null)
				_headNode.RotationDegrees = new Vector3(0, Mathf.Sin((float)_animTime * 0.8f) * 12f, 0);
			return;
		}

		if (CurrentStage <= 0) return;

		_visualRoot.Position = new Vector3(Mathf.Sin((float)_animTime * 1.8f) * 0.04f, Mathf.Cos((float)_animTime * 2.2f) * 0.03f, 0);

		if (CurrentStage >= 3)
		{
			_nextTwitchTime -= (float)delta;
			if (_nextTwitchTime <= 0f)
			{
				_nextTwitchTime = (float)(_rng.NextDouble() * 0.35 + 0.15);
				if (_headNode != null)
					_headNode.RotationDegrees = new((float)(_rng.NextDouble() * 18 - 9), (float)(_rng.NextDouble() * 24 - 12), (float)(_rng.NextDouble() * 20 - 10));
			}
		}
		else if (_headNode != null)
		{
			_headNode.RotationDegrees = _headNode.RotationDegrees.Lerp(Vector3.Zero, 8f * (float)delta);
		}
	}

	public void SetLurking(int spotIndex)
	{
		CurrentStage = 0;
		IsLurking = true;
		IsTargeting = false;
		CurrentLurkingSpot = Mathf.Clamp(spotIndex, 0, _lurkingVantages.Length - 1);

		var (pos, yaw) = _lurkingVantages[CurrentLurkingSpot];
		Visible = true;
		GlobalPosition = pos;
		RotationDegrees = new Vector3(0, yaw, 0);

		if (_eyeLight != null)
		{
			_eyeLight.LightEnergy = 0.45f;
			_eyeLight.LightColor = new Color(0.85f, 0.2f, 0.2f);
		}
	}

	private Tween _retreatTween;

	public void SetStage(int direction, int stage)
	{
		CurrentDirection = Mathf.Clamp(direction, 0, 3);
		CurrentStage = Mathf.Clamp(stage, 0, 3);
		IsTargeting = CurrentStage > 0;

		if (CurrentStage == 0)
		{
			// If monster is currently deactivated (daytime), preserve invisible state
			if (!Visible)
			{
				Deactivate();
				return;
			}

			// Remain visible in peripheral CCTV vantages during night lurking unless currently retreating
			if (_retreatTween == null || !_retreatTween.IsValid())
				SetLurking(_rng.Next(0, _lurkingVantages.Length));
			return;
		}

		if (_retreatTween != null && _retreatTween.IsValid())
			_retreatTween.Kill();

		IsLurking = false;
		Visible = true;
		GlobalPosition = _stagePositions[CurrentDirection, CurrentStage];
		RotationDegrees = new Vector3(0, _directionYaws[CurrentDirection], 0);

		Scale = Vector3.One * 0.85f;
		CreateTween().TweenProperty(this, "scale", Vector3.One, 0.20f).SetTrans(Tween.TransitionType.Back).SetEase(Tween.EaseType.Out);

		if (_eyeLight != null)
		{
			_eyeLight.LightEnergy = CurrentStage switch { 1 => 0.8f, 2 => 1.4f, _ => 2.5f };
			_eyeLight.LightColor = new Color(1.0f, 0.1f, 0.1f);
		}
	}

	public void PlayRepelEffect(Action onComplete = null)
	{
		CurrentStage = 0;
		IsLurking = false;
		IsTargeting = false;
		if (!Visible)
		{
			onComplete?.Invoke();
			return;
		}

		if (_retreatTween != null && _retreatTween.IsValid())
			_retreatTween.Kill();

		// Visibly sprint 12m away from cabin back into the dark tree line
		Vector3 retreatDir = -GlobalTransform.Basis.Z;
		Vector3 targetPos = GlobalPosition + retreatDir * 12.0f;

		_retreatTween = CreateTween();
		_retreatTween.SetParallel(true);
		_retreatTween.TweenProperty(this, "global_position", targetPos, 0.9f)
			.SetTrans(Tween.TransitionType.Quad)
			.SetEase(Tween.EaseType.In);
		_retreatTween.TweenProperty(this, "scale", Vector3.One * 0.4f, 0.9f);

		_retreatTween.Chain().TweenCallback(Callable.From(() =>
		{
			Scale = Vector3.One;
			if (Visible)
				SetLurking(_rng.Next(0, _lurkingVantages.Length));
			onComplete?.Invoke();
		}));
	}

	public void Deactivate()
	{
		Visible = false;
		IsLurking = false;
		IsTargeting = false;
		CurrentStage = 0;
	}
}
