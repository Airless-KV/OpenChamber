// =========================================================================================
// Original Author : air (Airless-KV) - Nov 16, 2026
// Responsibilities: - Dedicated tutorial level state machine & interactive progression
//                   - Real interaction verification for all in-game interactables
//                   - Scavenger balloon merchant flower shop visit and trading loop
//                   - Faithful Day 7 Night Defense climax with authentic difficulty scaling
//                   - Dynamic 3D waypoint target tracking and Roblox-style objective guidance
// -----------------------------------------------------------------------------------------
// Change Log:
// - Nov 16, 2026 | air : Created comprehensive TutorialLevelManager with event-driven steps and endless night climax
// - Nov 16, 2026 | air : Added IsMouseWanted, SkipTutorialButton, Alt/Tab cursor release, and keyboard shortcuts ([N] Next, [H] Toggle)
// - Nov 16, 2026 | air : Enabled full defense combat during Endless Night
// - Nov 16, 2026 | air : Set default mouse mode to captured on start with cursor off for seamless first-person immersion
// - Nov 17, 2026 | air : Automatically fade out and dismiss EndlessNightBanner after 6 seconds
// - Nov 17, 2026 | air : Removed manual cursor toggle hotkeys; cursor is captured and only toggles visible when opening the Cryo-Freezer
// - Nov 17, 2026 | air : Overhauled tutorial to verify all interactables, visit shop, and copy normal game Day 7 Night defense
// - Nov 17, 2026 | air : Added [K] hotkey to skip tutorial, set default cursor to off (Captured), and added [Alt] cursor toggle
// =========================================================================================
using Godot;
using System;

public partial class TutorialLevelManager : CanvasLayer
{
	public enum TutorialStep
	{
		Welcome,
		ControlsMovement,
		MechanicsPlanting,
		MechanicsMerging,
		MechanicsHarvesting,
		MechanicsDropAndPick,
		MechanicsVisitShop,
		MechanicsClimbLadder,
		MechanicsFreezer,
		MechanicsWorkbench,
		MechanicsBatteryDocking,
		MechanicsBatteryTransfer,
		MechanicsCCTV,
		MechanicsSirenSwitch,
		MechanicsFloodlight,
		Day7NightDefense
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
	[Export] public CropDevourer CropDevourerNode;
	[Export] public VictoryScreen VictoryScreenNode;

	public TutorialStep CurrentStep { get; private set; } = TutorialStep.Welcome;
	public bool IsMouseWanted { get; private set; } = false;

	// Default fallback coordinates if scene nodes are not found
	private static readonly Vector3 FarmCenter = new(-1.66f, 1.25f, 0.0f);
	private static readonly Vector3 BalloonDefaultPos = new(4.5f, 1.72f, 3.5f);
	private static readonly Vector3 HouseLadderDefaultPos = new(-1.66f, 1.5f, 2.5f);
	private static readonly Vector3 FreezerDefaultPos = new(-3.5f, 4.6f, -1.8f);
	private static readonly Vector3 WorkbenchDefaultPos = new(0.05f, 4.6f, 2.15f);
	private static readonly Vector3 BatteryTransferDefaultPos = new(-3.4f, 4.6f, 2.26f);
	private static readonly Vector3 CCTVDeskDefaultPos = new(-1.66f, 5.1f, -1.5f);
	private static readonly Vector3 SirenSwitchDefaultPos = new(-1.14f, 4.95f, -1.42f);
	private static readonly Vector3 FloodlightKeypadDefaultPos = new(-1.26f, 4.95f, -1.42f);

	// Dynamically resolved interactable node references
	private Node3D _ladderNode;
	private Node3D _freezerNode;
	private Node3D _workbenchNode;
	private Node3D _batteryTransferNode;
	private Node3D _cctvDeskNode;
	private Node3D _sirenSwitchNode;
	private Node3D _floodlightKeypadNode;
	private Node3D _cabinBatteryNode;
	private Node3D _powerStationNode;

	private float _stepTimer = 0f;
	private Vector3 _playerStartPos;
	private bool _itemDroppedObserved = false;
	private bool _cctvEntered = false;
	private bool _day7DefenseActive = false;

	public override void _Ready()
	{
		Layer = 45;

		ResolveDependencies();
		BindEvents();

		if (NextStepButton != null)
		{
			NextStepButton.Text = "Next Step [N] >";
			NextStepButton.Pressed += OnNextStepPressed;
		}

		if (SkipTutorialButton != null)
		{
			SkipTutorialButton.Text = "Skip Tutorial [K]";
			SkipTutorialButton.Pressed += () => SetStep(TutorialStep.Day7NightDefense);
		}

		if (Player != null)
		{
			_playerStartPos = Player.GlobalPosition;
		}

		Input.MouseMode = Input.MouseModeEnum.Captured;
		IsMouseWanted = false;

		SetStep(TutorialStep.Welcome);
	}

	public override void _ExitTree()
	{
		UnbindEvents();
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
		CropDevourerNode ??= GetTree().Root.FindChild("CropDevourer", true, false) as CropDevourer;
		VictoryScreenNode ??= GetTree().Root.FindChild("VictoryScreen", true, false) as VictoryScreen;

		// Dynamically resolve interactable nodes for pinpoint waypoint tracking
		_ladderNode = GetTree().Root.FindChild("KnotLadder", true, false) as Node3D;
		_freezerNode = GetTree().Root.FindChild("CryoFreezer", true, false) as Node3D;
		_workbenchNode = GetTree().Root.FindChild("SeedWorkbench", true, false) as Node3D;
		_batteryTransferNode = GetTree().Root.FindChild("BatteryTransferMachine", true, false) as Node3D;
		_cctvDeskNode = GetTree().Root.FindChild("CCTVStation", true, false) as Node3D;
		_sirenSwitchNode = GetTree().Root.FindChild("SirenSwitch", true, false) as Node3D;
		_floodlightKeypadNode = GetTree().Root.FindChild("FloodlightKeypad", true, false) as Node3D;
		_cabinBatteryNode = GetTree().Root.FindChild("StarterCarBattery", true, false) as Node3D;
		_powerStationNode = GetTree().Root.FindChild("UnderDeskPC", true, false) as Node3D
			?? GetTree().Root.FindChild("SirenBatteryDock", true, false) as Node3D;
	}

	private void BindEvents()
	{
		if (FarmGrid != null)
		{
			FarmGrid.CropPlanted += OnCropPlanted;
			FarmGrid.CropsMerged += OnCropsMerged;
			FarmGrid.CropHarvested += OnCropHarvested;
		}

		if (Player != null)
		{
			Player.LocationChanged += OnPlayerLocationChanged;
		}

		CarriedInventory.ItemDropped += OnItemDropped;
		CarriedInventory.ItemPickedUp += OnItemPickedUp;
		HotAirBalloonShop.ShopVisited += OnShopVisited;
		InteractableLadder.LadderClimbed += OnLadderClimbed;
		InteractableFreezer.FreezerInteracted += OnFreezerInteracted;
		InteractableWorkbench.WorkbenchCrafted += OnWorkbenchCrafted;
		InteractableBatteryStation.BatteryDocked += OnBatteryDocked;
		InteractableBatteryTransfer.BatteryTransferInteracted += OnBatteryTransferInteracted;
		InteractableSirenSwitch.SirenActivated += OnSirenActivated;
		InteractableFloodlightButton.FloodlightButtonPressed += OnFloodlightButtonPressed;
	}

	private void UnbindEvents()
	{
		if (FarmGrid != null)
		{
			FarmGrid.CropPlanted -= OnCropPlanted;
			FarmGrid.CropsMerged -= OnCropsMerged;
			FarmGrid.CropHarvested -= OnCropHarvested;
		}

		if (Player != null)
		{
			Player.LocationChanged -= OnPlayerLocationChanged;
		}

		CarriedInventory.ItemDropped -= OnItemDropped;
		CarriedInventory.ItemPickedUp -= OnItemPickedUp;
		HotAirBalloonShop.ShopVisited -= OnShopVisited;
		InteractableLadder.LadderClimbed -= OnLadderClimbed;
		InteractableFreezer.FreezerInteracted -= OnFreezerInteracted;
		InteractableWorkbench.WorkbenchCrafted -= OnWorkbenchCrafted;
		InteractableBatteryStation.BatteryDocked -= OnBatteryDocked;
		InteractableBatteryTransfer.BatteryTransferInteracted -= OnBatteryTransferInteracted;
		InteractableSirenSwitch.SirenActivated -= OnSirenActivated;
		InteractableFloodlightButton.FloodlightButtonPressed -= OnFloodlightButtonPressed;
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

			// [K] to Skip Tutorial straight to Day 7 Night Defense
			if (key.Keycode == Key.K)
			{
				SetStep(TutorialStep.Day7NightDefense);
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

			// [Alt] Toggle Mouse Cursor Mode (Visible / Captured) for clicking UI buttons
			if (key.Keycode == Key.Alt)
			{
				IsMouseWanted = !IsMouseWanted;
				Input.MouseMode = IsMouseWanted ? Input.MouseModeEnum.Visible : Input.MouseModeEnum.Captured;
				GetViewport().SetInputAsHandled();
				return;
			}
		}
	}

	public override void _Process(double delta)
	{
		float dt = (float)delta;
		_stepTimer += dt;

		// Keep daytime fixed and steady during active training before Night 7
		if (!_day7DefenseActive && GameManagerNode != null)
		{
			if (GameManagerNode.TimeRemaining < 200f)
			{
				typeof(GameManager).GetProperty("TimeRemaining")?.SetValue(GameManagerNode, 999.0f);
			}
		}

		switch (CurrentStep)
		{
			case TutorialStep.ControlsMovement:
				if (Player != null && Player.GlobalPosition.DistanceTo(_playerStartPos) > 4.0f && _stepTimer > 1.0f)
				{
					AdvanceStep();
				}
				break;

			case TutorialStep.MechanicsCCTV:
				// Advance once player has entered CCTV surveillance mode and then exited back to first-person
				if (NightCameraNode != null)
				{
					if (NightCameraNode.IsNightMode)
					{
						_cctvEntered = true;
					}
					else if (_cctvEntered && _stepTimer > 2.0f)
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
					"Welcome, survivor. This interactive course will walk you through every interactable, workstation, and survival system in the canyon, followed by the ultimate Night 7 defense.\nPress [N / Enter] to begin, [K] to skip straight to Night 7 Defense, or [Alt] to toggle cursor."
				);
				WaypointArrow?.ClearTarget();
				break;

			case TutorialStep.ControlsMovement:
				ShowQuest(
					"STEP 1 / 14 - CONTROLS",
					"MOVEMENT & CAMERA: [W] [A] [S] [D]",
					"Use [W], [A], [S], [D] to navigate across the canyon plateau and move your mouse to look around. Take a few steps across the dirt."
				);
				WaypointArrow?.SetTarget(FarmCenter, "FARM FIELD");
				break;

			case TutorialStep.MechanicsPlanting:
				ShowQuest(
					"STEP 2 / 14 - CULTIVATION",
					"PLANT LEVEL 1 SEED: [E / LMB]",
					"Select your Level 1 Seed in your hotbar ([1-3] or Mouse Wheel) and press [E / LMB] while aiming at an empty chessboard tile to plant the cure."
				);
				WaypointArrow?.SetTarget(FarmCenter, "CHESSBOARD TILE");
				break;

			case TutorialStep.MechanicsMerging:
				ShowQuest(
					"STEP 3 / 14 - CULTIVATION",
					"GRAFT & MERGE TIERS: [E / LMB]",
					"Plant a second seed on an adjacent tile. When both flowers bloom, press and hold [E / LMB] to drag one bloom onto another identical bloom to MERGE them into Level 2!"
				);
				Vector3 mergePos = GetFirstPlantedCropPos() ?? FarmCenter;
				WaypointArrow?.SetTarget(mergePos, "BLOOMING FLOWERS");
				break;

			case TutorialStep.MechanicsHarvesting:
				ShowQuest(
					"STEP 4 / 14 - CULTIVATION",
					"HARVEST MATURE BLOOM: [F / RMB]",
					"Aim at your merged Level 2 flower on the chessboard and press [F / RMB] to harvest it into a physical carried item."
				);
				Vector3 harvestPos = GetFirstPlantedCropPos() ?? FarmCenter;
				WaypointArrow?.SetTarget(harvestPos, "MATURE FLOWER");
				break;

			case TutorialStep.MechanicsDropAndPick:
				ShowQuest(
					"STEP 5 / 14 - INVENTORY",
					"DROP & PICK UP ITEMS: [SPACE BAR]",
					"Physical items can be placed in the world or carried. Press [SPACE BAR] to drop your held flower onto the ground, then aim at it and press [SPACE BAR] (or [E / LMB]) to pick it back up!"
				);
				_itemDroppedObserved = false;
				WaypointArrow?.SetTarget(Player?.GlobalPosition ?? FarmCenter, "DROPPED ITEM");
				break;

			case TutorialStep.MechanicsVisitShop:
				ShowQuest(
					"STEP 6 / 14 - COMMERCE",
					"VISIT THE SCAVENGER BALLOON: [E / LMB] / [F / RMB]",
					"Approach the Scavenger Merchant's Hot Air Balloon on the farm perimeter. Press [E / LMB] to open the shop catalog to inspect wares, or press [F / RMB] to quick-sell items for coins!"
				);
				Vector3 balloonPos = BalloonShop?.GlobalPosition ?? BalloonDefaultPos;
				WaypointArrow?.SetTarget(balloonPos + new Vector3(0, 1.0f, 0), "SCAVENGER BALLOON");
				break;

			case TutorialStep.MechanicsClimbLadder:
				ShowQuest(
					"STEP 7 / 14 - NAVIGATION",
					"CLIMB TOWER KNOT LADDER: [E / LMB]",
					"The sun burns the canyon floor. Approach the base of the colossal transmission tower and press [E / LMB] on the knot ladder to ascend to your elevated cabin."
				);
				Vector3 ladderPos = _ladderNode?.GlobalPosition ?? HouseLadderDefaultPos;
				WaypointArrow?.SetTarget(ladderPos, "KNOT LADDER");
				break;

			case TutorialStep.MechanicsFreezer:
				ShowQuest(
					"STEP 8 / 14 - CABIN WORKSTATIONS",
					"CRYO-FREEZER CHEST STORAGE: [E / LMB]",
					"Approach the Cryo-Freezer chest inside the cabin. Press [E / LMB] to open the chest inventory (or [F / RMB] to quick-store) to safely preserve valuable crops and seeds from solar decay."
				);
				Vector3 freezerPos = _freezerNode?.GlobalPosition ?? FreezerDefaultPos;
				WaypointArrow?.SetTarget(freezerPos, "CRYO-FREEZER");
				break;

			case TutorialStep.MechanicsWorkbench:
				ShowQuest(
					"STEP 9 / 14 - CABIN WORKSTATIONS",
					"SEED WORKBENCH EXTRACTION: [E / LMB]",
					"Approach the Seed Workbench inside the cabin. With a harvested flower in hand, press [E / LMB] to extract 2 seeds matching that flower's tier level."
				);
				EnsurePlayerHasFlower();
				Vector3 workbenchPos = _workbenchNode?.GlobalPosition ?? WorkbenchDefaultPos;
				WaypointArrow?.SetTarget(workbenchPos, "SEED WORKBENCH");
				break;

			case TutorialStep.MechanicsBatteryDocking:
				ShowQuest(
					"STEP 10 / 14 - POWER & DEFENSE",
					"CAR BATTERY & POWER STATION DOCKING: [E / LMB]",
					"Pick up a car battery ([Space Bar] or [E / LMB]) from the cabin floor. Aim at any empty Power Station dock under or beside the CCTV desk and press [E / LMB] to insert it and restore power!"
				);
				Vector3 stationPos = _powerStationNode?.GlobalPosition 
					?? (_cabinBatteryNode?.GlobalPosition ?? CCTVDeskDefaultPos);
				WaypointArrow?.SetTarget(stationPos, "POWER STATION DOCK");
				break;

			case TutorialStep.MechanicsBatteryTransfer:
				ShowQuest(
					"STEP 11 / 14 - POWER & DEFENSE",
					"BATTERY CHARGE TRANSFER MACHINE: [E / LMB]",
					"Approach the dual-dock Battery Transfer Machine. Aim at Slot A or Slot B with a battery and press [E / LMB] to dock it for automated charge balancing (1% per 2s). Press [E / LMB] again to retrieve."
				);
				Vector3 transferPos = _batteryTransferNode?.GlobalPosition ?? BatteryTransferDefaultPos;
				WaypointArrow?.SetTarget(transferPos, "BATTERY TRANSFER MACHINE");
				break;

			case TutorialStep.MechanicsCCTV:
				ShowQuest(
					"STEP 12 / 14 - SURVEILLANCE & DEFENSE",
					"CCTV MONITORING STATION: [E / LMB]",
					"Aim at the CRT monitor on the security desk and press [E / LMB] to enter live surveillance. Cycle North, East, South, and West feeds using [A]/[D] or [1-4]. Press [E / ESC] to exit back to cabin view."
				);
				_cctvEntered = false;
				Vector3 cctvPos = _cctvDeskNode?.GlobalPosition ?? CCTVDeskDefaultPos;
				WaypointArrow?.SetTarget(cctvPos, "CCTV MONITOR");
				break;

			case TutorialStep.MechanicsSirenSwitch:
				ShowQuest(
					"STEP 13 / 14 - DEFENSE SYSTEMS",
					"EMERGENCY CABIN SIREN SWITCH: [E / LMB]",
					"Locate the large red emergency Siren button on the desk/wall. Aim at it and press [E / LMB] to sound the roof siren and verify the alarm system works!"
				);
				Vector3 sirenPos = _sirenSwitchNode?.GlobalPosition ?? SirenSwitchDefaultPos;
				WaypointArrow?.SetTarget(sirenPos, "EMERGENCY SIREN SWITCH");
				break;

			case TutorialStep.MechanicsFloodlight:
				ShowQuest(
					"STEP 14 / 14 - DEFENSE SYSTEMS",
					"PERIMETER FLOODLIGHT KEYPAD: [E / LMB]",
					"Aim at any of the directional floodlight buttons (▲ N, ► E, ▼ S, ◄ W) on the desk and press [E / LMB] to fire a blinding burst across that sector to repel approaching threats!"
				);
				Vector3 floodPos = _floodlightKeypadNode?.GlobalPosition ?? FloodlightKeypadDefaultPos;
				WaypointArrow?.SetTarget(floodPos, "FLOODLIGHT KEYPAD");
				break;

			case TutorialStep.Day7NightDefense:
				InitiateDay7NightDefense();
				break;
		}
	}

	private void ShowQuest(string pill, string title, string description)
	{
		if (QuestCard != null) QuestCard.Visible = true;
		if (StepPillLabel != null) StepPillLabel.Text = pill;
		if (StepTitleLabel != null) StepTitleLabel.Text = title;
		if (StepDescLabel != null) StepDescLabel.Text = description;

		Input.MouseMode = IsMouseWanted ? Input.MouseModeEnum.Visible : Input.MouseModeEnum.Captured;
	}

	private void OnNextStepPressed()
	{
		AdvanceStep();
	}

	private void AdvanceStep()
	{
		if (CurrentStep == TutorialStep.MechanicsFloodlight)
		{
			SetStep(TutorialStep.Day7NightDefense);
		}
		else if (CurrentStep < TutorialStep.Day7NightDefense)
		{
			SetStep(CurrentStep + 1);
		}
	}

	private void EnsurePlayerHasFlower()
	{
		var inv = Player?.GetNodeOrNull<CarriedInventory>("CarriedInventory");
		if (inv == null) return;

		if (!inv.HasItem(ItemType.Flower))
		{
			inv.AddItem(ItemType.Flower, 2);
		}
	}

	private Vector3? GetFirstPlantedCropPos()
	{
		if (FarmGrid != null)
		{
			var highestCell = FarmGrid.GetHighestTierPlantedCell();
			if (highestCell.HasValue)
			{
				return FarmGrid.GridToWorld(highestCell.Value);
			}
		}
		return null;
	}

	// -----------------------------------------------------------------------------------------
	// Event Callbacks
	// -----------------------------------------------------------------------------------------
	private void OnCropPlanted(Vector2I cell, int tier)
	{
		if (CurrentStep == TutorialStep.MechanicsPlanting)
		{
			FarmGrid?.ForceBloomAllCrops();
			AdvanceStep();
		}
		else if (CurrentStep == TutorialStep.MechanicsMerging)
		{
			FarmGrid?.ForceBloomAllCrops();
		}
	}

	private void OnCropsMerged(int resultingTier)
	{
		if (CurrentStep == TutorialStep.MechanicsMerging)
		{
			FarmGrid?.ForceBloomAllCrops();
			AdvanceStep();
		}
	}

	private void OnCropHarvested(Vector2I cell, int tier)
	{
		if (CurrentStep == TutorialStep.MechanicsHarvesting)
		{
			AdvanceStep();
		}
	}

	private void OnItemDropped()
	{
		if (CurrentStep == TutorialStep.MechanicsDropAndPick)
		{
			_itemDroppedObserved = true;
		}
	}

	private void OnItemPickedUp()
	{
		if (CurrentStep == TutorialStep.MechanicsDropAndPick && _itemDroppedObserved)
		{
			AdvanceStep();
		}
	}

	private void OnShopVisited()
	{
		if (CurrentStep == TutorialStep.MechanicsVisitShop)
		{
			AdvanceStep();
		}
	}

	private void OnLadderClimbed()
	{
		if (CurrentStep == TutorialStep.MechanicsClimbLadder)
		{
			AdvanceStep();
		}
	}

	private void OnPlayerLocationChanged(bool isIndoors)
	{
		if (isIndoors && CurrentStep == TutorialStep.MechanicsClimbLadder)
		{
			AdvanceStep();
		}
	}

	private void OnFreezerInteracted()
	{
		if (CurrentStep == TutorialStep.MechanicsFreezer)
		{
			AdvanceStep();
		}
	}

	private void OnWorkbenchCrafted()
	{
		if (CurrentStep == TutorialStep.MechanicsWorkbench)
		{
			AdvanceStep();
		}
	}

	private void OnBatteryDocked()
	{
		if (CurrentStep == TutorialStep.MechanicsBatteryDocking)
		{
			AdvanceStep();
		}
	}

	private void OnBatteryTransferInteracted()
	{
		if (CurrentStep == TutorialStep.MechanicsBatteryTransfer)
		{
			AdvanceStep();
		}
	}

	private void OnSirenActivated()
	{
		if (CurrentStep == TutorialStep.MechanicsSirenSwitch)
		{
			AdvanceStep();
		}
	}

	private void OnFloodlightButtonPressed()
	{
		if (CurrentStep == TutorialStep.MechanicsFloodlight)
		{
			AdvanceStep();
		}
	}

	// -----------------------------------------------------------------------------------------
	// Faithful Day 7 Night Defense Climax
	// -----------------------------------------------------------------------------------------
	private void InitiateDay7NightDefense()
	{
		_day7DefenseActive = true;

		if (QuestCard != null) QuestCard.Visible = false;
		WaypointArrow?.ClearTarget();

		if (EndlessNightBanner != null)
		{
			EndlessNightBanner.Visible = true;
			EndlessNightBanner.Modulate = new Color(1, 1, 1, 1);

			if (EndlessNightBanner.GetNodeOrNull<Label>("Margin/VBox/Title") is { } titleLbl)
			{
				titleLbl.Text = "⚠ ALL OBJECTIVES COMPLETE - NIGHT 7 DEFENSE BEGINS ⚠";
			}

			if (EndlessNightBanner.GetNodeOrNull<Label>("Margin/VBox/LoreLabel") is { } loreLbl)
			{
				loreLbl.Text = "The final night has fallen. Stalking beasts surround the perimeter.\nMonitor CCTV, sound the cabin siren, and pulse perimeter floodlights to survive until dawn!";
			}

			Tween tw = CreateTween();
			tw.TweenInterval(5.5f);
			tw.TweenProperty(EndlessNightBanner, "modulate:a", 0.0f, 1.5f);
			tw.TweenCallback(Callable.From(() =>
			{
				EndlessNightBanner.Visible = false;
				EndlessNightBanner.Modulate = new Color(1, 1, 1, 1);
			}));
		}

		// Warning stinger audio
		AudioManager.Sfx("warning_stinger");

		// Transition GameManager to Day 7 Night (330 seconds standard duration)
		if (GameManagerNode != null)
		{
			typeof(GameManager).GetProperty("CurrentDay")?.SetValue(GameManagerNode, 7);
			typeof(GameManager).GetProperty("CurrentPhase")?.SetValue(GameManagerNode, GameManager.GamePhase.Night);
			typeof(GameManager).GetProperty("TimeRemaining")?.SetValue(GameManagerNode, 330.0f);

			GameManagerNode.EmitSignal("PhaseChanged", false, 7);
			GameManagerNode.EmitSignal("TimeUpdated", 330.0f);
		}

		// Apply exact Day 7 scaling to BreachManager
		if (BreachManagerNode != null)
		{
			BreachManagerNode.ApplyDailyDifficultyScaling(7);
			BreachManagerNode.EnableOutdoorLoss = true; // Match standard game: outdoor night mortality active
			BreachManagerNode.Set("_isNight", true);
			BreachManagerNode.Set("_nextAttackTimer", 18.0f);
		}

		// Apply exact Day 7 scaling to CropDevourer
		if (CropDevourerNode != null)
		{
			CropDevourerNode.ApplyDailyDifficultyScaling(7);
			CropDevourerNode.StartNightLoitering();
		}
	}
}
