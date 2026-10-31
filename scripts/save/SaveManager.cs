// =========================================================================================
// Original Author : air (Airless-KV) - Oct 2026
// Responsibilities: - Persistent local database and save system (user://save_data.json)
//                   - Player profile statistics tracking (survival, merges, attacks repelled)
//                   - Ranked leaderboard standings calculation and formatted table generation
// -----------------------------------------------------------------------------------------
// Change Log:
// - Oct 30, 2026 | air : Implemented persistent JSON save system and ranked leaderboard standings
// - Oct 30, 2026 | air : Added AudioSettings persistence for Master, Music, SFX, and display options
// =========================================================================================
using Godot;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

public class PlayerProfile
{
	public string PlayerName { get; set; } = "Survivor";
	public int TotalGamesPlayed { get; set; } = 0;
	public int TotalVictories { get; set; } = 0;
	public int TotalBreaches { get; set; } = 0;
	public int HighestDayReached { get; set; } = 1;
	public int AllTimeHighScore { get; set; } = 0;
	public int TotalCoinsEarned { get; set; } = 0;
	public int TotalMerges { get; set; } = 0;
	public int TotalAttacksRepelled { get; set; } = 0;
}

public class AudioSettings
{
	public float MasterVolume { get; set; } = 1.0f;
	public float MusicVolume { get; set; } = 0.8f;
	public float SFXVolume { get; set; } = 1.0f;
	public bool Fullscreen { get; set; } = false;
}

public class LeaderboardEntry
{
	public string PlayerName { get; set; } = "Survivor";
	public int Score { get; set; } = 0;
	public int DayReached { get; set; } = 1;
	public bool Survived { get; set; } = false;
	public int Coins { get; set; } = 0;
	public string Date { get; set; } = "";
}

public class SaveData
{
	public PlayerProfile Profile { get; set; } = new();
	public AudioSettings Audio { get; set; } = new();
	public List<LeaderboardEntry> Leaderboard { get; set; } = new();
}

public partial class SaveManager : Node
{
	public static SaveManager Instance { get; private set; }
	public const string SaveFilePath = "user://save_data.json";

	private static SaveData _cachedData;
	public static SaveData CurrentData
	{
		get
		{
			if (_cachedData == null) LoadDirect();
			return _cachedData;
		}
	}

	public override void _EnterTree()
	{
		if (Instance != null && Instance != this)
		{
			QueueFree();
			return;
		}
		Instance = this;
		LoadDirect();
	}

	public static void LoadDirect()
	{
		try
		{
			if (!FileAccess.FileExists(SaveFilePath))
			{
				_cachedData = CreateDefaultSaveData();
				SaveDirect();
				return;
			}

			using var file = FileAccess.Open(SaveFilePath, FileAccess.ModeFlags.Read);
			if (file == null)
			{
				_cachedData = CreateDefaultSaveData();
				return;
			}

			string json = file.GetAsText();
			_cachedData = JsonSerializer.Deserialize<SaveData>(json) ?? CreateDefaultSaveData();
		}
		catch (Exception ex)
		{
			GD.PrintErr($"[SaveManager] Failed to load {SaveFilePath}: {ex.Message}. Falling back to defaults.");
			_cachedData = CreateDefaultSaveData();
		}
	}

	public static void SaveDirect()
	{
		try
		{
			if (_cachedData == null)
				_cachedData = CreateDefaultSaveData();

			var options = new JsonSerializerOptions { WriteIndented = true };
			string json = JsonSerializer.Serialize(_cachedData, options);

			using var file = FileAccess.Open(SaveFilePath, FileAccess.ModeFlags.Write);
			if (file != null)
			{
				file.StoreString(json);
			}
		}
		catch (Exception ex)
		{
			GD.PrintErr($"[SaveManager] Failed to save {SaveFilePath}: {ex.Message}");
		}
	}

	private static SaveData CreateDefaultSaveData()
	{
		return new SaveData
		{
			Profile = new PlayerProfile { PlayerName = "Survivor" },
			Audio = new AudioSettings(),
			Leaderboard = new List<LeaderboardEntry>
			{
				new() { PlayerName = "Grand Champion", Score = 320, DayReached = 7, Survived = true, Coins = 20, Date = "Legacy Record" },
				new() { PlayerName = "Night Watcher", Score = 240, DayReached = 7, Survived = true, Coins = 15, Date = "Legacy Record" },
				new() { PlayerName = "Country Farmer", Score = 130, DayReached = 5, Survived = false, Coins = 8, Date = "Legacy Record" },
				new() { PlayerName = "Ani-Ani Evader", Score = 75, DayReached = 3, Survived = false, Coins = 5, Date = "Legacy Record" }
			}
		};
	}

	public static int RecordGameResult(string playerName, int score, int dayReached, bool survived, int coins, out LeaderboardEntry entry)
	{
		var data = CurrentData;
		if (string.IsNullOrWhiteSpace(playerName))
			playerName = data.Profile.PlayerName;

		entry = new LeaderboardEntry
		{
			PlayerName = playerName,
			Score = score,
			DayReached = dayReached,
			Survived = survived,
			Coins = coins,
			Date = DateTime.Now.ToString("yyyy-MM-dd HH:mm")
		};

		// Update lifetime profile statistics
		data.Profile.TotalGamesPlayed++;
		if (survived) data.Profile.TotalVictories++;
		else data.Profile.TotalBreaches++;

		data.Profile.HighestDayReached = Mathf.Max(data.Profile.HighestDayReached, dayReached);
		data.Profile.AllTimeHighScore = Mathf.Max(data.Profile.AllTimeHighScore, score);
		data.Profile.TotalCoinsEarned += coins;

		// Insert into ranked leaderboard
		data.Leaderboard.Add(entry);
		data.Leaderboard = data.Leaderboard
			.OrderByDescending(e => e.Score)
			.ThenByDescending(e => e.DayReached)
			.Take(10)
			.ToList();

		int rank = data.Leaderboard.IndexOf(entry) + 1; // 1-indexed rank, or 0 if knocked off top 10
		SaveDirect();
		return rank;
	}

	public static void RecordMerge()
	{
		CurrentData.Profile.TotalMerges++;
		SaveDirect();
	}

	public static void RecordAttackRepelled()
	{
		CurrentData.Profile.TotalAttacksRepelled++;
		SaveDirect();
	}

	public static void SetPlayerName(string name)
	{
		if (!string.IsNullOrWhiteSpace(name))
		{
			CurrentData.Profile.PlayerName = name.Trim();
			SaveDirect();
		}
	}

	public static string GetLeaderboardFormatted(LeaderboardEntry highlightEntry = null, int maxDisplay = 5)
	{
		var data = CurrentData;
		var lines = new List<string> { "RANK   NAME                 SCORE" };

		int count = Math.Min(data.Leaderboard.Count, maxDisplay);
		for (int i = 0; i < count; i++)
		{
			var item = data.Leaderboard[i];
			int rank = i + 1;
			bool isHighlight = highlightEntry != null && ReferenceEquals(item, highlightEntry);

			string name = item.PlayerName;
			if (name.Length > 18) name = name.Substring(0, 15) + "...";

			string line = isHighlight
				? $"#{rank,-4} >>> {name,-16} {item.Score,4} pts <<<"
				: $"#{rank,-4} {name,-20} {item.Score,4} pts";

			lines.Add(line);
		}

		return string.Join("\n", lines);
	}
}
