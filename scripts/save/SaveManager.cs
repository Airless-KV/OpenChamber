// =========================================================================================
// Original Author : air (Airless-KV) - Oct 2026
// Responsibilities: - Persistent local database and save system (user://save_data.json)
//                   - Player profile statistics tracking (survival, merges, attacks repelled)
//                   - Ranked leaderboard standings calculation and formatted table generation
// -----------------------------------------------------------------------------------------
// Change Log:
// - Oct 30, 2026 | air : Implemented persistent JSON save system and ranked leaderboard standings
// - Oct 30, 2026 | air : Added AudioSettings persistence for Master, Music, SFX, and display options
// - Oct 31, 2026 | air : Compacted file I/O, DTO initializers, and leaderboard table formatting
// =========================================================================================
using Godot;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

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
			if (FileAccess.FileExists(SaveFilePath))
			{
				using var file = FileAccess.Open(SaveFilePath, FileAccess.ModeFlags.Read);
				if (file != null)
					_cachedData = JsonSerializer.Deserialize<SaveData>(file.GetAsText());
			}
		}
		catch { }
		return _cachedData ??= CreateDefaultSaveData();
	}

	public static void SaveDirect()
	{
		try
		{
			using var file = FileAccess.Open(SaveFilePath, FileAccess.ModeFlags.Write);
			file?.StoreString(JsonSerializer.Serialize(_cachedData ??= CreateDefaultSaveData(), new JsonSerializerOptions { WriteIndented = true }));
		}
		catch { }
	}

	private static SaveData CreateDefaultSaveData() => new()
	{
		Profile = new PlayerProfile(),
		Audio = new AudioSettings(),
		Leaderboard = new List<LeaderboardEntry>
		{
			new() { PlayerName = "Grand Champion", Score = 320, DayReached = 7, Survived = true, Coins = 20, Date = "Legacy Record" },
			new() { PlayerName = "Night Watcher", Score = 240, DayReached = 7, Survived = true, Coins = 15, Date = "Legacy Record" },
			new() { PlayerName = "Country Farmer", Score = 130, DayReached = 5, Survived = false, Coins = 8, Date = "Legacy Record" },
			new() { PlayerName = "Ani-Ani Evader", Score = 75, DayReached = 3, Survived = false, Coins = 5, Date = "Legacy Record" }
		}
	};

	public static int RecordGameResult(string playerName, int score, int dayReached, bool survived, int coins, out LeaderboardEntry entry)
	{
		var data = CurrentData;
		entry = new LeaderboardEntry
		{
			PlayerName = string.IsNullOrWhiteSpace(playerName) ? data.Profile.PlayerName : playerName.Trim(),
			Score = score,
			DayReached = dayReached,
			Survived = survived,
			Coins = coins,
			Date = DateTime.Now.ToString("yyyy-MM-dd HH:mm")
		};

		data.Profile.TotalGamesPlayed++;
		if (survived) data.Profile.TotalVictories++; else data.Profile.TotalBreaches++;
		data.Profile.HighestDayReached = Mathf.Max(data.Profile.HighestDayReached, dayReached);
		data.Profile.AllTimeHighScore = Mathf.Max(data.Profile.AllTimeHighScore, score);
		data.Profile.TotalCoinsEarned += coins;

		data.Leaderboard.Add(entry);
		data.Leaderboard = data.Leaderboard.OrderByDescending(e => e.Score).ThenByDescending(e => e.DayReached).Take(10).ToList();

		int rank = data.Leaderboard.IndexOf(entry) + 1;
		SaveDirect();
		return rank;
	}

	public static void RecordMerge() { CurrentData.Profile.TotalMerges++; SaveDirect(); }
	public static void RecordAttackRepelled() { CurrentData.Profile.TotalAttacksRepelled++; SaveDirect(); }

	public static void SetPlayerName(string name)
	{
		if (!string.IsNullOrWhiteSpace(name))
		{
			CurrentData.Profile.PlayerName = name.Trim();
			SaveDirect();
		}
	}

	public static string GetLeaderboardFormatted(LeaderboardEntry highlight = null, int maxDisplay = 5)
	{
		var rows = CurrentData.Leaderboard.Take(maxDisplay).Select((e, i) =>
		{
			string name = e.PlayerName.Length > 18 ? e.PlayerName[..15] + "..." : e.PlayerName;
			bool hl = highlight != null && ReferenceEquals(e, highlight);
			return hl ? $"#{i + 1,-4} >>> {name,-16} {e.Score,4} pts <<<" : $"#{i + 1,-4} {name,-20} {e.Score,4} pts";
		});
		return "RANK   NAME                 SCORE\n" + string.Join("\n", rows);
	}
}
