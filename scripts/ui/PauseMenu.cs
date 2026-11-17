// =========================================================================================
// Original Author : air (Airless-KV) - Oct 2026
// Responsibilities: - In-game pause menu overlay and ESC key trigger management
//                   - Freezing active phase countdowns and gameplay processing via SceneTree pause
//                   - Mouse capture release and restoration
//                   - Audio volume slider adjustment and persistence via AudioManager
//                   - In-game How to Play guide popup, Restart run, and Title return
// -----------------------------------------------------------------------------------------
// Change Log:
// - Oct 30, 2026 | air : Implemented Pause Menu with countdown freeze, volume sliders, and title navigation
// - Oct 31, 2026 | air : Streamlined audio volume settings using shared AudioSettingsBinder
// - Oct 31, 2026 | air : Compacted ESC handling and sub-panel state management
// - Nov 15, 2026 | air : Expanded in-game settings panel with tabbed audio, display, resolution, aspect scaling, and controls
// - Nov 15, 2026 | air : Added 3D render scale, scaling mode, max FPS, and FPS counter controls
// - Nov 15, 2026 | air : Added rendering backend option, Lite preset button, GPU info label, and restart trigger
// - Nov 17, 2026 | air : Removed in-game guided tutorial button from main game pause menu (tutorial restricted to Main Menu)
// - Nov 17, 2026 | air : Added ExitConfirmModal for Restart Run & Return to Title with shared toggle setting
// - Nov 17, 2026 | air : Bypassed Escape handling when CCTV mode is active so Escape exits CCTV desk
// =========================================================================================
using Godot;
using System;

public partial class PauseMenu : CanvasLayer
{
	[ExportGroup("Main Pause Controls")]
	[Export] public Control PauseContainer;
	[Export] public Control ButtonsContainer;
	[Export] public Button ResumeButton;
	[Export] public Button SettingsButton;
	[Export] public Button HowToPlayButton;
	[Export] public Button RestartButton;
	[Export] public Button TitleButton;

	[ExportGroup("Sub Panels")]
	[Export] public Control SettingsPanel;
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

	[Export] public Control HowToPlayPanel;
	[Export] public Button CloseHowToPlayButton;

	[ExportGroup("Confirmation Modal")]
	[Export] public Control ExitConfirmModal;
	[Export] public Label ExitConfirmTitle;
	[Export] public Label ExitConfirmMessage;
	[Export] public Button ExitConfirmButton;
	[Export] public Button ExitCancelButton;

	private Action _pendingConfirmAction;

	private bool _wasMouseCaptured = true;
	public bool IsPaused => GetTree().Paused && Visible;

	public override void _Ready()
	{
		ProcessMode = ProcessModeEnum.Always;
		Layer = 90;
		Visible = false;

		if (ResumeButton != null) ResumeButton.Pressed += ResumeGame;
		if (SettingsButton != null) SettingsButton.Pressed += OnSettingsPressed;
		if (HowToPlayButton != null) HowToPlayButton.Pressed += () => ShowPanel(HowToPlayPanel);
		if (RestartButton != null) RestartButton.Pressed += OnRestartRequested;
		if (TitleButton != null) TitleButton.Pressed += OnTitleRequested;

		if (ExitConfirmButton != null) ExitConfirmButton.Pressed += OnConfirmExitAction;
		if (ExitCancelButton != null) ExitCancelButton.Pressed += HideExitConfirm;

		if (CloseSettingsButton != null) CloseSettingsButton.Pressed += ShowMainButtons;
		if (CloseHowToPlayButton != null) CloseHowToPlayButton.Pressed += ShowMainButtons;

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
		ShowMainButtons();
	}

	private void OnRestartRequested()
	{
		bool requireConfirm = SaveManager.CurrentData?.Audio?.ConfirmExitActions ?? true;
		if (!requireConfirm)
		{
			ExecuteRestart();
			return;
		}

		PromptExitConfirmation(
			"RESTART RUN?",
			"Are you sure you want to restart this run? All unbanked progress and survival days will be reset.",
			ExecuteRestart
		);
	}

	private void OnTitleRequested()
	{
		bool requireConfirm = SaveManager.CurrentData?.Audio?.ConfirmExitActions ?? true;
		if (!requireConfirm)
		{
			ExecuteReturnToTitle();
			return;
		}

		PromptExitConfirmation(
			"RETURN TO TITLE?",
			"Are you sure you want to return to the title menu? Any unsaved run progress will be lost.",
			ExecuteReturnToTitle
		);
	}

	private void PromptExitConfirmation(string title, string message, Action onConfirm)
	{
		_pendingConfirmAction = onConfirm;
		if (ExitConfirmTitle != null) ExitConfirmTitle.Text = title;
		if (ExitConfirmMessage != null) ExitConfirmMessage.Text = message;
		if (ExitConfirmModal != null) ExitConfirmModal.Visible = true;
	}

	private void OnConfirmExitAction()
	{
		var action = _pendingConfirmAction;
		_pendingConfirmAction = null;
		if (ExitConfirmModal != null) ExitConfirmModal.Visible = false;
		action?.Invoke();
	}

	private void HideExitConfirm()
	{
		_pendingConfirmAction = null;
		if (ExitConfirmModal != null) ExitConfirmModal.Visible = false;
	}

	private void ExecuteRestart()
	{
		GetTree().Paused = false;
		GetTree().ReloadCurrentScene();
	}

	private void ExecuteReturnToTitle()
	{
		GetTree().Paused = false;
		Input.MouseMode = Input.MouseModeEnum.Visible;
		GetTree().ChangeSceneToFile("res://scenes/ui/main_menu.tscn");
	}

	public override void _UnhandledInput(InputEvent @event)
	{
		if (@event is InputEventKey { Pressed: true, Echo: false, Keycode: Key.Escape })
		{
			if (NighttCamera.Instance != null && NighttCamera.Instance.IsNightMode) return;

			if (GetTree().Root.FindChild("JumpScareScreen", true, false) is CanvasLayer { Visible: true } ||
				GetTree().Root.FindChild("VictoryScreen", true, false) is CanvasLayer { Visible: true }) return;

			if (IsPaused)
			{
				if (SettingsPanel?.Visible == true || HowToPlayPanel?.Visible == true) ShowMainButtons();
				else ResumeGame();
			}
			else PauseGame();

			GetViewport().SetInputAsHandled();
		}
	}

	public void PauseGame()
	{
		_wasMouseCaptured = Input.MouseMode == Input.MouseModeEnum.Captured;
		Input.MouseMode = Input.MouseModeEnum.Visible;
		GetTree().Paused = true;
		Visible = true;
		ShowMainButtons();
	}

	public void ResumeGame()
	{
		GetTree().Paused = false;
		Visible = false;
		if (_wasMouseCaptured) Input.MouseMode = Input.MouseModeEnum.Captured;
	}

	private void ShowMainButtons()
	{
		if (ButtonsContainer != null) ButtonsContainer.Visible = true;
		if (SettingsPanel != null) SettingsPanel.Visible = false;
		if (HowToPlayPanel != null) HowToPlayPanel.Visible = false;
		if (ExitConfirmModal != null) ExitConfirmModal.Visible = false;
	}

	private void ShowPanel(Control panel)
	{
		if (ButtonsContainer != null) ButtonsContainer.Visible = false;
		if (SettingsPanel != null) SettingsPanel.Visible = false;
		if (HowToPlayPanel != null) HowToPlayPanel.Visible = false;
		if (ExitConfirmModal != null) ExitConfirmModal.Visible = false;
		if (panel != null) panel.Visible = true;
	}

	private void OnSettingsPressed()
	{
		ShowPanel(SettingsPanel);
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
	}
}
