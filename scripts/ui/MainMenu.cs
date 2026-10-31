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
// - Oct 31, 2026 | air : Streamlined audio volume settings using shared AudioSettingsBinder
// - Oct 31, 2026 | air : Parameterized leaderboard row construction and consolidated modal navigation
// =========================================================================================
using Godot;

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

		if (PlayButton != null) PlayButton.Pressed += OnPlayPressed;
		if (SettingsButton != null) SettingsButton.Pressed += OnSettingsPressed;
		if (LeaderboardButton != null) LeaderboardButton.Pressed += OnLeaderboardPressed;
		if (HowToPlayButton != null) HowToPlayButton.Pressed += () => ShowModal(HowToPlayModal);
		if (QuitButton != null) QuitButton.Pressed += () => GetTree().Quit();

		if (StartGameButton != null) StartGameButton.Pressed += OnStartGamePressed;
		if (CancelNameButton != null) CancelNameButton.Pressed += () => HideModal(NameEntryModal);
		if (NameInput != null) NameInput.TextSubmitted += _ => OnStartGamePressed();

		AudioSettingsBinder.BindSliders(MasterSlider, MasterValLabel, MusicSlider, MusicValLabel, SFXSlider, SFXValLabel);
		if (FullscreenCheck != null) FullscreenCheck.Toggled += OnFullscreenToggled;
		if (CloseSettingsButton != null) CloseSettingsButton.Pressed += () => HideModal(SettingsModal);
		if (CloseLeaderboardButton != null) CloseLeaderboardButton.Pressed += () => HideModal(LeaderboardModal);
		if (CloseHowToPlayButton != null) CloseHowToPlayButton.Pressed += () => HideModal(HowToPlayModal);

		UpdateProfileDisplay();
		CloseAllModals();
	}

	private void CloseAllModals()
	{
		foreach (var m in new[] { NameEntryModal, SettingsModal, LeaderboardModal, HowToPlayModal })
			if (m != null) m.Visible = false;
	}

	private void ShowModal(Control modal) { CloseAllModals(); if (modal != null) modal.Visible = true; }
	private void HideModal(Control modal) { if (modal != null) modal.Visible = false; UpdateProfileDisplay(); }

	private void UpdateProfileDisplay()
	{
		if (CurrentProfileLabel != null)
			CurrentProfileLabel.Text = $"CURRENT CALLSIGN: {SaveManager.CurrentData.Profile.PlayerName.ToUpper()}";
	}

	private void OnPlayPressed()
	{
		string name = SaveManager.CurrentData.Profile.PlayerName;
		if (NameInput != null) { NameInput.Text = string.IsNullOrWhiteSpace(name) ? "Survivor" : name; NameInput.SelectAll(); }
		if (NameErrorLabel != null) NameErrorLabel.Visible = false;
		ShowModal(NameEntryModal);
		NameInput?.GrabFocus();
	}

	private void OnStartGamePressed()
	{
		string name = NameInput?.Text?.Trim();
		if (string.IsNullOrWhiteSpace(name))
		{
			if (NameErrorLabel != null) { NameErrorLabel.Text = "Please enter a valid callsign (1-20 characters)."; NameErrorLabel.Visible = true; }
			return;
		}
		if (name.Length > 20) name = name[..20];
		SaveManager.SetPlayerName(name);
		GetTree().ChangeSceneToFile("res://scenes/game.tscn");
	}

	private void OnSettingsPressed()
	{
		AudioSettingsBinder.SyncSliders(MasterSlider, MasterValLabel, MusicSlider, MusicValLabel, SFXSlider, SFXValLabel);
		if (FullscreenCheck != null)
			FullscreenCheck.ButtonPressed = DisplayServer.WindowGetMode() == DisplayServer.WindowMode.Fullscreen;
		ShowModal(SettingsModal);
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
			LifetimeStatsLabel.Text = $"GAMES: {data.Profile.TotalGamesPlayed}  |  VICTORIES: {data.Profile.TotalVictories}  |  BEST DAY: {data.Profile.HighestDayReached}  |  HIGH SCORE: {data.Profile.AllTimeHighScore}";

		if (LeaderboardRowsContainer != null)
		{
			foreach (Node c in LeaderboardRowsContainer.GetChildren()) c.QueueFree();
			LeaderboardRowsContainer.AddChild(CreateRow(true, new(0.9f, 0.75f, 0.2f), ("#", 45), ("CALLSIGN", 220), ("SCORE", 110), ("DAY", 90), ("STATUS", 120), ("DATE", 160)));

			int rank = 1;
			foreach (var e in data.Leaderboard)
			{
				Color col = e.Survived ? new Color(0.4f, 0.95f, 0.5f) : new Color(0.85f, 0.85f, 0.85f);
				LeaderboardRowsContainer.AddChild(CreateRow(false, col, ($"{rank:00}", 45), (e.PlayerName, 220), (e.Score.ToString(), 110), ($"Day {e.DayReached}", 90), (e.Survived ? "SURVIVED" : "BREACHED", 120), (e.Date, 160)));
				rank++;
			}
		}
		ShowModal(LeaderboardModal);
	}

	private static HBoxContainer CreateRow(bool header, Color color, params (string text, float width)[] cells)
	{
		var row = new HBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
		foreach (var (text, width) in cells)
		{
			var lbl = new Label { Text = text, CustomMinimumSize = new(width, 26), HorizontalAlignment = HorizontalAlignment.Left };
			lbl.AddThemeColorOverride("font_color", color);
			lbl.AddThemeFontSizeOverride("font_size", header ? 16 : 14);
			row.AddChild(lbl);
		}
		return row;
	}
}
