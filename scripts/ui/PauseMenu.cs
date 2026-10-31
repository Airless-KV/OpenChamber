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
// =========================================================================================
using Godot;

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
	[Export] public HSlider MasterSlider;
	[Export] public Label MasterValLabel;
	[Export] public HSlider MusicSlider;
	[Export] public Label MusicValLabel;
	[Export] public HSlider SFXSlider;
	[Export] public Label SFXValLabel;
	[Export] public Button CloseSettingsButton;

	[Export] public Control HowToPlayPanel;
	[Export] public Button CloseHowToPlayButton;

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
		if (RestartButton != null) RestartButton.Pressed += () => { GetTree().Paused = false; GetTree().ReloadCurrentScene(); };
		if (TitleButton != null) TitleButton.Pressed += () => { GetTree().Paused = false; Input.MouseMode = Input.MouseModeEnum.Visible; GetTree().ChangeSceneToFile("res://scenes/ui/main_menu.tscn"); };

		if (CloseSettingsButton != null) CloseSettingsButton.Pressed += ShowMainButtons;
		if (CloseHowToPlayButton != null) CloseHowToPlayButton.Pressed += ShowMainButtons;

		AudioSettingsBinder.BindSliders(MasterSlider, MasterValLabel, MusicSlider, MusicValLabel, SFXSlider, SFXValLabel);
		ShowMainButtons();
	}

	public override void _UnhandledInput(InputEvent @event)
	{
		if (@event is InputEventKey { Pressed: true, Echo: false, Keycode: Key.Escape })
		{
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
	}

	private void ShowPanel(Control panel)
	{
		if (ButtonsContainer != null) ButtonsContainer.Visible = false;
		if (SettingsPanel != null) SettingsPanel.Visible = false;
		if (HowToPlayPanel != null) HowToPlayPanel.Visible = false;
		if (panel != null) panel.Visible = true;
	}

	private void OnSettingsPressed()
	{
		ShowPanel(SettingsPanel);
		AudioSettingsBinder.SyncSliders(MasterSlider, MasterValLabel, MusicSlider, MusicValLabel, SFXSlider, SFXValLabel);
	}
}
