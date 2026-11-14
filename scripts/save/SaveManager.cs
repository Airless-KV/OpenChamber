// =========================================================================================
// Original Author : air (Airless-KV) - Oct 2026
// Responsibilities: - Persistent SQLite database interface and save manager (user://open_chamber.db)
//                   - Player profile statistics tracking (survival, merges, attacks repelled)
//                   - Local ranked leaderboard standings calculation and formatted table generation
// -----------------------------------------------------------------------------------------
// Change Log:
// - Oct 30, 2026 | air : Implemented persistent JSON save system and ranked leaderboard standings
// - Oct 30, 2026 | air : Added AudioSettings persistence for Master, Music, SFX, and display options
// - Oct 31, 2026 | air : Compacted file I/O, DTO initializers, and leaderboard table formatting
// - Nov 06, 2026 | air : Migrated persistence backend to local SQLite database with match history and save slots
// - Nov 14, 2026 | air : Updated database interface documentation for open_chamber.db
// =========================================================================================
using Godot;
using System;
using System.Collections.Generic;
using System.Linq;

public class PlayerProfile
{
	public string PlayerName { get; set; } = "Survivor";
	public int TotalGamesPlayed { get; set; }
	public int TotalVictories { get; set; }
	public int TotalBreaches { get; set; }
	public int HighestDayReached { get; set; } = 1;
	public int AllTimeHighScore { get; set; }
	public int TotalCoinsEarned { get; set; }
	public int TotalMerges { get; set; }
	public int TotalAttacksRepelled { get; set; }
}

public class AudioSettings
{
	public float MasterVolume { get; set; } = 1.0f;
	public float MusicVolume { get; set; } = 0.8f;
	public float SFXVolume { get; set; } = 1.0f;
	public bool Fullscreen { get; set; }
}

public class LeaderboardEntry
{
	public string PlayerName { get; set; } = "Survivor";
	public int Score { get; set; }
	public int DayReached { get; set; } = 1;
	public bool Survived { get; set; }
	public int Coins { get; set; }
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
	public static SaveData CurrentData => _cachedData ?? LoadDirect();

	public override void _EnterTree()
	{
		if (Instance != null && Instance != this) { QueueFree(); return; }
		Instance = this;
		LoadDirect();
	}

	public static SaveData LoadDirect()
	{
		try
		{
			DatabaseManager.Initialize();
			_cachedData = new SaveData
			{
				Profile = DatabaseManager.LoadPlayerProfile(),
				Audio = DatabaseManager.LoadAudioSettings(),
				Leaderboard = DatabaseManager.GetTopLeaderboard(10)
			};
		}
		catch (Exception ex)
		{
			GD.PrintErr($"[SaveManager] Failed to load data from SQLite database: {ex.Message}");
			_cachedData = new SaveData();
		}
		return _cachedData;
	}

	public static void SaveDirect()
	{
		try
		{
			if (_cachedData == null) return;
			DatabaseManager.SavePlayerProfile(_cachedData.Profile);
			DatabaseManager.SaveAudioSettings(_cachedData.Audio);
		}
		catch (Exception ex)
		{
			GD.PrintErr($"[SaveManager] Failed to save data to SQLite database: {ex.Message}");
		}
	}

	public static int RecordGameResult(string playerName, int score, int dayReached, bool survived, int coins, out LeaderboardEntry entry)
	{
		var data = CurrentData;
		string resolvedName = string.IsNullOrWhiteSpace(playerName) ? data.Profile.PlayerName : playerName.Trim();

		entry = new LeaderboardEntry
		{
			PlayerName = resolvedName,
			Score = score,
			DayReached = dayReached,
			Survived = survived,
			Coins = coins,
			Date = DateTime.Now.ToString("yyyy-MM-dd HH:mm")
		};

		// Update lifetime profile statistics
		data.Profile.TotalGamesPlayed++;
		if (survived) data.Profile.TotalVictories++; else data.Profile.TotalBreaches++;
		data.Profile.HighestDayReached = Mathf.Max(data.Profile.HighestDayReached, dayReached);
		data.Profile.AllTimeHighScore = Mathf.Max(data.Profile.AllTimeHighScore, score);
		data.Profile.TotalCoinsEarned += coins;

		// Persist entry to SQLite and calculate dynamic rank
		int rank = DatabaseManager.InsertLeaderboardEntry(entry);
		DatabaseManager.SavePlayerProfile(data.Profile);

		// Refresh leaderboard cache with top 10
		data.Leaderboard = DatabaseManager.GetTopLeaderboard(10);
		return rank;
	}

	public static void RecordMerge()
	{
		CurrentData.Profile.TotalMerges++;
		DatabaseManager.SavePlayerProfile(CurrentData.Profile);
	}

	public static void RecordAttackRepelled()
	{
		CurrentData.Profile.TotalAttacksRepelled++;
		DatabaseManager.SavePlayerProfile(CurrentData.Profile);
	}

	public static void SetPlayerName(string name)
	{
		if (!string.IsNullOrWhiteSpace(name))
		{
			CurrentData.Profile.PlayerName = name.Trim();
			DatabaseManager.SavePlayerProfile(CurrentData.Profile);
		}
	}

	public static List<LeaderboardEntry> GetTopLeaderboard(int limit = 10) => DatabaseManager.GetTopLeaderboard(limit);

	public static List<LeaderboardEntry> GetMatchHistory(int limit = 50) => DatabaseManager.GetAllMatchHistory(limit);

	public static void SaveSlot(int slotId, string slotName, int dayNumber, int coins, string boardStateJson) =>
		DatabaseManager.SaveSlot(slotId, slotName, dayNumber, coins, boardStateJson);

	public static (bool exists, string slotName, int dayNumber, int coins, string boardJson, string savedAt) LoadSlot(int slotId) =>
		DatabaseManager.LoadSlot(slotId);

	public static string GetLeaderboardFormatted(LeaderboardEntry highlight = null, int maxDisplay = 5)
	{
		var entries = CurrentData.Leaderboard.Take(maxDisplay).ToList();
		var rows = entries.Select((e, i) =>
		{
			string name = e.PlayerName.Length > 18 ? e.PlayerName[..15] + "..." : e.PlayerName;
			bool hl = highlight != null &&
					  e.PlayerName == highlight.PlayerName &&
					  e.Score == highlight.Score &&
					  e.Date == highlight.Date;
			return hl ? $"#{i + 1,-4} >>> {name,-16} {e.Score,4} pts <<<" : $"#{i + 1,-4} {name,-20} {e.Score,4} pts";
		});
		return "RANK   NAME                 SCORE\n" + string.Join("\n", rows);
	}
}
