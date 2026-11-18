// =========================================================================================
// Original Author : air (Airless-KV) - Oct 2026
// Responsibilities: - Ani-Ani jumpscare defeat screen with red flash and camera shake
//                   - Defeat reason presentation and horror jumpscare audio triggering
//                   - Persistent defeat statistics recording via SaveManager
//                   - Restart and Main Menu loop handling with accessible mouse cursor
// -----------------------------------------------------------------------------------------
// Change Log:
// - Oct 24, 2026 | air : Streamlined audio synthesis and tween sequences for Alpha build
// - Oct 30, 2026 | air : Integrated SaveManager to persist breach survival stats and lifetime attempts
// - Oct 31, 2026 | air : Replaced redundant PCM audio synthesis with AudioManager.Sfx
// - Nov 07, 2026 | air : Released mouse cursor, paused gameplay tree, added MainMenuButton and ESC shortcut
// - Nov 14, 2026 | air : Updated jumpscare texture to high-contrast analog horror face asset
// - Nov 14, 2026 | air : Implemented full-screen edge-to-edge jumpscare scaling, centered pivot lunge, and F11 fullscreen toggle
// - Nov 15, 2026 | air : Added StartDeathVisuals and delayed ShowDefeatSummary for 3D animated death scenes
// - Nov 15, 2026 | air : Added TriggerImpactFlash (receding vignette) and decoupled TriggerFadeToBlackout
// - Nov 18, 2026 | air : Changed death scoring system to include all flower values in total score, database persistence, and defeat summary UI
// =========================================================================================
using Godot;
using System;
using System.Collections.Generic;

public partial class JumpScareScreen : CanvasLayer
{
	[ExportGroup("UI References")]
	[Export] public Control JumpscareContainer;
	[Export] public TextureRect MonsterImage;
	[Export] public ColorRect RedFlashRect;
	[Export] public Control DefeatPanel;
	[Export] public Label TitleLabel;
	[Export] public Label ReasonLabel;
	[Export] public Label StatsLabel;
	[Export] public Button RestartButton;
	[Export] public Button MainMenuButton;

	public override void _Ready()
	{
		ProcessMode = ProcessModeEnum.Always;
		Visible = false;
		Layer = 100;

		if (RestartButton != null)
		{
			RestartButton.Pressed += () =>
			{
				GetTree().Paused = false;
				Input.MouseMode = Input.MouseModeEnum.Captured;
				GetTree().ReloadCurrentScene();
			};
		}

		if (MainMenuButton != null)
		{
			MainMenuButton.Pressed += () =>
			{
				GetTree().Paused = false;
				Input.MouseMode = Input.MouseModeEnum.Visible;
				GetTree().ChangeSceneToFile("res://scenes/ui/main_menu.tscn");
			};
		}
	}

	public override void _UnhandledInput(InputEvent @event)
	{
		if (@event is InputEventKey { Pressed: true, Echo: false } keyEvent)
		{
			if (keyEvent.Keycode == Key.F11 || (keyEvent.Keycode == Key.Enter && keyEvent.AltPressed))
			{
				ToggleFullscreen();
				GetViewport().SetInputAsHandled();
				return;
			}
		}

		if (!Visible) return;

		if (@event is InputEventKey { Pressed: true, Echo: false, Keycode: Key.Escape })
		{
			GetTree().Paused = false;
			Input.MouseMode = Input.MouseModeEnum.Visible;
			GetTree().ChangeSceneToFile("res://scenes/ui/main_menu.tscn");
			GetViewport().SetInputAsHandled();
		}
	}

	public static void ToggleFullscreen()
	{
		var mode = DisplayServer.WindowGetMode();
		if (mode == DisplayServer.WindowMode.Fullscreen || mode == DisplayServer.WindowMode.ExclusiveFullscreen)
		{
			DisplayServer.WindowSetMode(DisplayServer.WindowMode.Windowed);
		}
		else
		{
			DisplayServer.WindowSetMode(DisplayServer.WindowMode.Fullscreen);
		}
	}

	public void StartDeathVisuals()
	{
		Visible = true;
		if (DefeatPanel != null) DefeatPanel.Modulate = new Color(1, 1, 1, 0);
		if (MonsterImage != null) MonsterImage.Visible = false;
		if (RedFlashRect != null) RedFlashRect.Color = new Color(0, 0, 0, 0);
	}

	public void TriggerImpactFlash()
	{
		if (RedFlashRect != null)
		{
			RedFlashRect.Color = new Color(0.85f, 0.05f, 0.05f, 0.65f);
			var tw = CreateTween();
			// Recede smoothly into a subtle bloody vignette so the 3D monster is fully visible while looming!
			tw.TweenProperty(RedFlashRect, "color", new Color(0.35f, 0.02f, 0.02f, 0.18f), 0.50f)
				.SetTrans(Tween.TransitionType.Quad).SetEase(Tween.EaseType.Out);
		}
	}

	public void TriggerFadeToBlackout(float duration = 1.0f, Action onBlackoutComplete = null)
	{
		if (RedFlashRect != null)
		{
			var tw = CreateTween().SetPauseMode(Tween.TweenPauseMode.Process);
			tw.TweenProperty(RedFlashRect, "color", new Color(0.01f, 0.01f, 0.01f, 1.0f), duration)
				.SetTrans(Tween.TransitionType.Quad).SetEase(Tween.EaseType.In);
			if (onBlackoutComplete != null)
			{
				tw.TweenCallback(Callable.From(onBlackoutComplete));
			}
		}
		else
		{
			onBlackoutComplete?.Invoke();
		}
	}

	public void TriggerImpactFlashAndBlackout(Action onBlackoutComplete = null)
	{
		TriggerImpactFlash();
		TriggerFadeToBlackout(1.0f, onBlackoutComplete);
	}

	public void ShowDefeatSummary(string reason, int currentDay, int coins, Dictionary<int, int> flowerCounts = null)
	{
		Visible = true;
		GetTree().Paused = true;
		Input.MouseMode = Input.MouseModeEnum.Visible;

		if (TitleLabel != null) TitleLabel.Text = "BREACH DETECTED";
		if (ReasonLabel != null) ReasonLabel.Text = reason;

		// If flower counts were not passed, attempt discovery from active scene
		flowerCounts ??= Node3d.CollectAllFlowerCounts(GetTree());

		int flowerScore = Node3d.CalculatePointsFromTiers(flowerCounts);
		int totalScore = flowerScore + coins;

		// Persist defeat run to local database with full score including flower values
		int rank = SaveManager.RecordGameResult(
			SaveManager.CurrentData.Profile.PlayerName,
			totalScore,
			currentDay,
			false,
			coins,
			out _
		);

		var prof = SaveManager.CurrentData.Profile;
		if (StatsLabel != null)
		{
			string rankBadge = rank > 0 ? $"  |  RANK #{rank}" : "";
			StatsLabel.Text = $"SCORE: {totalScore} PTS (FLOWERS: {flowerScore} | COINS: {coins}){rankBadge}  |  DAY {currentDay}  |  BEST: {prof.AllTimeHighScore} PTS";
		}

		if (MonsterImage != null)
		{
			MonsterImage.Visible = false;
		}

		if (DefeatPanel != null)
		{
			DefeatPanel.Modulate = new Color(1, 1, 1, 0);
			CreateTween().SetPauseMode(Tween.TweenPauseMode.Process)
				.TweenProperty(DefeatPanel, "modulate:a", 1.0f, 0.40f);
		}
	}

	public void TriggerJumpScare(string reason, int currentDay, int coins, Dictionary<int, int> flowerCounts = null)
	{
		StartDeathVisuals();
		AudioManager.Sfx("jumpscare_stinger");
		TriggerImpactFlashAndBlackout(() =>
		{
			ShowDefeatSummary(reason, currentDay, coins, flowerCounts);
		});
	}
}
