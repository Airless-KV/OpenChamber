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
		Layer = 90; // Renders above 3D game and HUD, below JumpscareScreen (Layer 100)
		Visible = false;

		if (ResumeButton != null) ResumeButton.Pressed += ResumeGame;
		if (SettingsButton != null) SettingsButton.Pressed += OnSettingsPressed;
		if (HowToPlayButton != null) HowToPlayButton.Pressed += OnHowToPlayPressed;
		if (RestartButton != null) RestartButton.Pressed += OnRestartPressed;
		if (TitleButton != null) TitleButton.Pressed += OnTitlePressed;

		if (CloseSettingsButton != null) CloseSettingsButton.Pressed += ShowMainButtons;
		if (CloseHowToPlayButton != null) CloseHowToPlayButton.Pressed += ShowMainButtons;

		if (MasterSlider != null) MasterSlider.ValueChanged += OnMasterVolumeChanged;
		if (MusicSlider != null) MusicSlider.ValueChanged += OnMusicVolumeChanged;
		if (SFXSlider != null) SFXSlider.ValueChanged += OnSFXVolumeChanged;

		ShowMainButtons();
	}

	public override void _UnhandledInput(InputEvent @event)
	{
		if (@event is InputEventKey { Pressed: true, Echo: false } k && k.Keycode == Key.Escape)
		{
			// Check if GameOver/Jumpscare or Victory screen is active
			var jumpscare = GetTree().Root.FindChild("JumpScareScreen", true, false) as JumpScareScreen;
			if (jumpscare != null && jumpscare.Visible) return;

			var victory = GetTree().Root.FindChild("VictoryScreen", true, false) as VictoryScreen;
			if (victory != null && victory.Visible) return;

			if (IsPaused)
			{
				if (SettingsPanel != null && SettingsPanel.Visible)
				{
					ShowMainButtons();
				}
				else if (HowToPlayPanel != null && HowToPlayPanel.Visible)
				{
					ShowMainButtons();
				}
				else
				{
					ResumeGame();
				}
			}
			else
			{
				PauseGame();
			}

			GetViewport().SetInputAsHandled();
		}
	}

	public void PauseGame()
	{
		_wasMouseCaptured = (Input.MouseMode == Input.MouseModeEnum.Captured);
		Input.MouseMode = Input.MouseModeEnum.Visible;

		GetTree().Paused = true;
		Visible = true;
		ShowMainButtons();
	}

	public void ResumeGame()
	{
		GetTree().Paused = false;
		Visible = false;

		if (_wasMouseCaptured)
		{
			Input.MouseMode = Input.MouseModeEnum.Captured;
		}
	}

	private void ShowMainButtons()
	{
		if (ButtonsContainer != null) ButtonsContainer.Visible = true;
		if (SettingsPanel != null) SettingsPanel.Visible = false;
		if (HowToPlayPanel != null) HowToPlayPanel.Visible = false;
	}

	private void OnSettingsPressed()
	{
		if (ButtonsContainer != null) ButtonsContainer.Visible = false;
		if (HowToPlayPanel != null) HowToPlayPanel.Visible = false;
		if (SettingsPanel != null) SettingsPanel.Visible = true;

		var audio = AudioManager.Instance;
		float master = audio != null ? audio.GetMasterVolume() : SaveManager.CurrentData.Audio.MasterVolume;
		float music = audio != null ? audio.GetMusicVolume() : SaveManager.CurrentData.Audio.MusicVolume;
		float sfx = audio != null ? audio.GetSFXVolume() : SaveManager.CurrentData.Audio.SFXVolume;

		if (MasterSlider != null) MasterSlider.Value = master * 100.0f;
		if (MusicSlider != null) MusicSlider.Value = music * 100.0f;
		if (SFXSlider != null) SFXSlider.Value = sfx * 100.0f;

		UpdateVolumeLabels(master, music, sfx);
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

	private void OnHowToPlayPressed()
	{
		if (ButtonsContainer != null) ButtonsContainer.Visible = false;
		if (SettingsPanel != null) SettingsPanel.Visible = false;
		if (HowToPlayPanel != null) HowToPlayPanel.Visible = true;
	}

	private void OnRestartPressed()
	{
		GetTree().Paused = false;
		GetTree().ReloadCurrentScene();
	}

	private void OnTitlePressed()
	{
		GetTree().Paused = false;
		Input.MouseMode = Input.MouseModeEnum.Visible;
		GetTree().ChangeSceneToFile("res://scenes/ui/main_menu.tscn");
	}
}
