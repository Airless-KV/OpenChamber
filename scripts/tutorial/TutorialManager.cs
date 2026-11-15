// =========================================================================================
// Original Author : air (Airless-KV) - Nov 15, 2026
// Responsibilities: - Lore onboarding dialog manager and 5-step interactive tutorial state machine
//                   - Roblox-style visual objective tracking and dynamic waypoint positioning
//                   - Event-driven progression (planting, flower merging, merchant visit, tower entry, CCTV)
//                   - Persistence synchronization with SaveManager and replay support
// -----------------------------------------------------------------------------------------
// Change Log:
// - Nov 15, 2026 | air : Implemented lore onboarding and 5-stage interactive Roblox-style tutorial
// - Nov 15, 2026 | air : Updated step descriptions to reflect CCTV siren & floodlight defense instead of barricades
// =========================================================================================
using Godot;
using System;

public partial class TutorialManager : CanvasLayer
{
	public enum Step
	{
		NotStarted,
		OnboardingModal,
		PlantSeed,
		MergeFlowers,
		VisitMerchant,
		ClimbTower,
		DefenseAndCCTV,
		Completed
	}

	[ExportGroup("UI References")]
	[Export] public Control OnboardingModal;
	[Export] public Button StartTutorialButton;
	[Export] public Button SkipOnboardingButton;

	[Export] public Control QuestCard;
	[Export] public Label StepPillLabel;
	[Export] public Label StepTitleLabel;
	[Export] public Label StepDescLabel;
	[Export] public Button NextStepButton;
	[Export] public Button SkipTutorialButton;

	[Export] public Control CelebrationBanner;
	[Export] public Label CelebrationLoreLabel;

	[ExportGroup("Waypoint Reference")]
	[Export] public TutorialWaypointArrow WaypointArrow;

	[ExportGroup("Scene Dependencies")]
	[Export] public Node3d FarmGrid;
	[Export] public PlayerController Player;
	[Export] public HotAirBalloonShop BalloonShop;
	[Export] public Node3D HouseInterior;

	[ExportGroup("Options")]
	[Export] public bool ForceShowOnStart = false;

	public Step CurrentStep { get; private set; } = Step.NotStarted;
	public bool IsActive => CurrentStep is >= Step.PlantSeed and <= Step.DefenseAndCCTV;

	private static readonly Vector3 FarmCenter = new(-1.66f, 1.25f, 0.0f);
	private static readonly Vector3 BalloonDefaultPos = new(4.5f, 1.72f, 3.5f);
	private static readonly Vector3 HouseLadderPos = new(-1.66f, 1.5f, 2.5f);
	private static readonly Vector3 HouseInteriorPos = new(-1.66f, 4.6f, 0.0f);

	private float _stepTimer = 0f;

	public override void _Ready()
	{
		Layer = 45; // Below pause menu (90) and fade layer (50)

		ResolveDependencies();
		BindButtons();

		// Check if player has already completed the tutorial
		bool alreadyDone = SaveManager.IsTutorialCompleted();

		if (ForceShowOnStart || !alreadyDone)
		{
			ShowOnboarding();
		}
		else
		{
			HideAll();
		}
	}

	private void ResolveDependencies()
	{
		FarmGrid ??= GetTree().Root.FindChild("GridManager", true, false) as Node3d;
		Player ??= GetTree().Root.FindChild("Player", true, false) as PlayerController;
		BalloonShop ??= GetTree().Root.FindChild("HotAirBalloonShop", true, false) as HotAirBalloonShop;
		HouseInterior ??= GetTree().Root.FindChild("HouseInterior", true, false) as Node3D;

		if (FarmGrid != null)
		{
			FarmGrid.CropPlanted += OnCropPlanted;
			FarmGrid.CropsMerged += OnCropsMerged;
		}

		if (Player != null)
		{
			Player.LocationChanged += OnPlayerLocationChanged;
		}
	}

	private void BindButtons()
	{
		if (StartTutorialButton != null)
			StartTutorialButton.Pressed += () => SetStep(Step.PlantSeed);

		if (SkipOnboardingButton != null)
			SkipOnboardingButton.Pressed += SkipTutorial;

		if (NextStepButton != null)
			NextStepButton.Pressed += AdvanceStep;

		if (SkipTutorialButton != null)
			SkipTutorialButton.Pressed += SkipTutorial;
	}

	public override void _UnhandledInput(InputEvent @event)
	{
		if (@event is InputEventKey { Pressed: true, Echo: false, Keycode: Key.H })
		{
			if (IsActive && QuestCard != null)
			{
				QuestCard.Visible = !QuestCard.Visible;
				GetViewport().SetInputAsHandled();
			}
		}
	}

	public override void _Process(double delta)
	{
		if (!IsActive) return;

		_stepTimer += (float)delta;

		// Proximity checks for Merchant Visit and Tower Arrival
		if (CurrentStep == Step.VisitMerchant)
		{
			Vector3 balloonPos = BalloonShop?.GlobalPosition ?? BalloonDefaultPos;
			if (Player != null && Player.GlobalPosition.DistanceTo(balloonPos) < 4.5f)
			{
				// Advanced after visiting the balloon shop
				if (_stepTimer > 2.5f)
				{
					SetStep(Step.ClimbTower);
				}
			}
		}
		else if (CurrentStep == Step.ClimbTower)
		{
			if (Player != null && Player.IsIndoors)
			{
				SetStep(Step.DefenseAndCCTV);
			}
		}
	}

	public void ShowOnboarding()
	{
		HideAll();
		CurrentStep = Step.OnboardingModal;

		if (OnboardingModal != null)
		{
			OnboardingModal.Visible = true;
		}

		// Ensure mouse is accessible for clicking dialogue
		Input.MouseMode = Input.MouseModeEnum.Visible;
	}

	public void SetStep(Step next)
	{
		CurrentStep = next;
		_stepTimer = 0f;

		if (OnboardingModal != null)
			OnboardingModal.Visible = false;

		// Return mouse to captured mode during active gameplay tutorial
		if (IsActive)
		{
			Input.MouseMode = Input.MouseModeEnum.Captured;
		}

		switch (CurrentStep)
		{
			case Step.PlantSeed:
				ShowQuest(
					"STEP 1 / 5",
					"CULTIVATE THE WALING-WALING",
					"Equip your Level 1 Seed (Scroll Wheel to Slot 1) and [Left Click] or [E] on any empty chessboard tile to plant the cure."
				);
				WaypointArrow?.SetTarget(FarmCenter, "FARM CHESSBOARD");
				break;

			case Step.MergeFlowers:
				ShowQuest(
					"STEP 2 / 5",
					"GRAFT & MERGE HIGHER TIERS",
					"When flowers bloom, [Left Click & Drag] one mature flower onto an identical bloom to merge them into a higher, more valuable tier."
				);
				Vector3 flowerPos = GetFirstPlantedCropPos() ?? FarmCenter;
				WaypointArrow?.SetTarget(flowerPos, "BLOOMING FLOWER");
				break;

			case Step.VisitMerchant:
				ShowQuest(
					"STEP 3 / 5",
					"THE SCAVENGER MERCHANT",
					"Approach the Scavenger Merchant's Hot Air Balloon. Press [Left Click] or [E] to trade harvested flowers for coins, seeds, and car batteries."
				);
				Vector3 balloonPos = BalloonShop?.GlobalPosition ?? BalloonDefaultPos;
				WaypointArrow?.SetTarget(balloonPos + new Vector3(0, 1.0f, 0), "SCAVENGER BALLOON");
				break;

			case Step.ClimbTower:
				ShowQuest(
					"STEP 4 / 5",
					"RETREAT TO TRANSMISSION TOWER",
					"The canyon sun burns fast. Climb the ladder up to your rotting shack on the colossal transmission tower before dusk."
				);
				WaypointArrow?.SetTarget(HouseLadderPos, "TOWER LADDER");
				break;

			case Step.DefenseAndCCTV:
				ShowQuest(
					"STEP 5 / 5",
					"SPARK-CELLS & CCTV DEFENSE",
					"Pick up the Spark-Cell battery [Space] and insert it into the Power Station, then inspect the CCTV Desk [Left Click] to monitor the Ani-Ani outside. Spot it on cameras and trigger Sirens or Floodlights to repel it!"
				);
				WaypointArrow?.SetTarget(HouseInteriorPos + new Vector3(0, 0.5f, 0), "TOWER CCTV DESK");
				break;

			case Step.Completed:
				CompleteTutorial();
				break;

			default:
				HideAll();
				break;
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

	private void ShowQuest(string pill, string title, string description)
	{
		if (QuestCard != null) QuestCard.Visible = true;
		if (StepPillLabel != null) StepPillLabel.Text = pill;
		if (StepTitleLabel != null) StepTitleLabel.Text = title;
		if (StepDescLabel != null) StepDescLabel.Text = description;
	}

	private void AdvanceStep()
	{
		if (CurrentStep == Step.DefenseAndCCTV)
		{
			CompleteTutorial();
		}
		else if (CurrentStep < Step.DefenseAndCCTV)
		{
			SetStep(CurrentStep + 1);
		}
	}

	public void SkipTutorial()
	{
		SaveManager.SetTutorialCompleted(true);
		HideAll();
		CurrentStep = Step.Completed;
		Input.MouseMode = Input.MouseModeEnum.Captured;
	}

	public void ReplayTutorial()
	{
		SetStep(Step.PlantSeed);
	}

	private void CompleteTutorial()
	{
		SaveManager.SetTutorialCompleted(true);
		CurrentStep = Step.Completed;

		WaypointArrow?.ClearTarget();
		if (QuestCard != null) QuestCard.Visible = false;

		if (CelebrationBanner != null)
		{
			CelebrationBanner.Visible = true;

			Tween tw = CreateTween();
			tw.TweenInterval(6.0f);
			tw.TweenProperty(CelebrationBanner, "modulate:a", 0.0f, 1.5f);
			tw.TweenCallback(Callable.From(() =>
			{
				CelebrationBanner.Visible = false;
				CelebrationBanner.Modulate = new Color(1, 1, 1, 1);
			}));
		}

		Input.MouseMode = Input.MouseModeEnum.Captured;
	}

	private void HideAll()
	{
		if (OnboardingModal != null) OnboardingModal.Visible = false;
		if (QuestCard != null) QuestCard.Visible = false;
		if (CelebrationBanner != null) CelebrationBanner.Visible = false;
		WaypointArrow?.ClearTarget();
	}

	// -----------------------------------------------------------------------------------------
	// Event Callbacks
	// -----------------------------------------------------------------------------------------
	private void OnCropPlanted(Vector2I cell, int tier)
	{
		if (CurrentStep == Step.PlantSeed)
		{
			SetStep(Step.MergeFlowers);
		}
	}

	private void OnCropsMerged(int resultingTier)
	{
		if (CurrentStep == Step.MergeFlowers)
		{
			SetStep(Step.VisitMerchant);
		}
	}

	private void OnPlayerLocationChanged(bool isIndoors)
	{
		if (isIndoors && CurrentStep == Step.ClimbTower)
		{
			SetStep(Step.DefenseAndCCTV);
		}
	}
}