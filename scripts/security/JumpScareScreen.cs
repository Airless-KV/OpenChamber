// =========================================================================================
// Original Author : air (Airless-KV) - Oct 2026
// Responsibilities: - Ani-Ani jumpscare defeat screen with red flash and camera shake
//                   - Synthesized horror screech audio and defeat reason presentation
//                   - Persistent defeat statistics recording via SaveManager
//                   - Restart loop handling
// -----------------------------------------------------------------------------------------
// Change Log:
// - Oct 24, 2026 | air : Streamlined audio synthesis and tween sequences for Alpha build
// - Oct 30, 2026 | air : Integrated SaveManager to persist breach survival stats and lifetime attempts
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

	private AudioStreamPlayer _screechPlayer;

	public override void _Ready()
	{
		Visible = false;
		Layer = 100;

		if (RestartButton != null) RestartButton.Pressed += () => { GetTree().Paused = false; GetTree().ReloadCurrentScene(); };
		_screechPlayer = new AudioStreamPlayer { Name = "JumpscareScreech" };
		AddChild(_screechPlayer);
	}

	public void TriggerJumpScare(string reason, int currentDay, int coins)
	{
		Visible = true;
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

		PlayHorrorScreech();

		if (RedFlashRect != null)
		{
			RedFlashRect.Color = new Color(0.85f, 0.05f, 0.05f, 0.85f);
			CreateTween().TweenProperty(RedFlashRect, "color:a", 0.35f, 0.8f);
		}

		if (MonsterImage != null)
		{
			MonsterImage.Scale = new Vector2(0.3f, 0.3f);
			MonsterImage.PivotOffset = MonsterImage.Size * 0.5f;

			Tween tw = CreateTween();
			tw.TweenProperty(MonsterImage, "scale", new Vector2(1.15f, 1.15f), 0.25f).SetTrans(Tween.TransitionType.Back).SetEase(Tween.EaseType.Out);
			Vector2 orig = MonsterImage.Position;
			for (int i = 0; i < 8; i++)
				tw.TweenProperty(MonsterImage, "position", orig + new Vector2((float)(GD.Randf() * 30 - 15), (float)(GD.Randf() * 30 - 15)), 0.04f);
			tw.TweenProperty(MonsterImage, "position", orig, 0.05f);
		}
	}

	private void PlayHorrorScreech()
	{
		try
		{
			int rate = 22050;
			byte[] data = new byte[(int)(rate * 1.2f)];
			for (int i = 0; i < data.Length; i++)
			{
				float t = (float)i / rate;
				float wave = Mathf.Sign(Mathf.Sin(t * Mathf.Tau * 850f)) * 0.4f + Mathf.Sign(Mathf.Sin(t * Mathf.Tau * 1150f)) * 0.4f + (float)(GD.Randf() * 2 - 1) * 0.4f;
				data[i] = (byte)(sbyte)(wave * Mathf.Clamp(1f - (t / 1.2f), 0f, 1f) * 115f);
			}
			_screechPlayer.Stream = new AudioStreamWav { Format = AudioStreamWav.FormatEnum.Format8Bits, MixRate = rate, Data = data };
			_screechPlayer.Play();
		}
		catch { }
	}
}
