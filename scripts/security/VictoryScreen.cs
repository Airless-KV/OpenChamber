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

	public override void _Ready()
	{
		Visible = false;
		Layer = 95;

		if (PlayAgainButton != null)
		{
			PlayAgainButton.Pressed += OnPlayAgainButtonPressed;
		}
	}

	public void ShowVictoryTally(Node3d gridManager, GameManager gameManager)
	{
		Visible = true;

		// 1. Calculate flower counts and points
		Dictionary<int, int> tierCounts = new()
		{
			{ 1, 0 }, { 2, 0 }, { 3, 0 }, { 4, 0 }, { 5, 0 }
		};

		if (gridManager != null)
		{
			tierCounts = gridManager.GetFlowerTierCounts();
		}

		int coins = gameManager != null ? gameManager.Coins : 0;

		// Scoring per PDF:
		// Level 1 = 5 pts, Level 2 = 10 pts, Level 3 = 15 pts, Level 4 = 20 pts, Level 5 = 25 pts
		int ptsL1 = tierCounts[1] * 5;
		int ptsL2 = tierCounts[2] * 10;
		int ptsL3 = tierCounts[3] * 15;
		int ptsL4 = tierCounts[4] * 20;
		int ptsL5 = tierCounts[5] * 25;
		int coinBonus = coins * 1; // 1 pt per coin kept

		int totalScore = ptsL1 + ptsL2 + ptsL3 + ptsL4 + ptsL5 + coinBonus;

		// 2. Format Itemized Breakdown Table
		if (BreakdownLabel != null)
		{
			BreakdownLabel.Text =
				$"Tier 1 Flowers:  {tierCounts[1],2}  x   5 pts  =  {ptsL1,4} pts\n" +
				$"Tier 2 Flowers:  {tierCounts[2],2}  x  10 pts  =  {ptsL2,4} pts\n" +
				$"Tier 3 Flowers:  {tierCounts[3],2}  x  15 pts  =  {ptsL3,4} pts\n" +
				$"Tier 4 Flowers:  {tierCounts[4],2}  x  20 pts  =  {ptsL4,4} pts\n" +
				$"Tier 5 Flowers:  {tierCounts[5],2}  x  25 pts  =  {ptsL5,4} pts\n" +
				$"---------------------------------------------\n" +
				$"Coin Wallet Bonus: {coins,2}  x   1 pt   =  {coinBonus,4} pts";
		}

		if (TotalScoreLabel != null)
		{
			TotalScoreLabel.Text = $"TOTAL VICTORY SCORE: {totalScore} PTS";
		}

		// 3. Format Leaderboard Standings
		if (LeaderboardLabel != null)
		{
			LeaderboardLabel.Text =
				$"RANK   NAME                 SCORE\n" +
				$"#1     Grand Champion       320 pts\n" +
				$"#2     Night Watcher        240 pts\n" +
				$"#3 >>> YOU (Survivor)       {totalScore} pts <<<\n" +
				$"#4     Country Farmer       130 pts\n" +
				$"#5     Ani-Ani Evader        75 pts";
		}
	}

	private void OnPlayAgainButtonPressed()
	{
		GetTree().Paused = false;
		GetTree().ReloadCurrentScene();
	}
}
