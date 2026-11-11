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
// =========================================================================================
using Godot;
using System;

public partial class JumpScareScreen : CanvasLayer
{
	[ExportGroup("UI References")]
	[Export] public Control JumpscareContainer;
	[Export] public TextureRect MonsterImage;
	[Export] public ColorRect RedFlashRect;
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
		if (!Visible) return;

		if (@event is InputEventKey { Pressed: true, Echo: false, Keycode: Key.Escape })
		{
			GetTree().Paused = false;
			Input.MouseMode = Input.MouseModeEnum.Visible;
			GetTree().ChangeSceneToFile("res://scenes/ui/main_menu.tscn");
			GetViewport().SetInputAsHandled();
		}
	}

	public void TriggerJumpScare(string reason, int currentDay, int coins)
	{
		Visible = true;
		GetTree().Paused = true;
		Input.MouseMode = Input.MouseModeEnum.Visible;

		if (TitleLabel != null) TitleLabel.Text = "BREACH DETECTED";
		if (ReasonLabel != null) ReasonLabel.Text = reason;

		// Persist defeat run to local database
		SaveManager.RecordGameResult(
			SaveManager.CurrentData.Profile.PlayerName,
			coins,
			currentDay,
			false,
			coins,
			out _
		);

		var prof = SaveManager.CurrentData.Profile;
		if (StatsLabel != null)
		{
			StatsLabel.Text = $"SURVIVED UNTIL DAY {currentDay}  |  BEST DAY: {prof.HighestDayReached}  |  ATTEMPTS: {prof.TotalGamesPlayed}  |  COINS: {coins}";
		}

		AudioManager.Sfx("jumpscare_stinger");

		if (RedFlashRect != null)
		{
			RedFlashRect.Color = new Color(0.85f, 0.05f, 0.05f, 0.85f);
			CreateTween().SetPauseMode(Tween.TweenPauseMode.Process)
				.TweenProperty(RedFlashRect, "color:a", 0.35f, 0.8f);
		}

		if (MonsterImage != null)
		{
			MonsterImage.Scale = new Vector2(0.3f, 0.3f);
			MonsterImage.PivotOffset = MonsterImage.Size * 0.5f;

			Tween tw = CreateTween().SetPauseMode(Tween.TweenPauseMode.Process);
			tw.TweenProperty(MonsterImage, "scale", new Vector2(1.15f, 1.15f), 0.25f).SetTrans(Tween.TransitionType.Back).SetEase(Tween.EaseType.Out);
			Vector2 orig = MonsterImage.Position;
			for (int i = 0; i < 8; i++)
				tw.TweenProperty(MonsterImage, "position", orig + new Vector2((float)(GD.Randf() * 30 - 15), (float)(GD.Randf() * 30 - 15)), 0.04f);
			tw.TweenProperty(MonsterImage, "position", orig, 0.05f);
		}
	}
}
