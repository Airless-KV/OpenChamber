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
// =========================================================================================
using Godot;
using System;

public partial class AniAniMonster : Node3D
{
	[ExportGroup("Visual Customization")]
	[Export] public Color BodyColor = new(0.03f, 0.02f, 0.04f);
	[Export] public Color EyeGlowColor = new(1.0f, 0.05f, 0.05f);
	[Export] public float EyeLightEnergy = 1.8f;

	public int CurrentStage { get; private set; } = 0; // 0=Hidden, 1=Distant, 2=Approaching, 3=Imminent
	public int CurrentDirection { get; private set; } = 0; // 0=North, 1=East, 2=South, 3=West

	private Node3D _visualRoot;
	private Node3D _headNode;
	private OmniLight3D _eyeLight;
	private double _animTime = 0.0;
	private Random _rng = new();
	private float _nextTwitchTime = 0.0f;

	// Stalking positions relative to house center (-1.66, 0.0, 0.0)
	// Stage 1 (Distant ~28m), Stage 2 (Approaching ~14m), Stage 3 (Imminent ~4.5m)
	private readonly Vector3[,] _stagePositions = new Vector3[4, 4]
	{
		// 0: North (-Z)
		{
			new(-1.66f, -100.0f, 0.0f),
			new(-1.66f, 0.0f, -28.0f),
			new(-1.66f, 0.0f, -14.0f),
			new(-1.66f, 0.0f, -4.5f)
		},
		// 1: East (+X, Front Door)
		{
			new(-1.66f, -100.0f, 0.0f),
			new(26.0f, 0.0f, -0.8f),
			new(13.0f, 0.0f, -0.8f),
			new(4.5f, 0.0f, -0.8f)
		},
		// 2: South (+Z)
		{
			new(-1.66f, -100.0f, 0.0f),
			new(-1.66f, 0.0f, 28.0f),
			new(-1.66f, 0.0f, 14.0f),
			new(-1.66f, 0.0f, 4.5f)
		},
		// 3: West (-X)
		{
			new(-1.66f, -100.0f, 0.0f),
			new(-28.0f, 0.0f, 0.0f),
			new(-14.0f, 0.0f, 0.0f),
			new(-5.0f, 0.0f, 0.0f)
		}
	};

	[Export] public bool AutoHideOnReady = true;

	// Yaw rotations facing inward toward the house
	private readonly float[] _directionYaws = { 180.0f, -90.0f, 0.0f, 90.0f };

	public override void _Ready()
	{
		BuildMonsterMesh();
		if (AutoHideOnReady)
		{
			SetStage(0, 0);
		}
		else
		{
			Visible = true;
			CurrentStage = 3; // Fully active for preview
		}
	}

	private void BuildMonsterMesh()
	{
		_visualRoot = GetNodeOrNull<Node3D>("VisualRoot");
		if (_visualRoot != null)
		{
			_headNode = _visualRoot.GetNodeOrNull<Node3D>("HeadNode");
			_eyeLight = _headNode?.GetNodeOrNull<OmniLight3D>("EyeGlowLight");
			return;
		}

		_visualRoot = new Node3D { Name = "VisualRoot" };
		AddChild(_visualRoot);

		var bodyMat = new StandardMaterial3D
		{
			AlbedoColor = BodyColor,
			Roughness = 0.95f,
			Metallic = 0.05f
		};

		var eyeMat = new StandardMaterial3D
		{
			AlbedoColor = EyeGlowColor,
			EmissionEnabled = true,
			Emission = EyeGlowColor,
			EmissionEnergyMultiplier = 4.0f
		};

		// Torso (Elongated, hunched silhouette)
		var torso = new MeshInstance3D
		{
			Name = "Torso",
			Mesh = new CapsuleMesh { Radius = 0.48f, Height = 2.4f },
			MaterialOverride = bodyMat,
			Position = new Vector3(0, 1.7f, 0),
			RotationDegrees = new Vector3(12.0f, 0, 0)
		};
		_visualRoot.AddChild(torso);

		// Head & Neck
		_headNode = new Node3D { Name = "HeadNode", Position = new Vector3(0, 2.85f, 0.25f) };
		_visualRoot.AddChild(_headNode);

		var head = new MeshInstance3D
		{
			Name = "Head",
			Mesh = new SphereMesh { Radius = 0.38f, Height = 0.70f },
			MaterialOverride = bodyMat
		};
		_headNode.AddChild(head);

		// Jagged Horns / Spikes
		var leftHorn = new MeshInstance3D
		{
			Name = "LeftHorn",
			Mesh = new CylinderMesh { TopRadius = 0.02f, BottomRadius = 0.12f, Height = 0.65f },
			MaterialOverride = bodyMat,
			Position = new Vector3(-0.22f, 0.42f, -0.08f),
			RotationDegrees = new Vector3(-15.0f, 0, -25.0f)
		};
		_headNode.AddChild(leftHorn);

		var rightHorn = new MeshInstance3D
		{
			Name = "RightHorn",
			Mesh = new CylinderMesh { TopRadius = 0.02f, BottomRadius = 0.12f, Height = 0.65f },
			MaterialOverride = bodyMat,
			Position = new Vector3(0.22f, 0.42f, -0.08f),
			RotationDegrees = new Vector3(-15.0f, 0, 25.0f)
		};
		_headNode.AddChild(rightHorn);

		// Glowing Crimson Eyes
		var leftEye = new MeshInstance3D
		{
			Name = "LeftEye",
			Mesh = new SphereMesh { Radius = 0.065f, Height = 0.09f },
			MaterialOverride = eyeMat,
			Position = new Vector3(-0.13f, 0.06f, 0.32f)
		};
		_headNode.AddChild(leftEye);

		var rightEye = new MeshInstance3D
		{
			Name = "RightEye",
			Mesh = new SphereMesh { Radius = 0.065f, Height = 0.09f },
			MaterialOverride = eyeMat,
			Position = new Vector3(0.13f, 0.06f, 0.32f)
		};
		_headNode.AddChild(rightEye);

		// Eye Light (illuminates face and nearby night fog)
		_eyeLight = new OmniLight3D
		{
			Name = "EyeGlowLight",
			LightColor = EyeGlowColor,
			LightEnergy = EyeLightEnergy,
			OmniRange = 4.5f,
			Position = new Vector3(0, 0.10f, 0.45f)
		};
		_headNode.AddChild(_eyeLight);

		// Elongated Arms & Claws
		var leftArm = new MeshInstance3D
		{
			Name = "LeftArm",
			Mesh = new CapsuleMesh { Radius = 0.12f, Height = 2.1f },
			MaterialOverride = bodyMat,
			Position = new Vector3(-0.62f, 1.45f, 0.10f),
			RotationDegrees = new Vector3(15.0f, 0, 10.0f)
		};
		_visualRoot.AddChild(leftArm);

		var rightArm = new MeshInstance3D
		{
			Name = "RightArm",
			Mesh = new CapsuleMesh { Radius = 0.12f, Height = 2.1f },
			MaterialOverride = bodyMat,
			Position = new Vector3(0.62f, 1.45f, 0.10f),
			RotationDegrees = new Vector3(15.0f, 0, -10.0f)
		};
		_visualRoot.AddChild(rightArm);

		// Legs
		var leftLeg = new MeshInstance3D
		{
			Name = "LeftLeg",
			Mesh = new CapsuleMesh { Radius = 0.18f, Height = 1.9f },
			MaterialOverride = bodyMat,
			Position = new Vector3(-0.28f, 0.85f, -0.05f)
		};
		_visualRoot.AddChild(leftLeg);

		var rightLeg = new MeshInstance3D
		{
			Name = "RightLeg",
			Mesh = new CapsuleMesh { Radius = 0.18f, Height = 1.9f },
			MaterialOverride = bodyMat,
			Position = new Vector3(0.28f, 0.85f, -0.05f)
		};
		_visualRoot.AddChild(rightLeg);
	}

	public override void _Process(double delta)
	{
		if (CurrentStage <= 0 || _visualRoot == null) return;

		_animTime += delta;

		// Menacing breathing and swaying
		float sway = Mathf.Sin((float)_animTime * 1.8f) * 0.04f;
		float bob = Mathf.Cos((float)_animTime * 2.2f) * 0.03f;
		_visualRoot.Position = new Vector3(sway, bob, 0);

		// Stage 3: Intense unnatural head twitches
		if (CurrentStage >= 3)
		{
			_nextTwitchTime -= (float)delta;
			if (_nextTwitchTime <= 0.0f)
			{
				_nextTwitchTime = (float)(_rng.NextDouble() * 0.35 + 0.15);
				if (_headNode != null)
				{
					_headNode.RotationDegrees = new Vector3(
						(float)(_rng.NextDouble() * 18 - 9),
						(float)(_rng.NextDouble() * 24 - 12),
						(float)(_rng.NextDouble() * 20 - 10)
					);
				}
			}
		}
		else if (_headNode != null)
		{
			_headNode.RotationDegrees = _headNode.RotationDegrees.Lerp(Vector3.Zero, 8.0f * (float)delta);
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

		// Pulse scale on stage transition
		Scale = Vector3.One * 0.85f;
		Tween tw = CreateTween();
		tw.TweenProperty(this, "scale", Vector3.One, 0.20f).SetTrans(Tween.TransitionType.Back).SetEase(Tween.EaseType.Out);

		// Adjust eye brightness based on proximity
		if (_eyeLight != null)
		{
			_eyeLight.LightEnergy = CurrentStage switch
			{
				1 => 0.8f,
				2 => 1.4f,
				_ => 2.2f
			};
		}
	}

	public void PlayRepelEffect()
	{
		CurrentStage = 0;
		if (!Visible) return;

		// Retreat backward into the fog and vanish
		Vector3 backwardStep = -GlobalTransform.Basis.Z * 6.0f;
		Tween tw = CreateTween();
		tw.SetParallel(true);
		tw.TweenProperty(this, "global_position", GlobalPosition + backwardStep, 0.45f).SetTrans(Tween.TransitionType.Quad).SetEase(Tween.EaseType.In);
		tw.TweenProperty(this, "scale", Vector3.Zero, 0.45f);

		tw.Chain().TweenCallback(Callable.From(() =>
		{
			Visible = false;
			Scale = Vector3.One;
			GlobalPosition = _stagePositions[CurrentDirection, 0];
		}));
	}
}
