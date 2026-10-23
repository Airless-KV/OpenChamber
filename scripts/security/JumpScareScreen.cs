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
		Layer = 100; // Above all other HUD elements

		if (RestartButton != null)
		{
			RestartButton.Pressed += OnRestartButtonPressed;
		}

		_screechPlayer = new AudioStreamPlayer();
		_screechPlayer.Name = "JumpscareScreech";
		AddChild(_screechPlayer);
	}

	public void TriggerJumpScare(string reason, int currentDay, int coins)
	{
		Visible = true;

		if (TitleLabel != null)
			TitleLabel.Text = "BREACH DETECTED";

		if (ReasonLabel != null)
			ReasonLabel.Text = reason;

		if (StatsLabel != null)
			StatsLabel.Text = $"SURVIVED UNTIL DAY {currentDay}  |  TOTAL COINS: {coins}";

		PlayHorrorScreech();
		AnimateJumpscare();
	}

	private void AnimateJumpscare()
	{
		// 1. Violent red screen flash
		if (RedFlashRect != null)
		{
			RedFlashRect.Color = new Color(0.85f, 0.05f, 0.05f, 0.85f);
			Tween flashTween = CreateTween();
			flashTween.TweenProperty(RedFlashRect, "color:a", 0.35f, 0.8f);
		}

		// 2. Monster violent zoom-in & shake
		if (MonsterImage != null)
		{
			MonsterImage.Scale = new Vector2(0.3f, 0.3f);
			MonsterImage.PivotOffset = MonsterImage.Size * 0.5f;

			Tween monsterTween = CreateTween();
			monsterTween.TweenProperty(MonsterImage, "scale", new Vector2(1.15f, 1.15f), 0.25f)
				.SetTrans(Tween.TransitionType.Back)
				.SetEase(Tween.EaseType.Out);

			// Violent shaking
			Vector2 originalPos = MonsterImage.Position;
			for (int i = 0; i < 8; i++)
			{
				float offsetX = (float)(GD.Randf() * 30.0 - 15.0);
				float offsetY = (float)(GD.Randf() * 30.0 - 15.0);
				monsterTween.TweenProperty(MonsterImage, "position", originalPos + new Vector2(offsetX, offsetY), 0.04f);
			}
			monsterTween.TweenProperty(MonsterImage, "position", originalPos, 0.05f);
		}
	}

	private void PlayHorrorScreech()
	{
		try
		{
			int sampleRate = 22050;
			float duration = 1.2f;
			int totalSamples = (int)(sampleRate * duration);
			byte[] data = new byte[totalSamples];

			// Harsh high-frequency noise & dissonant square waves
			for (int i = 0; i < totalSamples; i++)
			{
				float t = (float)i / sampleRate;
				float wave1 = Mathf.Sign(Mathf.Sin(t * Mathf.Tau * 850.0f));
				float wave2 = Mathf.Sign(Mathf.Sin(t * Mathf.Tau * 1150.0f));
				float noise = (float)(GD.Randf() * 2.0 - 1.0) * 0.4f;

				float sample = (wave1 * 0.4f + wave2 * 0.4f + noise) * Mathf.Clamp(1.0f - (t / duration), 0f, 1f);
				sbyte s = (sbyte)(sample * 115.0f);
				data[i] = (byte)s;
			}

			var wav = new AudioStreamWav
			{
				Format = AudioStreamWav.FormatEnum.Format8Bits,
				MixRate = sampleRate,
				Data = data
			};

			_screechPlayer.Stream = wav;
			_screechPlayer.Play();
		}
		catch (Exception)
		{
			// Fallback
		}
	}

	private void OnRestartButtonPressed()
	{
		GetTree().Paused = false;
		GetTree().ReloadCurrentScene();
	}
}
