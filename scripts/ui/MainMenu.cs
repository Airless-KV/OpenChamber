// =========================================================================================
// Original Author : air (Airless-KV) - Oct 2026
// Responsibilities: - Main Menu navigation flow (Play, Settings, Leaderboard, How to Play, Quit)
//                   - Player name entry modal and profile initialization
//                   - Settings controls (Volume sliders, Fullscreen toggle)
//                   - Ranked leaderboard standings modal and lifetime stats display
//                   - How to Play gameplay guide presentation
// -----------------------------------------------------------------------------------------
// Change Log:
// - Oct 30, 2026 | air : Implemented full Main Menu navigation flow, name entry, and settings
// =========================================================================================
using Godot;
using System;
using System.Collections.Generic;

public partial class MainMenu : Control
{
	[ExportGroup("Main Navigation Buttons")]
	[Export] public Button PlayButton;
	[Export] public Button SettingsButton;
	[Export] public Button LeaderboardButton;
	[Export] public Button HowToPlayButton;
	[Export] public Button QuitButton;
	[Export] public Label CurrentProfileLabel;

	[ExportGroup("Name Entry Modal")]
	[Export] public Control NameEntryModal;
	[Export] public LineEdit NameInput;
	[Export] public Button StartGameButton;
	[Export] public Button CancelNameButton;
	[Export] public Label NameErrorLabel;

	[ExportGroup("Settings Modal")]
	[Export] public Control SettingsModal;
	[Export] public HSlider MasterSlider;
	[Export] public Label MasterValLabel;
	[Export] public HSlider MusicSlider;
	[Export] public Label MusicValLabel;
	[Export] public HSlider SFXSlider;
	[Export] public Label SFXValLabel;
	[Export] public CheckBox FullscreenCheck;
	[Export] public Button CloseSettingsButton;

	[ExportGroup("Leaderboard Modal")]
	[Export] public Control LeaderboardModal;
	[Export] public Label LifetimeStatsLabel;
	[Export] public VBoxContainer LeaderboardRowsContainer;
	[Export] public Button CloseLeaderboardButton;

	[ExportGroup("How To Play Modal")]
	[Export] public Control HowToPlayModal;
	[Export] public Button CloseHowToPlayButton;

	public override void _Ready()
	{
		Input.MouseMode = Input.MouseModeEnum.Visible;

		// Wire Main Nav
		if (PlayButton != null) PlayButton.Pressed += OnPlayPressed;
		if (SettingsButton != null) SettingsButton.Pressed += OnSettingsPressed;
		if (LeaderboardButton != null) LeaderboardButton.Pressed += OnLeaderboardPressed;
		if (HowToPlayButton != null) HowToPlayButton.Pressed += OnHowToPlayPressed;
		if (QuitButton != null) QuitButton.Pressed += OnQuitPressed;

		// Wire Name Entry Modal
		if (StartGameButton != null) StartGameButton.Pressed += OnStartGamePressed;
		if (CancelNameButton != null) CancelNameButton.Pressed += () => HideModal(NameEntryModal);
		if (NameInput != null)
		{
			NameInput.TextSubmitted += (text) => OnStartGamePressed();
		}

		// Wire Settings
		if (MasterSlider != null) MasterSlider.ValueChanged += OnMasterVolumeChanged;
		if (MusicSlider != null) MusicSlider.ValueChanged += OnMusicVolumeChanged;
		if (SFXSlider != null) SFXSlider.ValueChanged += OnSFXVolumeChanged;
		if (FullscreenCheck != null) FullscreenCheck.Toggled += OnFullscreenToggled;
		if (CloseSettingsButton != null) CloseSettingsButton.Pressed += () => HideModal(SettingsModal);

		// Wire Leaderboard
		if (CloseLeaderboardButton != null) CloseLeaderboardButton.Pressed += () => HideModal(LeaderboardModal);

		// Wire How To Play
		if (CloseHowToPlayButton != null) CloseHowToPlayButton.Pressed += () => HideModal(HowToPlayModal);

		UpdateCurrentProfileDisplay();
		CloseAllModals();
	}

	private void CloseAllModals()
	{
		if (NameEntryModal != null) NameEntryModal.Visible = false;
		if (SettingsModal != null) SettingsModal.Visible = false;
		if (LeaderboardModal != null) LeaderboardModal.Visible = false;
		if (HowToPlayModal != null) HowToPlayModal.Visible = false;
	}

	private void ShowModal(Control modal)
	{
		CloseAllModals();
		if (modal != null) modal.Visible = true;
	}

	private void HideModal(Control modal)
	{
		if (modal != null) modal.Visible = false;
		UpdateCurrentProfileDisplay();
	}

	private void UpdateCurrentProfileDisplay()
	{
		string currentName = SaveManager.CurrentData.Profile.PlayerName;
		if (CurrentProfileLabel != null)
		{
			CurrentProfileLabel.Text = $"CURRENT CALLSIGN: {currentName.ToUpper()}";
		}
	}

	private void OnPlayPressed()
	{
		string currentName = SaveManager.CurrentData.Profile.PlayerName;
		if (NameInput != null)
		{
			NameInput.Text = string.IsNullOrWhiteSpace(currentName) ? "Survivor" : currentName;
			NameInput.SelectAll();
		}
		if (NameErrorLabel != null) NameErrorLabel.Visible = false;

		ShowModal(NameEntryModal);
		NameInput?.GrabFocus();
	}

	private void OnStartGamePressed()
	{
		string enteredName = NameInput?.Text?.Trim();
		if (string.IsNullOrWhiteSpace(enteredName))
		{
			if (NameErrorLabel != null)
			{
				NameErrorLabel.Text = "Please enter a valid callsign (1-20 characters).";
				NameErrorLabel.Visible = true;
			}
			return;
		}

		if (enteredName.Length > 20)
		{
			enteredName = enteredName.Substring(0, 20);
		}

		SaveManager.SetPlayerName(enteredName);
		GetTree().ChangeSceneToFile("res://scenes/game.tscn");
	}

	private void OnSettingsPressed()
	{
		var audio = AudioManager.Instance;
		float master = audio != null ? audio.GetMasterVolume() : SaveManager.CurrentData.Audio.MasterVolume;
		float music = audio != null ? audio.GetMusicVolume() : SaveManager.CurrentData.Audio.MusicVolume;
		float sfx = audio != null ? audio.GetSFXVolume() : SaveManager.CurrentData.Audio.SFXVolume;

		if (MasterSlider != null) MasterSlider.Value = master * 100.0f;
		if (MusicSlider != null) MusicSlider.Value = music * 100.0f;
		if (SFXSlider != null) SFXSlider.Value = sfx * 100.0f;

		UpdateVolumeLabels(master, music, sfx);

		if (FullscreenCheck != null)
		{
			FullscreenCheck.ButtonPressed = DisplayServer.WindowGetMode() == DisplayServer.WindowMode.Fullscreen;
		}

		ShowModal(SettingsModal);
	}

	private void OnMasterVolumeChanged(double val)
	{
		float linear = (float)val / 100.0f;
		AudioManager.Instance?.SetMasterVolume(linear);
		if (MasterValLabel != null) MasterValLabel.Text = $"{Mathf.RoundToInt(val)}%";
	}

	private void OnMusicVolumeChanged(double val)
	{
		float linear = (float)val / 100.0f;
		AudioManager.Instance?.SetMusicVolume(linear);
		if (MusicValLabel != null) MusicValLabel.Text = $"{Mathf.RoundToInt(val)}%";
	}

	private void OnSFXVolumeChanged(double val)
	{
		float linear = (float)val / 100.0f;
		AudioManager.Instance?.SetSFXVolume(linear);
		if (SFXValLabel != null) SFXValLabel.Text = $"{Mathf.RoundToInt(val)}%";
	}

	private void UpdateVolumeLabels(float master, float music, float sfx)
	{
		if (MasterValLabel != null) MasterValLabel.Text = $"{Mathf.RoundToInt(master * 100.0f)}%";
		if (MusicValLabel != null) MusicValLabel.Text = $"{Mathf.RoundToInt(music * 100.0f)}%";
		if (SFXValLabel != null) SFXValLabel.Text = $"{Mathf.RoundToInt(sfx * 100.0f)}%";
	}

	private void OnFullscreenToggled(bool isFullscreen)
	{
		DisplayServer.WindowSetMode(isFullscreen ? DisplayServer.WindowMode.Fullscreen : DisplayServer.WindowMode.Windowed);
		SaveManager.CurrentData.Audio.Fullscreen = isFullscreen;
		SaveManager.SaveDirect();
	}

	private void OnLeaderboardPressed()
	{
		var data = SaveManager.CurrentData;
		if (LifetimeStatsLabel != null)
		{
			LifetimeStatsLabel.Text = $"GAMES PLAYED: {data.Profile.TotalGamesPlayed}  |  VICTORIES: {data.Profile.TotalVictories}  |  BEST DAY: {data.Profile.HighestDayReached}  |  HIGH SCORE: {data.Profile.AllTimeHighScore}";
		}

		if (LeaderboardRowsContainer != null)
		{
			// Clear old rows
			foreach (Node child in LeaderboardRowsContainer.GetChildren())
			{
				child.QueueFree();
			}

			// Header row
			var headerRow = CreateLeaderboardRow("#", "CALLSIGN", "SCORE", "DAY", "STATUS", "DATE", true);
			LeaderboardRowsContainer.AddChild(headerRow);

			// Populate top entries
			int rank = 1;
			foreach (var entry in data.Leaderboard)
			{
				string status = entry.Survived ? "SURVIVED" : "BREACHED";
				var row = CreateLeaderboardRow(
					$"{rank:00}",
					entry.PlayerName,
					entry.Score.ToString(),
					$"Day {entry.DayReached}",
					status,
					entry.Date,
					false
				);
				LeaderboardRowsContainer.AddChild(row);
				rank++;
			}

			if (data.Leaderboard.Count == 0)
			{
				var emptyLabel = new Label
				{
					Text = "No survival records yet. Surpass the night to claim your standing!",
					HorizontalAlignment = HorizontalAlignment.Center
				};
				LeaderboardRowsContainer.AddChild(emptyLabel);
			}
		}

		ShowModal(LeaderboardModal);
	}

	private HBoxContainer CreateLeaderboardRow(string rank, string name, string score, string day, string status, string date, bool isHeader)
	{
		var row = new HBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
		Color col = isHeader ? new Color(0.9f, 0.75f, 0.2f) : (status == "SURVIVED" ? new Color(0.4f, 0.95f, 0.5f) : new Color(0.85f, 0.85f, 0.85f));

		row.AddChild(CreateCell(rank, 45, col, isHeader));
		row.AddChild(CreateCell(name, 220, col, isHeader));
		row.AddChild(CreateCell(score, 110, col, isHeader));
		row.AddChild(CreateCell(day, 90, col, isHeader));
		row.AddChild(CreateCell(status, 120, col, isHeader));
		row.AddChild(CreateCell(date, 160, col, isHeader));

		return row;
	}

	private Label CreateCell(string text, float minWidth, Color color, bool isHeader)
	{
		var label = new Label
		{
			Text = text,
			CustomMinimumSize = new Vector2(minWidth, 26),
			HorizontalAlignment = HorizontalAlignment.Left
		};
		label.AddThemeColorOverride("font_color", color);
		label.AddThemeFontSizeOverride("font_size", isHeader ? 16 : 14);
		return label;
	}

	private void OnHowToPlayPressed()
	{
		ShowModal(HowToPlayModal);
	}

	private void OnQuitPressed()
	{
		GetTree().Quit();
	}
}
