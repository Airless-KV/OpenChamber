// =========================================================================================
// Original Author : air (Airless-KV) - Oct 2026
// Responsibilities: - Main Menu navigation flow (Play, Settings, Leaderboard, How to Play, Quit)
//                   - Player name entry modal and profile initialization
//                   - Settings controls (Volume sliders, Fullscreen toggle)
//                   - Ranked leaderboard standings modal and lifetime stats display
//                   - Interactive tutorial level launching via How to Play button
// -----------------------------------------------------------------------------------------
// Change Log:
// - Oct 30, 2026 | air : Implemented full Main Menu navigation flow, name entry, and settings
// - Oct 31, 2026 | air : Streamlined audio volume settings using shared AudioSettingsBinder
// - Oct 31, 2026 | air : Parameterized leaderboard row construction and consolidated modal navigation
// - Nov 15, 2026 | air : Expanded settings modal with tabbed audio, display, resolution, aspect scaling, and controls
// - Nov 15, 2026 | air : Added rendering backend option, Lite preset button, GPU info label, and restart trigger
// - Nov 16, 2026 | air : Added Are You Sure confirmation modal before launching interactive tutorial level
// - Nov 16, 2026 | air : Added SQL query filtering bar, callsign search, and aggregate stats to leaderboard modal
// - Nov 17, 2026 | air : Integrated Anji's eerie horror title/subtitle fonts and animated CCTV background
// - Nov 17, 2026 | air : Added ConfirmExitCheck to controls settings tab for restart/title confirmation prompt toggle
// - Nov 17, 2026 | air : Added 4-digit PIN authentication to name entry modal for user profile verification
// - Nov 17, 2026 | air : Added clickable leaderboard rows and Survivor Dossier stats modal
// - Nov 17, 2026 | air : Implemented LIFO modal navigation stack and ESC back-navigation
// =========================================================================================
using Godot;
using System.Collections.Generic;

public partial class MainMenu : Control
{
	// =========================================================================================
	// ITEC 104: LIFO MODAL NAVIGATION STACK
	// =========================================================================================
	private readonly Stack<Control> _modalStack = new();
	private Control _activeModal = null;
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
	[Export] public LineEdit PinInput;
	[Export] public Button StartGameButton;
	[Export] public Button CancelNameButton;
	[Export] public Label NameErrorLabel;
	[Export] public Label PinHintLabel;

	[ExportGroup("Settings Modal")]
	[Export] public Control SettingsModal;
	[Export] public Button AudioTabButton;
	[Export] public Button DisplayTabButton;
	[Export] public Button ControlsTabButton;
	[Export] public Control AudioCategory;
	[Export] public Control DisplayCategory;
	[Export] public Control ControlsCategory;

	// Audio
	[Export] public HSlider MasterSlider;
	[Export] public Label MasterValLabel;
	[Export] public HSlider MusicSlider;
	[Export] public Label MusicValLabel;
	[Export] public HSlider SFXSlider;
	[Export] public Label SFXValLabel;

	// Display & Video
	[Export] public OptionButton ResolutionOption;
	[Export] public OptionButton AspectOption;
	[Export] public CheckBox FullscreenCheck;
	[Export] public CheckBox VSyncCheck;
	[Export] public HSlider RenderScaleSlider;
	[Export] public Label RenderScaleValLabel;
	[Export] public OptionButton ScalingModeOption;
	[Export] public OptionButton MaxFpsOption;
	[Export] public CheckBox ShowFpsCheck;
	[Export] public HSlider FovSlider;
	[Export] public Label FovValLabel;
	[Export] public OptionButton BackendOption;
	[Export] public Button ApplyLitePresetButton;
	[Export] public Label GpuInfoLabel;
	[Export] public Control RestartNoticeContainer;
	[Export] public Button RestartNowButton;

	// Controls & Accessibility
	[Export] public HSlider SensitivitySlider;
	[Export] public Label SensitivityValLabel;
	[Export] public CheckBox InvertYCheck;
	[Export] public CheckBox HeadbobCheck;
	[Export] public CheckBox ScreenShakeCheck;
	[Export] public CheckBox ConfirmExitCheck;

	[Export] public Button ResetDefaultsButton;
	[Export] public Button CloseSettingsButton;

	[ExportGroup("Leaderboard Modal")]
	[Export] public Control LeaderboardModal;
	[Export] public Label LifetimeStatsLabel;
	[Export] public VBoxContainer LeaderboardRowsContainer;
	[Export] public Button CloseLeaderboardButton;
	[Export] public Button FilterAllButton;
	[Export] public Button FilterVictoriesButton;
	[Export] public LineEdit LeaderboardSearchInput;

	[ExportGroup("Survivor Dossier Modal")]
	[Export] public Control DossierModal;
	[Export] public Label DossierTitleLabel;
	[Export] public Label DossierSubtitleLabel;
	[Export] public Label DossierCombatStatsLabel;
	[Export] public Label DossierFieldStatsLabel;
	[Export] public Label DossierStatsGridLabel;
	[Export] public Button CloseDossierButton;

	[ExportGroup("How To Play Modal")]
	[Export] public Control HowToPlayModal;
	[Export] public Button CloseHowToPlayButton;

	[ExportGroup("Tutorial Confirmation Modal")]
	[Export] public Control TutorialConfirmModal;
	[Export] public Button ConfirmTutorialButton;
	[Export] public Button CancelTutorialButton;

	public override void _Ready()
	{
		Input.MouseMode = Input.MouseModeEnum.Visible;

		if (PlayButton != null) PlayButton.Pressed += OnPlayPressed;
		if (SettingsButton != null) SettingsButton.Pressed += OnSettingsPressed;
		if (LeaderboardButton != null) LeaderboardButton.Pressed += OnLeaderboardPressed;
		if (HowToPlayButton != null) HowToPlayButton.Pressed += () => ShowModal(TutorialConfirmModal ?? HowToPlayModal);
		if (QuitButton != null) QuitButton.Pressed += () => GetTree().Quit();

		if (StartGameButton != null) StartGameButton.Pressed += OnStartGamePressed;
		if (CancelNameButton != null) CancelNameButton.Pressed += () => HideModal(NameEntryModal);
		if (NameInput != null) NameInput.TextSubmitted += _ => { if (PinInput != null) PinInput.GrabFocus(); else OnStartGamePressed(); };
		if (PinInput != null) PinInput.TextSubmitted += _ => OnStartGamePressed();

		if (ConfirmTutorialButton != null) ConfirmTutorialButton.Pressed += OnHowToPlayPressed;
		if (CancelTutorialButton != null) CancelTutorialButton.Pressed += () => HideModal(TutorialConfirmModal);

		SettingsBinder.BindFullUI(
			GetTree(),
			AudioTabButton, DisplayTabButton, ControlsTabButton,
			AudioCategory, DisplayCategory, ControlsCategory,
			MasterSlider, MasterValLabel,
			MusicSlider, MusicValLabel,
			SFXSlider, SFXValLabel,
			ResolutionOption, AspectOption,
			FullscreenCheck, VSyncCheck,
			RenderScaleSlider, RenderScaleValLabel,
			ScalingModeOption,
			MaxFpsOption,
			ShowFpsCheck,
			FovSlider, FovValLabel,
			SensitivitySlider, SensitivityValLabel,
			InvertYCheck, HeadbobCheck, ScreenShakeCheck,
			ResetDefaultsButton,
			BackendOption,
			ApplyLitePresetButton,
			GpuInfoLabel,
			RestartNoticeContainer,
			RestartNowButton,
			ConfirmExitCheck
		);
		if (CloseSettingsButton != null) CloseSettingsButton.Pressed += () => HideModal(SettingsModal);
		if (CloseLeaderboardButton != null) CloseLeaderboardButton.Pressed += () => HideModal(LeaderboardModal);
		if (CloseHowToPlayButton != null) CloseHowToPlayButton.Pressed += () => HideModal(HowToPlayModal);
		if (CloseDossierButton != null) CloseDossierButton.Pressed += () => { HideModal(DossierModal); ShowModal(LeaderboardModal); };

		if (FilterAllButton != null) FilterAllButton.Pressed += () => LoadLeaderboardData(null);
		if (FilterVictoriesButton != null) FilterVictoriesButton.Pressed += () => LoadLeaderboardData(true);
		if (LeaderboardSearchInput != null)
		{
			LeaderboardSearchInput.TextChanged += text => LoadLeaderboardData(null, text);
		}

		UpdateProfileDisplay();
		CloseAllModals();

		MenuTitleFont.Apply(this);
		MenuSubtitleFont.Apply(this);
		MenuCCTV.Attach(this);
	}

	private void CloseAllModals()
	{
		_modalStack.Clear();
		_activeModal = null;
		foreach (var m in new[] { NameEntryModal, SettingsModal, LeaderboardModal, HowToPlayModal, TutorialConfirmModal, DossierModal })
			if (m != null) m.Visible = false;
	}

	private void ShowModal(Control modal)
	{
		if (modal == null) return;
		if (_activeModal != null && _activeModal != modal)
		{
			_modalStack.Push(_activeModal);
			_activeModal.Visible = false;
		}
		_activeModal = modal;
		modal.Visible = true;
	}

	private void HideModal(Control modal)
	{
		if (modal != null) modal.Visible = false;
		if (_activeModal == modal) _activeModal = null;

		if (_modalStack.Count > 0)
		{
			_activeModal = _modalStack.Pop();
			if (_activeModal != null) _activeModal.Visible = true;
		}
		UpdateProfileDisplay();
	}

	public override void _UnhandledInput(InputEvent @event)
	{
		if (@event.IsActionPressed("ui_cancel") && _activeModal != null)
		{
			HideModal(_activeModal);
			GetViewport().SetInputAsHandled();
		}
	}

	private void UpdateProfileDisplay()
	{
		if (CurrentProfileLabel != null)
			CurrentProfileLabel.Text = $"CURRENT CALLSIGN: {SaveManager.CurrentData.Profile.PlayerName.ToUpper()}";
	}

	private void OnPlayPressed()
	{
		string name = SaveManager.CurrentData.Profile.PlayerName;
		if (NameInput != null) { NameInput.Text = string.IsNullOrWhiteSpace(name) ? "Survivor" : name; NameInput.SelectAll(); }
		if (PinInput != null) { PinInput.Text = SaveManager.ActivePlayerPin ?? "0000"; }
		if (NameErrorLabel != null) NameErrorLabel.Visible = false;
		ShowModal(NameEntryModal);
		NameInput?.GrabFocus();
	}

	private void OnStartGamePressed()
	{
		string name = NameInput?.Text?.Trim();
		string pin = PinInput?.Text?.Trim();

		if (string.IsNullOrWhiteSpace(name))
		{
			if (NameErrorLabel != null) { NameErrorLabel.Text = "Please enter a valid callsign (1-20 characters)."; NameErrorLabel.Visible = true; }
			return;
		}
		if (name.Length > 20) name = name[..20];

		if (string.IsNullOrWhiteSpace(pin) || pin.Length != 4 || !int.TryParse(pin, out _))
		{
			if (NameErrorLabel != null) { NameErrorLabel.Text = "PIN must be exactly 4 numeric digits (e.g. 1234)."; NameErrorLabel.Visible = true; }
			PinInput?.GrabFocus();
			return;
		}

		// Authenticate with SQLite multi-user database
		var (success, error) = SaveManager.LoginOrRegisterPlayer(name, pin);
		if (!success)
		{
			if (NameErrorLabel != null) { NameErrorLabel.Text = error ?? "Authentication failed."; NameErrorLabel.Visible = true; }
			PinInput?.GrabFocus();
			return;
		}

		UpdateProfileDisplay();
		GetTree().ChangeSceneToFile("res://scenes/game.tscn");
	}

	private void OnHowToPlayPressed()
	{
		string name = SaveManager.CurrentData.Profile.PlayerName;
		if (string.IsNullOrWhiteSpace(name))
		{
			SaveManager.SetPlayerName("Survivor");
		}
		Input.MouseMode = Input.MouseModeEnum.Captured;
		GetTree().ChangeSceneToFile("res://scenes/tutorial_level.tscn");
	}

	private void OnSettingsPressed()
	{
		SettingsBinder.SyncFullUI(
			MasterSlider, MasterValLabel,
			MusicSlider, MusicValLabel,
			SFXSlider, SFXValLabel,
			ResolutionOption, AspectOption,
			FullscreenCheck, VSyncCheck,
			RenderScaleSlider, RenderScaleValLabel,
			ScalingModeOption,
			MaxFpsOption,
			ShowFpsCheck,
			FovSlider, FovValLabel,
			SensitivitySlider, SensitivityValLabel,
			InvertYCheck, HeadbobCheck, ScreenShakeCheck,
			BackendOption,
			GpuInfoLabel,
			RestartNoticeContainer,
			ConfirmExitCheck
		);
		ShowModal(SettingsModal);
	}

	private void OnLeaderboardPressed()
	{
		if (LeaderboardSearchInput != null) LeaderboardSearchInput.Text = "";
		LoadLeaderboardData(null);
		ShowModal(LeaderboardModal);
	}

	private void LoadLeaderboardData(bool? onlyVictories = null, string searchQuery = "")
	{
		// 1. Dynamic Aggregate Query from SQLite
		var stats = SaveManager.GetLeaderboardStats();
		if (LifetimeStatsLabel != null)
		{
			LifetimeStatsLabel.Text = $"RUNS: {stats.TotalRuns}  |  WINS: {stats.TotalVictories} ({stats.WinRatePercentage:0.0}%)  |  AVG SCORE: {stats.AvgScore:0.0}  |  HIGH SCORE: {stats.HighScore}";
		}

		// 2. Query execution based on user filter/search
		System.Collections.Generic.List<LeaderboardEntry> entries;
		if (!string.IsNullOrWhiteSpace(searchQuery))
		{
			entries = SaveManager.SearchLeaderboard(searchQuery.Trim(), 20);
		}
		else
		{
			entries = SaveManager.GetLeaderboardByFilter(onlyVictories, 20);
		}

		// ITEC 104: Algorithmic In-Memory Quicksort demonstration
		entries = SaveManager.SortLeaderboardAlgorithmically(entries, "score");

		// 3. Render table rows (adjusted column widths to eliminate horizontal overflow)
		if (LeaderboardRowsContainer != null)
		{
			foreach (Node c in LeaderboardRowsContainer.GetChildren()) c.QueueFree();
			LeaderboardRowsContainer.AddChild(CreateRow(true, new(0.9f, 0.75f, 0.2f), null, ("#", 36), ("CALLSIGN", 175), ("SCORE", 90), ("DAY", 80), ("STATUS", 110), ("DATE", 145)));

			if (entries.Count == 0)
			{
				var emptyRow = new HBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
				var emptyLbl = new Label { Text = "No matching records found.", CustomMinimumSize = new(696, 32), HorizontalAlignment = HorizontalAlignment.Center };
				emptyLbl.AddThemeColorOverride("font_color", new Color(0.6f, 0.6f, 0.6f));
				emptyRow.AddChild(emptyLbl);
				LeaderboardRowsContainer.AddChild(emptyRow);
				return;
			}

			int rank = 1;
			foreach (var e in entries)
			{
				string callsign = e.PlayerName;
				Color col = e.Survived ? new Color(0.4f, 0.95f, 0.5f) : new Color(0.85f, 0.85f, 0.85f);
				LeaderboardRowsContainer.AddChild(CreateRow(false, col, () => OpenSurvivorDossier(callsign), ($"{rank:00}", 36), (callsign, 175), (e.Score.ToString(), 90), ($"Day {e.DayReached}", 80), (e.Survived ? "SURVIVED" : "BREACHED", 110), (e.Date, 145)));
				rank++;
			}
		}
	}

	private void OpenSurvivorDossier(string playerName)
	{
		var dossier = SaveManager.GetPlayerDossier(playerName);
		if (DossierTitleLabel != null)
		{
			DossierTitleLabel.Text = $"SURVIVOR DOSSIER: {dossier.Callsign.ToUpper()}";
		}
		if (DossierSubtitleLabel != null)
		{
			DossierSubtitleLabel.Text = $"Registered Callsign File  •  Active Since: {dossier.MemberSince}";
		}

		if (DossierCombatStatsLabel != null)
		{
			DossierCombatStatsLabel.Text =
				$"TOTAL RUNS LOGGED   : {dossier.TotalGames}\n" +
				$"DAY 7 VICTORIES    : {dossier.TotalVictories}\n" +
				$"CABIN BREACH LOSSES: {dossier.TotalBreaches}\n" +
				$"CAREER ESCAPE RATE : {dossier.WinRatePercentage:0.0}%\n" +
				$"HIGHEST DAY REACHED: Day {dossier.HighestDayReached}\n" +
				$"ALL-TIME HIGH SCORE: {dossier.AllTimeHighScore} pts";
		}

		if (DossierFieldStatsLabel != null)
		{
			DossierFieldStatsLabel.Text =
				$"LIFETIME HARVEST   : {dossier.TotalCoinsEarned} coins\n" +
				$"PLANT MERGES       : {dossier.TotalMerges} merges\n" +
				$"ATTACKS REPELLED   : {dossier.TotalAttacksRepelled} attacks\n" +
				$"SECURITY STATUS    : VERIFIED\n" +
				$"CLASSIFICATION     : RESTRICTED\n" +
				$"CLEARANCE LEVEL    : CABIN OPERATIVE";
		}

		ShowModal(DossierModal);
	}

	private static HBoxContainer CreateRow(bool header, Color color, System.Action onInspect, params (string text, float width)[] cells)
	{
		var row = new HBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
		foreach (var (text, width) in cells)
		{
			var lbl = new Label { Text = text, CustomMinimumSize = new(width, 26), HorizontalAlignment = HorizontalAlignment.Left };
			lbl.AddThemeColorOverride("font_color", color);
			lbl.AddThemeFontSizeOverride("font_size", header ? 15 : 13);
			row.AddChild(lbl);
		}

		if (header)
		{
			var hdr = new Label { Text = "DOSSIER", CustomMinimumSize = new(70, 26), HorizontalAlignment = HorizontalAlignment.Center };
			hdr.AddThemeColorOverride("font_color", color);
			hdr.AddThemeFontSizeOverride("font_size", 14);
			row.AddChild(hdr);
		}
		else if (onInspect != null)
		{
			var btn = new Button
			{
				Text = "VIEW",
				CustomMinimumSize = new(60, 24),
				SizeFlagsVertical = SizeFlags.ShrinkCenter
			};
			btn.AddThemeFontSizeOverride("font_size", 11);
			btn.Pressed += onInspect;
			row.AddChild(btn);
		}
		return row;
	}
}
