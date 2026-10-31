// =========================================================================================
// Original Author : air (Airless-KV) - Oct 2026
// Responsibilities: - Night horror attack state machine and Ani-Ani breach management
//                   - Directional monster approach alerts and CCTV stalking stages (Distant, Approaching, Imminent)
//                   - Entrance security checks (doors/windows) & CCTV monitoring verification
//                   - Audio warning stinger generation & defeat trigger
// -----------------------------------------------------------------------------------------
// Change Log:
// - Oct 24, 2026 | air : Streamlined breach state machine, entrance Linq queries, and audio stinger for Alpha build
// - Oct 30, 2026 | air : Integrated SaveManager to record repelled attacks in persistent profile stats
// - Oct 30, 2026 | air : Added progressive CCTV stalking stages across directional feeds with AniAniMonster
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
	[Export] public bool EnableOutdoorLoss = true;

	public bool IsMonsterAttacking { get; private set; } = false;
	public AttackDirection CurrentApproachDir { get; private set; } = AttackDirection.North;
	public StalkingStage CurrentStage { get; private set; } = StalkingStage.Hidden;
	public float AttackCountdown { get; private set; } = 0.0f;

	private float _nextAttackTimer = 0.0f;
	private bool _isNight = false;
	private Random _rng = new();
	private AudioStreamPlayer _stingerPlayer;

	public override void _Ready()
	{
		GameManagerNode ??= GetTree().Root.FindChild("GameManager", true, false) as GameManager;
		NightCameraNode ??= GetTree().Root.FindChild("NighttCamera", true, false) as NighttCamera;
		PlayerNode ??= GetTree().Root.FindChild("Player", true, false) as PlayerController;
		GridManagerNode ??= GetTree().Root.FindChild("GridManager", true, false) as Node3d;
		JumpScareScreenNode ??= GetTree().Root.FindChild("JumpScareScreen", true, false) as JumpScareScreen;
		VictoryScreenNode ??= GetTree().Root.FindChild("VictoryScreen", true, false) as VictoryScreen;
		HUDNode ??= GetTree().Root.FindChild("HUD", true, false) as HUDManager;
		MonsterNode ??= GetTree().Root.FindChild("AniAniMonster", true, false) as AniAniMonster;

		if (GameManagerNode != null)
		{
			GameManagerNode.PhaseChanged += OnPhaseChanged;
			GameManagerNode.GameCompleted += OnGameCompleted;
		}

		_stingerPlayer = new AudioStreamPlayer { Name = "WarningStinger" };
		AddChild(_stingerPlayer);

		SetStalkingStage(StalkingStage.Hidden);
	}

	public override void _Process(double delta)
	{
		if ((GameManagerNode != null && GameManagerNode.IsGameFinished) || !_isNight)
			return;

		float dt = (float)delta;

		if (EnableOutdoorLoss && PlayerNode != null && !PlayerNode.IsIndoors)
		{
			TriggerBreach("CAUGHT OUTSIDE! You stepped outside after sundown into the Ani-Ani's hunting grounds!");
			return;
		}

		if (IsMonsterAttacking)
		{
			AttackCountdown -= dt;
			HUDNode?.SetInteractionPrompt($"[!] ANI-ANI AT {CurrentApproachDir.ToString().ToUpper()} ENTRANCE! ({Mathf.CeilToInt(AttackCountdown)}s) - MONITOR CAM {(int)CurrentApproachDir + 1}!");

			if (AttackCountdown <= 0.0f)
				ResolveMonsterAttack();
			return;
		}

		_nextAttackTimer -= dt;

		// Progressive CCTV Stalking Stages
		if (_nextAttackTimer <= 16.0f && CurrentStage == StalkingStage.Hidden)
		{
			CurrentApproachDir = (AttackDirection)_rng.Next(0, 4);
			SetStalkingStage(StalkingStage.Distant);
		}
		else if (_nextAttackTimer <= 8.0f && CurrentStage == StalkingStage.Distant)
		{
			SetStalkingStage(StalkingStage.Approaching);
		}

		if (_nextAttackTimer <= 0.0f)
		{
			StartMonsterAttack();
		}
	}

	private void SetStalkingStage(StalkingStage stage)
	{
		CurrentStage = stage;
		MonsterNode?.SetStage((int)CurrentApproachDir, (int)CurrentStage);
		EmitSignal(SignalName.StalkingStageChanged, (int)CurrentApproachDir, (int)CurrentStage);
	}

	private void OnPhaseChanged(bool isDay, int currentDay)
	{
		_isNight = !isDay;
		IsMonsterAttacking = false;
		SetStalkingStage(StalkingStage.Hidden);

		if (_isNight)
		{
			_nextAttackTimer = FirstAttackDelay;
		}
		else
		{
			HUDNode?.ClearInteractionPrompt();
		}
	}

	private void OnGameCompleted()
	{
		_isNight = false;
		IsMonsterAttacking = false;
		SetStalkingStage(StalkingStage.Hidden);
		VictoryScreenNode?.ShowVictoryTally(GridManagerNode, GameManagerNode);
	}

	public void StartMonsterAttack()
	{
		if (!_isNight || IsMonsterAttacking) return;

		IsMonsterAttacking = true;
		AttackCountdown = ReactionWindow;
		SetStalkingStage(StalkingStage.Imminent);
		string dirName = CurrentApproachDir.ToString().ToUpper();

		PlayWarningStinger();
		EmitSignal(SignalName.MonsterAttacking, (int)CurrentApproachDir, dirName, ReactionWindow);
	}

	public void ResolveMonsterAttack()
	{
		IsMonsterAttacking = false;
		string dirName = CurrentApproachDir.ToString().ToUpper();

		if (IsAnyEntranceOpen())
		{
			TriggerBreach($"SECURITY BREACH! The Ani-Ani infiltrated through an unsecured door or window on the {dirName} perimeter!");
		}
		else if (IsPlayerNotLooking(CurrentApproachDir))
		{
			TriggerBreach($"UNNOTICED ATTACK! You were not watching the {dirName} CCTV Camera (Cam {(int)CurrentApproachDir + 1}) when the Ani-Ani struck!");
		}
		else
		{
			EmitSignal(SignalName.MonsterRepelled, (int)CurrentApproachDir, dirName);
			HUDNode?.SetInteractionPrompt($"[✓] ANI-ANI REPELLED FROM {dirName}!");
			SaveManager.RecordAttackRepelled();

			CurrentStage = StalkingStage.Hidden;
			MonsterNode?.PlayRepelEffect();

			_nextAttackTimer = AttackInterval;
		}
	}

	public void TriggerBreach(string reason)
	{
		_isNight = false;
		IsMonsterAttacking = false;
		SetStalkingStage(StalkingStage.Hidden);
		EmitSignal(SignalName.BreachOccurred, reason);
		JumpScareScreenNode?.TriggerJumpScare(reason, GameManagerNode?.CurrentDay ?? 1, GameManagerNode?.Coins ?? 0);
	}

	public bool IsAnyEntranceOpen() =>
		GetTree().GetNodesInGroup("Doors").Any(n => n is InteractableDoor { IsOpen: true }) ||
		GetTree().GetNodesInGroup("Windows").Any(n => n is InteractableWindow { IsOpen: true });

	public bool IsPlayerNotLooking(AttackDirection direction) =>
		NightCameraNode == null || !NightCameraNode.IsNightMode || NightCameraNode.CurrentNightIndex != (int)direction;

	private void PlayWarningStinger()
	{
		try
		{
			int rate = 22050;
			byte[] data = new byte[(int)(rate * 0.8f)];
			for (int i = 0; i < data.Length; i++)
			{
				float t = (float)i / rate;
				float s = (Mathf.Sin(t * Mathf.Tau * 110f) * 0.6f + Mathf.Sin(t * Mathf.Tau * 440f) * 0.3f) * (1f - t / 0.8f);
				data[i] = (byte)(sbyte)(s * 110f);
			}
			_stingerPlayer.Stream = new AudioStreamWav { Format = AudioStreamWav.FormatEnum.Format8Bits, MixRate = rate, Data = data };
			_stingerPlayer.Play();
		}
		catch { }
	}
}
