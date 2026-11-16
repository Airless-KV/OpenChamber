// =========================================================================================
// Original Author : air (Airless-KV) - Nov 16, 2026
// Responsibilities: - Dedicated tutorial level state machine & interactive Roblox-style progression
//                   - Basic Controls: WASD movement, Space Bar (Pick/Drop), LMB & E (Primary Interact), RMB & F (Secondary Interact)
//                   - In-world mechanics: Planting & merging, Freezer storage, Seed workbench extraction,
//                     Car Battery handling & Battery Transfer Machine, CCTV monitoring & Siren/Floodlight defense
//                   - Indefinite daytime phase (no timer expiration) until all steps are complete
//                   - Guaranteed Endless Night transition with relentless Ani-Ani breach and horror defeat
// -----------------------------------------------------------------------------------------
// Change Log:
// - Nov 16, 2026 | air : Created comprehensive TutorialLevelManager with event-driven steps and endless night climax
// - Nov 16, 2026 | air : Added IsMouseWanted, SkipTutorialButton, Alt/Tab cursor release, and keyboard shortcuts ([N] Next, [H] Toggle)
// - Nov 16, 2026 | air : Enabled full defense combat during Endless Night (allowing player to fight with batteries, CCTV, sirens & floodlights until depletion)
// - Nov 16, 2026 | air : Set default mouse mode to captured on start with cursor off for seamless first-person immersion
// =========================================================================================
using Godot;
using System;

public partial class TutorialLevelManager : CanvasLayer
{
	public enum TutorialStep
	{
		Welcome,
		ControlsMovement,
		ControlsPickDrop,
		ControlsInteractPrimary,
		ControlsInteractSecondary,
		MechanicsPlanting,
		MechanicsFreezer,
		MechanicsWorkbench,
		MechanicsBatteryTransfer,
		MechanicsCCTVAndDefense,
		OverallObjective,
		EndlessNightBreach
	}

	[ExportGroup("UI Elements")]
	[Export] public Control QuestCard;
	[Export] public Label StepPillLabel;
	[Export] public Label StepTitleLabel;
	[Export] public Label StepDescLabel;
	[Export] public Button NextStepButton;
	[Export] public Button SkipTutorialButton;
	[Export] public Control EndlessNightBanner;

	[ExportGroup("Waypoint Reference")]
	[Export] public TutorialWaypointArrow WaypointArrow;

	[ExportGroup("Scene Dependencies")]
	[Export] public GameManager GameManagerNode;
	[Export] public Node3d FarmGrid;
	[Export] public PlayerController Player;
	[Export] public HotAirBalloonShop BalloonShop;
	[Export] public Node3D HouseInterior;
	[Export] public BreachManager BreachManagerNode;
	[Export] public NighttCamera NightCameraNode;
	[Export] public AtmosphereManager AtmosphereNode;

	public TutorialStep CurrentStep { get; private set; } = TutorialStep.Welcome;
	public bool IsMouseWanted { get; private set; } = false;

	// Key locations
	private static readonly Vector3 FarmCenter = new(-1.66f, 1.25f, 0.0f);
	private static readonly Vector3 HouseLadderPos = new(-1.66f, 1.5f, 2.5f);
	private static readonly Vector3 HouseInteriorCenter = new(-1.66f, 4.6f, 0.0f);
	private static readonly Vector3 FreezerPos = new(-3.5f, 4.6f, -1.8f);
	private static readonly Vector3 WorkbenchPos = new(0.05f, 4.6f, 2.15f);
	private static readonly Vector3 BatteryTransferPos = new(-3.4f, 4.6f, 2.26f);
	private static readonly Vector3 CCTVDeskPos = new(-1.66f, 5.1f, -1.5f);

	private float _stepTimer = 0f;
	private Vector3 _playerStartPos;
	private bool _movedFarEnough = false;
	private bool _spaceActionObserved = false;
	private bool _primaryInteractObserved = false;
	private bool _secondaryInteractObserved = false;
	private bool _plantObserved = false;
	private bool _freezerObserved = false;
	private bool _workbenchObserved = false;
	private bool _batteryTransferObserved = false;
	private bool _cctvObserved = false;
	private bool _endlessNightTriggered = false;

	public override void _Ready()
	{
		Layer = 45;

		ResolveDependencies();
		BindEvents();

		if (NextStepButton != null)
		{
			NextStepButton.Pressed += OnNextStepPressed;
		}

		if (SkipTutorialButton != null)
		{
			SkipTutorialButton.Pressed += () => SetStep(TutorialStep.EndlessNightBreach);
		}

		if (Player != null)
		{
			_playerStartPos = Player.GlobalPosition;
		}

		Input.MouseMode = Input.MouseModeEnum.Captured;
		IsMouseWanted = false;

		// Keep daylight steady by keeping GameManager time high so daytime never naturally runs out
		SetStep(TutorialStep.Welcome);
	}

	private void ResolveDependencies()
	{
		GameManagerNode ??= GetTree().Root.FindChild("GameManager", true, false) as GameManager;
		FarmGrid ??= GetTree().Root.FindChild("GridManager", true, false) as Node3d;
		Player ??= GetTree().Root.FindChild("Player", true, false) as PlayerController;
		BalloonShop ??= GetTree().Root.FindChild("HotAirBalloonShop", true, false) as HotAirBalloonShop;
		HouseInterior ??= GetTree().Root.FindChild("HouseInterior", true, false) as Node3D;
		BreachManagerNode ??= GetTree().Root.FindChild("BreachManager", true, false) as BreachManager;
		NightCameraNode ??= GetTree().Root.FindChild("NighttCamera", true, false) as NighttCamera;
		AtmosphereNode ??= GetTree().Root.FindChild("AtmosphereManager", true, false) as AtmosphereManager;
	}

	private void BindEvents()
	{
		if (FarmGrid != null)
		{
			FarmGrid.CropPlanted += (cell, tier) =>
			{
				_plantObserved = true;
				if (CurrentStep == TutorialStep.MechanicsPlanting)
				{
					AdvanceStep();
				}
			};
		}
	}

	public override void _UnhandledInput(InputEvent @event)
	{
		if (@event is InputEventKey { Pressed: true, Echo: false } key)
		{
			// [H] Toggle Quest Card Visibility
			if (key.Keycode == Key.H && QuestCard != null)
			{
				QuestCard.Visible = !QuestCard.Visible;
				GetViewport().SetInputAsHandled();
				return;
			}

			// [N] or [Enter] to Advance Tutorial Step via keyboard
			if ((key.Keycode == Key.N || key.Keycode == Key.Enter || key.Keycode == Key.KpEnter) && QuestCard != null && QuestCard.Visible)
			{
				AdvanceStep();
				GetViewport().SetInputAsHandled();
				return;
			}

			// [Alt] or [Tab] to toggle mouse cursor visibility for UI clicking
			if (key.Keycode == Key.Alt || key.Keycode == Key.Tab)
			{
				ToggleMouseMode();
				GetViewport().SetInputAsHandled();
				return;
			}

			if (key.Keycode == Key.Space)
			{
				_spaceActionObserved = true;
				if (CurrentStep == TutorialStep.ControlsPickDrop && _stepTimer > 0.5f)
				{
					AdvanceStep();
				}
			}
			else if (key.Keycode == Key.E)
			{
				_primaryInteractObserved = true;
				if (CurrentStep == TutorialStep.ControlsInteractPrimary && _stepTimer > 0.5f)
				{
					AdvanceStep();
				}
			}
			else if (key.Keycode == Key.F)
			{
				_secondaryInteractObserved = true;
				if (CurrentStep == TutorialStep.ControlsInteractSecondary && _stepTimer > 0.5f)
				{
					AdvanceStep();
				}
			}
		}
		else if (@event is InputEventMouseButton { Pressed: true } mb)
		{
			if (mb.ButtonIndex == MouseButton.Left)
			{
				_primaryInteractObserved = true;
				if (CurrentStep == TutorialStep.ControlsInteractPrimary && _stepTimer > 0.5f)
				{
					AdvanceStep();
				}
			}
			else if (mb.ButtonIndex == MouseButton.Right)
			{
				_secondaryInteractObserved = true;
				if (CurrentStep == TutorialStep.ControlsInteractSecondary && _stepTimer > 0.5f)
				{
					AdvanceStep();
				}
			}
		}
	}

	public void ToggleMouseMode()
	{
		if (Input.MouseMode == Input.MouseModeEnum.Captured)
		{
			Input.MouseMode = Input.MouseModeEnum.Visible;
			IsMouseWanted = true;
		}
		else
		{
			Input.MouseMode = Input.MouseModeEnum.Captured;
			IsMouseWanted = false;
		}
	}

	public override void _Process(double delta)
	{
		float dt = (float)delta;
		_stepTimer += dt;

		// Maintain indefinite daytime during tutorial before Endless Night
		if (!_endlessNightTriggered && GameManagerNode != null)
		{
			// Keep day timer at 999s so daytime never expires on its own
			if (GameManagerNode.TimeRemaining < 200f)
			{
				GameManagerNode.Set("TimeRemaining", 999f);
			}
		}

		switch (CurrentStep)
		{
			case TutorialStep.ControlsMovement:
				if (Player != null && Player.GlobalPosition.DistanceTo(_playerStartPos) > 4.0f)
				{
					_movedFarEnough = true;
					if (_stepTimer > 1.2f)
					{
						AdvanceStep();
					}
				}
				break;

			case TutorialStep.MechanicsFreezer:
				// Advance if player interacts with freezer or approaches it closely
				if (Player != null && Player.GlobalPosition.DistanceTo(FreezerPos) < 2.5f)
				{
					if (_stepTimer > 3.0f || _freezerObserved)
					{
						AdvanceStep();
					}
				}
				break;

			case TutorialStep.MechanicsWorkbench:
				// Advance if player approaches workbench closely
				if (Player != null && Player.GlobalPosition.DistanceTo(WorkbenchPos) < 2.5f)
				{
					if (_stepTimer > 3.0f || _workbenchObserved)
					{
						AdvanceStep();
					}
				}
				break;

			case TutorialStep.MechanicsBatteryTransfer:
				// Advance if player checks transfer machine
				if (Player != null && Player.GlobalPosition.DistanceTo(BatteryTransferPos) < 2.5f)
				{
					if (_stepTimer > 4.0f || _batteryTransferObserved)
					{
						AdvanceStep();
					}
				}
				break;

			case TutorialStep.MechanicsCCTVAndDefense:
				// Advance if player enters CCTV or views desk
				if (NightCameraNode != null && NightCameraNode.IsNightMode)
				{
					_cctvObserved = true;
					if (_stepTimer > 4.0f)
					{
						AdvanceStep();
					}
				}
				break;
		}
	}

	public void SetStep(TutorialStep next)
	{
		CurrentStep = next;
		_stepTimer = 0f;

		switch (CurrentStep)
		{
			case TutorialStep.Welcome:
				ShowQuest(
					"OVERVIEW",
					"SURVIVAL TRAINING INITIATION",
					"Welcome, survivor. This interactive course will teach you the controls, farming, workstation mechanics, and horror defense required to stay alive. Click [Next] or press any key to begin."
				);
				WaypointArrow?.ClearTarget();
				break;

			case TutorialStep.ControlsMovement:
				ShowQuest(
					"STEP 1 / 10 - CONTROLS",
					"MOVEMENT: [W] [A] [S] [D]",
					"Use [W], [A], [S], [D] to walk across the canyon plateau and move your mouse to look around. Take a few steps now."
				);
				WaypointArrow?.SetTarget(FarmCenter, "FARM FIELD");
				break;

			case TutorialStep.ControlsPickDrop:
				ShowQuest(
					"STEP 2 / 10 - CONTROLS",
					"PICK UP & DROP: [SPACE BAR]",
					"Press [SPACE BAR] while aiming at physical items (like car batteries or harvested flowers) to pick them up into your carried inventory. Press [SPACE BAR] again while holding an item to drop it onto the floor."
				);
				WaypointArrow?.ClearTarget();
				break;

			case TutorialStep.ControlsInteractPrimary:
				ShowQuest(
					"STEP 3 / 10 - CONTROLS",
					"PRIMARY INTERACT (1_INTERACT): [LMB] & [E]",
					"Use [Left Mouse Button] or [E] to perform primary interactions: opening doors, climbing ladders, operating CCTV monitors, and placing seeds on chessboard tiles."
				);
				WaypointArrow?.ClearTarget();
				break;

			case TutorialStep.ControlsInteractSecondary:
				ShowQuest(
					"STEP 4 / 10 - CONTROLS",
					"SECONDARY INTERACT (2_INTERACT): [RMB] & [F]",
					"Use [Right Mouse Button] or [F] to execute secondary actions: initiating battery charge transfers, fast-harvesting bloomed flowers, or secondary workstation functions."
				);
				WaypointArrow?.ClearTarget();
				break;

			case TutorialStep.MechanicsPlanting:
				ShowQuest(
					"STEP 5 / 10 - MECHANICS",
					"CULTIVATION & MERGING",
					"Select your Level 1 Seed in your hotbar ([1] or Mouse Wheel) and [Left Click] or [E] on an empty chessboard tile to plant. When two identical flowers bloom, drag one onto another to MERGE them into a higher tier!"
				);
				WaypointArrow?.SetTarget(FarmCenter, "CHESSBOARD GRID");
				break;

			case TutorialStep.MechanicsFreezer:
				ShowQuest(
					"STEP 6 / 10 - MECHANICS",
					"CRYO-FREEZER STORAGE",
					"Climb the tower ladder into your cabin and approach the Cryo-Freezer. [Left Click] or [E] to store valuable flowers and seeds so they never decay or spoil in the sun."
				);
				WaypointArrow?.SetTarget(FreezerPos, "CRYO-FREEZER");
				break;

			case TutorialStep.MechanicsWorkbench:
				ShowQuest(
					"STEP 7 / 10 - MECHANICS",
					"SEED WORKBENCH",
					"Locate the Seed Workbench inside the cabin. Insert a harvested flower [E] to extract 2 seeds matching that flower's tier level, multiplying your crop yields!"
				);
				WaypointArrow?.SetTarget(WorkbenchPos, "SEED WORKBENCH");
				break;

			case TutorialStep.MechanicsBatteryTransfer:
				ShowQuest(
					"STEP 8 / 10 - MECHANICS",
					"BATTERIES & CHARGE TRANSFER",
					"Pick up car batteries with [SPACE]. Dock a donor battery in Slot A and a recipient in Slot B at the Battery Transfer Machine, then use Secondary Action ([RMB] / [F]) to siphon and combine electrical charge!"
				);
				WaypointArrow?.SetTarget(BatteryTransferPos, "BATTERY TRANSFER MACHINE");
				break;

			case TutorialStep.MechanicsCCTVAndDefense:
				ShowQuest(
					"STEP 9 / 10 - MECHANICS",
					"CCTV SURVEILLANCE & DEFENSE",
					"Inspect the Security Desk [LMB] to monitor North, East, South, and West perimeter cameras. Spot the Ani-Ani entity and blast the Roof Siren ([SPACE] in CCTV) or Floodlights to drive it back!"
				);
				WaypointArrow?.SetTarget(CCTVDeskPos, "CCTV STATION");
				break;

			case TutorialStep.OverallObjective:
				ShowQuest(
					"STEP 10 / 10 - OBJECTIVE",
					"THE SURVIVAL MANDATE & ENDLESS NIGHT",
					"Your core objective: Survive 7 Days and 7 Nights. Farm crops by day, trade at the Scavenger Balloon, and survive the horror by night. But here... night falls forever. Brace yourself!"
				);
				WaypointArrow?.ClearTarget();
				break;

			case TutorialStep.EndlessNightBreach:
				InitiateEndlessNight();
				break;
		}
	}

	private void ShowQuest(string pill, string title, string description)
	{
		if (QuestCard != null) QuestCard.Visible = true;
		if (StepPillLabel != null) StepPillLabel.Text = pill;
		if (StepTitleLabel != null) StepTitleLabel.Text = title;
		if (StepDescLabel != null) StepDescLabel.Text = description;

		// Default to captured mouse (cursor off) so player can freely look and move
		Input.MouseMode = Input.MouseModeEnum.Captured;
		IsMouseWanted = false;
	}

	private void OnNextStepPressed()
	{
		AdvanceStep();
	}

	private void AdvanceStep()
	{
		if (CurrentStep == TutorialStep.OverallObjective)
		{
			SetStep(TutorialStep.EndlessNightBreach);
		}
		else if (CurrentStep < TutorialStep.OverallObjective)
		{
			SetStep(CurrentStep + 1);
		}
	}

	private void InitiateEndlessNight()
	{
		_endlessNightTriggered = true;

		if (QuestCard != null) QuestCard.Visible = false;
		WaypointArrow?.ClearTarget();

		if (EndlessNightBanner != null)
		{
			EndlessNightBanner.Visible = true;
		}

		// Sound warning stinger
		AudioManager.Sfx("warning_stinger");

		// Switch atmosphere and lighting to Night immediately
		if (GameManagerNode != null)
		{
			GameManagerNode.Set("TimeRemaining", 999999f);
			GameManagerNode.EmitSignal("PhaseChanged", false, 1);
			GameManagerNode.EmitSignal("TimeUpdated", 999999f);
		}

		// Configure BreachManager for fully interactive, fightable Endless Night
		if (BreachManagerNode != null)
		{
			// Give player comfortable reaction windows and standard approach pacing so they can defend
			BreachManagerNode.FirstAttackDelay = 20.0f;
			BreachManagerNode.AttackInterval = 18.0f;
			BreachManagerNode.DistantStageDuration = 12.0f;
			BreachManagerNode.ApproachingStageDuration = 12.0f;
			BreachManagerNode.Stage3StandDuration = 3.0f;
			BreachManagerNode.ReactionWindow = 7.0f;
			BreachManagerNode.MinRetreatCooldown = 18.0f;
			BreachManagerNode.MaxRetreatCooldown = 32.0f;
			BreachManagerNode.EnableOutdoorLoss = false; // Allow moving outdoors to replace batteries / check yard without instant death

			// Force night state on BreachManager
			BreachManagerNode.Set("_isNight", true);
			BreachManagerNode.Set("_nextAttackTimer", 15.0f);
		}
	}
}
