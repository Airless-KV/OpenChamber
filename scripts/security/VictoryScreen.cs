// =========================================================================================
// Original Author : air (Airless-KV) - Oct 2026
// Responsibilities: - Day 7 victory tally screen with itemized flower score breakdown table
//                   - Coin wallet bonus calculation and persistent leaderboard standings display
//                   - Restart and Main Menu navigation with liberated mouse cursor
// -----------------------------------------------------------------------------------------
// Change Log:
// - Oct 24, 2026 | air : Streamlined score calculation and breakdown string formatting for Alpha build
// - Oct 30, 2026 | air : Integrated persistent SaveManager for recording victories and ranking leaderboard
// - Nov 07, 2026 | air : Released mouse cursor, paused gameplay tree, and added MainMenuButton
// =========================================================================================
using Godot;
using System.Collections.Generic;

public partial class VictoryScreen : CanvasLayer
{
	[ExportGroup("UI References")]
	[Export] public Label TitleLabel;
	[Export] public Label SubtitleLabel;
	[Export] public Label BreakdownLabel;
	[Export] public Label TotalScoreLabel;
	[Export] public Label LeaderboardLabel;
	[Export] public Button PlayAgainButton;
	[Export] public Button MainMenuButton;

	public override void _Ready()
	{
		ProcessMode = ProcessModeEnum.Always;
		Visible = false;
		Layer = 95;

		if (PlayAgainButton != null)
		{
			PlayAgainButton.Pressed += () =>
			{
				GetTree().Paused = false;
				Input.MouseMode = Input.MouseModeEnum.Captured;
				GetTree().ReloadCurrentScene();
			};
		}

		if (MainMenuButton != null)
		{
			MainMenuButton.Pressed += () =>
			{
				GetTree().Paused = false;
				Input.MouseMode = Input.MouseModeEnum.Visible;
				GetTree().ChangeSceneToFile("res://scenes/ui/main_menu.tscn");
			};
		}
	}

	public override void _UnhandledInput(InputEvent @event)
	{
		if (!Visible) return;

		if (@event is InputEventKey { Pressed: true, Echo: false, Keycode: Key.Escape })
		{
			GetTree().Paused = false;
			Input.MouseMode = Input.MouseModeEnum.Visible;
			GetTree().ChangeSceneToFile("res://scenes/ui/main_menu.tscn");
			GetViewport().SetInputAsHandled();
		}
	}

	public void ShowVictoryTally(Node3d gridManager, GameManager gameManager)
	{
		Visible = true;
		GetTree().Paused = true;
		Input.MouseMode = Input.MouseModeEnum.Visible;
		var tiers = gridManager?.GetFlowerTierCounts() ?? new Dictionary<int, int> { { 1, 0 }, { 2, 0 }, { 3, 0 }, { 4, 0 }, { 5, 0 } };
		int coins = gameManager?.Coins ?? 0;

		int p1 = tiers[1] * 5, p2 = tiers[2] * 10, p3 = tiers[3] * 15, p4 = tiers[4] * 20, p5 = tiers[5] * 25;
		int totalScore = p1 + p2 + p3 + p4 + p5 + coins;

		if (BreakdownLabel != null)
		{
			BreakdownLabel.Text =
				$"Tier 1 Flowers:  {tiers[1],2}  x   5 pts  =  {p1,4} pts\n" +
				$"Tier 2 Flowers:  {tiers[2],2}  x  10 pts  =  {p2,4} pts\n" +
				$"Tier 3 Flowers:  {tiers[3],2}  x  15 pts  =  {p3,4} pts\n" +
				$"Tier 4 Flowers:  {tiers[4],2}  x  20 pts  =  {p4,4} pts\n" +
				$"Tier 5 Flowers:  {tiers[5],2}  x  25 pts  =  {p5,4} pts\n" +
				$"---------------------------------------------\n" +
				$"Coin Wallet Bonus: {coins,2}  x   1 pt   =  {coins,4} pts";
		}

		// Record result in persistent local save database
		string playerName = SaveManager.CurrentData.Profile.PlayerName;
		int rank = SaveManager.RecordGameResult(playerName, totalScore, 7, true, coins, out var entry);

		if (TotalScoreLabel != null)
		{
			string rankBadge = rank > 0 ? $"  (RANK #{rank} ON LEADERBOARD!)" : "";
			TotalScoreLabel.Text = $"TOTAL VICTORY SCORE: {totalScore} PTS{rankBadge}";
		}

		if (LeaderboardLabel != null)
		{
			LeaderboardLabel.Text = SaveManager.GetLeaderboardFormatted(entry, 5);
		}
	}
}
