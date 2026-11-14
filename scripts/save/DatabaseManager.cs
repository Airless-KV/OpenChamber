// =========================================================================================
// Original Author : air (Airless-KV) - Nov 2026
// Responsibilities: - SQLite database connection management (user://open_chamber.db)
//                   - Local database schema migration, table initialization, and seeding
//                   - Local leaderboard queries, rank calculation, and persistent match history
//                   - Player profile statistics and system settings persistence
// -----------------------------------------------------------------------------------------
// Change Log:
// - Nov 06, 2026 | air : Created SQLite database layer with local leaderboards and profiles
// - Nov 14, 2026 | air : Updated database filename to open_chamber.db with automatic migration from illusion_of_choice.db
// =========================================================================================
using Godot;
using Microsoft.Data.Sqlite;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

public static class DatabaseManager
{
	private const string DatabaseRelativePath = "user://open_chamber.db";
	private const string LegacyDbPath = "user://illusion_of_choice.db";
	private const string LegacySavePath = "user://save_data.json";
	private static readonly object _dbLock = new();
	private static bool _isInitialized = false;

	public static string GetDatabaseFilePath() => ProjectSettings.GlobalizePath(DatabaseRelativePath);

	public static string GetConnectionString() => $"Data Source={GetDatabaseFilePath()};Mode=ReadWriteCreate;";

	public static void Initialize()
	{
		lock (_dbLock)
		{
			if (_isInitialized) return;

			// Ensure user directory exists
			string dbFile = GetDatabaseFilePath();
			string directory = Path.GetDirectoryName(dbFile);
			if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
			{
				Directory.CreateDirectory(directory);
			}

			// Seamless migration from legacy database filename if needed
			string legacyDbFile = ProjectSettings.GlobalizePath(LegacyDbPath);
			if (File.Exists(legacyDbFile) && !File.Exists(dbFile))
			{
				try
				{
					File.Copy(legacyDbFile, dbFile, false);
					GD.Print("[DatabaseManager] Migrated existing save data from illusion_of_choice.db to open_chamber.db");
				}
				catch (Exception ex)
				{
					GD.PrintErr($"[DatabaseManager] Failed to copy legacy db: {ex.Message}");
				}
			}

			using var conn = new SqliteConnection(GetConnectionString());
			conn.Open();

			using var cmd = conn.CreateCommand();
			cmd.CommandText = @"
				PRAGMA journal_mode = WAL;

				CREATE TABLE IF NOT EXISTS player_profile (
					id INTEGER PRIMARY KEY CHECK (id = 1),
					player_name TEXT NOT NULL DEFAULT 'Survivor',
					total_games_played INTEGER NOT NULL DEFAULT 0,
					total_victories INTEGER NOT NULL DEFAULT 0,
					total_breaches INTEGER NOT NULL DEFAULT 0,
					highest_day_reached INTEGER NOT NULL DEFAULT 1,
					all_time_high_score INTEGER NOT NULL DEFAULT 0,
					total_coins_earned INTEGER NOT NULL DEFAULT 0,
					total_merges INTEGER NOT NULL DEFAULT 0,
					total_attacks_repelled INTEGER NOT NULL DEFAULT 0,
					updated_at TEXT NOT NULL
				);

				CREATE TABLE IF NOT EXISTS audio_settings (
					id INTEGER PRIMARY KEY CHECK (id = 1),
					master_volume REAL NOT NULL DEFAULT 1.0,
					music_volume REAL NOT NULL DEFAULT 0.8,
					sfx_volume REAL NOT NULL DEFAULT 1.0,
					fullscreen INTEGER NOT NULL DEFAULT 0
				);

				CREATE TABLE IF NOT EXISTS leaderboard (
					id INTEGER PRIMARY KEY AUTOINCREMENT,
					player_name TEXT NOT NULL,
					score INTEGER NOT NULL,
					day_reached INTEGER NOT NULL,
					survived INTEGER NOT NULL,
					coins INTEGER NOT NULL,
					created_at TEXT NOT NULL
				);

				CREATE INDEX IF NOT EXISTS idx_leaderboard_score 
				ON leaderboard (score DESC, day_reached DESC);

				CREATE TABLE IF NOT EXISTS save_slots (
					slot_id INTEGER PRIMARY KEY,
					slot_name TEXT NOT NULL,
					day_number INTEGER NOT NULL DEFAULT 1,
					coins INTEGER NOT NULL DEFAULT 0,
					board_state_json TEXT,
					saved_at TEXT NOT NULL
				);
			";
			cmd.ExecuteNonQuery();

			SeedInitialDataIfEmpty(conn);
			CheckAndMigrateLegacyJson(conn);

			_isInitialized = true;
		}
	}

	private static void SeedInitialDataIfEmpty(SqliteConnection conn)
	{
		// Seed default profile if missing
		using (var checkProfileCmd = conn.CreateCommand())
		{
			checkProfileCmd.CommandText = "SELECT COUNT(*) FROM player_profile WHERE id = 1;";
			long count = (long)(checkProfileCmd.ExecuteScalar() ?? 0L);
			if (count == 0)
			{
				using var insertProfile = conn.CreateCommand();
				insertProfile.CommandText = @"
					INSERT INTO player_profile (
						id, player_name, total_games_played, total_victories, total_breaches,
						highest_day_reached, all_time_high_score, total_coins_earned,
						total_merges, total_attacks_repelled, updated_at
					) VALUES (
						1, 'Survivor', 0, 0, 0, 1, 0, 0, 0, 0, $updated
					);
				";
				insertProfile.Parameters.AddWithValue("$updated", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
				insertProfile.ExecuteNonQuery();
			}
		}

		// Seed default audio settings if missing
		using (var checkAudioCmd = conn.CreateCommand())
		{
			checkAudioCmd.CommandText = "SELECT COUNT(*) FROM audio_settings WHERE id = 1;";
			long count = (long)(checkAudioCmd.ExecuteScalar() ?? 0L);
			if (count == 0)
			{
				using var insertAudio = conn.CreateCommand();
				insertAudio.CommandText = @"
					INSERT INTO audio_settings (id, master_volume, music_volume, sfx_volume, fullscreen)
					VALUES (1, 1.0, 0.8, 1.0, 0);
				";
				insertAudio.ExecuteNonQuery();
			}
		}

		// Seed initial benchmark local leaderboard entries if table is empty
		using (var checkLeaderboardCmd = conn.CreateCommand())
		{
			checkLeaderboardCmd.CommandText = "SELECT COUNT(*) FROM leaderboard;";
			long count = (long)(checkLeaderboardCmd.ExecuteScalar() ?? 0L);
			if (count == 0)
			{
				using var seedCmd = conn.CreateCommand();
				seedCmd.CommandText = @"
					INSERT INTO leaderboard (player_name, score, day_reached, survived, coins, created_at) VALUES
					('Grand Champion', 320, 7, 1, 20, 'Legacy Record'),
					('Night Watcher',  240, 7, 1, 15, 'Legacy Record'),
					('Country Farmer', 130, 5, 0, 8,  'Legacy Record'),
					('Ani-Ani Evader', 75,  3, 0, 5,  'Legacy Record');
				";
				seedCmd.ExecuteNonQuery();
			}
		}
	}

	private static void CheckAndMigrateLegacyJson(SqliteConnection conn)
	{
		try
		{
			string globalLegacyPath = ProjectSettings.GlobalizePath(LegacySavePath);
			if (!File.Exists(globalLegacyPath)) return;

			string json = File.ReadAllText(globalLegacyPath);
			var legacyData = JsonSerializer.Deserialize<SaveData>(json);
			if (legacyData == null) return;

			// Migrate Profile
			if (legacyData.Profile != null)
			{
				using var profCmd = conn.CreateCommand();
				profCmd.CommandText = @"
					UPDATE player_profile SET
						player_name = $name,
						total_games_played = MAX(total_games_played, $played),
						total_victories = MAX(total_victories, $victories),
						total_breaches = MAX(total_breaches, $breaches),
						highest_day_reached = MAX(highest_day_reached, $day),
						all_time_high_score = MAX(all_time_high_score, $highScore),
						total_coins_earned = MAX(total_coins_earned, $coins),
						total_merges = MAX(total_merges, $merges),
						total_attacks_repelled = MAX(total_attacks_repelled, $repelled),
						updated_at = $updated
					WHERE id = 1;
				";
				profCmd.Parameters.AddWithValue("$name", legacyData.Profile.PlayerName ?? "Survivor");
				profCmd.Parameters.AddWithValue("$played", legacyData.Profile.TotalGamesPlayed);
				profCmd.Parameters.AddWithValue("$victories", legacyData.Profile.TotalVictories);
				profCmd.Parameters.AddWithValue("$breaches", legacyData.Profile.TotalBreaches);
				profCmd.Parameters.AddWithValue("$day", legacyData.Profile.HighestDayReached);
				profCmd.Parameters.AddWithValue("$highScore", legacyData.Profile.AllTimeHighScore);
				profCmd.Parameters.AddWithValue("$coins", legacyData.Profile.TotalCoinsEarned);
				profCmd.Parameters.AddWithValue("$merges", legacyData.Profile.TotalMerges);
				profCmd.Parameters.AddWithValue("$repelled", legacyData.Profile.TotalAttacksRepelled);
				profCmd.Parameters.AddWithValue("$updated", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
				profCmd.ExecuteNonQuery();
			}

			// Migrate Audio Settings
			if (legacyData.Audio != null)
			{
				using var audioCmd = conn.CreateCommand();
				audioCmd.CommandText = @"
					UPDATE audio_settings SET
						master_volume = $master,
						music_volume = $music,
						sfx_volume = $sfx,
						fullscreen = $fullscreen
					WHERE id = 1;
				";
				audioCmd.Parameters.AddWithValue("$master", legacyData.Audio.MasterVolume);
				audioCmd.Parameters.AddWithValue("$music", legacyData.Audio.MusicVolume);
				audioCmd.Parameters.AddWithValue("$sfx", legacyData.Audio.SFXVolume);
				audioCmd.Parameters.AddWithValue("$fullscreen", legacyData.Audio.Fullscreen ? 1 : 0);
				audioCmd.ExecuteNonQuery();
			}

			// Migrate Leaderboard entries if non-default
			if (legacyData.Leaderboard != null && legacyData.Leaderboard.Count > 0)
			{
				foreach (var entry in legacyData.Leaderboard)
				{
					if (entry.Date == "Legacy Record") continue; // Skip default duplicates

					using var checkDup = conn.CreateCommand();
					checkDup.CommandText = @"
						SELECT COUNT(*) FROM leaderboard 
						WHERE player_name = $name AND score = $score AND created_at = $date;
					";
					checkDup.Parameters.AddWithValue("$name", entry.PlayerName);
					checkDup.Parameters.AddWithValue("$score", entry.Score);
					checkDup.Parameters.AddWithValue("$date", entry.Date);
					long exists = (long)(checkDup.ExecuteScalar() ?? 0L);

					if (exists == 0)
					{
						using var insertEntry = conn.CreateCommand();
						insertEntry.CommandText = @"
							INSERT INTO leaderboard (player_name, score, day_reached, survived, coins, created_at)
							VALUES ($name, $score, $day, $survived, $coins, $date);
						";
						insertEntry.Parameters.AddWithValue("$name", entry.PlayerName);
						insertEntry.Parameters.AddWithValue("$score", entry.Score);
						insertEntry.Parameters.AddWithValue("$day", entry.DayReached);
						insertEntry.Parameters.AddWithValue("$survived", entry.Survived ? 1 : 0);
						insertEntry.Parameters.AddWithValue("$coins", entry.Coins);
						insertEntry.Parameters.AddWithValue("$date", entry.Date);
						insertEntry.ExecuteNonQuery();
					}
				}
			}

			// Rename migrated JSON to preserve backup without re-triggering migration
			string backupPath = globalLegacyPath + ".migrated";
			if (File.Exists(backupPath)) File.Delete(backupPath);
			File.Move(globalLegacyPath, backupPath);
			GD.Print("[DatabaseManager] Successfully migrated legacy save_data.json to SQLite database.");
		}
		catch (Exception ex)
		{
			GD.PrintErr($"[DatabaseManager] Legacy migration exception: {ex.Message}");
		}
	}

	public static PlayerProfile LoadPlayerProfile()
	{
		Initialize();
		lock (_dbLock)
		{
			using var conn = new SqliteConnection(GetConnectionString());
			conn.Open();

			using var cmd = conn.CreateCommand();
			cmd.CommandText = @"
				SELECT player_name, total_games_played, total_victories, total_breaches,
				       highest_day_reached, all_time_high_score, total_coins_earned,
				       total_merges, total_attacks_repelled
				FROM player_profile WHERE id = 1;
			";
			using var reader = cmd.ExecuteReader();
			if (reader.Read())
			{
				return new PlayerProfile
				{
					PlayerName = reader.GetString(0),
					TotalGamesPlayed = reader.GetInt32(1),
					TotalVictories = reader.GetInt32(2),
					TotalBreaches = reader.GetInt32(3),
					HighestDayReached = reader.GetInt32(4),
					AllTimeHighScore = reader.GetInt32(5),
					TotalCoinsEarned = reader.GetInt32(6),
					TotalMerges = reader.GetInt32(7),
					TotalAttacksRepelled = reader.GetInt32(8)
				};
			}
			return new PlayerProfile();
		}
	}

	public static void SavePlayerProfile(PlayerProfile profile)
	{
		if (profile == null) return;
		Initialize();
		lock (_dbLock)
		{
			using var conn = new SqliteConnection(GetConnectionString());
			conn.Open();

			using var cmd = conn.CreateCommand();
			cmd.CommandText = @"
				UPDATE player_profile SET
					player_name = $name,
					total_games_played = $played,
					total_victories = $victories,
					total_breaches = $breaches,
					highest_day_reached = $day,
					all_time_high_score = $highScore,
					total_coins_earned = $coins,
					total_merges = $merges,
					total_attacks_repelled = $repelled,
					updated_at = $updated
				WHERE id = 1;
			";
			cmd.Parameters.AddWithValue("$name", profile.PlayerName ?? "Survivor");
			cmd.Parameters.AddWithValue("$played", profile.TotalGamesPlayed);
			cmd.Parameters.AddWithValue("$victories", profile.TotalVictories);
			cmd.Parameters.AddWithValue("$breaches", profile.TotalBreaches);
			cmd.Parameters.AddWithValue("$day", profile.HighestDayReached);
			cmd.Parameters.AddWithValue("$highScore", profile.AllTimeHighScore);
			cmd.Parameters.AddWithValue("$coins", profile.TotalCoinsEarned);
			cmd.Parameters.AddWithValue("$merges", profile.TotalMerges);
			cmd.Parameters.AddWithValue("$repelled", profile.TotalAttacksRepelled);
			cmd.Parameters.AddWithValue("$updated", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
			cmd.ExecuteNonQuery();
		}
	}

	public static AudioSettings LoadAudioSettings()
	{
		Initialize();
		lock (_dbLock)
		{
			using var conn = new SqliteConnection(GetConnectionString());
			conn.Open();

			using var cmd = conn.CreateCommand();
			cmd.CommandText = "SELECT master_volume, music_volume, sfx_volume, fullscreen FROM audio_settings WHERE id = 1;";
			using var reader = cmd.ExecuteReader();
			if (reader.Read())
			{
				return new AudioSettings
				{
					MasterVolume = (float)reader.GetDouble(0),
					MusicVolume = (float)reader.GetDouble(1),
					SFXVolume = (float)reader.GetDouble(2),
					Fullscreen = reader.GetInt32(3) == 1
				};
			}
			return new AudioSettings();
		}
	}

	public static void SaveAudioSettings(AudioSettings settings)
	{
		if (settings == null) return;
		Initialize();
		lock (_dbLock)
		{
			using var conn = new SqliteConnection(GetConnectionString());
			conn.Open();

			using var cmd = conn.CreateCommand();
			cmd.CommandText = @"
				UPDATE audio_settings SET
					master_volume = $master,
					music_volume = $music,
					sfx_volume = $sfx,
					fullscreen = $fullscreen
				WHERE id = 1;
			";
			cmd.Parameters.AddWithValue("$master", settings.MasterVolume);
			cmd.Parameters.AddWithValue("$music", settings.MusicVolume);
			cmd.Parameters.AddWithValue("$sfx", settings.SFXVolume);
			cmd.Parameters.AddWithValue("$fullscreen", settings.Fullscreen ? 1 : 0);
			cmd.ExecuteNonQuery();
		}
	}

	public static int InsertLeaderboardEntry(LeaderboardEntry entry)
	{
		if (entry == null) return -1;
		Initialize();
		lock (_dbLock)
		{
			using var conn = new SqliteConnection(GetConnectionString());
			conn.Open();

			using var insertCmd = conn.CreateCommand();
			insertCmd.CommandText = @"
				INSERT INTO leaderboard (player_name, score, day_reached, survived, coins, created_at)
				VALUES ($name, $score, $day, $survived, $coins, $created);
			";
			insertCmd.Parameters.AddWithValue("$name", entry.PlayerName);
			insertCmd.Parameters.AddWithValue("$score", entry.Score);
			insertCmd.Parameters.AddWithValue("$day", entry.DayReached);
			insertCmd.Parameters.AddWithValue("$survived", entry.Survived ? 1 : 0);
			insertCmd.Parameters.AddWithValue("$coins", entry.Coins);
			insertCmd.Parameters.AddWithValue("$created", string.IsNullOrEmpty(entry.Date) ? DateTime.Now.ToString("yyyy-MM-dd HH:mm") : entry.Date);
			insertCmd.ExecuteNonQuery();

			// Calculate dynamic arcade rank: Count of entries with higher score (or equal score but higher day reached)
			using var rankCmd = conn.CreateCommand();
			rankCmd.CommandText = @"
				SELECT COUNT(*) + 1 FROM leaderboard
				WHERE score > $score OR (score = $score AND day_reached > $day);
			";
			rankCmd.Parameters.AddWithValue("$score", entry.Score);
			rankCmd.Parameters.AddWithValue("$day", entry.DayReached);
			long rank = (long)(rankCmd.ExecuteScalar() ?? 1L);
			return (int)rank;
		}
	}

	public static List<LeaderboardEntry> GetTopLeaderboard(int limit = 10)
	{
		Initialize();
		lock (_dbLock)
		{
			var list = new List<LeaderboardEntry>();
			using var conn = new SqliteConnection(GetConnectionString());
			conn.Open();

			using var cmd = conn.CreateCommand();
			cmd.CommandText = @"
				SELECT player_name, score, day_reached, survived, coins, created_at
				FROM leaderboard
				ORDER BY score DESC, day_reached DESC, id ASC
				LIMIT $limit;
			";
			cmd.Parameters.AddWithValue("$limit", limit);

			using var reader = cmd.ExecuteReader();
			while (reader.Read())
			{
				list.Add(new LeaderboardEntry
				{
					PlayerName = reader.GetString(0),
					Score = reader.GetInt32(1),
					DayReached = reader.GetInt32(2),
					Survived = reader.GetInt32(3) == 1,
					Coins = reader.GetInt32(4),
					Date = reader.GetString(5)
				});
			}
			return list;
		}
	}

	public static List<LeaderboardEntry> GetAllMatchHistory(int limit = 50)
	{
		Initialize();
		lock (_dbLock)
		{
			var list = new List<LeaderboardEntry>();
			using var conn = new SqliteConnection(GetConnectionString());
			conn.Open();

			using var cmd = conn.CreateCommand();
			cmd.CommandText = @"
				SELECT player_name, score, day_reached, survived, coins, created_at
				FROM leaderboard
				ORDER BY id DESC
				LIMIT $limit;
			";
			cmd.Parameters.AddWithValue("$limit", limit);

			using var reader = cmd.ExecuteReader();
			while (reader.Read())
			{
				list.Add(new LeaderboardEntry
				{
					PlayerName = reader.GetString(0),
					Score = reader.GetInt32(1),
					DayReached = reader.GetInt32(2),
					Survived = reader.GetInt32(3) == 1,
					Coins = reader.GetInt32(4),
					Date = reader.GetString(5)
				});
			}
			return list;
		}
	}

	public static void SaveSlot(int slotId, string slotName, int dayNumber, int coins, string boardStateJson)
	{
		Initialize();
		lock (_dbLock)
		{
			using var conn = new SqliteConnection(GetConnectionString());
			conn.Open();

			using var cmd = conn.CreateCommand();
			cmd.CommandText = @"
				INSERT INTO save_slots (slot_id, slot_name, day_number, coins, board_state_json, saved_at)
				VALUES ($slotId, $slotName, $day, $coins, $boardJson, $savedAt)
				ON CONFLICT(slot_id) DO UPDATE SET
					slot_name = excluded.slot_name,
					day_number = excluded.day_number,
					coins = excluded.coins,
					board_state_json = excluded.board_state_json,
					saved_at = excluded.saved_at;
			";
			cmd.Parameters.AddWithValue("$slotId", slotId);
			cmd.Parameters.AddWithValue("$slotName", slotName ?? $"Slot {slotId}");
			cmd.Parameters.AddWithValue("$day", dayNumber);
			cmd.Parameters.AddWithValue("$coins", coins);
			cmd.Parameters.AddWithValue("$boardJson", boardStateJson ?? "");
			cmd.Parameters.AddWithValue("$savedAt", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
			cmd.ExecuteNonQuery();
		}
	}

	public static (bool exists, string slotName, int dayNumber, int coins, string boardJson, string savedAt) LoadSlot(int slotId)
	{
		Initialize();
		lock (_dbLock)
		{
			using var conn = new SqliteConnection(GetConnectionString());
			conn.Open();

			using var cmd = conn.CreateCommand();
			cmd.CommandText = "SELECT slot_name, day_number, coins, board_state_json, saved_at FROM save_slots WHERE slot_id = $slotId;";
			cmd.Parameters.AddWithValue("$slotId", slotId);

			using var reader = cmd.ExecuteReader();
			if (reader.Read())
			{
				return (
					true,
					reader.GetString(0),
					reader.GetInt32(1),
					reader.GetInt32(2),
					reader.IsDBNull(3) ? "" : reader.GetString(3),
					reader.GetString(4)
				);
			}
			return (false, "", 1, 0, "", "");
		}
	}
}
