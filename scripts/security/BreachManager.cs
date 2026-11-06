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

	public bool IsMonsterAttacking { get; private set; }
	public AttackDirection CurrentApproachDir { get; private set; }
	public StalkingStage CurrentStage { get; private set; }
	public float AttackCountdown { get; private set; }

	private float _nextAttackTimer;
	private bool _isNight;
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
				if (_isNight) _nextAttackTimer = FirstAttackDelay;
				else HUDNode?.ClearInteractionPrompt();
			};
			GameManagerNode.GameCompleted += () =>
			{
				_isNight = false;
				ResetAttack();
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
			AttackCountdown -= dt;
			bool hasBarrier = IsDirectionFortifiedWithBarrier(CurrentApproachDir);
			string barrierBadge = hasBarrier ? " [BARRIER SLOW]" : "";
			HUDNode?.SetInteractionPrompt($"[!] ANI-ANI AT {CurrentApproachDir.ToString().ToUpper()} ENTRANCE! ({Mathf.CeilToInt(AttackCountdown)}s){barrierBadge} - MONITOR CAM {(int)CurrentApproachDir + 1}!");
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
		SetStalkingStage(StalkingStage.Hidden);
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

		bool hasBarrier = IsDirectionFortifiedWithBarrier(CurrentApproachDir);
		AttackCountdown = hasBarrier ? (ReactionWindow + BarrierExtraReactionTime) : ReactionWindow;
		SetStalkingStage(StalkingStage.Imminent);

		string dirName = CurrentApproachDir.ToString().ToUpper();
		AudioManager.Sfx("warning_stinger");
		EmitSignal(SignalName.MonsterAttacking, (int)CurrentApproachDir, dirName, AttackCountdown);
	}

	public void ResolveMonsterAttack()
	{
		IsMonsterAttacking = false;
		string dir = CurrentApproachDir.ToString().ToUpper();

		bool entranceOpen = IsAnyEntranceOpen();
		bool playerNotLooking = IsPlayerNotLooking(CurrentApproachDir);

		if (entranceOpen || playerNotLooking)
		{
		if (entranceOpen)
		{
			TriggerBreach($"SECURITY BREACH! The Ani-Ani infiltrated through an unsecured entrance on the {dir} perimeter!");
			return;
		}

		if (playerNotLooking)
		{
			if (TryDelayBreach(CurrentApproachDir))
			{
				HUDNode?.SetInteractionPrompt($"[!] BARRICADE/LOCK DELAYED ATTACK! Planks damaged on {dir} perimeter!");
				return;
			}

			// Check if any entrance is fortified with wooden planks to absorb the breach
			var barricade = GetTree().GetNodesInGroup("Entrances").OfType<InteractableEntrance>().FirstOrDefault(e => e.IsBarricaded);
			if (barricade != null)
			{
				barricade.AbsorbBreachWithBarricade();
				HUDNode?.SetInteractionPrompt($"[!] BARRICADE SAVED YOU! Wooden planks shattered on {dir} perimeter!");
				SaveManager.RecordAttackRepelled();
				CurrentStage = StalkingStage.Hidden;
				MonsterNode?.PlayRepelEffect();
				_nextAttackTimer = AttackInterval;
				return;
			}

			TriggerBreach($"UNNOTICED ATTACK! You were not watching the {dir} CCTV Camera (Cam {(int)CurrentApproachDir + 1}) when the Ani-Ani struck!");
			return;
		}
		}
		else
		{
			EmitSignal(SignalName.MonsterRepelled, (int)CurrentApproachDir, dir);
			HUDNode?.SetInteractionPrompt($"[✓] ANI-ANI REPELLED FROM {dir}!");
			SaveManager.RecordAttackRepelled();
			CurrentStage = StalkingStage.Hidden;
			MonsterNode?.PlayRepelEffect();
			_nextAttackTimer = AttackInterval;
		}
	}

	// Kapag may lock o plank ang pinakamahinang entrance sa direksyong inaatake,
	// sisirain ng halimaw ang isa at maaantala ang breach.
	private bool TryDelayBreach(AttackDirection dir)
	{
		var weakest = GetTree().GetNodesInGroup("Lockables")
			.OfType<LockableOpening>()
			.Where(o => o.AttackDirection == (int)dir)
			.OrderBy(o => o.DefenseLevel)
			.FirstOrDefault();

		if (weakest == null || weakest.DefenseLevel <= 0)
			return false;

		weakest.TryBreach();
		IsMonsterAttacking = true;
		AttackCountdown = BarricadeDelay;
		return true;
	}

	public void TriggerBreach(string reason)
	{
		_isNight = false;
		ResetAttack();
		EmitSignal(SignalName.BreachOccurred, reason);
		JumpScareScreenNode?.TriggerJumpScare(reason, GameManagerNode?.CurrentDay ?? 1, GameManagerNode?.Coins ?? 0);
	}

	// BAGO: ang bukas na entrance na may plank ay hindi na bibilangin bilang bukas.
	public bool IsAnyEntranceOpen() =>
		GetTree().GetNodesInGroup("Entrances").OfType<InteractableEntrance>().Any(e => e.IsOpen && !IsBarricaded(e));

	// BAGO
	private bool IsBarricaded(InteractableEntrance entrance) =>
		GetTree().GetNodesInGroup("Lockables").OfType<LockableOpening>().Any(o => o.Entrance == entrance && o.PlanksPlaced > 0);

	public bool IsPlayerNotLooking(AttackDirection direction) =>
		NightCameraNode == null || !NightCameraNode.IsNightMode || NightCameraNode.CurrentNightIndex != (int)direction;

	public bool IsDirectionFortifiedWithBarrier(AttackDirection direction) =>
		GetTree().GetNodesInGroup("BarrierSlots").OfType<InteractableBarrierSlot>().Any(s => s.Direction == direction && s.IsDeployed);
}
