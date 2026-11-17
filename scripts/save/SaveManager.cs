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
// - Nov 15, 2026 | air : Expanded settings schema with resolution, aspect scaling, FOV, VSync, sensitivity, and accessibility
// - Nov 15, 2026 | air : Added RenderScale, ScalingMode, MaxFps, and ShowFpsCounter to AudioSettings
// - Nov 15, 2026 | air : Added IsTutorialCompleted and SetTutorialCompleted persistent tracking
// - Nov 15, 2026 | air : Added RenderingMethod and LiteMode options for low-end hardware support
// - Nov 16, 2026 | air : Added wrappers for relational JOINs, query filtering, callsign search, and aggregate stats
// - Nov 17, 2026 | air : Added ConfirmExitActions setting toggle to AudioSettings schema
// - Nov 17, 2026 | air : Added active 4-digit PIN session security and per-user profile authentication
// - Nov 17, 2026 | air : Implemented recursive in-memory QuickSort algorithm and SQL view wrappers
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
	public bool TutorialCompleted { get; set; } = false;
}

public class AudioSettings
{
	// Audio
	public float MasterVolume { get; set; } = 1.0f;
	public float MusicVolume { get; set; } = 0.8f;
	public float SFXVolume { get; set; } = 1.0f;

	// Display & Video
	public bool Fullscreen { get; set; }
	public int ResolutionWidth { get; set; } = 1920;
	public int ResolutionHeight { get; set; } = 1080;
	public int AspectScaling { get; set; } = 0; // 0 = Expand, 1 = Keep, 2 = KeepWidth, 3 = KeepHeight
	public bool VSync { get; set; } = true;
	public float RenderScale { get; set; } = 1.0f; // 0.5f to 1.0f (3D viewport resolution scaling)
	public int ScalingMode { get; set; } = 0; // 0 = Bilinear (Zero Overhead), 1 = FSR 1.0
	public int MaxFps { get; set; } = 60; // 0 = Unlimited, 30, 60, 90, 120, etc.
	public bool ShowFpsCounter { get; set; } = false;
	public string RenderingMethod { get; set; } = "forward_plus"; // "forward_plus", "mobile", "gl_compatibility"
	public bool LiteMode { get; set; } = false;
	public float Fov { get; set; } = 75.0f;

	// Controls & Accessibility
	public float MouseSensitivity { get; set; } = 0.0035f;
	public bool InvertY { get; set; } = false;
	public bool Headbob { get; set; } = true;
	public bool ScreenShake { get; set; } = true;
	public bool ConfirmExitActions { get; set; } = true;
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

	/// <summary>
	/// Active 4-digit PIN for the current logged-in session.
	/// </summary>
	public static string ActivePlayerPin { get; set; } = "0000";

	public override void _EnterTree()
	{
		if (Instance != null && Instance != this) { QueueFree(); return; }
		Instance = this;
		LoadDirect();
		Callable.From(() => SettingsBinder.ApplyAllSettings(GetTree())).CallDeferred();
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

		// Persist entry to SQLite with 4-digit PIN authentication check and calculate dynamic rank
		int rank = DatabaseManager.InsertLeaderboardEntry(entry, ActivePlayerPin);
		if (rank > 0)
		{
			DatabaseManager.SavePlayerProfile(data.Profile);
		}
		else
		{
			GD.PrintErr($"[SaveManager] Failed to record run under {resolvedName} due to PIN authentication mismatch.");
		}

		// Refresh leaderboard cache with top 10
		data.Leaderboard = DatabaseManager.GetTopLeaderboard(10);
		return rank;
	}

	public static (bool success, string error) LoginOrRegisterPlayer(string playerName, string pin)
	{
		var res = DatabaseManager.AuthenticateOrRegisterPlayer(playerName, pin);
		if (!res.success)
		{
			return (false, res.errorMessage);
		}

		ActivePlayerPin = string.IsNullOrWhiteSpace(pin) ? "0000" : pin.Trim();
		if (res.profile != null)
		{
			CurrentData.Profile = res.profile;
			DatabaseManager.SavePlayerProfile(res.profile);
		}
		return (true, null);
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

	public static bool IsTutorialCompleted()
	{
		if (CurrentData?.Profile != null && CurrentData.Profile.TutorialCompleted)
			return true;

		if (FileAccess.FileExists("user://tutorial_state.json"))
		{
			using var f = FileAccess.Open("user://tutorial_state.json", FileAccess.ModeFlags.Read);
			if (f != null && f.GetAsText().Contains("\"completed\":true"))
				return true;
		}
		return false;
	}

	public static void SetTutorialCompleted(bool completed)
	{
		if (CurrentData?.Profile != null)
		{
			CurrentData.Profile.TutorialCompleted = completed;
			DatabaseManager.SavePlayerProfile(CurrentData.Profile);
		}

		using var f = FileAccess.Open("user://tutorial_state.json", FileAccess.ModeFlags.Write);
		f?.StoreString($"{{\"completed\":{(completed ? "true" : "false")}}}");
	}

	public static List<LeaderboardEntry> GetTopLeaderboard(int limit = 10) => DatabaseManager.GetTopLeaderboard(limit);

	public static List<LeaderboardEntry> GetTopSurvivorsFromView(int limit = 10) => DatabaseManager.GetTopSurvivorsFromView(limit);

	public static List<PlayerCareerSummaryViewRecord> GetPlayerCareerSummariesFromView() => DatabaseManager.GetPlayerCareerSummariesFromView();

	public static List<LeaderboardEntry> GetLeaderboardByFilter(bool? onlyVictories = null, int limit = 10) =>
		DatabaseManager.GetLeaderboardWithPlayerJoin(onlyVictories, limit);

	public static LeaderboardStatsReport GetLeaderboardStats() =>
		DatabaseManager.GetLeaderboardStatsReport();

	public static List<LeaderboardEntry> SearchLeaderboard(string query, int limit = 10) =>
		DatabaseManager.SearchLeaderboardByName(query, limit);

	public static PlayerPublicDossier GetPlayerDossier(string playerName) =>
		DatabaseManager.GetPlayerPublicDossier(playerName);

	public static bool DeleteLeaderboardEntry(int entryId) =>
		DatabaseManager.DeleteLeaderboardEntry(entryId);

	public static int ClearUserLeaderboard() =>
		DatabaseManager.ClearUserLeaderboard();

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

	// =========================================================================================
	// ITEC 104: ALGORITHMIC IN-MEMORY QUICKSORT IMPLEMENTATION
	// =========================================================================================

	/// <summary>
	/// Custom in-memory Quicksort algorithm implementing Divide and Conquer.
	/// Time Complexity: Average O(N log N), Worst-case O(N^2).
	/// Space Complexity: O(log N) auxiliary recursive stack space.
	/// </summary>
	public static void QuickSort(List<LeaderboardEntry> list, int low, int high, Comparison<LeaderboardEntry> comparison)
	{
		if (list == null || low >= high || low < 0) return;

		int pivotIndex = Partition(list, low, high, comparison);
		QuickSort(list, low, pivotIndex - 1, comparison);
		QuickSort(list, pivotIndex + 1, high, comparison);
	}

	private static int Partition(List<LeaderboardEntry> list, int low, int high, Comparison<LeaderboardEntry> comparison)
	{
		LeaderboardEntry pivot = list[high];
		int i = low - 1;

		for (int j = low; j < high; j++)
		{
			if (comparison(list[j], pivot) <= 0)
			{
				i++;
				(list[i], list[j]) = (list[j], list[i]);
			}
		}

		(list[i + 1], list[high]) = (list[high], list[i + 1]);
		return i + 1;
	}

	/// <summary>
	/// Sorts leaderboard entries programmatically using custom in-memory QuickSort.
	/// Demonstrates custom algorithmic data sorting without relying on external SQL ORDER BY.
	/// </summary>
	public static List<LeaderboardEntry> SortLeaderboardAlgorithmically(List<LeaderboardEntry> entries, string criterion = "score")
	{
		if (entries == null || entries.Count <= 1) return entries ?? new List<LeaderboardEntry>();

		var sorted = new List<LeaderboardEntry>(entries);
		Comparison<LeaderboardEntry> comp = criterion.ToLowerInvariant() switch
		{
			"day" => (a, b) => b.DayReached.CompareTo(a.DayReached) != 0 
				? b.DayReached.CompareTo(a.DayReached) 
				: b.Score.CompareTo(a.Score),
			"coins" => (a, b) => b.Coins.CompareTo(a.Coins) != 0 
				? b.Coins.CompareTo(a.Coins) 
				: b.Score.CompareTo(a.Score),
			_ => (a, b) => b.Score.CompareTo(a.Score) != 0 
				? b.Score.CompareTo(a.Score) 
				: b.DayReached.CompareTo(a.DayReached)
		};

		QuickSort(sorted, 0, sorted.Count - 1, comp);
		return sorted;
	}
}
