// =========================================================================================
// Original Author : air (Airless-KV) - Oct 2026
// Responsibilities: - Night horror attack state machine and Ani-Ani breach management
//                   - Directional monster approach alerts and CCTV stalking stages (Distant, Approaching, Imminent)
//                   - Entrance security checks (doors/windows) & CCTV monitoring verification
//                   - Barricade fortification absorption and Garden Barrier perimeter slowdown
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
	[Export] public float FirstAttackDelay = 12.0f;
	[Export] public float AttackInterval = 25.0f;
	[Export] public float ReactionWindow = 7.0f;
	[Export] public float BarrierExtraReactionTime = 5.0f;
	[Export] public bool EnableOutdoorLoss = true;
	[Export] public float BarricadeDelay = 6.0f;

	[ExportGroup("Plank Shredding & Siege Settings")]
	[Export(PropertyHint.Range, "0.3,5.0,0.1")] public float PlankTearInterval = 1.5f;
	[Export(PropertyHint.Range, "1,3,1")] public int MinPlanksToTear = 1;
	[Export(PropertyHint.Range, "1,3,1")] public int MaxPlanksToTear = 3;

	[ExportGroup("Assault Cooldowns")]
	[Export(PropertyHint.Range, "5.0,60.0,1.0")] public float MinRetreatCooldown = 15.0f;
	[Export(PropertyHint.Range, "15.0,180.0,1.0")] public float MaxRetreatCooldown = 75.0f;

	public bool IsMonsterAttacking { get; private set; }
	public AttackDirection CurrentApproachDir { get; private set; }
	public StalkingStage CurrentStage { get; private set; }
	public float AttackCountdown { get; private set; }

	private float _nextAttackTimer;
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

				HUDNode?.SetInteractionPrompt($"[!] ANI-ANI SHREDDING {dirName}! ({lockable.PlanksPlaced} planks left) - Hold [G] (3s) to nail planks!");

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
						// All planks torn! Ani-Ani strikes the entrance itself (1 HP Lawnmower defense)!
						_isTearingPlanks = false;
						var result = lockable.TakeHitAndCheckLawnmower();

						if (result == LockableOpening.DefenseResult.LawnmowerTriggered)
						{
							TriggerMonsterRetreat($"[!] LAWNMOWER DEFENSE! {dirName} entrance shattered repelling Ani-Ani! (0 HP - VULNERABLE!)");
							return;
						}
						else
						{
							TriggerBreach($"SECURITY BREACH! The Ani-Ani tore down barricades and charged through the broken {dirName} entrance!");
							return;
						}
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
			bool hasBarrier = IsDirectionFortifiedWithBarrier(CurrentApproachDir);
			string barrierBadge = hasBarrier ? " [BARRIER SLOW]" : "";
			HUDNode?.SetInteractionPrompt($"[!] ANI-ANI AT {CurrentApproachDir.ToString().ToUpper()} ENTRANCE! ({Mathf.CeilToInt(AttackCountdown)}s){barrierBadge} - MONITOR CAM {(int)CurrentApproachDir + 1}!");

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
		if (_nextAttackTimer <= 16f && CurrentStage == StalkingStage.Hidden)
		{
			CurrentApproachDir = (AttackDirection)_rng.Next(0, 4);
			SetStalkingStage(StalkingStage.Distant);
		}
		else if (_nextAttackTimer <= 8f && CurrentStage == StalkingStage.Distant)
		{
			SetStalkingStage(StalkingStage.Approaching);
		}

		if (_nextAttackTimer <= 0f) StartMonsterAttack();
	}

	private void ResetAttack()
	{
		IsMonsterAttacking = false;
		_isTearingPlanks = false;
		_planksTornSoFar = 0;
		if (_isNight && MonsterNode != null)
		{
			int spot = _rng.Next(0, 6);
			MonsterNode.SetLurking(spot);
			CurrentStage = StalkingStage.Hidden;
		}
		else
		{
			SetStalkingStage(StalkingStage.Hidden);
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
			HUDNode?.SetInteractionPrompt($"[!] ANI-ANI AT {dirName}! Shredding barricades ({lockable.PlanksPlaced} planks)! Hold [G] (3s) to reinforce!");
			EmitSignal(SignalName.MonsterAttacking, (int)CurrentApproachDir, dirName, PlankTearInterval * _planksQuotaToTear);
			return;
		}

		_isTearingPlanks = false;
		bool hasBarrier = IsDirectionFortifiedWithBarrier(CurrentApproachDir);
		AttackCountdown = hasBarrier ? (ReactionWindow + BarrierExtraReactionTime) : ReactionWindow;
		EmitSignal(SignalName.MonsterAttacking, (int)CurrentApproachDir, dirName, AttackCountdown);
	}

	public void ResolveMonsterAttack()
	{
		IsMonsterAttacking = false;
		_isTearingPlanks = false;
		string dir = CurrentApproachDir.ToString().ToUpper();

		bool entranceOpen = IsAnyEntranceOpen();
		bool playerNotLooking = IsPlayerNotLooking(CurrentApproachDir);

		if (!entranceOpen && !playerNotLooking)
		{
			// Player actively monitored the CCTV feed! Ani-Ani repelled!
			TriggerMonsterRetreat($"[✓] ANI-ANI REPELLED FROM {dir} BY CCTV MONITORING!");
			return;
		}

		// Bare entrance hit by monster: Lawnmower defense test!
		var lockable = GetOpeningForDirection(CurrentApproachDir);
		if (lockable != null)
		{
			var result = lockable.TakeHitAndCheckLawnmower();
			if (result == LockableOpening.DefenseResult.LawnmowerTriggered)
			{
				TriggerMonsterRetreat($"[!] LAWNMOWER DEFENSE! {dir} entrance shattered repelling Ani-Ani! (0 HP - VULNERABLE!)");
				return;
			}
		}

		// No lawnmower defense remaining: fatal security breach!
		TriggerBreach($"SECURITY BREACH! The Ani-Ani infiltrated through the undefended {dir} entrance!");
	}

	public void TriggerMonsterRetreat(string reason)
	{
		IsMonsterAttacking = false;
		_isTearingPlanks = false;
		_planksTornSoFar = 0;

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

	public bool IsDirectionFortifiedWithBarrier(AttackDirection direction) =>
		GetTree().GetNodesInGroup("BarrierSlots").OfType<InteractableBarrierSlot>().Any(s => s.Direction == direction && s.IsDeployed);
}
