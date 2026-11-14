// =========================================================================================
// Original Author : air (Airless-KV) - Nov 2026
// Responsibilities: - Tracks player position relative to the farm chessboard boundary
//                   - Drives 3D environment darkness and cold suffocating fog via AtmosphereManager
//                   - Animates distant Ani-Ani glowing red eyes stalking through forest trees
//                   - Projects lingering crimson eye illumination staring directly at the player
//                   - Escalates staged horror audio cues (rustle -> stalking footsteps -> stinger)
//                   - Triggers Ani-Ani jumpscare breach defeat when strayed outside for full duration
//                   - Smoothly disperses dark fog and restores daytime sunlight when returning to farm
// -----------------------------------------------------------------------------------------
// Change Log:
// - Nov 13, 2026 | air : Initial implementation — horror boundary zone
// - Nov 13, 2026 | air : Converted from 2D screen overlay to pure 3D environment lighting dimming,
//                        extended survival window to 20s, and added periodic red stalker eyes behind head
// - Nov 13, 2026 | air : Repositioned eyes deep into forest trees (10-18m away) facing player,
//                        extended sustained glow duration to ~6s, and scaled eye emission for distance
// =========================================================================================
using Godot;

public partial class FarmBoundaryZone : Node3D
{
	[ExportGroup("Dependencies")]
	[Export] public PlayerController PlayerNode;
	[Export] public CameraPivot CameraPivotNode;
	[Export] public AtmosphereManager AtmosphereNode;
	[Export] public BreachManager BreachManagerNode;

	[ExportGroup("Distant Stalker Eyes")]
	[Export] public Node3D StalkerEyesRoot;
	[Export] public MeshInstance3D LeftEyeMesh;
	[Export] public MeshInstance3D RightEyeMesh;
	[Export] public SpotLight3D LeftEyeLight;
	[Export] public SpotLight3D RightEyeLight;
	[Export] public OmniLight3D EyeGlowOmni;

	[ExportGroup("Stalker Distance & Timing")]
	[Export] public float MaxStalkDistance = 18.0f; // Initial distance in the deep forest
	[Export] public float MinStalkDistance = 10.0f; // Closes in as danger peaks
	[Export] public float GlowFadeInTime = 0.8f;    // Seconds to burn into visibility
	[Export] public float GlowSustainTime = 3.8f;   // Seconds eyes linger staring unblinkingly
	[Export] public float GlowFadeOutTime = 1.2f;   // Seconds to dissolve back into mist
	[Export] public float PulseInterval = 6.8f;     // Total interval between appearances

	[ExportGroup("Farm Boundary")]
	[Export] public bool AutoDetectChessboard = true;
	[Export] public Vector2 BoundaryMin = new(-25.66f, -24.02f); // X-min, Z-min
	[Export] public Vector2 BoundaryMax = new(22.29f, 23.93f);   // X-max, Z-max
	[Export] public float BoundaryBuffer = 0.5f;

	[ExportGroup("Danger Timing")]
	[Export] public float DeathDelay = 20.0f;     // Total seconds outside before fatal jumpscare
	[Export] public float RecoverySpeed = 0.65f;  // Speed fog clears when returning to farm

	private float _outsideTimer = 0f;
	private float _pulseTimer = 0f;
	private int _audioTier = 0;
	private bool _isDead = false;
	private Tween _eyeTween;
	private StandardMaterial3D _eyeMat;
	private Vector3 _currentStalkOffset = Vector3.Zero;
	private readonly System.Random _rng = new();

	public override void _Ready()
	{
		ResolveDependencies();

		if (AutoDetectChessboard)
		{
			DetectChessboardBounds();
		}

		if (LeftEyeMesh?.MaterialOverride is StandardMaterial3D sm)
		{
			_eyeMat = sm;
		}

		ResetEyes();
		GD.Print($"[FarmBoundaryZone] Initialized with distant forest stalker eyes ({MinStalkDistance:F0}m-{MaxStalkDistance:F0}m). Boundary X: [{BoundaryMin.X:F1}, {BoundaryMax.X:F1}], Z: [{BoundaryMin.Y:F1}, {BoundaryMax.Y:F1}].");
	}

	public override void _Process(double delta)
	{
		if (_isDead) return;

		// Lazy resolve if nodes initialized later
		if (PlayerNode == null || AtmosphereNode == null || BreachManagerNode == null)
		{
			ResolveDependencies();
			if (PlayerNode == null) return;
		}

		float dt = (float)delta;
		bool outside = IsOutsideSafeZone();

		if (outside)
		{
			_outsideTimer += dt;
			float progress = Mathf.Clamp(_outsideTimer / DeathDelay, 0.0f, 1.0f);

			// Progressively darken 3D environment (sun dies, dark fog rolls in)
			if (AtmosphereNode != null)
			{
				AtmosphereNode.DangerDarkness = progress;
			}

			// Update distant stalker eyes position & orientation in the forest
			UpdateDistantEyesPosition(dt, progress);

			// Periodic lingering red eyes glow
			UpdateStalkerPulse(dt, progress);

			// Staged horror audio escalation
			HandleAudioEscalation(_outsideTimer);

			// Lethal death threshold
			if (_outsideTimer >= DeathDelay)
			{
				TriggerDeath();
			}
		}
		else
		{
			// Safe on the farm
			if (_outsideTimer > 0f)
			{
				_outsideTimer = 0f;
				_audioTier = 0;
				_pulseTimer = 0f;
				ResetEyes();
			}

			// Smoothly clear dark fog and bring warm sunlight back
			if (AtmosphereNode != null && AtmosphereNode.DangerDarkness > 0f)
			{
				AtmosphereNode.DangerDarkness = Mathf.MoveToward(AtmosphereNode.DangerDarkness, 0f, dt * RecoverySpeed);
			}
		}
	}

	private void ResolveDependencies()
	{
		PlayerNode ??= GetTree().GetFirstNodeInGroup("Player") as PlayerController
		             ?? GetTree().Root.FindChild("Player", true, false) as PlayerController;

		CameraPivotNode ??= GetTree().Root.FindChild("CameraPivot", true, false) as CameraPivot;
		AtmosphereNode ??= GetTree().Root.FindChild("AtmosphereManager", true, false) as AtmosphereManager;
		BreachManagerNode ??= GetTree().Root.FindChild("BreachManager", true, false) as BreachManager;

		StalkerEyesRoot ??= GetNodeOrNull<Node3D>("StalkerEyes");
		LeftEyeMesh ??= GetNodeOrNull<MeshInstance3D>("StalkerEyes/LeftEye");
		RightEyeMesh ??= GetNodeOrNull<MeshInstance3D>("StalkerEyes/RightEye");
		LeftEyeLight ??= GetNodeOrNull<SpotLight3D>("StalkerEyes/LeftSpot");
		RightEyeLight ??= GetNodeOrNull<SpotLight3D>("StalkerEyes/RightSpot");
		EyeGlowOmni ??= GetNodeOrNull<OmniLight3D>("StalkerEyes/EyeGlowOmni");

		if (_eyeMat == null && LeftEyeMesh?.MaterialOverride is StandardMaterial3D sm)
		{
			_eyeMat = sm;
		}
	}

	private void DetectChessboardBounds()
	{
		var board = GetTree().Root.FindChild("grassPlaceHolder", true, false) as Node3D;
		if (board == null) return;

		Vector3 gpos = board.GlobalPosition;
		float halfExtentX = 24.0f;
		float halfExtentZ = 24.0f;

		var col = board.GetNodeOrNull<CollisionShape3D>("StaticBody3D/CollisionShape3D");
		if (col != null && col.Scale.X > 1.0f)
		{
			halfExtentX = col.Scale.X * 0.5f;
			halfExtentZ = col.Scale.Z * 0.5f;
		}

		BoundaryMin = new Vector2(gpos.X - halfExtentX, gpos.Z - halfExtentZ);
		BoundaryMax = new Vector2(gpos.X + halfExtentX, gpos.Z + halfExtentZ);
	}

	private bool IsOutsideSafeZone()
	{
		if (PlayerNode == null) return false;
		if (PlayerNode.IsIndoors) return false;

		Vector3 pos = PlayerNode.GlobalPosition;
		return pos.X < (BoundaryMin.X - BoundaryBuffer) ||
		       pos.X > (BoundaryMax.X + BoundaryBuffer) ||
		       pos.Z < (BoundaryMin.Y - BoundaryBuffer) ||
		       pos.Z > (BoundaryMax.Y + BoundaryBuffer);
	}

	private void UpdateDistantEyesPosition(float dt, float dangerProgress)
	{
		if (StalkerEyesRoot == null) return;

		// Distance closes in as danger increases: 18m -> 10m
		float stalkDistance = Mathf.Lerp(MaxStalkDistance, MinStalkDistance, dangerProgress);

		Vector3 camPos = CameraPivotNode != null ? CameraPivotNode.GlobalPosition : PlayerNode.GlobalPosition + new Vector3(0, 1.45f, 0);
		Vector3 backward = CameraPivotNode != null ? CameraPivotNode.GlobalBasis.Z : PlayerNode.GlobalBasis.Z;

		// Stalking direction blends behind the player with a subtle horizontal flank
		Vector3 flatBack = new Vector3(backward.X, 0, backward.Z).Normalized();
		Vector3 stalkDir = (flatBack + _currentStalkOffset).Normalized();

		Vector3 targetPos = camPos + stalkDir * stalkDistance;
		targetPos.Y = Mathf.Max(camPos.Y + 0.35f, 2.1f); // Elevated in forest tree branches

		StalkerEyesRoot.GlobalPosition = StalkerEyesRoot.GlobalPosition.Lerp(targetPos, dt * 4.0f);

		// Direct unblinking gaze pointing straight at the player's head
		if (StalkerEyesRoot.GlobalPosition.DistanceSquaredTo(camPos) > 1.0f)
		{
			StalkerEyesRoot.LookAt(camPos, Vector3.Up);
		}
	}

	private void UpdateStalkerPulse(float dt, float dangerProgress)
	{
		_pulseTimer += dt;

		// Pulse interval slightly tightens as danger peaks (from 6.8s down to 5.2s)
		float currentInterval = Mathf.Lerp(PulseInterval, 5.2f, dangerProgress);

		if (_pulseTimer >= currentInterval)
		{
			_pulseTimer = 0f;
			PickNewStalkOffset();
			TriggerLingeringEyeGlow(dangerProgress);
		}
	}

	private void PickNewStalkOffset()
	{
		// Subtle flank angle (-0.45 to +0.45 lateral offset) so eyes appear between different trees
		float lateral = (float)(_rng.NextDouble() * 0.9 - 0.45);
		Vector3 right = CameraPivotNode != null ? CameraPivotNode.GlobalBasis.X : Vector3.Right;
		_currentStalkOffset = right * lateral;
	}

	private void TriggerLingeringEyeGlow(float dangerProgress)
	{
		_eyeTween?.Kill();
		_eyeTween = CreateTween();

		if (StalkerEyesRoot != null)
		{
			StalkerEyesRoot.Visible = true;
		}

		// Peak brightness increases with danger (3.5 to 5.5 for clear distance glow)
		float peakEnergy = Mathf.Lerp(3.5f, 5.5f, dangerProgress);

		// 1. Slow ominous burn-in (0.8s)
		_eyeTween.TweenMethod(Callable.From<float>(SetEyeEnergy), 0.0f, peakEnergy, GlowFadeInTime)
			.SetTrans(Tween.TransitionType.Sine).SetEase(Tween.EaseType.Out);

		// 2. Lingering unblinking stare (3.8s)
		_eyeTween.TweenInterval(GlowSustainTime);

		// 3. Slow dissolve back into the dark mist (1.2s)
		_eyeTween.TweenMethod(Callable.From<float>(SetEyeEnergy), peakEnergy, 0.0f, GlowFadeOutTime)
			.SetTrans(Tween.TransitionType.Sine).SetEase(Tween.EaseType.In);

		_eyeTween.TweenCallback(Callable.From(() =>
		{
			if (_outsideTimer <= 0f && StalkerEyesRoot != null)
			{
				StalkerEyesRoot.Visible = false;
			}
		}));
	}

	private void SetEyeEnergy(float energy)
	{
		if (LeftEyeLight != null) LeftEyeLight.LightEnergy = energy;
		if (RightEyeLight != null) RightEyeLight.LightEnergy = energy;
		if (EyeGlowOmni != null) EyeGlowOmni.LightEnergy = energy * 0.65f;

		if (_eyeMat != null)
		{
			// High HDR emission so the red dots punch clearly through the dark fog
			_eyeMat.EmissionEnergyMultiplier = energy * 1.5f;
		}
	}

	private void ResetEyes()
	{
		_eyeTween?.Kill();
		SetEyeEnergy(0.0f);
		if (StalkerEyesRoot != null)
		{
			StalkerEyesRoot.Visible = false;
		}
	}

	private void HandleAudioEscalation(float time)
	{
		if (time >= 4.0f && _audioTier < 1)
		{
			_audioTier = 1;
			AudioManager.Sfx("foliage_rustle", -6f, 0.15f);
		}
		else if (time >= 8.5f && _audioTier < 2)
		{
			_audioTier = 2;
			AudioManager.Sfx("monster_footsteps", -3f, 0.1f);
		}
		else if (time >= 13.5f && _audioTier < 3)
		{
			_audioTier = 3;
			AudioManager.Sfx("warning_stinger", 0f, 0.05f);
		}
		else if (time >= 17.5f && _audioTier < 4)
		{
			_audioTier = 4;
			AudioManager.SfxPitch("monster_footsteps", 1.3f, 2f);
		}
	}

	private void TriggerDeath()
	{
		if (_isDead) return;
		_isDead = true;

		ResetEyes();

		// Restore daytime atmosphere for game-over screen readability
		if (AtmosphereNode != null)
		{
			AtmosphereNode.DangerDarkness = 0.0f;
		}

		BreachManagerNode?.TriggerBreach("You wandered into the forbidden wild... Ani-Ani consumed you in the dark.");
	}
}
