// =========================================================================================
// Original Author : air (Airless-KV) - Nov 2026
// Responsibilities: - Night horror entity targeting the 8x8 farming chessboard crops
//                   - Crop priority tracking (seeks highest tier flower to consume)
//                   - Garden creep movement, devour eating timer, and crop destruction
//                   - Blinded retreat reaction triggered by exterior cabin floodlights
// -----------------------------------------------------------------------------------------
// Change Log:
// - Nov 06, 2026 | air : Implemented Crop Devourer entity and cabin floodlight vulnerability
// - Nov 06, 2026 | air : Added binding for standalone 3D scene (res://scenes/security/crop_devourer.tscn)
// - Nov 06, 2026 | air : Implemented persistent night loitering along garden perimeter and CCTV presence
// - Nov 06, 2026 | air : Scaled model by 1.6x, added close perimeter waypoints (6-8m), enhanced amber glow, and continuous fence patrolling
// - Nov 07, 2026 | air : Added AutoHideOnReady export and standalone preview support
// =========================================================================================
using Godot;
using System;

public partial class CropDevourer : Node3D
{
	[Signal] public delegate void CropAttackedEventHandler(Vector2I cell, int tier);
	[Signal] public delegate void CropDevouredEventHandler(Vector2I cell, int tier);
	[Signal] public delegate void CropDevourerRepelledEventHandler();

	[ExportGroup("Dependencies")]
	[Export] public Node3d GridManagerNode;
	[Export] public GameManager GameManagerNode;
	[Export] public bool AutoHideOnReady = true;

	[ExportGroup("Devourer Timers")]
	[Export] public float FirstSpawnDelay = 18.0f;
	[Export] public float SpawnInterval = 38.0f;
	[Export] public float ApproachDuration = 16.0f;
	[Export] public float EatingDuration = 5.0f;

	public enum State { Inactive, Loitering, Stalking, Eating, Fleeing }
	public State CurrentState { get; private set; } = State.Inactive;
	public Vector2I? TargetCell { get; private set; }
	public float AttackCountdown { get; private set; }

	private Node3D _visualRoot;
	private OmniLight3D _eyeLight;
	private float _timer;
	private Vector3 _spawnPos = new(14.0f, 0.5f, 22.0f);
	private Vector3 _targetWorldPos;
	private float _eatTimer;
	private double _animTime;
	private readonly Random _rng = new();
	private int _currentLoiterIndex;

	private readonly Vector3[] _loiterWaypoints =
	{
		new(7.5f, 0.45f, 2.0f),   // Right outside Front Door & East Window (CAM 2)
		new(0.0f, 0.45f, 8.0f),   // Right outside South Window (CAM 3)
		new(-1.0f, 0.45f, -8.0f), // Right outside North Window (CAM 1)
		new(5.0f, 0.45f, 6.0f),   // Corner by Garden chessboard
		new(8.5f, 0.45f, -4.0f)   // Near Front lawn
	};

	public override void _Ready()
	{
		AddToGroup("CropDevourers");
		GridManagerNode ??= GetTree().Root.FindChild("GridManager", true, false) as Node3d;
		GameManagerNode ??= GetTree().Root.FindChild("GameManager", true, false) as GameManager;

		_visualRoot = FindChild("VisualRoot", true, false) as Node3D;
		_eyeLight = FindChild("EyeLight", true, false) as OmniLight3D;

		if (_visualRoot == null)
		{
			BuildProceduralVisuals();
		}

		Scale = new Vector3(1.6f, 1.6f, 1.6f);

		if (!AutoHideOnReady || GameManagerNode == null)
		{
			// Preview / Showcase standalone mode: stay visible at placed transform
			Visible = true;
			CurrentState = State.Loitering;
			if (_eyeLight != null)
			{
				_eyeLight.LightEnergy = 2.5f;
				_eyeLight.OmniRange = 8.0f;
				_eyeLight.LightColor = new Color(1.0f, 0.72f, 0.15f);
			}
			return;
		}

		if (GameManagerNode != null)
		{
			GameManagerNode.PhaseChanged += (isDay, _) =>
			{
				if (isDay) Deactivate();
				else StartNightLoitering();
			};

			if (GameManagerNode.CurrentPhase == GameManager.GamePhase.Night)
			{
				StartNightLoitering();
			}
			else
			{
				Visible = false;
			}
		}
	}

	public void StartNightLoitering()
	{
		Visible = true;
		Scale = new Vector3(1.6f, 1.6f, 1.6f);
		CurrentState = State.Loitering;
		_timer = FirstSpawnDelay;

		_currentLoiterIndex = _rng.Next(0, _loiterWaypoints.Length);
		GlobalPosition = _loiterWaypoints[_currentLoiterIndex];
		LookAt(new Vector3(0, GlobalPosition.Y, 0), Vector3.Up);

		if (_eyeLight != null)
		{
			_eyeLight.LightEnergy = 2.5f;
			_eyeLight.OmniRange = 8.0f;
			_eyeLight.LightColor = new Color(1.0f, 0.72f, 0.15f);
		}
	}

	private void BuildProceduralVisuals()
	{
		_visualRoot = new Node3D { Name = "VisualRoot" };
		AddChild(_visualRoot);

		// Hunched dark beast torso
		var body = new MeshInstance3D
		{
			Mesh = new BoxMesh { Size = new Vector3(0.9f, 0.6f, 1.4f) },
			MaterialOverride = new StandardMaterial3D
			{
				AlbedoColor = new Color(0.08f, 0.07f, 0.09f),
				Roughness = 0.9f
			}
		};
		body.Position = new Vector3(0, 0.45f, 0);
		_visualRoot.AddChild(body);

		// Head with predatory angle
		var head = new MeshInstance3D
		{
			Mesh = new BoxMesh { Size = new Vector3(0.5f, 0.4f, 0.6f) },
			Position = new Vector3(0, 0.55f, -0.85f),
			MaterialOverride = body.MaterialOverride
		};
		_visualRoot.AddChild(head);

		// Menacing amber eye light
		_eyeLight = new OmniLight3D
		{
			LightColor = new Color(0.98f, 0.72f, 0.15f),
			LightEnergy = 1.2f,
			OmniRange = 4.5f,
			Position = new Vector3(0, 0.6f, -1.05f)
		};
		_visualRoot.AddChild(_eyeLight);
	}

	public override void _Process(double delta)
	{
		if (GameManagerNode != null && GameManagerNode.CurrentPhase != GameManager.GamePhase.Night) return;
		if (!Visible) return;
		float dt = (float)delta;
		_animTime += delta;

		if (CurrentState == State.Inactive)
		{
			StartNightLoitering();
			return;
		}

		if (CurrentState == State.Loitering)
		{
			_timer -= dt;

			// Predatory breathing & idle twitching
			if (_visualRoot != null)
			{
				_visualRoot.Position = new Vector3(0, Mathf.Abs(Mathf.Sin((float)_animTime * 2.0f)) * 0.06f, 0);
				_visualRoot.RotationDegrees = new Vector3(
					Mathf.Sin((float)_animTime * 1.5f) * 3f,
					Mathf.Cos((float)_animTime * 1.2f) * 4f,
					0
				);
			}

			if (_eyeLight != null)
				_eyeLight.LightEnergy = 0.55f + Mathf.Sin((float)_animTime * 2.5f) * 0.18f;

			if (_timer <= 0f && GameManagerNode != null)
			{
				TryStartStalking();
			}
			return;
		}

		if (CurrentState == State.Stalking)
		{
			AttackCountdown -= dt;
			float progress = Mathf.Clamp(1.0f - (AttackCountdown / ApproachDuration), 0f, 1f);
			GlobalPosition = _spawnPos.Lerp(_targetWorldPos, progress);

			// Predatory creeping gait bob
			if (_visualRoot != null)
				_visualRoot.Position = new Vector3(0, Mathf.Abs(Mathf.Sin((float)_animTime * 6f)) * 0.12f, 0);

			if (AttackCountdown <= 0f)
			{
				StartEating();
			}
			return;
		}

		if (CurrentState == State.Eating)
		{
			_eatTimer -= dt;
			// Violent thrashing eating animation
			if (_visualRoot != null)
				_visualRoot.RotationDegrees = new Vector3(
					Mathf.Sin((float)_animTime * 14f) * 12f,
					Mathf.Cos((float)_animTime * 10f) * 15f,
					0
				);

			if (_eatTimer <= 0f)
			{
				CompleteDevour();
			}
		}
	}

	private void TryStartStalking()
	{
		if (GridManagerNode == null) return;
		var best = GridManagerNode.GetHighestTierPlantedCell();
		if (!best.HasValue)
		{
			// No crops planted to eat; pace along the perimeter fence visibly
			_currentLoiterIndex = (_currentLoiterIndex + 1) % _loiterWaypoints.Length;
			Vector3 nextWp = _loiterWaypoints[_currentLoiterIndex];
			Tween tw = CreateTween();
			tw.TweenProperty(this, "global_position", nextWp, 3.5f)
				.SetTrans(Tween.TransitionType.Quad)
				.SetEase(Tween.EaseType.InOut);
			LookAt(new Vector3(0, GlobalPosition.Y, 0), Vector3.Up);
			_timer = 10.0f;
			return;
		}

		TargetCell = best.Value;
		int tier = GridManagerNode.GetCropTier(TargetCell.Value);
		_targetWorldPos = GridManagerNode.GridToWorld(TargetCell.Value);

		// Choose a spawn edge on the garden lawn
		_spawnPos = new Vector3(
			TargetCell.Value.X > 4 ? 24.0f : -24.0f,
			0.5f,
			TargetCell.Value.Y > 4 ? 24.0f : -24.0f
		);

		GlobalPosition = _spawnPos;
		LookAt(new Vector3(_targetWorldPos.X, GlobalPosition.Y, _targetWorldPos.Z), Vector3.Up);
		Visible = true;
		CurrentState = State.Stalking;
		AttackCountdown = ApproachDuration;

		AudioManager.Sfx("foliage_rustle");
		EmitSignal(SignalName.CropAttacked, TargetCell.Value, tier);
	}

	private void StartEating()
	{
		CurrentState = State.Eating;
		_eatTimer = EatingDuration;
		AudioManager.Sfx("monster_footsteps");
	}

	private void CompleteDevour()
	{
		if (TargetCell.HasValue && GridManagerNode != null)
		{
			int tier = GridManagerNode.GetCropTier(TargetCell.Value);
			GridManagerNode.DestroyCropAt(TargetCell.Value);
			EmitSignal(SignalName.CropDevoured, TargetCell.Value, tier);
		}
		RetreatToPerimeter();
	}

	public bool RepelByFloodlight()
	{
		if (CurrentState is not (State.Stalking or State.Eating)) return false;

		AudioManager.Sfx("banner_sting");
		EmitSignal(SignalName.CropDevourerRepelled);
		RetreatToPerimeter();
		return true;
	}

	private void RetreatToPerimeter()
	{
		CurrentState = State.Fleeing;
		_currentLoiterIndex = _rng.Next(0, _loiterWaypoints.Length);
		Vector3 fleeTarget = _loiterWaypoints[_currentLoiterIndex];

		Tween tw = CreateTween();
		tw.TweenProperty(this, "global_position", fleeTarget, 1.2f).SetTrans(Tween.TransitionType.Quad).SetEase(Tween.EaseType.Out);
		tw.Chain().TweenCallback(Callable.From(() =>
		{
			CurrentState = State.Loitering;
			TargetCell = null;
			_timer = SpawnInterval;
			Scale = new Vector3(1.6f, 1.6f, 1.6f);
			LookAt(new Vector3(0, GlobalPosition.Y, 0), Vector3.Up);
		}));
	}

	private void Deactivate()
	{
		Visible = false;
		Scale = Vector3.One;
		CurrentState = State.Inactive;
		TargetCell = null;
		_timer = SpawnInterval;
		if (_visualRoot != null)
		{
			_visualRoot.Position = Vector3.Zero;
			_visualRoot.RotationDegrees = Vector3.Zero;
		}
	}
}
