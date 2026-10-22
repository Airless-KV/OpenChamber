
using Godot;

public partial class GameManager : Node
{
	// Signals for updating the HUD
	[Signal]
	public delegate void TimeUpdatedEventHandler(float timeRemaining);

	[Signal]
	public delegate void PhaseChangedEventHandler(bool isDay, int currentDay);

	[Signal]
	public delegate void CoinsChangedEventHandler(int totalCoins);

	[Signal]
	public delegate void SeedsChangedEventHandler(int totalSeeds);

	[Signal]
	public delegate void GameCompletedEventHandler();

	public enum GamePhase
	{
		Day,
		Night
	}

	// Daytime and nighttime durations in seconds, from Day 1 to Day 7.
	private readonly float[] DayDurations =
		{ 300, 300, 240, 240, 180, 180, 120 };

	private readonly float[] NightDurations =
		{ 180, 180, 240, 240, 300, 300, 600 };

	[ExportGroup("Cycle Settings")]
	[Export] public int MaxDays = 7;

	[ExportGroup("Economy Settings")]
	[Export] public int NightSurvivalReward = 10;
	[Export] public int SeedCost = 1;

	[ExportGroup("Transition Settings")]
	[Export] public ColorRect FadeRect;
	[Export] public float FadeDuration = 1.0f;

	public GamePhase CurrentPhase { get; private set; } = GamePhase.Day;
	public int CurrentDay { get; private set; } = 1;
	public float TimeRemaining { get; private set; }

	public int Coins { get; private set; } = 0;
	public int Level1Seeds { get; private set; } = 0;

	public bool IsGameFinished { get; private set; } = false;

	private bool isTransitioning = false;

	public override void _Ready()
	{
		SetProcess(true);

		if (FadeRect != null)
		{
			FadeRect.Color = new Color(0, 0, 0, 0);
			FadeRect.MouseFilter = Control.MouseFilterEnum.Ignore;
		}

		StartDayPhase();
	}

	public override void _Process(double delta)
	{
		if (IsGameFinished || isTransitioning)
			return;

		TimeRemaining -= (float)delta;

		if (TimeRemaining <= 0)
		{
			TimeRemaining = 0;
			EmitSignal(SignalName.TimeUpdated, TimeRemaining);
			ChangePhaseWithFade();
			return;
		}

		EmitSignal(SignalName.TimeUpdated, TimeRemaining);
	}

	private void StartDayPhase()
	{
		CurrentPhase = GamePhase.Day;
		TimeRemaining = DayDurations[CurrentDay - 1];

		EmitSignal(SignalName.PhaseChanged, true, CurrentDay);
		EmitSignal(SignalName.TimeUpdated, TimeRemaining);
	}

	private void StartNightPhase()
	{
		CurrentPhase = GamePhase.Night;
		TimeRemaining = NightDurations[CurrentDay - 1];

		EmitSignal(SignalName.PhaseChanged, false, CurrentDay);
		EmitSignal(SignalName.TimeUpdated, TimeRemaining);
	}

	private async void ChangePhaseWithFade()
	{
		if (isTransitioning || IsGameFinished)
			return;

		isTransitioning = true;

		// Fade to black before changing the phase.
		if (FadeRect != null && FadeDuration > 0)
		{
			Tween fadeOut = CreateTween();
			fadeOut.TweenProperty(
				FadeRect, "color:a", 1.0f, FadeDuration
			);

			await ToSignal(fadeOut, Tween.SignalName.Finished);
		}

		if (!IsInsideTree())
			return;

		if (CurrentPhase == GamePhase.Day)
		{
			StartNightPhase();
		}
		else
		{
			// Reward the player after surviving the night.
			AddCoins(NightSurvivalReward);

			if (CurrentDay >= MaxDays)
			{
				IsGameFinished = true;
				isTransitioning = false;
				EmitSignal(SignalName.GameCompleted);
				return;
			}

			CurrentDay++;
			StartDayPhase();
		}

		// Fade back in after changing the phase.
		if (FadeRect != null && FadeDuration > 0)
		{
			Tween fadeIn = CreateTween();
			fadeIn.TweenProperty(
				FadeRect, "color:a", 0.0f, FadeDuration
			);

			await ToSignal(fadeIn, Tween.SignalName.Finished);
		}

		if (!IsInsideTree())
			return;

		isTransitioning = false;
	}

	public void AddCoins(int amount)
	{
		Coins += amount;
		EmitSignal(SignalName.CoinsChanged, Coins);
	}

	public bool TryBuyLevel1Seed()
	{
		if (IsGameFinished || Coins < SeedCost)
			return false;

		Coins -= SeedCost;
		Level1Seeds++;

		EmitSignal(SignalName.CoinsChanged, Coins);
		EmitSignal(SignalName.SeedsChanged, Level1Seeds);

		return true;
	}
}
