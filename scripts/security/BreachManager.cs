// =========================================================================================
// Original Author : air (Airless-KV) - Oct 2026
// Responsibilities: - Night horror attack state machine and Ani-Ani breach management
//                   - Directional monster approach alerts and CCTV stalking stages (Distant, Approaching, Imminent)
//                   - Entrance security checks (doors/windows) & CCTV monitoring verification
//                   - Barricade fortification absorption and defense resolution
//                   - House attack trauma wobble and shockwave triggers for CameraPivot
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
// - Nov 14, 2026 | air : Triggered house attack trauma shakes on CameraPivot during monster assault and plank tearing
// - Nov 15, 2026 | air : Implemented 4-scenario 3D animated death scenes (CCTV ambush, house breach, wilderness, night wandering)
// - Nov 15, 2026 | air : Aligned look targets with Ani-Ani face/horns (Y = +4.9m), 5.5s pacing, and decoupled blackout
// - Nov 15, 2026 | air : Concealed dormant Ani-Ani staging during Hidden and delay phases to prevent in-view pop-ins
// - Nov 15, 2026 | air : Replaced 7s CCTV stare expulsion with 30% power Siren mechanic scaring Ani-Ani
// - Nov 15, 2026 | air : Positioned Ani-Ani in perimeter loiter spots during night downtime and attack resets
// - Nov 15, 2026 | air : Removed barricades and added 3-second stage 3 stand timer before entity attack
// - Nov 16, 2026 | air : Implemented indoors opening grab and fling death animation out of the attacked window/door
// - Nov 17, 2026 | air : Added silent 3-second outdoor grace period before night jumpscare and aligned Ani-Ani fling orientation
// - Nov 17, 2026 | air : Removed approach warnings so players must spot Ani-Ani manually on CCTV; pull off CCTV into house breach fling
// - Nov 17, 2026 | air : Implemented daily difficulty scaling (Days 1-7) and increased siren consumption to 50% power
// - Nov 17, 2026 | air : Implemented ThreatAlert FIFO Queue and SurveillanceGraph with Breadth-First Search (BFS)
// - Nov 18, 2026 | air : Updated FinishDeathSequence to pass flower tier counts into ShowDefeatSummary for complete score tracking on death
// =========================================================================================
using Godot;
using System;
using System.Collections.Generic;
using System.Linq;

public partial class BreachManager : Node
{
	[Signal] public delegate void MonsterAttackingEventHandler(int direction, string dirName, float timeToReact);
	[Signal] public delegate void MonsterRepelledEventHandler(int direction, string dirName);
	[Signal] public delegate void BreachOccurredEventHandler(string reason);
	[Signal] public delegate void StalkingStageChangedEventHandler(int direction, int stage);

	public enum AttackDirection { North = 0, East = 1, South = 2, West = 3 }
	public enum StalkingStage { Hidden = 0, Distant = 1, Approaching = 2, Imminent = 3 }
	public enum DeathScenario { HouseBreach = 0, CCTVAmbush = 1, WildernessConsumption = 2, CaughtOutsideNight = 3 }

	[ExportGroup("Dependencies")]
	[Export] public GameManager GameManagerNode;
	[Export] public NighttCamera NightCameraNode;
	[Export] public PlayerController PlayerNode;
	[Export] public CameraPivot CameraPivotNode;
	[Export] public Node3d GridManagerNode;
	[Export] public JumpScareScreen JumpScareScreenNode;
	[Export] public VictoryScreen VictoryScreenNode;
	[Export] public HUDManager HUDNode;
	[Export] public AniAniMonster MonsterNode;

	[ExportGroup("Monster Attack Settings")]
	[Export] public float FirstAttackDelay = 42.0f;
	[Export] public float AttackInterval = 25.0f;
	[Export] public float ReactionWindow = 7.0f;
	[Export] public float DistantStageDuration = 15.0f;
	[Export] public float ApproachingStageDuration = 15.0f;
	[Export] public float Stage3StandDuration = 3.0f;
	[Export] public bool EnableOutdoorLoss = true;

	[ExportGroup("Assault Cooldowns")]
	[Export(PropertyHint.Range, "15.0,90.0,1.0")] public float MinRetreatCooldown = 42.0f;
	[Export(PropertyHint.Range, "30.0,240.0,1.0")] public float MaxRetreatCooldown = 95.0f;

	public bool IsMonsterAttacking { get; private set; }
	public AttackDirection CurrentApproachDir { get; private set; }
	public StalkingStage CurrentStage { get; private set; }
	public float AttackCountdown { get; private set; }

	// =========================================================================================
	// ITEC 104: THREAT ALERT FIFO QUEUE
	// =========================================================================================
	public readonly record struct ThreatAlert(string Message, AttackDirection Direction, StalkingStage Stage, ulong TimestampMsec);
	private readonly Queue<ThreatAlert> _alertQueue = new();
	private float _alertProcessTimer = 0f;

	public void EnqueueThreatAlert(string message, AttackDirection dir, StalkingStage stage)
	{
		_alertQueue.Enqueue(new ThreatAlert(message, dir, stage, Time.GetTicksMsec()));
	}

	// =========================================================================================
	// ITEC 104: SURVEILLANCE GRAPH & BREADTH-FIRST SEARCH (BFS)
	// =========================================================================================
	public class SurveillanceGraph
	{
		private readonly Dictionary<string, List<string>> _adjacencyList = new();

		public void AddEdge(string u, string v)
		{
			if (!_adjacencyList.ContainsKey(u)) _adjacencyList[u] = new List<string>();
			if (!_adjacencyList.ContainsKey(v)) _adjacencyList[v] = new List<string>();
			_adjacencyList[u].Add(v);
			_adjacencyList[v].Add(u);
		}

		/// <summary>
		/// Breadth-First Search (BFS) to compute the shortest threat traversal between surveillance zones.
		/// Time Complexity: O(V + E) where V is surveillance zones and E is connecting paths.
		/// Space Complexity: O(V) for visited set and queue storage.
		/// </summary>
		public List<string> FindShortestThreatPath(string startNode, string targetNode)
		{
			var visited = new HashSet<string>();
			var parent = new Dictionary<string, string>();
			var queue = new Queue<string>();

			queue.Enqueue(startNode);
			visited.Add(startNode);

			bool found = false;
			while (queue.Count > 0)
			{
				string current = queue.Dequeue();
				if (current == targetNode)
				{
					found = true;
					break;
				}

				if (_adjacencyList.TryGetValue(current, out var neighbors))
				{
					foreach (var neighbor in neighbors)
					{
						if (!visited.Contains(neighbor))
						{
							visited.Add(neighbor);
							parent[neighbor] = current;
							queue.Enqueue(neighbor);
						}
					}
				}
			}

			if (!found) return new List<string>();

			var path = new List<string>();
			string curr = targetNode;
			while (curr != null)
			{
				path.Add(curr);
				parent.TryGetValue(curr, out curr);
			}
			path.Reverse();
			return path;
		}
	}

	private readonly SurveillanceGraph _surveillanceGraph = new();

	private float _nextAttackTimer;
	private bool _isNight;
	private bool _isDeathSequenceRunning;
	private float _outdoorTimer;
	private readonly Random _rng = new();

	private T Find<T>(string name) where T : class => GetTree().Root.FindChild(name, true, false) as T;

	public void ApplyDailyDifficultyScaling(int day)
	{
		int d = Mathf.Clamp(day, 1, 7);
		float t = (d - 1) / 6.0f;

		FirstAttackDelay = Mathf.Lerp(45.0f, 18.0f, t);
		MinRetreatCooldown = Mathf.Lerp(45.0f, 18.0f, t);
		MaxRetreatCooldown = Mathf.Lerp(75.0f, 30.0f, t);
		DistantStageDuration = Mathf.Lerp(13.0f, 8.0f, t);
		ApproachingStageDuration = Mathf.Lerp(13.0f, 8.0f, t);
		Stage3StandDuration = Mathf.Lerp(3.5f, 2.2f, t);
	}

	public override void _Ready()
	{
		GameManagerNode ??= Find<GameManager>("GameManager");
		NightCameraNode ??= Find<NighttCamera>("NighttCamera");
		PlayerNode ??= Find<PlayerController>("Player");
		CameraPivotNode ??= Find<CameraPivot>("CameraPivot");
		GridManagerNode ??= Find<Node3d>("GridManager");
		JumpScareScreenNode ??= Find<JumpScareScreen>("JumpScareScreen");
		VictoryScreenNode ??= Find<VictoryScreen>("VictoryScreen");
		HUDNode ??= Find<HUDManager>("HUD");
		MonsterNode ??= Find<AniAniMonster>("AniAniMonster");

		if (GameManagerNode != null)
		{
			_isNight = GameManagerNode.CurrentPhase == GameManager.GamePhase.Night;
			ApplyDailyDifficultyScaling(GameManagerNode.CurrentDay);
			GameManagerNode.PhaseChanged += (isDay, day) =>
			{
				_isNight = !isDay;
				ApplyDailyDifficultyScaling(day);
				ResetAttack();
				if (_isNight)
				{
					_nextAttackTimer = FirstAttackDelay;
					MonsterNode?.SetLurking(_rng.Next(0, 12));
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

		// Initialize Surveillance Graph edges (ITEC 104)
		_surveillanceGraph.AddEdge("Perimeter_North", "CCTV_North");
		_surveillanceGraph.AddEdge("Perimeter_South", "CCTV_South");
		_surveillanceGraph.AddEdge("Perimeter_East", "CCTV_East");
		_surveillanceGraph.AddEdge("Perimeter_West", "CCTV_West");
		_surveillanceGraph.AddEdge("CCTV_North", "Garden_Meadow");
		_surveillanceGraph.AddEdge("CCTV_South", "Garden_Meadow");
		_surveillanceGraph.AddEdge("CCTV_East", "Garden_Meadow");
		_surveillanceGraph.AddEdge("CCTV_West", "Garden_Meadow");
		_surveillanceGraph.AddEdge("Garden_Meadow", "House_Entrance_North");
		_surveillanceGraph.AddEdge("Garden_Meadow", "House_Entrance_South");
		_surveillanceGraph.AddEdge("Garden_Meadow", "House_Entrance_East");
		_surveillanceGraph.AddEdge("Garden_Meadow", "House_Entrance_West");
	}

	public override void _Process(double delta)
	{
		if (_isDeathSequenceRunning || GameManagerNode?.IsGameFinished == true || !_isNight) return;
		float dt = (float)delta;

		_alertProcessTimer -= dt;
		if (_alertProcessTimer <= 0f && _alertQueue.Count > 0)
		{
			var alert = _alertQueue.Dequeue();
			_alertProcessTimer = 1.2f;
		}

		if (EnableOutdoorLoss && PlayerNode != null && !PlayerNode.IsIndoors)
		{
			_outdoorTimer += dt;
			if (_outdoorTimer >= 3.0f)
			{
				TriggerBreach("CAUGHT OUTSIDE! You stepped outside after sundown into the Ani-Ani's hunting grounds!", DeathScenario.CaughtOutsideNight);
				return;
			}
		}
		else
		{
			_outdoorTimer = 0f;
		}

		if (IsMonsterAttacking)
		{
			AttackCountdown -= dt;
			string dirName = CurrentApproachDir.ToString().ToUpper();
			HUDNode?.SetInteractionPrompt($"[!] ANI-ANI AT {dirName} ENTRANCE! ({Mathf.CeilToInt(AttackCountdown)}s) - BLAST SIREN OR FLOODLIGHT!");
			(CameraPivotNode ?? CameraPivot.Instance)?.AddTrauma(0.50f * dt);

			if (AttackCountdown <= 0f)
			{
				ResolveMonsterAttack();
			}
			return;
		}

		_nextAttackTimer -= dt;
		float totalApproachTime = DistantStageDuration + ApproachingStageDuration;
		if (_nextAttackTimer <= totalApproachTime && CurrentStage == StalkingStage.Hidden)
		{
			AttackDirection nextDir = (AttackDirection)_rng.Next(0, 4);
			if (GameManagerNode != null && GameManagerNode.CurrentDay >= 4 && nextDir == CurrentApproachDir)
			{
				nextDir = (AttackDirection)(((int)CurrentApproachDir + _rng.Next(1, 4)) % 4);
			}
			CurrentApproachDir = nextDir;
			var threatPath = _surveillanceGraph.FindShortestThreatPath($"Perimeter_{CurrentApproachDir}", $"House_Entrance_{CurrentApproachDir}");
			SetStalkingStage(StalkingStage.Distant);
		}
		else if (_nextAttackTimer <= ApproachingStageDuration && CurrentStage == StalkingStage.Distant)
		{
			SetStalkingStage(StalkingStage.Approaching);
		}

		if (_nextAttackTimer <= 0f) StartMonsterAttack();
	}

	private void ResetAttack()
	{
		IsMonsterAttacking = false;
		AttackCountdown = 0f;
		CurrentStage = StalkingStage.Hidden;
		if (_isNight)
		{
			MonsterNode?.SetLurking(_rng.Next(0, 12));
		}
		else
		{
			MonsterNode?.Deactivate();
		}
		EmitSignal(SignalName.StalkingStageChanged, (int)CurrentApproachDir, (int)CurrentStage);
	}

	private void SetStalkingStage(StalkingStage stage)
	{
		CurrentStage = stage;
		EnqueueThreatAlert($"Ani-Ani threat stage transitioned to {stage} from {CurrentApproachDir}", CurrentApproachDir, stage);
		if (stage == StalkingStage.Hidden)
		{
			if (_isNight) MonsterNode?.SetLurking(_rng.Next(0, 12));
			else MonsterNode?.Deactivate();
		}
		else
		{
			MonsterNode?.SetStage((int)CurrentApproachDir, (int)CurrentStage);
		}
		EmitSignal(SignalName.StalkingStageChanged, (int)CurrentApproachDir, (int)CurrentStage);
	}

	public void StartMonsterAttack()
	{
		if (!_isNight || IsMonsterAttacking) return;
		IsMonsterAttacking = true;
		AttackCountdown = Stage3StandDuration;

		SetStalkingStage(StalkingStage.Imminent);

		string dirName = CurrentApproachDir.ToString().ToUpper();
		AudioManager.Sfx("warning_stinger");
		(CameraPivotNode ?? CameraPivot.Instance)?.AddTrauma(0.65f);

		HUDNode?.SetInteractionPrompt($"[!] ANI-ANI AT {dirName} ENTRANCE! ({Mathf.CeilToInt(AttackCountdown)}s) - BLAST SIREN OR FLOODLIGHT!");
		EmitSignal(SignalName.MonsterAttacking, (int)CurrentApproachDir, dirName, Stage3StandDuration);
	}

	public void ResolveMonsterAttack()
	{
		IsMonsterAttacking = false;
		string dir = CurrentApproachDir.ToString().ToUpper();

		TriggerBreach($"SECURITY BREACH! The Ani-Ani attacked through the {dir} entrance after standing outside!", DeathScenario.HouseBreach);
	}

	public void TriggerMonsterRetreat(string reason)
	{
		IsMonsterAttacking = false;
		AttackCountdown = 0f;

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

	public bool RepelBySiren()
	{
		if (!_isNight) return false;
		if (CurrentStage == StalkingStage.Hidden && !IsMonsterAttacking) return false;

		string dirName = CurrentApproachDir.ToString().ToUpper();
		TriggerMonsterRetreat($"[✓] SIREN BLASTED! ANI-ANI AT {dirName} SCARED AWAY INTO THE WOODS!");
		return true;
	}

	public bool ActivateSiren(out string message)
	{
		var station = InteractableBatteryStation.GetStation(PowerSystemType.Siren)
			?? InteractableBatteryStation.Instance;
		if (station == null || !station.HasPower || station.BatteryHealth < 50)
		{
			int currentHp = station != null && station.HasBattery ? station.BatteryHealth : 0;
			message = $"[!] INSUFFICIENT POWER! Siren requires 50% battery power (Current: {currentHp}%).";
			AudioManager.Sfx("window_rattle", -2f);
			return false;
		}

		station.ConsumePower(50);
		AudioManager.Sfx("siren");
		RoofSiren.Instance?.Activate(2.5f);

		var cctv = GetTree().Root.FindChild("CCTVOverlay", true, false) as CCTVOverlay;
		cctv?.TriggerCameraGlitch(0.45f);

		if (RepelBySiren())
		{
			message = "[✓] SIREN BLASTED (-50% PWR)! ANI-ANI SCARED AWAY INTO THE WOODS!";
		}
		else
		{
			message = "[!] SIREN BLASTED (-50% PWR)! No active Ani-Ani threat in range.";
		}

		HUDNode?.SetInteractionPrompt(message);
		return true;
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
		DeathScenario scenario = DeathScenario.HouseBreach;
		if (PlayerNode != null && !PlayerNode.IsIndoors)
		{
			scenario = DeathScenario.CaughtOutsideNight;
		}

		TriggerBreach(reason, scenario);
	}

	public void TriggerBreach(string reason, DeathScenario scenario, Vector3? stalkerPos = null)
	{
		if (_isDeathSequenceRunning) return;
		_isDeathSequenceRunning = true;
		_isNight = false;
		ResetAttack();
		EmitSignal(SignalName.BreachOccurred, reason);

		// If viewing CCTV monitor, immediately pull player off CCTV so normal house breach fling plays
		if (NightCameraNode != null && NightCameraNode.IsNightMode)
		{
			NightCameraNode.ApplyMode(false);
			if (scenario == DeathScenario.CCTVAmbush)
			{
				scenario = DeathScenario.HouseBreach;
			}
		}

		// Lock player controls during cinematic death sequence
		if (PlayerNode != null)
		{
			PlayerNode.IsMovementLocked = true;
		}

		HUDNode?.ClearInteractionPrompt();
		JumpScareScreenNode?.StartDeathVisuals();

		switch (scenario)
		{
			case DeathScenario.CCTVAmbush:
				PlayCCTVAmbushSequence(reason);
				break;
			case DeathScenario.WildernessConsumption:
				PlayWildernessConsumptionSequence(reason, stalkerPos);
				break;
			case DeathScenario.CaughtOutsideNight:
				PlayCaughtOutsideSequence(reason);
				break;
			case DeathScenario.HouseBreach:
			default:
				PlayHouseBreachSequence(reason);
				break;
		}
	}

	private void PlayCCTVAmbushSequence(string reason)
	{
		// Force CCTV feed to collapse back into 1st person view
		if (NightCameraNode != null && NightCameraNode.IsNightMode)
		{
			NightCameraNode.ApplyMode(false);
		}

		Vector3 cctvStationPos = new Vector3(-1.66f, 4.6f, -2.6f);
		Vector3 ambushSpawnPos = cctvStationPos + new Vector3(0, 0, 1.8f);
		Vector3 playerPos = PlayerNode != null ? PlayerNode.GlobalPosition : (CameraPivotNode?.GlobalPosition ?? cctvStationPos);

		if (MonsterNode != null)
		{
			MonsterNode.GlobalPosition = ambushSpawnPos;
			MonsterNode.RotationDegrees = new Vector3(0, 180f, 0);
		}

		// Point directly at the monster's face / glowing red eyes (Y = +4.9m above feet)
		Vector3 lookTarget = MonsterNode != null ? MonsterNode.GetHeadPosition() : ambushSpawnPos + Vector3.Up * 4.9f;

		AudioManager.Sfx("glass_shatter");
		AudioManager.Sfx("warning_stinger");

		CameraPivotNode?.PlayDeathSequence(
			lookAtTarget: lookTarget,
			targetRollDegrees: 45f,
			targetHeightDrop: 1.25f,
			snapDuration: 0.28f,
			onLungeImpact: () =>
			{
				AudioManager.Sfx("door_pound");
				AudioManager.Sfx("jumpscare_stinger");
				JumpScareScreenNode?.TriggerImpactFlash();
			},
			onComplete: () =>
			{
				JumpScareScreenNode?.TriggerFadeToBlackout(1.0f, () =>
				{
					FinishDeathSequence(reason);
				});
			}
		);

		Vector3 lungeTarget = playerPos + new Vector3(0, 0, 0.45f);
		MonsterNode?.PlayDeathLunge(
			spawnPos: ambushSpawnPos,
			targetPos: lungeTarget,
			targetYaw: 180f,
			lungeDuration: 1.25f,
			onPeak: null,
			onComplete: null
		);
	}

	private void PlayHouseBreachSequence(string reason)
	{
		Vector3 breachPos = CurrentApproachDir switch
		{
			AttackDirection.North => new Vector3(-1.66f, 4.6f, -3.75f),
			AttackDirection.East => new Vector3(2.09f, 4.6f, 0.0f),
			AttackDirection.South => new Vector3(-1.66f, 4.6f, 3.75f),
			AttackDirection.West => new Vector3(-5.41f, 4.6f, 0.0f),
			_ => new Vector3(-1.66f, 4.6f, -3.75f)
		};

		Vector3 outdoorOffset = CurrentApproachDir switch
		{
			AttackDirection.North => new Vector3(0, 0, -10.5f),
			AttackDirection.East => new Vector3(10.5f, 0, 0),
			AttackDirection.South => new Vector3(0, 0, 10.5f),
			AttackDirection.West => new Vector3(-10.5f, 0, 0),
			_ => new Vector3(0, 0, -10.5f)
		};

		Vector3 playerPos = PlayerNode != null ? PlayerNode.GlobalPosition : (CameraPivotNode?.GlobalPosition ?? Vector3.Zero);
		bool isIndoors = PlayerNode != null && PlayerNode.IsIndoors;

		// Check bounding box as safety fallback for indoors check
		if (!isIndoors && PlayerNode != null)
		{
			Vector3 diff = (PlayerNode.GlobalPosition - PlayerNode.HouseCenter).Abs();
			isIndoors = diff.X <= PlayerNode.HouseExtents.X && diff.Z <= PlayerNode.HouseExtents.Z && diff.Y <= PlayerNode.HouseExtents.Y;
		}

		if (isIndoors)
		{
			PlayHouseFlingSequence(reason, breachPos, outdoorOffset);
			return;
		}

		// Fallback for when player is already outside near the house during breach
		Vector3 toPlayer = (playerPos - breachPos).Normalized();
		float monsterYaw = Mathf.RadToDeg(Mathf.Atan2(toPlayer.X, toPlayer.Z));

		if (MonsterNode != null)
		{
			MonsterNode.GlobalPosition = breachPos;
			MonsterNode.RotationDegrees = new Vector3(0, monsterYaw, 0);
		}

		// Point directly at the monster's face / glowing red eyes (Y = +4.9m above feet)
		Vector3 lookTarget = MonsterNode != null ? MonsterNode.GetHeadPosition() : breachPos + Vector3.Up * 4.9f;
		Vector3 lungeTarget = playerPos - toPlayer * 0.95f;
		lungeTarget.Y = playerPos.Y;

		AudioManager.Sfx("window_rattle");
		AudioManager.Sfx("warning_stinger");

		CameraPivotNode?.PlayDeathSequence(
			lookAtTarget: lookTarget,
			targetRollDegrees: -35f,
			targetHeightDrop: 1.20f,
			snapDuration: 0.32f,
			onLungeImpact: () =>
			{
				AudioManager.Sfx("door_pound");
				AudioManager.Sfx("jumpscare_stinger");
				JumpScareScreenNode?.TriggerImpactFlash();
			},
			onComplete: () =>
			{
				JumpScareScreenNode?.TriggerFadeToBlackout(1.0f, () =>
				{
					FinishDeathSequence(reason);
				});
			}
		);

		MonsterNode?.PlayDeathLunge(
			spawnPos: breachPos,
			targetPos: lungeTarget,
			targetYaw: monsterYaw,
			lungeDuration: 1.35f,
			onPeak: null,
			onComplete: null
		);
	}

	private void PlayHouseFlingSequence(string reason, Vector3 openingPos, Vector3 outdoorOffset)
	{
		Vector3 playerPos = PlayerNode != null ? PlayerNode.GlobalPosition : (CameraPivotNode?.GlobalPosition ?? Vector3.Zero);

		Vector3 outdoorLandingPos = openingPos + outdoorOffset;
		outdoorLandingPos.Y = 1.25f; // Ground level in the yard outside

		Vector3 toOpening = (openingPos - playerPos).Normalized();
		float openingYaw = Mathf.RadToDeg(Mathf.Atan2(toOpening.X, toOpening.Z));

		if (MonsterNode != null)
		{
			MonsterNode.GlobalPosition = openingPos;
			MonsterNode.RotationDegrees = new Vector3(0, openingYaw, 0);
		}

		Vector3 lookTarget = MonsterNode != null ? MonsterNode.GetHeadPosition() : openingPos + Vector3.Up * 4.9f;

		AudioManager.Sfx("window_rattle");
		AudioManager.Sfx("warning_stinger");

		CameraPivotNode?.PlayHouseFlingSequence(
			lookAtHeadPos: lookTarget,
			openingPos: openingPos,
			outdoorLandingPos: outdoorLandingPos,
			targetRollDegrees: -38f,
			onGrabbed: () =>
			{
				AudioManager.Sfx("door_pound");
			},
			onFlingRelease: () =>
			{
				AudioManager.Sfx("glass_shatter");
				AudioManager.Sfx("warning_stinger");
			},
			onGroundImpact: () =>
			{
				AudioManager.Sfx("door_pound");
				AudioManager.Sfx("jumpscare_stinger");
				JumpScareScreenNode?.TriggerImpactFlash();
			},
			onComplete: () =>
			{
				JumpScareScreenNode?.TriggerFadeToBlackout(1.0f, () =>
				{
					FinishDeathSequence(reason);
				});
			}
		);

		MonsterNode?.PlayDeathHouseFling(
			breachOpeningPos: openingPos,
			playerInsidePos: playerPos,
			outdoorLandingPos: outdoorLandingPos,
			openingYaw: openingYaw,
			onFlingThrow: () =>
			{
				// Callback when monster thrusts forward throwing player outside
			},
			onComplete: null
		);
	}

	private void PlayWildernessConsumptionSequence(string reason, Vector3? stalkerPos)
	{
		Vector3 playerPos = PlayerNode != null ? PlayerNode.GlobalPosition : (CameraPivotNode?.GlobalPosition ?? Vector3.Zero);
		Vector3 spawnPos = stalkerPos ?? (playerPos + (CameraPivotNode != null ? CameraPivotNode.GlobalBasis.Z * 11f : Vector3.Back * 11f));
		spawnPos.Y = playerPos.Y;

		Vector3 toPlayer = (playerPos - spawnPos).Normalized();
		float monsterYaw = Mathf.RadToDeg(Mathf.Atan2(toPlayer.X, toPlayer.Z));

		if (MonsterNode != null)
		{
			MonsterNode.GlobalPosition = spawnPos;
			MonsterNode.RotationDegrees = new Vector3(0, monsterYaw, 0);
		}

		// Point directly at the monster's face / glowing red eyes (Y = +4.9m above feet)
		Vector3 lookTarget = MonsterNode != null ? MonsterNode.GetHeadPosition() : spawnPos + Vector3.Up * 4.9f;
		Vector3 lungeTarget = playerPos - toPlayer * 0.95f;
		lungeTarget.Y = playerPos.Y;

		AudioManager.Sfx("foliage_rustle");
		AudioManager.Sfx("monster_footsteps");

		CameraPivotNode?.PlayDeathSequence(
			lookAtTarget: lookTarget,
			targetRollDegrees: 30f,
			targetHeightDrop: 1.25f,
			snapDuration: 0.38f,
			onLungeImpact: () =>
			{
				AudioManager.Sfx("jumpscare_stinger");
				JumpScareScreenNode?.TriggerImpactFlash();
			},
			onComplete: () =>
			{
				JumpScareScreenNode?.TriggerFadeToBlackout(1.0f, () =>
				{
					FinishDeathSequence(reason);
				});
			}
		);

		MonsterNode?.PlayDeathLunge(
			spawnPos: spawnPos,
			targetPos: lungeTarget,
			targetYaw: monsterYaw,
			lungeDuration: 1.45f,
			onPeak: null,
			onComplete: null
		);
	}

	private void PlayCaughtOutsideSequence(string reason)
	{
		Vector3 playerPos = PlayerNode != null ? PlayerNode.GlobalPosition : (CameraPivotNode?.GlobalPosition ?? Vector3.Zero);
		Vector3 playerFwd = PlayerNode != null ? -PlayerNode.GlobalTransform.Basis.Z : Vector3.Forward;

		Vector3 spawnPos = playerPos + playerFwd * 4.5f;
		spawnPos.Y = playerPos.Y;
		Vector3 toPlayer = (playerPos - spawnPos).Normalized();
		float monsterYaw = Mathf.RadToDeg(Mathf.Atan2(toPlayer.X, toPlayer.Z));

		if (MonsterNode != null)
		{
			MonsterNode.GlobalPosition = spawnPos;
			MonsterNode.RotationDegrees = new Vector3(0, monsterYaw, 0);
		}

		// Point directly at the monster's face / glowing red eyes (Y = +4.9m above feet)
		Vector3 lookTarget = MonsterNode != null ? MonsterNode.GetHeadPosition() : spawnPos + Vector3.Up * 4.9f;
		Vector3 lungeTarget = playerPos - toPlayer * 0.95f;
		lungeTarget.Y = playerPos.Y;

		AudioManager.Sfx("warning_stinger");

		CameraPivotNode?.PlayDeathSequence(
			lookAtTarget: lookTarget,
			targetRollDegrees: -40f,
			targetHeightDrop: 1.20f,
			snapDuration: 0.30f,
			onLungeImpact: () =>
			{
				AudioManager.Sfx("door_pound");
				AudioManager.Sfx("jumpscare_stinger");
				JumpScareScreenNode?.TriggerImpactFlash();
			},
			onComplete: () =>
			{
				JumpScareScreenNode?.TriggerFadeToBlackout(1.0f, () =>
				{
					FinishDeathSequence(reason);
				});
			}
		);

		MonsterNode?.PlayDeathLunge(
			spawnPos: spawnPos,
			targetPos: lungeTarget,
			targetYaw: monsterYaw,
			lungeDuration: 1.35f,
			onPeak: null,
			onComplete: null
		);
	}

	private void FinishDeathSequence(string reason)
	{
		var flowerCounts = GridManagerNode?.GetAllFlowerTierCounts() 
			?? Node3d.CollectAllFlowerCounts(GetTree());

		JumpScareScreenNode?.ShowDefeatSummary(
			reason,
			GameManagerNode?.CurrentDay ?? 1,
			GameManagerNode?.Coins ?? 0,
			flowerCounts
		);
	}

	public bool IsAnyEntranceOpen() =>
		GetTree().GetNodesInGroup("Entrances").OfType<InteractableEntrance>().Any(e => e.IsOpen);

	public bool IsPlayerNotLooking(AttackDirection direction) =>
		NightCameraNode == null || !NightCameraNode.IsNightMode || NightCameraNode.CurrentNightIndex != (int)direction;
}
