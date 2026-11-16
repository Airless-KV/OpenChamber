// =========================================================================================
// Original Author : air (Airless-KV) - Nov 2026
// Responsibilities: - SQLite database connection management (user://open_chamber.db)
//                   - Local database schema migration, table initialization, and seeding
//                   - Local leaderboard queries, rank calculation, and persistent match history
//                   - Player profile statistics and system settings persistence
//                   - Relational normalization (players, match_sessions, match_stat_details)
//                   - CRUD, multi-table JOINs, and analytical aggregate query functions
// -----------------------------------------------------------------------------------------
// Change Log:
// - Nov 06, 2026 | air : Created SQLite database layer with local leaderboards and profiles
// - Nov 14, 2026 | air : Updated database filename to open_chamber.db with automatic migration from illusion_of_choice.db
// - Nov 15, 2026 | air : Expanded audio_settings schema with resolution, aspect scaling, FOV, VSync, sensitivity, and accessibility
// - Nov 15, 2026 | air : Added render_scale, scaling_mode, max_fps, and show_fps_counter persistence
// - Nov 15, 2026 | air : Added rendering_method and lite_mode schema migrations and persistence
// - Nov 16, 2026 | air : Normalized schema with players, match_sessions, and match_stat_details; added CRUD, JOIN, and aggregate queries
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
				PRAGMA foreign_keys = ON;

				-- 1. Normalized players entity (3NF)
				CREATE TABLE IF NOT EXISTS players (
					id INTEGER PRIMARY KEY AUTOINCREMENT,
					player_name TEXT NOT NULL UNIQUE,
					created_at TEXT NOT NULL,
					updated_at TEXT NOT NULL
				);

				-- 2. Single-profile summary cache for fast startup
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

				-- 3. System audio, display, and controls configuration
				CREATE TABLE IF NOT EXISTS audio_settings (
					id INTEGER PRIMARY KEY CHECK (id = 1),
					master_volume REAL NOT NULL DEFAULT 1.0,
					music_volume REAL NOT NULL DEFAULT 0.8,
					sfx_volume REAL NOT NULL DEFAULT 1.0,
					fullscreen INTEGER NOT NULL DEFAULT 0,
					resolution_width INTEGER NOT NULL DEFAULT 1920,
					resolution_height INTEGER NOT NULL DEFAULT 1080,
					aspect_scaling INTEGER NOT NULL DEFAULT 0,
					vsync INTEGER NOT NULL DEFAULT 1,
					fov REAL NOT NULL DEFAULT 75.0,
					mouse_sensitivity REAL NOT NULL DEFAULT 0.0035,
					invert_y INTEGER NOT NULL DEFAULT 0,
					headbob INTEGER NOT NULL DEFAULT 1,
					screen_shake INTEGER NOT NULL DEFAULT 1,
					render_scale REAL NOT NULL DEFAULT 1.0,
					scaling_mode INTEGER NOT NULL DEFAULT 0,
					max_fps INTEGER NOT NULL DEFAULT 60,
					show_fps_counter INTEGER NOT NULL DEFAULT 0
				);

				-- 4. Leaderboard records (relational link to players)
				CREATE TABLE IF NOT EXISTS leaderboard (
					id INTEGER PRIMARY KEY AUTOINCREMENT,
					player_id INTEGER DEFAULT 1,
					player_name TEXT NOT NULL,
					score INTEGER NOT NULL,
					day_reached INTEGER NOT NULL,
					survived INTEGER NOT NULL,
					coins INTEGER NOT NULL,
					created_at TEXT NOT NULL
				);

				CREATE INDEX IF NOT EXISTS idx_leaderboard_score 
				ON leaderboard (score DESC, day_reached DESC);

				-- 5. Persistent save slots
				CREATE TABLE IF NOT EXISTS save_slots (
					slot_id INTEGER PRIMARY KEY,
					slot_name TEXT NOT NULL,
					day_number INTEGER NOT NULL DEFAULT 1,
					coins INTEGER NOT NULL DEFAULT 0,
					board_state_json TEXT,
					saved_at TEXT NOT NULL
				);

				-- 6. Match session records for deep analytics
				CREATE TABLE IF NOT EXISTS match_sessions (
					session_id INTEGER PRIMARY KEY AUTOINCREMENT,
					player_id INTEGER NOT NULL DEFAULT 1,
					final_day INTEGER NOT NULL,
					final_score INTEGER NOT NULL,
					survived INTEGER NOT NULL,
					coins_earned INTEGER NOT NULL,
					attacks_repelled INTEGER NOT NULL,
					merges_performed INTEGER NOT NULL,
					session_start TEXT NOT NULL,
					session_end TEXT NOT NULL
				);

				-- 7. Per-night detailed incident telemetry (1:N with match_sessions)
				CREATE TABLE IF NOT EXISTS match_stat_details (
					detail_id INTEGER PRIMARY KEY AUTOINCREMENT,
					session_id INTEGER NOT NULL,
					night_number INTEGER NOT NULL,
					crops_lost INTEGER NOT NULL DEFAULT 0,
					planks_used INTEGER NOT NULL DEFAULT 0,
					breaches_occurred INTEGER NOT NULL DEFAULT 0,
					FOREIGN KEY (session_id) REFERENCES match_sessions(session_id) ON DELETE CASCADE
				);
			";
			cmd.ExecuteNonQuery();

			EnsureSettingsColumns(conn);
			SeedInitialDataIfEmpty(conn);
			CheckAndMigrateLegacyJson(conn);

			_isInitialized = true;
		}
	}

	private static void EnsureSettingsColumns(SqliteConnection conn)
	{
		string[] columns =
		{
			"ALTER TABLE audio_settings ADD COLUMN resolution_width INTEGER NOT NULL DEFAULT 1920;",
			"ALTER TABLE audio_settings ADD COLUMN resolution_height INTEGER NOT NULL DEFAULT 1080;",
			"ALTER TABLE audio_settings ADD COLUMN aspect_scaling INTEGER NOT NULL DEFAULT 0;",
			"ALTER TABLE audio_settings ADD COLUMN vsync INTEGER NOT NULL DEFAULT 1;",
			"ALTER TABLE audio_settings ADD COLUMN fov REAL NOT NULL DEFAULT 75.0;",
			"ALTER TABLE audio_settings ADD COLUMN mouse_sensitivity REAL NOT NULL DEFAULT 0.0035;",
			"ALTER TABLE audio_settings ADD COLUMN invert_y INTEGER NOT NULL DEFAULT 0;",
			"ALTER TABLE audio_settings ADD COLUMN headbob INTEGER NOT NULL DEFAULT 1;",
			"ALTER TABLE audio_settings ADD COLUMN screen_shake INTEGER NOT NULL DEFAULT 1;",
			"ALTER TABLE audio_settings ADD COLUMN render_scale REAL NOT NULL DEFAULT 1.0;",
			"ALTER TABLE audio_settings ADD COLUMN scaling_mode INTEGER NOT NULL DEFAULT 0;",
			"ALTER TABLE audio_settings ADD COLUMN max_fps INTEGER NOT NULL DEFAULT 60;",
			"ALTER TABLE audio_settings ADD COLUMN show_fps_counter INTEGER NOT NULL DEFAULT 0;",
			"ALTER TABLE audio_settings ADD COLUMN rendering_method TEXT NOT NULL DEFAULT 'forward_plus';",
			"ALTER TABLE audio_settings ADD COLUMN lite_mode INTEGER NOT NULL DEFAULT 0;",
			"ALTER TABLE leaderboard ADD COLUMN player_id INTEGER DEFAULT 1;"
		};

		foreach (var colSql in columns)
		{
			try
			{
				using var cmd = conn.CreateCommand();
				cmd.CommandText = colSql;
				cmd.ExecuteNonQuery();
			}
			catch
			{
				// Column already exists in schema
			}
		}
	}

	private static void SeedInitialDataIfEmpty(SqliteConnection conn)
	{
		// Seed default players record if missing (3NF normalized anchor)
		using (var checkPlayersCmd = conn.CreateCommand())
		{
			checkPlayersCmd.CommandText = "SELECT COUNT(*) FROM players WHERE id = 1;";
			long count = (long)(checkPlayersCmd.ExecuteScalar() ?? 0L);
			if (count == 0)
			{
				using var insertPlayer = conn.CreateCommand();
				string now = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
				insertPlayer.CommandText = @"
					INSERT OR IGNORE INTO players (id, player_name, created_at, updated_at)
					VALUES (1, 'Survivor', $created, $updated);
				";
				insertPlayer.Parameters.AddWithValue("$created", now);
				insertPlayer.Parameters.AddWithValue("$updated", now);
				insertPlayer.ExecuteNonQuery();
			}
		}

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
					INSERT INTO audio_settings (
						id, master_volume, music_volume, sfx_volume, fullscreen,
						resolution_width, resolution_height, aspect_scaling,
						vsync, fov, mouse_sensitivity, invert_y, headbob, screen_shake
					) VALUES (1, 1.0, 0.8, 1.0, 0, 1920, 1080, 0, 1, 75.0, 0.0035, 0, 1, 1);
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
			cmd.CommandText = @"
				SELECT master_volume, music_volume, sfx_volume, fullscreen,
				       resolution_width, resolution_height, aspect_scaling,
				       vsync, fov, mouse_sensitivity, invert_y, headbob, screen_shake,
				       render_scale, scaling_mode, max_fps, show_fps_counter,
				       rendering_method, lite_mode
				FROM audio_settings WHERE id = 1;
			";
			using var reader = cmd.ExecuteReader();
			if (reader.Read())
			{
				return new AudioSettings
				{
					MasterVolume = (float)reader.GetDouble(0),
					MusicVolume = (float)reader.GetDouble(1),
					SFXVolume = (float)reader.GetDouble(2),
					Fullscreen = reader.GetInt32(3) == 1,
					ResolutionWidth = reader.GetInt32(4),
					ResolutionHeight = reader.GetInt32(5),
					AspectScaling = reader.GetInt32(6),
					VSync = reader.GetInt32(7) == 1,
					Fov = (float)reader.GetDouble(8),
					MouseSensitivity = (float)reader.GetDouble(9),
					InvertY = reader.GetInt32(10) == 1,
					Headbob = reader.GetInt32(11) == 1,
					ScreenShake = reader.GetInt32(12) == 1,
					RenderScale = reader.FieldCount > 13 ? (float)reader.GetDouble(13) : 1.0f,
					ScalingMode = reader.FieldCount > 14 ? reader.GetInt32(14) : 0,
					MaxFps = reader.FieldCount > 15 ? reader.GetInt32(15) : 60,
					ShowFpsCounter = reader.FieldCount > 16 && reader.GetInt32(16) == 1,
					RenderingMethod = reader.FieldCount > 17 && !reader.IsDBNull(17) ? reader.GetString(17) : "forward_plus",
					LiteMode = reader.FieldCount > 18 && !reader.IsDBNull(18) && reader.GetInt32(18) == 1
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
					fullscreen = $fullscreen,
					resolution_width = $resW,
					resolution_height = $resH,
					aspect_scaling = $aspect,
					vsync = $vsync,
					fov = $fov,
					mouse_sensitivity = $sens,
					invert_y = $invertY,
					headbob = $headbob,
					screen_shake = $screenShake,
					render_scale = $renderScale,
					scaling_mode = $scalingMode,
					max_fps = $maxFps,
					show_fps_counter = $showFps,
					rendering_method = $renderMethod,
					lite_mode = $liteMode
				WHERE id = 1;
			";
			cmd.Parameters.AddWithValue("$master", settings.MasterVolume);
			cmd.Parameters.AddWithValue("$music", settings.MusicVolume);
			cmd.Parameters.AddWithValue("$sfx", settings.SFXVolume);
			cmd.Parameters.AddWithValue("$fullscreen", settings.Fullscreen ? 1 : 0);
			cmd.Parameters.AddWithValue("$resW", settings.ResolutionWidth);
			cmd.Parameters.AddWithValue("$resH", settings.ResolutionHeight);
			cmd.Parameters.AddWithValue("$aspect", settings.AspectScaling);
			cmd.Parameters.AddWithValue("$vsync", settings.VSync ? 1 : 0);
			cmd.Parameters.AddWithValue("$fov", settings.Fov);
			cmd.Parameters.AddWithValue("$sens", settings.MouseSensitivity);
			cmd.Parameters.AddWithValue("$invertY", settings.InvertY ? 1 : 0);
			cmd.Parameters.AddWithValue("$headbob", settings.Headbob ? 1 : 0);
			cmd.Parameters.AddWithValue("$screenShake", settings.ScreenShake ? 1 : 0);
			cmd.Parameters.AddWithValue("$renderScale", settings.RenderScale);
			cmd.Parameters.AddWithValue("$scalingMode", settings.ScalingMode);
			cmd.Parameters.AddWithValue("$maxFps", settings.MaxFps);
			cmd.Parameters.AddWithValue("$showFps", settings.ShowFpsCounter ? 1 : 0);
			cmd.Parameters.AddWithValue("$renderMethod", string.IsNullOrWhiteSpace(settings.RenderingMethod) ? "forward_plus" : settings.RenderingMethod);
			cmd.Parameters.AddWithValue("$liteMode", settings.LiteMode ? 1 : 0);
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

			// Ensure player exists in normalized players table
			int playerId = EnsurePlayerExists(conn, entry.PlayerName);

			using var insertCmd = conn.CreateCommand();
			insertCmd.CommandText = @"
				INSERT INTO leaderboard (player_id, player_name, score, day_reached, survived, coins, created_at)
				VALUES ($playerId, $name, $score, $day, $survived, $coins, $created);
			";
			insertCmd.Parameters.AddWithValue("$playerId", playerId);
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

	private static int EnsurePlayerExists(SqliteConnection conn, string playerName)
	{
		string resolved = string.IsNullOrWhiteSpace(playerName) ? "Survivor" : playerName.Trim();
		using var selectCmd = conn.CreateCommand();
		selectCmd.CommandText = "SELECT id FROM players WHERE player_name = $name LIMIT 1;";
		selectCmd.Parameters.AddWithValue("$name", resolved);
		var res = selectCmd.ExecuteScalar();
		if (res != null && res != DBNull.Value)
		{
			return Convert.ToInt32(res);
		}

		using var insertCmd = conn.CreateCommand();
		string now = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
		insertCmd.CommandText = @"
			INSERT INTO players (player_name, created_at, updated_at)
			VALUES ($name, $created, $updated);
			SELECT last_insert_rowid();
		";
		insertCmd.Parameters.AddWithValue("$name", resolved);
		insertCmd.Parameters.AddWithValue("$created", now);
		insertCmd.Parameters.AddWithValue("$updated", now);
		return Convert.ToInt32(insertCmd.ExecuteScalar() ?? 1);
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

	// =========================================================================================
	// EXPANDED RELATIONAL QUERIES: CRUD, JOINS, AND AGGREGATES
	// =========================================================================================

	/// <summary>
	/// Relational JOIN query: joins leaderboard with players table.
	/// Supports optional victory filter (survived = 1) and customizable limits.
	/// </summary>
	public static List<LeaderboardEntry> GetLeaderboardWithPlayerJoin(bool? onlyVictories = null, int limit = 10)
	{
		Initialize();
		lock (_dbLock)
		{
			var list = new List<LeaderboardEntry>();
			using var conn = new SqliteConnection(GetConnectionString());
			conn.Open();

			using var cmd = conn.CreateCommand();
			string whereClause = onlyVictories.HasValue 
				? "WHERE l.survived = " + (onlyVictories.Value ? "1" : "0") 
				: "";

			cmd.CommandText = $@"
				SELECT COALESCE(p.player_name, l.player_name) AS resolved_name,
				       l.score, l.day_reached, l.survived, l.coins, l.created_at
				FROM leaderboard l
				LEFT JOIN players p ON l.player_id = p.id
				{whereClause}
				ORDER BY l.score DESC, l.day_reached DESC, l.id ASC
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

	/// <summary>
	/// Aggregate query: calculates summary statistics (COUNT, SUM, MAX, AVG)
	/// across all recorded leaderboard matches in SQLite.
	/// </summary>
	public static LeaderboardStatsReport GetLeaderboardStatsReport()
	{
		Initialize();
		lock (_dbLock)
		{
			using var conn = new SqliteConnection(GetConnectionString());
			conn.Open();

			using var cmd = conn.CreateCommand();
			cmd.CommandText = @"
				SELECT 
					COUNT(*) AS total_runs,
					SUM(CASE WHEN survived = 1 THEN 1 ELSE 0 END) AS total_victories,
					COALESCE(MAX(score), 0) AS high_score,
					COALESCE(ROUND(AVG(score), 1), 0.0) AS avg_score,
					COALESCE(SUM(coins), 0) AS total_coins
				FROM leaderboard;
			";

			using var reader = cmd.ExecuteReader();
			if (reader.Read())
			{
				int totalRuns = reader.GetInt32(0);
				int totalWins = reader.IsDBNull(1) ? 0 : reader.GetInt32(1);
				int highScore = reader.GetInt32(2);
				double avgScore = reader.GetDouble(3);
				int totalCoins = reader.GetInt32(4);
				double winRate = totalRuns > 0 ? Math.Round((double)totalWins / totalRuns * 100.0, 1) : 0.0;

				return new LeaderboardStatsReport(totalRuns, totalWins, highScore, avgScore, totalCoins, winRate);
			}
			return new LeaderboardStatsReport(0, 0, 0, 0.0, 0, 0.0);
		}
	}

	/// <summary>
	/// Parameterized search query using SQL LIKE pattern matching to find entries by callsign.
	/// </summary>
	public static List<LeaderboardEntry> SearchLeaderboardByName(string query, int limit = 10)
	{
		Initialize();
		lock (_dbLock)
		{
			var list = new List<LeaderboardEntry>();
			using var conn = new SqliteConnection(GetConnectionString());
			conn.Open();

			using var cmd = conn.CreateCommand();
			cmd.CommandText = @"
				SELECT COALESCE(p.player_name, l.player_name) AS resolved_name,
				       l.score, l.day_reached, l.survived, l.coins, l.created_at
				FROM leaderboard l
				LEFT JOIN players p ON l.player_id = p.id
				WHERE l.player_name LIKE $pattern OR p.player_name LIKE $pattern
				ORDER BY l.score DESC, l.day_reached DESC
				LIMIT $limit;
			";
			cmd.Parameters.AddWithValue("$pattern", $"%{query}%");
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

	/// <summary>
	/// CRUD Delete operation: deletes a specific entry by its primary key ID.
	/// </summary>
	public static bool DeleteLeaderboardEntry(int entryId)
	{
		Initialize();
		lock (_dbLock)
		{
			using var conn = new SqliteConnection(GetConnectionString());
			conn.Open();

			using var cmd = conn.CreateCommand();
			cmd.CommandText = "DELETE FROM leaderboard WHERE id = $id;";
			cmd.Parameters.AddWithValue("$id", entryId);
			int rows = cmd.ExecuteNonQuery();
			return rows > 0;
		}
	}

	/// <summary>
	/// CRUD Delete operation: clears user records while keeping benchmark defaults.
	/// </summary>
	public static int ClearUserLeaderboard()
	{
		Initialize();
		lock (_dbLock)
		{
			using var conn = new SqliteConnection(GetConnectionString());
			conn.Open();

			using var cmd = conn.CreateCommand();
			cmd.CommandText = "DELETE FROM leaderboard WHERE created_at != 'Legacy Record';";
			return cmd.ExecuteNonQuery();
		}
	}

	/// <summary>
	/// CRUD Insert operation: logs a deep match session and its per-night telemetry details.
	/// </summary>
	public static int RecordMatchSession(
		string playerName, 
		int finalDay, 
		int finalScore, 
		bool survived, 
		int coinsEarned, 
		int attacksRepelled, 
		int mergesPerformed,
		List<(int night, int cropsLost, int planksUsed, int breaches)> nightDetails = null)
	{
		Initialize();
		lock (_dbLock)
		{
			using var conn = new SqliteConnection(GetConnectionString());
			conn.Open();

			int playerId = EnsurePlayerExists(conn, playerName);
			string now = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");

			using var cmd = conn.CreateCommand();
			cmd.CommandText = @"
				INSERT INTO match_sessions (
					player_id, final_day, final_score, survived, coins_earned,
					attacks_repelled, merges_performed, session_start, session_end
				) VALUES (
					$playerId, $day, $score, $survived, $coins,
					$repelled, $merges, $start, $end
				);
				SELECT last_insert_rowid();
			";
			cmd.Parameters.AddWithValue("$playerId", playerId);
			cmd.Parameters.AddWithValue("$day", finalDay);
			cmd.Parameters.AddWithValue("$score", finalScore);
			cmd.Parameters.AddWithValue("$survived", survived ? 1 : 0);
			cmd.Parameters.AddWithValue("$coins", coinsEarned);
			cmd.Parameters.AddWithValue("$repelled", attacksRepelled);
			cmd.Parameters.AddWithValue("$merges", mergesPerformed);
			cmd.Parameters.AddWithValue("$start", now);
			cmd.Parameters.AddWithValue("$end", now);

			long sessionId = Convert.ToInt64(cmd.ExecuteScalar() ?? 0);

			if (nightDetails != null && nightDetails.Count > 0 && sessionId > 0)
			{
				foreach (var detail in nightDetails)
				{
					using var detailCmd = conn.CreateCommand();
					detailCmd.CommandText = @"
						INSERT INTO match_stat_details (session_id, night_number, crops_lost, planks_used, breaches_occurred)
						VALUES ($sessionId, $night, $crops, $planks, $breaches);
					";
					detailCmd.Parameters.AddWithValue("$sessionId", sessionId);
					detailCmd.Parameters.AddWithValue("$night", detail.night);
					detailCmd.Parameters.AddWithValue("$crops", detail.cropsLost);
					detailCmd.Parameters.AddWithValue("$planks", detail.planksUsed);
					detailCmd.Parameters.AddWithValue("$breaches", detail.breaches);
					detailCmd.ExecuteNonQuery();
				}
			}

			return (int)sessionId;
		}
	}

	/// <summary>
	/// Multi-table JOIN query: retrieves match sessions joined with their detailed night incidents.
	/// </summary>
	public static List<MatchSessionSummary> GetRecentMatchSessionsWithDetails(int limit = 10)
	{
		Initialize();
		lock (_dbLock)
		{
			var sessions = new List<MatchSessionSummary>();
			using var conn = new SqliteConnection(GetConnectionString());
			conn.Open();

			using var cmd = conn.CreateCommand();
			cmd.CommandText = @"
				SELECT s.session_id, p.player_name, s.final_day, s.final_score, s.survived, 
				       s.coins_earned, s.attacks_repelled, s.merges_performed, s.session_end
				FROM match_sessions s
				INNER JOIN players p ON s.player_id = p.id
				ORDER BY s.session_id DESC
				LIMIT $limit;
			";
			cmd.Parameters.AddWithValue("$limit", limit);

			using (var reader = cmd.ExecuteReader())
			{
				while (reader.Read())
				{
					sessions.Add(new MatchSessionSummary
					{
						SessionId = reader.GetInt32(0),
						PlayerName = reader.GetString(1),
						FinalDay = reader.GetInt32(2),
						FinalScore = reader.GetInt32(3),
						Survived = reader.GetInt32(4) == 1,
						CoinsEarned = reader.GetInt32(5),
						AttacksRepelled = reader.GetInt32(6),
						MergesPerformed = reader.GetInt32(7),
						Timestamp = reader.GetString(8)
					});
				}
			}

			// For each session, fetch per-night incident aggregates
			foreach (var s in sessions)
			{
				using var detailCmd = conn.CreateCommand();
				detailCmd.CommandText = @"
					SELECT night_number, crops_lost, planks_used, breaches_occurred
					FROM match_stat_details
					WHERE session_id = $sessionId
					ORDER BY night_number ASC;
				";
				detailCmd.Parameters.AddWithValue("$sessionId", s.SessionId);
				using var detailReader = detailCmd.ExecuteReader();
				while (detailReader.Read())
				{
					s.NightDetails.Add(new NightDetailRecord
					{
						NightNumber = detailReader.GetInt32(0),
						CropsLost = detailReader.GetInt32(1),
						PlanksUsed = detailReader.GetInt32(2),
						BreachesOccurred = detailReader.GetInt32(3)
					});
				}
			}

			return sessions;
		}
	}
}

public record LeaderboardStatsReport(
	int TotalRuns,
	int TotalVictories,
	int HighScore,
	double AvgScore,
	int TotalCoins,
	double WinRatePercentage
);

public class MatchSessionSummary
{
	public int SessionId { get; set; }
	public string PlayerName { get; set; }
	public int FinalDay { get; set; }
	public int FinalScore { get; set; }
	public bool Survived { get; set; }
	public int CoinsEarned { get; set; }
	public int AttacksRepelled { get; set; }
	public int MergesPerformed { get; set; }
	public string Timestamp { get; set; }
	public List<NightDetailRecord> NightDetails { get; set; } = new();
}

public class NightDetailRecord
{
	public int NightNumber { get; set; }
	public int CropsLost { get; set; }
	public int PlanksUsed { get; set; }
	public int BreachesOccurred { get; set; }
}
