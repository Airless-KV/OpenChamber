// =========================================================================================
// Original Author : air (Airless-KV) - Oct 2026
// Responsibilities: - 3D Ani-Ani horror entity model and silhouette generation
//                   - Directional stalking positioning across North, East, South, West feeds
//                   - Stalking stage state transitions (Distant, Approaching, Imminent)
//                   - Sinister eye glow emission, jitter twitches, and repel retreat animation
// -----------------------------------------------------------------------------------------
// Change Log:
// - Oct 30, 2026 | air : Implemented 3D Ani-Ani stalking entity with directional CCTV stages
// - Oct 30, 2026 | air : Added scene-tree node binding support for standalone ani_ani_monster.tscn
// - Oct 31, 2026 | air : Streamlined procedural geometry generation and symmetry instantiation
// - Oct 31, 2026 | air : Migrated Ani-Ani to pure scene asset (res://scenes/security/ani_ani_monster.tscn) and removed procedural geometry fallbacks
// =========================================================================================
using Godot;
using System;

public partial class AniAniMonster : Node3D
{
	[Export] public PackedScene ModelPrefab;
	[Export] public bool AutoHideOnReady = true;

	public int CurrentStage { get; private set; }
	public int CurrentDirection { get; private set; }

	private Node3D _visualRoot;
	private Node3D _headNode;
	private OmniLight3D _eyeLight;
	private double _animTime;
	private readonly Random _rng = new();
	private float _nextTwitchTime;

	private readonly Vector3[,] _stagePositions =
	{
		{ new(-1.66f, -100f, 0), new(-1.66f, 0, -28f), new(-1.66f, 0, -14f), new(-1.66f, 0, -4.5f) },
		{ new(-1.66f, -100f, 0), new(26f, 0, -0.8f), new(13f, 0, -0.8f), new(4.5f, 0, -0.8f) },
		{ new(-1.66f, -100f, 0), new(-1.66f, 0, 28f), new(-1.66f, 0, 14f), new(-1.66f, 0, 4.5f) },
		{ new(-1.66f, -100f, 0), new(-28f, 0, 0), new(-14f, 0, 0), new(-5f, 0, 0) }
	};

	private readonly float[] _directionYaws = { 180f, -90f, 0f, 90f };

	public override void _Ready()
	{
		InitVisuals();
		if (AutoHideOnReady) SetStage(0, 0);
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
		if (CurrentStage <= 0 || _visualRoot == null) return;
		_animTime += delta;

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

	public void SetStage(int direction, int stage)
	{
		CurrentDirection = Mathf.Clamp(direction, 0, 3);
		CurrentStage = Mathf.Clamp(stage, 0, 3);

		if (CurrentStage == 0)
		{
			Visible = false;
			GlobalPosition = _stagePositions[CurrentDirection, 0];
			return;
		}

		Visible = true;
		GlobalPosition = _stagePositions[CurrentDirection, CurrentStage];
		RotationDegrees = new Vector3(0, _directionYaws[CurrentDirection], 0);

		Scale = Vector3.One * 0.85f;
		CreateTween().TweenProperty(this, "scale", Vector3.One, 0.20f).SetTrans(Tween.TransitionType.Back).SetEase(Tween.EaseType.Out);

		if (_eyeLight != null)
			_eyeLight.LightEnergy = CurrentStage switch { 1 => 0.8f, 2 => 1.4f, _ => 2.2f };
	}

	public void PlayRepelEffect()
	{
		CurrentStage = 0;
		if (!Visible) return;

		Vector3 retreat = -GlobalTransform.Basis.Z * 6.0f;
		Tween tw = CreateTween();
		tw.SetParallel(true);
		tw.TweenProperty(this, "global_position", GlobalPosition + retreat, 0.45f).SetTrans(Tween.TransitionType.Quad).SetEase(Tween.EaseType.In);
		tw.TweenProperty(this, "scale", Vector3.Zero, 0.45f);
		tw.Chain().TweenCallback(Callable.From(() =>
		{
			Visible = false;
			Scale = Vector3.One;
			GlobalPosition = _stagePositions[CurrentDirection, 0];
		}));
	}
}
