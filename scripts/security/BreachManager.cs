// =========================================================================================
// Original Author : air (Airless-KV) - Oct 2026
// Responsibilities: - Night horror attack state machine and Ani-Ani breach management
//                   - Directional monster approach alerts and CCTV stalking stages (Distant, Approaching, Imminent)
//                   - Entrance security checks (doors/windows) & CCTV monitoring verification
//                   - Barricade fortification absorption and defense resolution
//                   - Audio warning stinger generation & defeat trigger
// -----------------------------------------------------------------------------------------
// Change Log:
// - Oct 24, 2026 | air : Streamlined breach state machine, entrance Linq queries, and audio stinger for Alpha build
// - Oct 30, 2026 | air : Integrated SaveManager to record repelled attacks in persistent profile stats
// - Oct 30, 2026 | air : Added progressive CCTV stalking stages across directional feeds with AniAniMonster
// - Oct 31, 2026 | air : Routed monster attack stinger to AudioManager.Sfx, removing duplicate PCM audio synthesis
// - Oct 31, 2026 | air : Simplified entrance verification using unified InteractableEntrance group query
// - Oct 31, 2026 | air : Consolidated state reset transitions and dependency resolution
// - Nov 06, 2026 | air : Integrated Wooden Plank barricade breach absorption, manual locking delay, & Garden Barrier reaction slowdown
// - Nov 06, 2026 | air : Implemented sequential plank shredding loop, random retreat cooldowns (15-75s), and floodlight repel integration
// - Nov 06, 2026 | air : Integrated PvZ Lawnmower 1 HP entrance defense, 3-plank cap, and unified retreat dispatch
// - Nov 10, 2026 | air : Synchronized initial day phase in _Ready and ensured daytime ResetAttack deactivates monster
// - Nov 11, 2026 | air : Streamlined breach state machine by removing unneeded outside barrier checks
// - Nov 13, 2026 | air : Decommissioned Lawnmower defense; immediate breach upon total barricade loss or bare entrance assault
// - Nov 14, 2026 | air : Slower Ani-Ani plank tearing speed (3.0s interval matching placement) and 3-plank cap
// - Nov 14, 2026 | air : Implemented 7s CCTV camera stare expulsion during approach and mandatory wooden planks at window
// - Nov 14, 2026 | air : Extended approach stage durations (15s Distant, 15s Approaching, 30s total) and scaled cooldowns
// =========================================================================================
using Godot;
using System;
using System.Linq;

public partial class BreachManager : Node
{
	[Signal] public delegate void MonsterAttackingEventHandler(int direction, string dirName, float timeToReact);
	[Signal] public delegate void MonsterRepelledEventHandler(int direction, string dirName);
	[Signal] public delegate void BreachOccurredEventHandler(string reason);
	[Signal] public delegate void StalkingStageChangedEventHandler(int direction, int stage);

	public enum AttackDirection { North = 0, East = 1, South = 2, West = 3 }
	public enum StalkingStage { Hidden = 0, Distant = 1, Approaching = 2, Imminent = 3 }

	[ExportGroup("Dependencies")]
	[Export] public GameManager GameManagerNode;
	[Export] public NighttCamera NightCameraNode;
	[Export] public PlayerController PlayerNode;
	[Export] public Node3d GridManagerNode;
	[Export] public JumpScareScreen JumpScareScreenNode;
	[Export] public VictoryScreen VictoryScreenNode;
	[Export] public HUDManager HUDNode;
	[Export] public AniAniMonster MonsterNode;

	[ExportGroup("Monster Attack Settings")]
	[Export] public float FirstAttackDelay = 42.0f;
	[Export] public float AttackInterval = 25.0f;
	[Export] public float ReactionWindow = 7.0f;
	[Export] public float CameraExpelRequiredDuration = 7.0f;
	[Export] public float DistantStageDuration = 15.0f;
	[Export] public float ApproachingStageDuration = 15.0f;
	[Export] public bool EnableOutdoorLoss = true;
	[Export] public float BarricadeDelay = 6.0f;

	[ExportGroup("Plank Shredding & Siege Settings")]
	[Export(PropertyHint.Range, "0.5,10.0,0.1")] public float PlankTearInterval = 3.0f;
	[Export(PropertyHint.Range, "1,3,1")] public int MinPlanksToTear = 1;
	[Export(PropertyHint.Range, "1,3,1")] public int MaxPlanksToTear = 3;

	[ExportGroup("Assault Cooldowns")]
	[Export(PropertyHint.Range, "15.0,90.0,1.0")] public float MinRetreatCooldown = 42.0f;
	[Export(PropertyHint.Range, "30.0,240.0,1.0")] public float MaxRetreatCooldown = 95.0f;

	public bool IsMonsterAttacking { get; private set; }
	public AttackDirection CurrentApproachDir { get; private set; }
	public StalkingStage CurrentStage { get; private set; }
	public float AttackCountdown { get; private set; }

	private float _nextAttackTimer;
	private float _cameraSpottedTimer;
	private bool _isNight;
	private bool _isTearingPlanks;
	private float _tearTimer;
	private int _planksQuotaToTear;
	private int _planksTornSoFar;
	private readonly Random _rng = new();

	private T Find<T>(string name) where T : class => GetTree().Root.FindChild(name, true, false) as T;

	public override void _Ready()
	{
		GameManagerNode ??= Find<GameManager>("GameManager");
		NightCameraNode ??= Find<NighttCamera>("NighttCamera");
		PlayerNode ??= Find<PlayerController>("Player");
		GridManagerNode ??= Find<Node3d>("GridManager");
		JumpScareScreenNode ??= Find<JumpScareScreen>("JumpScareScreen");
		VictoryScreenNode ??= Find<VictoryScreen>("VictoryScreen");
		HUDNode ??= Find<HUDManager>("HUD");
		MonsterNode ??= Find<AniAniMonster>("AniAniMonster");

		if (GameManagerNode != null)
		{
			_isNight = GameManagerNode.CurrentPhase == GameManager.GamePhase.Night;
			GameManagerNode.PhaseChanged += (isDay, _) =>
			{
				_isNight = !isDay;
				ResetAttack();
				if (_isNight)
				{
					_nextAttackTimer = FirstAttackDelay;
					MonsterNode?.SetLurking(_rng.Next(0, 6));
				}
				else
				{
					MonsterNode?.Deactivate();
					HUDNode?.ClearInteractionPrompt();
				}
			};
			GameManagerNode.GameCompleted += () =>
			{
				_isNight = false;
				ResetAttack();
				MonsterNode?.Deactivate();
				VictoryScreenNode?.ShowVictoryTally(GridManagerNode, GameManagerNode);
			};
		}
		ResetAttack();
		if (!_isNight)
		{
			MonsterNode?.Deactivate();
		}
	}

	public override void _Process(double delta)
	{
		if (GameManagerNode?.IsGameFinished == true || !_isNight) return;
		float dt = (float)delta;

		if (EnableOutdoorLoss && PlayerNode != null && !PlayerNode.IsIndoors)
		{
			TriggerBreach("CAUGHT OUTSIDE! You stepped outside after sundown into the Ani-Ani's hunting grounds!");
			return;
		}

		if (IsMonsterAttacking)
		{
			if (_isTearingPlanks)
			{
				_tearTimer -= dt;
				var lockable = GetOpeningForDirection(CurrentApproachDir);
				string dirName = CurrentApproachDir.ToString().ToUpper();

				if (lockable == null)
				{
					ResolveMonsterAttack();
					return;
				}

				HUDNode?.SetInteractionPrompt($"[!] ANI-ANI SHREDDING {dirName}! ({lockable.PlanksPlaced} planks left) - Hold Left Click (1.5s) to reinforce!");

				if (_tearTimer <= 0f)
				{
					_tearTimer = PlankTearInterval;

					if (lockable.PlanksPlaced > 0)
					{
						lockable.TearSinglePlank();
						_planksTornSoFar++;
					}

					if (lockable.PlanksPlaced <= 0)
					{
						// All planks torn down: immediate fatal security breach!
						_isTearingPlanks = false;
						TriggerBreach($"SECURITY BREACH! The Ani-Ani tore down all barricades and broke through the {dirName} entrance!");
						return;
					}

					if (_planksTornSoFar >= _planksQuotaToTear && lockable.PlanksPlaced > 0)
					{
						// Monster reached tear quota and planks remain -> retreats into the woods!
						TriggerMonsterRetreat($"[✓] ANI-ANI TORE {_planksTornSoFar} PLANKS AND RETREATED!");
						return;
					}
				}
				return;
			}

			AttackCountdown -= dt;
			HUDNode?.SetInteractionPrompt($"[!] ANI-ANI AT {CurrentApproachDir.ToString().ToUpper()} ENTRANCE! ({Mathf.CeilToInt(AttackCountdown)}s) - BARRICADE WITH PLANKS!");

			var currentLockable = GetOpeningForDirection(CurrentApproachDir);
			if (currentLockable != null && currentLockable.PlanksPlaced > 0)
			{
				_isTearingPlanks = true;
				_planksQuotaToTear = _rng.Next(MinPlanksToTear, MaxPlanksToTear + 1);
				_planksTornSoFar = 0;
				_tearTimer = PlankTearInterval;
				return;
			}

			if (AttackCountdown <= 0f) ResolveMonsterAttack();
			return;
		}

		_nextAttackTimer -= dt;
		float totalApproachTime = DistantStageDuration + ApproachingStageDuration;
		if (_nextAttackTimer <= totalApproachTime && CurrentStage == StalkingStage.Hidden)
		{
			CurrentApproachDir = (AttackDirection)_rng.Next(0, 4);
			_cameraSpottedTimer = 0f;
			SetStalkingStage(StalkingStage.Distant);
		}
		else if (_nextAttackTimer <= ApproachingStageDuration && CurrentStage == StalkingStage.Distant)
		{
			SetStalkingStage(StalkingStage.Approaching);
		}

		// Active CCTV surveillance check during approach (Distant / Approaching)
		if (CurrentStage is StalkingStage.Distant or StalkingStage.Approaching)
		{
			string dirName = CurrentApproachDir.ToString().ToUpper();
			int camIndex = (int)CurrentApproachDir + 1;
			bool isPlayerWatching = !IsPlayerNotLooking(CurrentApproachDir);

			if (isPlayerWatching)
			{
				_cameraSpottedTimer += dt;
				HUDNode?.SetInteractionPrompt($"[!] ANI-ANI SPOTTED ON CAM {camIndex} ({dirName})! EXPELLING: ({_cameraSpottedTimer:F1}s / {CameraExpelRequiredDuration:F1}s) - KEEP WATCH!");

				if (_cameraSpottedTimer >= CameraExpelRequiredDuration)
				{
					_cameraSpottedTimer = 0f;
					TriggerMonsterRetreat($"[✓] ANI-ANI SPOTTED ON CCTV FOR {CameraExpelRequiredDuration:F1}s! EXPELLED BACK INTO THE WOODS!");
					return;
				}
			}
			else
			{
				if (_cameraSpottedTimer > 0f)
				{
					HUDNode?.SetInteractionPrompt($"[!] ANI-ANI APPROACHING FROM {dirName}! RETURN TO CAM {camIndex} TO EXPEL ({_cameraSpottedTimer:F1}s / {CameraExpelRequiredDuration:F1}s)!");
				}
				else
				{
					HUDNode?.SetInteractionPrompt($"[!] ANI-ANI APPROACHING FROM {dirName}! MONITOR CAM {camIndex} FOR {CameraExpelRequiredDuration:F0}s TO EXPEL!");
				}
			}
		}

		if (_nextAttackTimer <= 0f) StartMonsterAttack();
	}

	private void ResetAttack()
	{
		IsMonsterAttacking = false;
		_isTearingPlanks = false;
		_planksTornSoFar = 0;
		_cameraSpottedTimer = 0f;
		if (_isNight && MonsterNode != null)
		{
			int spot = _rng.Next(0, 6);
			MonsterNode.SetLurking(spot);
			CurrentStage = StalkingStage.Hidden;
		}
		else
		{
			CurrentStage = StalkingStage.Hidden;
			MonsterNode?.Deactivate();
			EmitSignal(SignalName.StalkingStageChanged, (int)CurrentApproachDir, (int)CurrentStage);
		}
	}

	private void SetStalkingStage(StalkingStage stage)
	{
		CurrentStage = stage;
		MonsterNode?.SetStage((int)CurrentApproachDir, (int)CurrentStage);
		EmitSignal(SignalName.StalkingStageChanged, (int)CurrentApproachDir, (int)CurrentStage);
	}

	public void StartMonsterAttack()
	{
		if (!_isNight || IsMonsterAttacking) return;
		IsMonsterAttacking = true;
		_cameraSpottedTimer = 0f;

		SetStalkingStage(StalkingStage.Imminent);

		string dirName = CurrentApproachDir.ToString().ToUpper();
		AudioManager.Sfx("warning_stinger");

		var lockable = GetOpeningForDirection(CurrentApproachDir);
		if (lockable != null && lockable.PlanksPlaced > 0)
		{
			_isTearingPlanks = true;
			_planksQuotaToTear = _rng.Next(MinPlanksToTear, MaxPlanksToTear + 1);
			_planksTornSoFar = 0;
			_tearTimer = PlankTearInterval;
			HUDNode?.SetInteractionPrompt($"[!] ANI-ANI AT {dirName}! Shredding barricades ({lockable.PlanksPlaced} planks)! Hold Left Click (1.5s) to reinforce!");
			EmitSignal(SignalName.MonsterAttacking, (int)CurrentApproachDir, dirName, PlankTearInterval * _planksQuotaToTear);
			return;
		}

		// NO PLANKS AT THE WINDOW: Cannot be countered by camera once at the window!
		_isTearingPlanks = false;
		TriggerBreach($"SECURITY BREACH! The Ani-Ani reached the unbarricaded {dirName} opening! Bare openings cannot stop it once at the window!");
	}

	public void ResolveMonsterAttack()
	{
		IsMonsterAttacking = false;
		_isTearingPlanks = false;
		_cameraSpottedTimer = 0f;
		string dir = CurrentApproachDir.ToString().ToUpper();

		// Bare entrance hit by monster with no barricades: fatal security breach!
		TriggerBreach($"SECURITY BREACH! The Ani-Ani reached the unbarricaded {dir} opening! Wooden planks required at window!");
	}

	public void TriggerMonsterRetreat(string reason)
	{
		IsMonsterAttacking = false;
		_isTearingPlanks = false;
		_planksTornSoFar = 0;
		_cameraSpottedTimer = 0f;

		float cd = (float)_rng.NextDouble() * (MaxRetreatCooldown - MinRetreatCooldown) + MinRetreatCooldown;
		_nextAttackTimer = cd;

		HUDNode?.SetInteractionPrompt(reason + $" ({Mathf.CeilToInt(cd)}s cooldown)");
		SaveManager.RecordAttackRepelled();
		EmitSignal(SignalName.MonsterRepelled, (int)CurrentApproachDir, CurrentApproachDir.ToString().ToUpper());

		MonsterNode?.PlayRepelEffect(() =>
		{
			SetStalkingStage(StalkingStage.Hidden);
		});
	}

	public void RepelByFloodlight()
	{
		if (!IsMonsterAttacking) return;
		TriggerMonsterRetreat($"[✓] FLOODLIGHT BLASTED ANI-ANI AT {CurrentApproachDir.ToString().ToUpper()}! MONSTER REPELLED!");
	}

	private LockableOpening GetOpeningForDirection(AttackDirection dir)
	{
		return GetTree().GetNodesInGroup("Lockables")
			.OfType<LockableOpening>()
			.FirstOrDefault(o => o.AttackDirection == (int)dir);
	}

	public void TriggerBreach(string reason)
	{
		_isNight = false;
		ResetAttack();
		EmitSignal(SignalName.BreachOccurred, reason);
		JumpScareScreenNode?.TriggerJumpScare(reason, GameManagerNode?.CurrentDay ?? 1, GameManagerNode?.Coins ?? 0);
	}

	public bool IsAnyEntranceOpen() =>
		GetTree().GetNodesInGroup("Entrances").OfType<InteractableEntrance>().Any(e => e.IsOpen && !IsBarricaded(e));

	private bool IsBarricaded(InteractableEntrance entrance) =>
		GetTree().GetNodesInGroup("Lockables").OfType<LockableOpening>().Any(o => o.Entrance == entrance && o.PlanksPlaced > 0);

	public bool IsPlayerNotLooking(AttackDirection direction) =>
		NightCameraNode == null || !NightCameraNode.IsNightMode || NightCameraNode.CurrentNightIndex != (int)direction;
}
