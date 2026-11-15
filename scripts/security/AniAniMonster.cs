// =========================================================================================
// Original Author : air (Airless-KV) - Oct 2026
// Responsibilities: - 3D Ani-Ani horror entity model and silhouette generation
//                   - Directional stalking positioning across North, East, South, West feeds
//                   - Passive lurking state positioning across perimeter woods and ridges
//                   - Stalking stage state transitions (Distant, Approaching, Imminent)
//                   - Sinister eye glow emission, jitter twitches, and repel retreat animation
//                   - 3D animated death scene lunges, cinematic face lighting, and overhead looming
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
// - Nov 13, 2026 | air : Preserved scene-configured BaseScale across stage/retreat tweens and aligned imminent stage Y with window frames
// - Nov 13, 2026 | air : Enlarged monster proportions for elevated treehouse and grounded all stage positions at Y=1.25
// - Nov 14, 2026 | air : Replaced sprint/scale morph with CRT camera glitch burst and instant disappearance
// - Nov 15, 2026 | air : Masked body meshes to Visual Layer 2, tuned subtle eye illumination, and concealed dormant staging coordinates
// - Nov 15, 2026 | air : Added customizable editor LoiterSpotsContainer and perimeter Marker3D spot resolution for night loitering
// - Nov 15, 2026 | air : Added PlayDeathLunge for 3D animated death scene jumpscares and looming behavior
// - Nov 15, 2026 | air : Added cinematic face light, GetHeadPosition, head-bend looming, and 5.5s death pacing
// - Nov 15, 2026 | air : Corrected cardinal staging yaws (Front door 180 deg, North 0 deg) and lurking orientations to face farmhouse
// =========================================================================================
using Godot;
using System;

public partial class AniAniMonster : Node3D
{
	[Export] public PackedScene ModelPrefab;
	[Export] public bool AutoHideOnReady = true;
	[Export] public Node3D LoiterSpotsContainer;

	public int CurrentStage { get; private set; }
	public int CurrentDirection { get; private set; }
	public bool IsLurking { get; private set; }
	public bool IsTargeting { get; private set; }
	public int CurrentLurkingSpot { get; private set; } = -1;
	public bool IsInDeathAnimation { get; private set; } = false;

	private Node3D _visualRoot;
	private Node3D _headNode;
	private OmniLight3D _eyeLight;
	private OmniLight3D _cinematicFaceLight;
	private double _animTime;
	private readonly Random _rng = new();
	private float _nextTwitchTime;
	private Vector3 _baseScale = Vector3.One;
	private float _deathHeadBendPitch = 0f;

	private readonly Vector3[,] _stagePositions =
	{
		{ new(-1.66f, -100f, 0), new(-1.66f, 1.25f, -28f), new(-1.66f, 1.25f, -14f), new(-1.66f, 1.25f, -3.75f) },
		{ new(-1.66f, -100f, 0), new(26f, 1.25f, 0),       new(13f, 1.25f, 0),        new(2.09f, 1.25f, 0) },
		{ new(-1.66f, -100f, 0), new(-1.66f, 1.25f, 28f),  new(-1.66f, 1.25f, 14f),  new(-1.66f, 1.25f, 3.75f) },
		{ new(-1.66f, -100f, 0), new(-28f, 1.25f, 0),      new(-14f, 1.25f, 0),       new(-5.41f, 1.25f, 0) }
	};

	private readonly float[] _directionYaws = { 0f, -90f, 180f, 90f };

	// Peripheral lurking vantage points in the woods and ridges outside cardinal entrances (all facing inward toward house at X=-1.66, Z=0)
	private readonly (Vector3 pos, float yaw)[] _lurkingVantages =
	{
		(new Vector3(18f, 1.25f, -20f), -45f),    // North-East Tree Line (CAM 1 & 2)
		(new Vector3(18f, 1.25f, 20f), -135f),    // South-East Ridge (CAM 2 & 3)
		(new Vector3(-20f, 1.25f, 20f), 135f),    // South-West Hill (CAM 3 & 4)
		(new Vector3(-20f, 1.25f, -20f), 45f),    // North-West Thicket (CAM 4 & 1)
		(new Vector3(-1.66f, 1.25f, -32f), 0f),   // Deep North Woods (CAM 1 background)
		(new Vector3(-1.66f, 1.25f, 32f), 180f)   // Deep South Woods (CAM 3 background)
	};

	public override void _Ready()
	{
		_baseScale = Scale;
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
		if (_eyeLight != null)
		{
			_eyeLight.LightCullMask = 1;
			_eyeLight.OmniRange = 3.0f;
		}

		if (_headNode != null)
		{
			_cinematicFaceLight = _headNode.GetNodeOrNull<OmniLight3D>("CinematicFaceLight");
			if (_cinematicFaceLight == null)
			{
				_cinematicFaceLight = new OmniLight3D
				{
					Name = "CinematicFaceLight",
					LightColor = new Color(1.0f, 0.22f, 0.18f),
					LightEnergy = 0f,
					OmniRange = 14.0f,
					OmniAttenuation = 0.85f,
					Position = new Vector3(0, 0.2f, 0.85f)
				};
				_headNode.AddChild(_cinematicFaceLight);
			}
		}

		if (_visualRoot != null)
		{
			foreach (var node in _visualRoot.FindChildren("*", "MeshInstance3D", true, false))
			{
				if (node is MeshInstance3D mesh)
				{
					if (mesh.Name.ToString().Contains("Eye"))
					{
						mesh.Layers = 1;
					}
					else
					{
						mesh.Layers = 2;
					}
				}
			}
		}
	}

	public Vector3 GetHeadPosition()
	{
		if (_headNode != null && IsInstanceValid(_headNode))
		{
			return _headNode.GlobalPosition;
		}
		return GlobalPosition + Vector3.Up * 4.9f;
	}

	public override void _Process(double delta)
	{
		if (!Visible || _visualRoot == null) return;
		_animTime += delta;

		if (IsInDeathAnimation)
		{
			_nextTwitchTime -= (float)delta;
			if (_nextTwitchTime <= 0f)
			{
				_nextTwitchTime = (float)(_rng.NextDouble() * 0.14 + 0.04);
				if (_headNode != null)
				{
					_headNode.RotationDegrees = new Vector3(
						_deathHeadBendPitch + (float)(_rng.NextDouble() * 16 - 8),
						(float)(_rng.NextDouble() * 22 - 11),
						(float)(_rng.NextDouble() * 18 - 9)
					);
				}
			}
			return;
		}

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
		IsInDeathAnimation = false;
		_deathHeadBendPitch = 0f;
		if (_cinematicFaceLight != null) _cinematicFaceLight.LightEnergy = 0f;

		CurrentStage = 0;
		IsLurking = true;
		IsTargeting = false;

		if (LoiterSpotsContainer != null && LoiterSpotsContainer.GetChildCount() > 0)
		{
			var children = LoiterSpotsContainer.GetChildren();
			int idx = Mathf.Clamp(spotIndex % children.Count, 0, children.Count - 1);
			if (children[idx] is Node3D marker)
			{
				CurrentLurkingSpot = idx;
				GlobalPosition = marker.GlobalPosition;
				// Face the house center (-1.66f, GlobalPosition.Y, 0f) so Ani-Ani glares towards the farmhouse
				Vector3 toHouse = new Vector3(-1.66f, GlobalPosition.Y, 0f) - GlobalPosition;
				if (toHouse.LengthSquared() > 0.01f)
				{
					float yaw = Mathf.RadToDeg(Mathf.Atan2(toHouse.X, toHouse.Z));
					RotationDegrees = new Vector3(0, yaw, 0);
				}
				else
				{
					RotationDegrees = marker.RotationDegrees;
				}
			}
		}
		else
		{
			CurrentLurkingSpot = Mathf.Clamp(spotIndex, 0, _lurkingVantages.Length - 1);
			var (pos, yaw) = _lurkingVantages[CurrentLurkingSpot];
			GlobalPosition = pos;
			RotationDegrees = new Vector3(0, yaw, 0);
		}

		Visible = true;
		Scale = _baseScale;

		if (_eyeLight != null)
		{
			_eyeLight.LightCullMask = 1;
			_eyeLight.OmniRange = 3.0f;
			_eyeLight.LightEnergy = 0.45f;
			_eyeLight.LightColor = new Color(0.85f, 0.05f, 0.05f);
		}
	}

	private Tween _retreatTween;
	private Tween _lungeTween;

	public void PlayDeathLunge(Vector3 spawnPos, Vector3 targetPos, float targetYaw, float lungeDuration, Action onPeak, Action onComplete)
	{
		if (_retreatTween != null && _retreatTween.IsValid()) _retreatTween.Kill();
		if (_lungeTween != null && _lungeTween.IsValid()) _lungeTween.Kill();

		IsInDeathAnimation = true;
		_deathHeadBendPitch = 0f;
		Visible = true;
		Scale = _baseScale;
		GlobalPosition = spawnPos;
		RotationDegrees = new Vector3(0, targetYaw, 0);

		if (_eyeLight != null)
		{
			_eyeLight.LightEnergy = 6.0f;
			_eyeLight.LightColor = new Color(1.0f, 0.05f, 0.05f);
		}

		if (_cinematicFaceLight != null)
		{
			_cinematicFaceLight.LightEnergy = 9.5f;
		}

		CurrentStage = 3;

		_lungeTween = CreateTween().SetParallel(false);
		// 1. Terrifying charge toward the camera
		_lungeTween.TweenProperty(this, "global_position", targetPos, lungeDuration)
			.SetTrans(Tween.TransitionType.Quad).SetEase(Tween.EaseType.In);
		_lungeTween.TweenCallback(Callable.From(() =>
		{
			onPeak?.Invoke();
		}));

		// 2. Head-bend and step-over: bend head downwards 52 degrees directly over the fallen player
		var loomTween = _lungeTween.Parallel();
		loomTween.TweenMethod(Callable.From<float>(v => _deathHeadBendPitch = v), 0f, 52.0f, 0.65f)
			.SetTrans(Tween.TransitionType.Back).SetEase(Tween.EaseType.Out);

		// 3. Loom and twitch directly over the camera for 2.2 seconds
		_lungeTween.TweenInterval(2.2f);
		_lungeTween.TweenCallback(Callable.From(() =>
		{
			onComplete?.Invoke();
		}));
	}

	public void SetStage(int direction, int stage)
	{
		IsInDeathAnimation = false;
		_deathHeadBendPitch = 0f;
		if (_cinematicFaceLight != null) _cinematicFaceLight.LightEnergy = 0f;

		CurrentDirection = Mathf.Clamp(direction, 0, 3);
		CurrentStage = Mathf.Clamp(stage, 0, 3);
		IsTargeting = CurrentStage > 0;

		if (CurrentStage == 0)
		{
			if (_retreatTween == null || !_retreatTween.IsValid())
			{
				SetLurking(_rng.Next(0, 12));
			}
			return;
		}

		if (_retreatTween != null && _retreatTween.IsValid())
			_retreatTween.Kill();

		IsLurking = false;
		Scale = _baseScale;

		// Mask stage shift behind an analog static blip if player is watching this feed
		var cctv = GetTree().Root.FindChild("CCTVOverlay", true, false) as CCTVOverlay;
		var nightCam = GetTree().Root.FindChild("NighttCamera", true, false) as NighttCamera;
		bool isWatchingFeed = cctv != null && cctv.Visible && nightCam != null && nightCam.IsNightMode && nightCam.CurrentNightIndex == CurrentDirection;

		if (isWatchingFeed)
		{
			cctv.TriggerCameraGlitch(0.24f, onPeak: () =>
			{
				GlobalPosition = _stagePositions[CurrentDirection, CurrentStage];
				RotationDegrees = new Vector3(0, _directionYaws[CurrentDirection], 0);
				Visible = true;
			});
		}
		else
		{
			GlobalPosition = _stagePositions[CurrentDirection, CurrentStage];
			RotationDegrees = new Vector3(0, _directionYaws[CurrentDirection], 0);
			Visible = true;
		}

		if (_eyeLight != null)
		{
			_eyeLight.LightCullMask = 1;
			_eyeLight.OmniRange = 3.0f;
			_eyeLight.LightEnergy = CurrentStage switch { 1 => 0.4f, 2 => 0.8f, _ => 1.2f };
			_eyeLight.LightColor = new Color(1.0f, 0.05f, 0.05f);
		}
	}

	public void PlayRepelEffect() => PlayRepelEffect(null);

	public void PlayRepelEffect(Action onComplete)
	{
		IsInDeathAnimation = false;
		_deathHeadBendPitch = 0f;
		if (_cinematicFaceLight != null) _cinematicFaceLight.LightEnergy = 0f;

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

		Scale = _baseScale;

		var overlay = GetTree().Root.FindChild("CCTVOverlay", true, false) as CCTVOverlay;
		if (overlay != null && overlay.Visible)
		{
			// Violent CRT camera glitch burst: Ani-Ani vanishes at peak static snow!
			overlay.TriggerCameraGlitch(0.65f, onPeak: () =>
			{
				Visible = false;
				onComplete?.Invoke();
			});
		}
		else
		{
			// Direct vanish when not on CCTV
			Visible = false;
			onComplete?.Invoke();
		}
	}

	public void Deactivate()
	{
		Visible = false;
		IsLurking = false;
		IsTargeting = false;
		IsInDeathAnimation = false;
		_deathHeadBendPitch = 0f;
		if (_cinematicFaceLight != null) _cinematicFaceLight.LightEnergy = 0f;
		CurrentStage = 0;
		GlobalPosition = new Vector3(0, -100, 0);
	}
}
