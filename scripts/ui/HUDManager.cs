//anj - Oct 22 - Initial Implement 

using Godot;

public partial class HUDManager : CanvasLayer
{
	[Export] public GameManager GameManagerNode;

	[ExportGroup("UI Elements")]
	[Export] public Label DayLabel;
	[Export] public Label PhaseLabel;
	[Export] public Label TimerLabel;
	[Export] public Label CoinLabel;
	[Export] public Label SeedLabel;
	[Export] public Button BuySeedButton;
	[Export] public Label InteractionPromptLabel;

	public override void _Ready()
	{
		if (GameManagerNode == null)
		{
			GameManagerNode = GetTree().Root.FindChild("GameManager", true, false) as GameManager;
		}

		if (GameManagerNode == null)
		{
			GD.PushError(
                "Assign GameManagerNode in the HUD Inspector."
			);
			return;
		}

		// Connect to the GameManager signals.
		GameManagerNode.TimeUpdated += OnTimeUpdated;
		GameManagerNode.PhaseChanged += OnPhaseChanged;
		GameManagerNode.CoinsChanged += OnCoinsChanged;
		GameManagerNode.SeedsChanged += OnSeedsChanged;
		GameManagerNode.GameCompleted += OnGameCompleted;

		if (BuySeedButton != null)
			BuySeedButton.Pressed += OnBuySeedButtonPressed;

		// Initialize the HUD.
		OnTimeUpdated(GameManagerNode.TimeRemaining);

		OnPhaseChanged(
			GameManagerNode.CurrentPhase == GameManager.GamePhase.Day,
			GameManagerNode.CurrentDay
		);

		OnCoinsChanged(GameManagerNode.Coins);
		OnSeedsChanged(GameManagerNode.Level1Seeds);

		ClearInteractionPrompt();

		if (GameManagerNode.IsGameFinished)
			OnGameCompleted();
	}

	public void SetInteractionPrompt(string text)
	{
		if (InteractionPromptLabel != null)
		{
			InteractionPromptLabel.Text = text;
			InteractionPromptLabel.Visible = !string.IsNullOrEmpty(text);
		}
	}

	public void ClearInteractionPrompt()
	{
		if (InteractionPromptLabel != null)
		{
			InteractionPromptLabel.Text = "";
			InteractionPromptLabel.Visible = false;
		}
	}

	private void OnTimeUpdated(float timeRemaining)
	{
		if (TimerLabel == null)
			return;

		int totalSeconds = Mathf.Max(
			0, Mathf.CeilToInt(timeRemaining)
		);

		int minutes = totalSeconds / 60;
		int seconds = totalSeconds % 60;

		TimerLabel.Text = $"{minutes:00}:{seconds:00}";
	}

	private void OnPhaseChanged(bool isDay, int currentDay)
	{
		if (DayLabel != null)
			DayLabel.Text = $"Day {currentDay} / 7";

		if (PhaseLabel != null)
		{
			PhaseLabel.Text = isDay ? "DAYTIME" : "NIGHTTIME";

			PhaseLabel.Modulate = isDay
				? Colors.Yellow
				: Colors.MediumPurple;
		}
	}

	private void OnCoinsChanged(int totalCoins)
	{
		if (CoinLabel != null)
			CoinLabel.Text = $"Coins: {totalCoins}";
	}

	private void OnSeedsChanged(int totalSeeds)
	{
		if (SeedLabel != null)
			SeedLabel.Text = $"Lvl 1 Seeds: {totalSeeds}";
	}

	private void OnBuySeedButtonPressed()
	{
		if (GameManagerNode == null)
			return;

		bool purchased = GameManagerNode.TryBuyLevel1Seed();

		if (!purchased)
			GD.Print("Not enough coins to buy a seed!");
	}

	private void OnGameCompleted()
	{
		if (PhaseLabel != null)
		{
			PhaseLabel.Text = "YOU SURVIVED 7 DAYS!";
			PhaseLabel.Modulate = Colors.Gold;
		}

		if (TimerLabel != null)
			TimerLabel.Text = "GAME COMPLETE!";

		if (BuySeedButton != null)
			BuySeedButton.Disabled = true;
	}
}
