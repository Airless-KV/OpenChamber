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
// - Nov 17, 2026 | air : Added user account authentication, 4-digit PIN security, and per-account lifetime statistics
// - Nov 17, 2026 | air : Added GetPlayerPublicDossier for inspecting other players' extended career statistics
// - Nov 17, 2026 | air : Implemented ACID SqliteTransaction guarantees and relational SQL views (v_top_survivors, v_player_career_summary)
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

				-- 1. Normalized players entity (3NF) with 4-digit PIN authentication & per-user stats
				CREATE TABLE IF NOT EXISTS players (
					id INTEGER PRIMARY KEY AUTOINCREMENT,
					player_name TEXT NOT NULL UNIQUE COLLATE NOCASE,
					pin_code TEXT NOT NULL DEFAULT '0000',
					total_games_played INTEGER NOT NULL DEFAULT 0,
					total_victories INTEGER NOT NULL DEFAULT 0,
					total_breaches INTEGER NOT NULL DEFAULT 0,
					highest_day_reached INTEGER NOT NULL DEFAULT 1,
					all_time_high_score INTEGER NOT NULL DEFAULT 0,
					total_coins_earned INTEGER NOT NULL DEFAULT 0,
					total_merges INTEGER NOT NULL DEFAULT 0,
					total_attacks_repelled INTEGER NOT NULL DEFAULT 0,
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

				-- 8. Relational SQL Views for reporting and data abstraction
				CREATE VIEW IF NOT EXISTS v_top_survivors AS
				SELECT COALESCE(p.player_name, l.player_name) AS player_name,
				       l.score, l.day_reached, l.coins, l.created_at
				FROM leaderboard l
				LEFT JOIN players p ON l.player_id = p.id
				WHERE l.survived = 1
				ORDER BY l.score DESC;

				CREATE VIEW IF NOT EXISTS v_player_career_summary AS
				SELECT player_name,
				       total_games_played,
				       total_victories,
				       total_breaches,
				       highest_day_reached,
				       all_time_high_score,
				       total_coins_earned,
				       CASE WHEN total_games_played > 0 
				            THEN ROUND((CAST(total_victories AS REAL) / total_games_played) * 100.0, 1) 
				            ELSE 0.0 END AS win_rate_percent,
				       created_at AS member_since
				FROM players;
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
			"ALTER TABLE leaderboard ADD COLUMN player_id INTEGER DEFAULT 1;",
			"ALTER TABLE players ADD COLUMN pin_code TEXT NOT NULL DEFAULT '0000';",
			"ALTER TABLE players ADD COLUMN total_games_played INTEGER NOT NULL DEFAULT 0;",
			"ALTER TABLE players ADD COLUMN total_victories INTEGER NOT NULL DEFAULT 0;",
			"ALTER TABLE players ADD COLUMN total_breaches INTEGER NOT NULL DEFAULT 0;",
			"ALTER TABLE players ADD COLUMN highest_day_reached INTEGER NOT NULL DEFAULT 1;",
			"ALTER TABLE players ADD COLUMN all_time_high_score INTEGER NOT NULL DEFAULT 0;",
			"ALTER TABLE players ADD COLUMN total_coins_earned INTEGER NOT NULL DEFAULT 0;",
			"ALTER TABLE players ADD COLUMN total_merges INTEGER NOT NULL DEFAULT 0;",
			"ALTER TABLE players ADD COLUMN total_attacks_repelled INTEGER NOT NULL DEFAULT 0;"
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
		// Seed default players records if missing (Normalized 3NF accounts with 4-digit PINs & career stats)
		using (var checkPlayersCmd = conn.CreateCommand())
		{
			checkPlayersCmd.CommandText = "SELECT COUNT(*) FROM players;";
			long count = (long)(checkPlayersCmd.ExecuteScalar() ?? 0L);
			if (count == 0)
			{
				string date1 = "2026-11-15 10:00:00";
				string date2 = "2026-11-15 11:30:00";
				string date3 = "2026-11-15 12:00:00";
				string date4 = "2026-11-15 13:15:00";

				using var insertPlayer = conn.CreateCommand();
				insertPlayer.CommandText = @"
					INSERT INTO players (
						id, player_name, pin_code, total_games_played, total_victories, total_breaches,
						highest_day_reached, all_time_high_score, total_coins_earned, total_merges,
						total_attacks_repelled, created_at, updated_at
					) VALUES 
					(1, 'air',            '1337', 5, 2, 3, 7, 310, 48, 45, 18, $d1, $d1),
					(2, 'anj',            '1111', 4, 2, 2, 7, 285, 56, 52, 12, $d2, $d2),
					(3, 'rbn',            '2222', 3, 2, 1, 7, 295, 42, 38, 24, $d3, $d3),
					(4, 'khurt',          '3333', 3, 1, 2, 7, 270, 34, 35, 16, $d4, $d4),
					(5, 'Grand Champion', '7777', 4, 3, 1, 7, 320, 60, 60, 20, '2026-11-01 00:00:00', '2026-11-01 00:00:00'),
					(6, 'Night Watcher',  '1024', 3, 1, 2, 7, 240, 30, 30, 15, '2026-11-02 00:00:00', '2026-11-02 00:00:00'),
					(7, 'Country Farmer', '5555', 4, 0, 4, 5, 130, 18, 40,  6, '2026-11-03 00:00:00', '2026-11-03 00:00:00'),
					(8, 'Ani-Ani Evader', '2048', 3, 0, 3, 3,  75, 12, 15,  4, '2026-11-04 00:00:00', '2026-11-04 00:00:00');
				";
				insertPlayer.Parameters.AddWithValue("$d1", date1);
				insertPlayer.Parameters.AddWithValue("$d2", date2);
				insertPlayer.Parameters.AddWithValue("$d3", date3);
				insertPlayer.Parameters.AddWithValue("$d4", date4);
				insertPlayer.ExecuteNonQuery();
			}
		}

		// Seed default active profile if missing
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
						1, 'air', 5, 2, 3, 7, 310, 48, 45, 18, $updated
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

		// Seed rich initial match history runs into leaderboard
		using (var checkLeaderboardCmd = conn.CreateCommand())
		{
			checkLeaderboardCmd.CommandText = "SELECT COUNT(*) FROM leaderboard;";
			long count = (long)(checkLeaderboardCmd.ExecuteScalar() ?? 0L);
			if (count == 0)
			{
				using var seedCmd = conn.CreateCommand();
				seedCmd.CommandText = @"
					INSERT INTO leaderboard (player_id, player_name, score, day_reached, survived, coins, created_at) VALUES
					(5, 'Grand Champion', 320, 7, 1, 20, '2026-11-15 14:00'),
					(1, 'air',            310, 7, 1, 25, '2026-11-16 18:30'),
					(3, 'rbn',            295, 7, 1, 22, '2026-11-16 19:15'),
					(2, 'anj',            285, 7, 1, 30, '2026-11-16 20:00'),
					(4, 'khurt',          270, 7, 1, 18, '2026-11-16 21:10'),
					(6, 'Night Watcher',  240, 7, 1, 15, '2026-11-15 22:00'),
					(2, 'anj',            190, 6, 0, 18, '2026-11-16 12:45'),
					(3, 'rbn',            175, 6, 0, 14, '2026-11-16 14:20'),
					(4, 'khurt',          165, 5, 0, 11, '2026-11-16 16:00'),
					(1, 'air',            160, 5, 0, 12, '2026-11-15 17:30'),
					(7, 'Country Farmer', 130, 5, 0,  8, '2026-11-14 11:20'),
					(8, 'Ani-Ani Evader',  75, 3, 0,  5, '2026-11-14 09:10');
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
		return InsertLeaderboardEntry(entry, null);
	}

	public static int InsertLeaderboardEntry(LeaderboardEntry entry, string pin)
	{
		if (entry == null) return -1;
		Initialize();
		lock (_dbLock)
		{
			using var conn = new SqliteConnection(GetConnectionString());
			conn.Open();

			// Authenticate or register player
			string resolvedName = string.IsNullOrWhiteSpace(entry.PlayerName) ? "Survivor" : entry.PlayerName.Trim();
			var (authSuccess, playerId, userProfile, err) = AuthenticateOrRegisterPlayerInternal(conn, resolvedName, pin);
			if (!authSuccess)
			{
				GD.PrintErr($"[DatabaseManager] Upload rejected for '{resolvedName}': {err}");
				return -1;
			}

			// Update per-user lifetime statistics in normalized players table
			using (var updateStatsCmd = conn.CreateCommand())
			{
				updateStatsCmd.CommandText = @"
					UPDATE players SET
						total_games_played = total_games_played + 1,
						total_victories = total_victories + (CASE WHEN $survived = 1 THEN 1 ELSE 0 END),
						total_breaches = total_breaches + (CASE WHEN $survived = 0 THEN 1 ELSE 0 END),
						highest_day_reached = MAX(highest_day_reached, $day),
						all_time_high_score = MAX(all_time_high_score, $score),
						total_coins_earned = total_coins_earned + $coins,
						updated_at = $updated
					WHERE id = $playerId;
				";
				updateStatsCmd.Parameters.AddWithValue("$survived", entry.Survived ? 1 : 0);
				updateStatsCmd.Parameters.AddWithValue("$day", entry.DayReached);
				updateStatsCmd.Parameters.AddWithValue("$score", entry.Score);
				updateStatsCmd.Parameters.AddWithValue("$coins", entry.Coins);
				updateStatsCmd.Parameters.AddWithValue("$updated", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
				updateStatsCmd.Parameters.AddWithValue("$playerId", playerId);
				updateStatsCmd.ExecuteNonQuery();
			}

			using var insertCmd = conn.CreateCommand();
			insertCmd.CommandText = @"
				INSERT INTO leaderboard (player_id, player_name, score, day_reached, survived, coins, created_at)
				VALUES ($playerId, $name, $score, $day, $survived, $coins, $created);
			";
			insertCmd.Parameters.AddWithValue("$playerId", playerId);
			insertCmd.Parameters.AddWithValue("$name", resolvedName);
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

	/// <summary>
	/// Retrieves public career statistics (dossier) for any survivor callsign.
	/// Accurately aggregates recorded match runs and account stats while keeping security PIN private.
	/// </summary>
	public static PlayerPublicDossier GetPlayerPublicDossier(string playerName)
	{
		Initialize();
		lock (_dbLock)
		{
			using var conn = new SqliteConnection(GetConnectionString());
			conn.Open();

			string resolvedName = string.IsNullOrWhiteSpace(playerName) ? "Survivor" : playerName.Trim();

			// 1. Fetch persistent account metadata & farming/combat stats
			bool accountExists = false;
			string created = "Legacy Record";
			int merges = 0;
			int repelled = 0;

			using (var cmd = conn.CreateCommand())
			{
				cmd.CommandText = @"
					SELECT player_name, created_at, total_merges, total_attacks_repelled
					FROM players
					WHERE player_name = $name COLLATE NOCASE
					LIMIT 1;
				";
				cmd.Parameters.AddWithValue("$name", resolvedName);

				using var reader = cmd.ExecuteReader();
				if (reader.Read())
				{
					accountExists = true;
					resolvedName = reader.GetString(0);
					created = reader.GetString(1);
					merges = reader.GetInt32(2);
					repelled = reader.GetInt32(3);
				}
			}

			// 2. Dynamically aggregate all match history runs recorded for this callsign
			using (var lbCmd = conn.CreateCommand())
			{
				lbCmd.CommandText = @"
					SELECT COALESCE(COUNT(*), 0),
					       COALESCE(SUM(CASE WHEN survived = 1 THEN 1 ELSE 0 END), 0),
					       COALESCE(SUM(CASE WHEN survived = 0 THEN 1 ELSE 0 END), 0),
					       COALESCE(MAX(day_reached), 1),
					       COALESCE(MAX(score), 0),
					       COALESCE(SUM(coins), 0),
						   COALESCE(MIN(created_at), 'Legacy')
					FROM leaderboard
					WHERE player_name = $name COLLATE NOCASE;
				";
				lbCmd.Parameters.AddWithValue("$name", resolvedName);

				using var lbReader = lbCmd.ExecuteReader();
				if (lbReader.Read())
				{
					int games = lbReader.GetInt32(0);
					int wins = lbReader.GetInt32(1);
					int breaches = lbReader.GetInt32(2);
					int bestDay = games > 0 ? lbReader.GetInt32(3) : 1;
					int highScore = lbReader.GetInt32(4);
					int coins = lbReader.GetInt32(5);
					string firstRecordedDate = lbReader.GetString(6);

					if (!accountExists && firstRecordedDate != "Legacy")
					{
						created = firstRecordedDate;
					}

					double winRate = games > 0 ? Math.Round((double)wins / games * 100.0, 1) : 0.0;
					return new PlayerPublicDossier(resolvedName, games, wins, breaches, bestDay, highScore, coins, merges, repelled, winRate, created);
				}
			}

			return new PlayerPublicDossier(resolvedName, 0, 0, 0, 1, 0, 0, 0, 0, 0.0, created);
		}
	}
	public static (bool success, int playerId, PlayerProfile profile, string errorMessage) AuthenticateOrRegisterPlayer(string playerName, string pin)
	{
		Initialize();
		lock (_dbLock)
		{
			using var conn = new SqliteConnection(GetConnectionString());
			conn.Open();
			return AuthenticateOrRegisterPlayerInternal(conn, playerName, pin);
		}
	}

	private static (bool success, int playerId, PlayerProfile profile, string errorMessage) AuthenticateOrRegisterPlayerInternal(SqliteConnection conn, string playerName, string pin)
	{
		string resolvedName = string.IsNullOrWhiteSpace(playerName) ? "Survivor" : playerName.Trim();
		string resolvedPin = string.IsNullOrWhiteSpace(pin) ? "0000" : pin.Trim();

		using var selectCmd = conn.CreateCommand();
		selectCmd.CommandText = @"
			SELECT id, player_name, pin_code, total_games_played, total_victories, total_breaches,
			       highest_day_reached, all_time_high_score, total_coins_earned, total_merges, total_attacks_repelled
			FROM players
			WHERE player_name = $name COLLATE NOCASE
			LIMIT 1;
		";
		selectCmd.Parameters.AddWithValue("$name", resolvedName);

		using var reader = selectCmd.ExecuteReader();
		if (reader.Read())
		{
			int id = reader.GetInt32(0);
			string name = reader.GetString(1);
			string storedPin = reader.GetString(2);

			// Check 4-digit PIN security
			if (!string.Equals(storedPin, resolvedPin, StringComparison.Ordinal))
			{
				return (false, id, null, $"CALLSIGN '{name.ToUpper()}' IS ALREADY CLAIMED. ENTER CORRECT 4-DIGIT PIN TO SIGN IN.");
			}

			var profile = new PlayerProfile
			{
				PlayerName = name,
				TotalGamesPlayed = reader.GetInt32(3),
				TotalVictories = reader.GetInt32(4),
				TotalBreaches = reader.GetInt32(5),
				HighestDayReached = reader.GetInt32(6),
				AllTimeHighScore = reader.GetInt32(7),
				TotalCoinsEarned = reader.GetInt32(8),
				TotalMerges = reader.GetInt32(9),
				TotalAttacksRepelled = reader.GetInt32(10)
			};
			return (true, id, profile, null);
		}
		reader.Close();

		// New Account Registration
		string now = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
		using var insertCmd = conn.CreateCommand();
		insertCmd.CommandText = @"
			INSERT INTO players (
				player_name, pin_code, total_games_played, total_victories, total_breaches,
				highest_day_reached, all_time_high_score, total_coins_earned, total_merges, total_attacks_repelled,
				created_at, updated_at
			) VALUES (
				$name, $pin, 0, 0, 0, 1, 0, 0, 0, 0, $created, $updated
			);
			SELECT last_insert_rowid();
		";
		insertCmd.Parameters.AddWithValue("$name", resolvedName);
		insertCmd.Parameters.AddWithValue("$pin", resolvedPin);
		insertCmd.Parameters.AddWithValue("$created", now);
		insertCmd.Parameters.AddWithValue("$updated", now);

		int newId = Convert.ToInt32(insertCmd.ExecuteScalar() ?? 1);
		var newProfile = new PlayerProfile { PlayerName = resolvedName };
		return (true, newId, newProfile, null);
	}

	private static int EnsurePlayerExists(SqliteConnection conn, string playerName)
	{
		var res = AuthenticateOrRegisterPlayerInternal(conn, playerName, "0000");
		return res.playerId;
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

			using var transaction = conn.BeginTransaction();
			try
			{
				using var cmd = conn.CreateCommand();
				cmd.Transaction = transaction;
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

				transaction.Commit();
			}
			catch (Exception ex)
			{
				transaction.Rollback();
				GD.PrintErr($"[DatabaseManager] SaveSlot transaction rolled back: {ex.Message}");
			}
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
				SELECT l.player_name,
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
				SELECT l.player_name,
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
	/// Executes within an ACID SqliteTransaction to guarantee multi-table atomic consistency.
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

			using var transaction = conn.BeginTransaction();
			try
			{
				int playerId = EnsurePlayerExists(conn, playerName);
				string now = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");

				using var cmd = conn.CreateCommand();
				cmd.Transaction = transaction;
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
						detailCmd.Transaction = transaction;
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

				transaction.Commit();
				return (int)sessionId;
			}
			catch (Exception ex)
			{
				transaction.Rollback();
				GD.PrintErr($"[DatabaseManager] RecordMatchSession transaction rolled back: {ex.Message}");
				return -1;
			}
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

	/// <summary>
	/// Relational View Query: queries the 'v_top_survivors' SQL View.
	/// Demonstrates database abstraction via virtual relational views.
	/// </summary>
	public static List<LeaderboardEntry> GetTopSurvivorsFromView(int limit = 10)
	{
		Initialize();
		lock (_dbLock)
		{
			var list = new List<LeaderboardEntry>();
			using var conn = new SqliteConnection(GetConnectionString());
			conn.Open();

			using var cmd = conn.CreateCommand();
			cmd.CommandText = @"
				SELECT player_name, score, day_reached, coins, created_at
				FROM v_top_survivors
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
					Survived = true,
					Coins = reader.GetInt32(3),
					Date = reader.GetString(4)
				});
			}
			return list;
		}
	}

	/// <summary>
	/// Relational View Query: queries the 'v_player_career_summary' SQL View.
	/// Pre-calculates win rate percentages and aggregate career statistics.
	/// </summary>
	public static List<PlayerCareerSummaryViewRecord> GetPlayerCareerSummariesFromView()
	{
		Initialize();
		lock (_dbLock)
		{
			var list = new List<PlayerCareerSummaryViewRecord>();
			using var conn = new SqliteConnection(GetConnectionString());
			conn.Open();

			using var cmd = conn.CreateCommand();
			cmd.CommandText = @"
				SELECT player_name, total_games_played, total_victories, total_breaches,
				       highest_day_reached, all_time_high_score, total_coins_earned,
				       win_rate_percent, member_since
				FROM v_player_career_summary
				ORDER BY all_time_high_score DESC, total_victories DESC;
			";

			using var reader = cmd.ExecuteReader();
			while (reader.Read())
			{
				list.Add(new PlayerCareerSummaryViewRecord
				{
					PlayerName = reader.GetString(0),
					TotalGamesPlayed = reader.GetInt32(1),
					TotalVictories = reader.GetInt32(2),
					TotalBreaches = reader.GetInt32(3),
					HighestDayReached = reader.GetInt32(4),
					AllTimeHighScore = reader.GetInt32(5),
					TotalCoinsEarned = reader.GetInt32(6),
					WinRatePercent = reader.GetDouble(7),
					MemberSince = reader.GetString(8)
				});
			}
			return list;
		}
	}
}

public class PlayerCareerSummaryViewRecord
{
	public string PlayerName { get; set; }
	public int TotalGamesPlayed { get; set; }
	public int TotalVictories { get; set; }
	public int TotalBreaches { get; set; }
	public int HighestDayReached { get; set; }
	public int AllTimeHighScore { get; set; }
	public int TotalCoinsEarned { get; set; }
	public double WinRatePercent { get; set; }
	public string MemberSince { get; set; }
}

public record LeaderboardStatsReport(
	int TotalRuns,
	int TotalVictories,
	int HighScore,
	double AvgScore,
	int TotalCoins,
	double WinRatePercentage
);

public record PlayerPublicDossier(
	string Callsign,
	int TotalGames,
	int TotalVictories,
	int TotalBreaches,
	int HighestDayReached,
	int AllTimeHighScore,
	int TotalCoinsEarned,
	int TotalMerges,
	int TotalAttacksRepelled,
	double WinRatePercentage,
	string MemberSince
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
