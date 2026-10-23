using Godot;
using System;
using System.Collections.Generic;

public partial class BreachManager : Node
{
	[Signal]
	public delegate void MonsterAttackingEventHandler(int direction, string dirName, float timeToReact);

	[Signal]
	public delegate void MonsterRepelledEventHandler(int direction, string dirName);

	[Signal]
	public delegate void BreachOccurredEventHandler(string reason);

	public enum AttackDirection
	{
		North = 0,
		East = 1,
		South = 2,
		West = 3
	}

	[ExportGroup("Dependencies")]
	[Export] public GameManager GameManagerNode;
	[Export] public NighttCamera NightCameraNode;
	[Export] public PlayerController PlayerNode;
	[Export] public Node3d GridManagerNode;
	[Export] public JumpScareScreen JumpScareScreenNode;
	[Export] public VictoryScreen VictoryScreenNode;
	[Export] public HUDManager HUDNode;

	[ExportGroup("Monster Attack Settings")]
	[Export] public float FirstAttackDelay = 12.0f;     // Seconds after nightfall before first attack
	[Export] public float AttackInterval = 25.0f;       // Seconds between subsequent attacks
	[Export] public float ReactionWindow = 7.0f;        // Seconds to check camera / secure entrances
	[Export] public bool EnableOutdoorLoss = true;      // Instant loss if outside after sundown

	public bool IsMonsterAttacking { get; private set; } = false;
	public AttackDirection CurrentApproachDir { get; private set; } = AttackDirection.North;
	public float AttackCountdown { get; private set; } = 0.0f;

	private float _nextAttackTimer = 0.0f;
	private bool _isNight = false;
	private Random _rng = new();
	private AudioStreamPlayer _stingerPlayer;

	public override void _Ready()
	{
		// Auto-locate dependencies if not wired in scene
		if (GameManagerNode == null)
			GameManagerNode = GetTree().Root.FindChild("GameManager", true, false) as GameManager;
		if (NightCameraNode == null)
			NightCameraNode = GetTree().Root.FindChild("NighttCamera", true, false) as NighttCamera;
		if (PlayerNode == null)
			PlayerNode = GetTree().Root.FindChild("Player", true, false) as PlayerController;
		if (GridManagerNode == null)
			GridManagerNode = GetTree().Root.FindChild("GridManager", true, false) as Node3d;
		if (JumpScareScreenNode == null)
			JumpScareScreenNode = GetTree().Root.FindChild("JumpScareScreen", true, false) as JumpScareScreen;
		if (VictoryScreenNode == null)
			VictoryScreenNode = GetTree().Root.FindChild("VictoryScreen", true, false) as VictoryScreen;
		if (HUDNode == null)
			HUDNode = GetTree().Root.FindChild("HUD", true, false) as HUDManager;

		// Listen to phase changes
		if (GameManagerNode != null)
		{
			GameManagerNode.PhaseChanged += OnPhaseChanged;
			GameManagerNode.GameCompleted += OnGameCompleted;
		}

		SetupAudio();
	}

	public override void _Process(double delta)
	{
		if (GameManagerNode != null && GameManagerNode.IsGameFinished)
			return;

		if (!_isNight)
			return;

		float dt = (float)delta;

		// 1. Check if player stepped outside after sundown
		if (EnableOutdoorLoss && PlayerNode != null && !PlayerNode.IsIndoors)
		{
			TriggerBreach("CAUGHT OUTSIDE! You stepped outside after sundown into the Ani-Ani's hunting grounds!");
			return;
		}

		// 2. Active attack reaction timer
		if (IsMonsterAttacking)
		{
			AttackCountdown -= dt;

			// Display countdown on HUD
			if (HUDNode != null)
			{
				string dirName = CurrentApproachDir.ToString().ToUpper();
				HUDNode.SetInteractionPrompt($"[!] ANI-ANI APPROACHING FROM {dirName}! ({Mathf.CeilToInt(AttackCountdown)}s)");
			}

			if (AttackCountdown <= 0.0f)
			{
				// Attack resolves now! Perform the breach checks
				ResolveMonsterAttack();
			}
			return;
		}

		// 3. Countdown towards next attack
		_nextAttackTimer -= dt;
		if (_nextAttackTimer <= 0.0f)
		{
			StartMonsterAttack();
		}
	}

	private void OnPhaseChanged(bool isDay, int currentDay)
	{
		_isNight = !isDay;

		if (_isNight)
		{
			// Reset attack timing for the new night
			_nextAttackTimer = FirstAttackDelay;
			IsMonsterAttacking = false;
			GD.Print($"[Alarte/BreachSystem] Night {currentDay} has begun. Ani-Ani is stalking the farm.");
		}
		else
		{
			IsMonsterAttacking = false;
			if (HUDNode != null) HUDNode.ClearInteractionPrompt();
			GD.Print($"[Alarte/BreachSystem] Dawn has arrived. Ani-Ani retreated.");
		}
	}

	private void OnGameCompleted()
	{
		_isNight = false;
		IsMonsterAttacking = false;

		if (VictoryScreenNode != null)
		{
			VictoryScreenNode.ShowVictoryTally(GridManagerNode, GameManagerNode);
		}
	}

	public void StartMonsterAttack()
	{
		if (!_isNight || IsMonsterAttacking)
			return;

		IsMonsterAttacking = true;
		AttackCountdown = ReactionWindow;

		// Pick random direction: North(0), East(1), South(2), West(3)
		CurrentApproachDir = (AttackDirection)_rng.Next(0, 4);
		string dirName = CurrentApproachDir.ToString().ToUpper();

		PlayWarningStinger();

		GD.Print($"[Alarte/BreachSystem] WARNING: Ani-Ani is approaching from {dirName}! Player has {ReactionWindow}s to secure entrances and monitor feed.");
		EmitSignal(SignalName.MonsterAttacking, (int)CurrentApproachDir, dirName, ReactionWindow);
	}

	public void ResolveMonsterAttack()
	{
		IsMonsterAttacking = false;
		string dirName = CurrentApproachDir.ToString().ToUpper();

		// Check 1: Entrance open check (doors & windows)
		bool entranceOpen = IsAnyEntranceOpen();

		// Check 2: Player not looking check (surveillance camera / active view)
		bool playerNotLooking = IsPlayerNotLooking(CurrentApproachDir);

		if (entranceOpen)
		{
			TriggerBreach($"SECURITY BREACH! The Ani-Ani infiltrated through an unsecured door or window on the {dirName} perimeter!");
		}
		else if (playerNotLooking)
		{
			int camIndex = (int)CurrentApproachDir + 1;
			TriggerBreach($"UNNOTICED ATTACK! You were not watching the {dirName} CCTV Camera (Cam {camIndex}) when the Ani-Ani struck!");
		}
		else
		{
			// Player survived the attack!
			OnMonsterRepelled(dirName);
		}
	}

	private void OnMonsterRepelled(string dirName)
	{
		GD.Print($"[Alarte/BreachSystem] DEFENSE SUCCESSFUL! You spotted the Ani-Ani on {dirName} camera with all entrances secured. The monster retreated!");
		EmitSignal(SignalName.MonsterRepelled, (int)CurrentApproachDir, dirName);

		if (HUDNode != null)
		{
			HUDNode.SetInteractionPrompt($"[✓] ANI-ANI REPELLED FROM {dirName}!");
		}

		// Schedule next attack
		_nextAttackTimer = AttackInterval;
	}

	public void TriggerBreach(string reason)
	{
		_isNight = false;
		IsMonsterAttacking = false;

		GD.PrintErr($"[Alarte/BreachSystem] BREACH OCCURRED: {reason}");
		EmitSignal(SignalName.BreachOccurred, reason);

		if (JumpScareScreenNode != null)
		{
			int day = GameManagerNode != null ? GameManagerNode.CurrentDay : 1;
			int coins = GameManagerNode != null ? GameManagerNode.Coins : 0;
			JumpScareScreenNode.TriggerJumpScare(reason, day, coins);
		}
	}

	public bool IsAnyEntranceOpen()
	{
		// Check all Doors in scene
		var doors = GetTree().GetNodesInGroup("Doors");
		foreach (var node in doors)
		{
			if (node is InteractableDoor door && door.IsOpen)
				return true;
		}

		// Check all Windows in scene
		var windows = GetTree().GetNodesInGroup("Windows");
		foreach (var node in windows)
		{
			if (node is InteractableWindow win && win.IsOpen)
				return true;
		}

		return false;
	}

	public bool IsPlayerNotLooking(AttackDirection direction)
	{
		if (NightCameraNode == null)
			return true;

		// If player is not even in CCTV mode, they are not looking through security cameras
		if (!NightCameraNode.IsNightMode)
			return true;

		// Direction indices: 0 = North, 1 = East, 2 = South, 3 = West
		int requiredIndex = (int)direction;
		return NightCameraNode.CurrentNightIndex != requiredIndex;
	}

	private void SetupAudio()
	{
		_stingerPlayer = new AudioStreamPlayer();
		_stingerPlayer.Name = "WarningStinger";
		AddChild(_stingerPlayer);
	}

	private void PlayWarningStinger()
	{
		// Generate an ominous pulsating low-frequency warning tone buffer
		try
		{
			int sampleRate = 22050;
			float duration = 0.8f;
			int totalSamples = (int)(sampleRate * duration);
			byte[] data = new byte[totalSamples];

			for (int i = 0; i < totalSamples; i++)
			{
				float t = (float)i / sampleRate;
				// 110Hz low rumble modulated with 440Hz alarm tone
				float sample = Mathf.Sin(t * Mathf.Tau * 110.0f) * 0.6f + Mathf.Sin(t * Mathf.Tau * 440.0f) * 0.3f;
				float env = 1.0f - (t / duration); // Decay
				sbyte s = (sbyte)(sample * env * 110.0f);
				data[i] = (byte)s;
			}

			var wav = new AudioStreamWav
			{
				Format = AudioStreamWav.FormatEnum.Format8Bits,
				MixRate = sampleRate,
				Data = data
			};

			_stingerPlayer.Stream = wav;
			_stingerPlayer.Play();
		}
		catch (Exception)
		{
			// Audio generation fallback
		}
	}
}
