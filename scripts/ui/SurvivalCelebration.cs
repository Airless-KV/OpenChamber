// =========================================================================================
// Original Author : anjivv (Anj Ferrer) - Oct 31, 2026
// Responsibilities: - Daytime survival celebration banner ("YOU SURVIVED DAY N")
//                   - Animated coin particle flight to HUD CoinLabel target
// -----------------------------------------------------------------------------------------
// Change Log:
// - Oct 31, 2026 | anjivv : Implemented daytime survival celebration banner with animated coin flight particles and audio stingers
// - Oct 31, 2026 | air : Applied standard header formatting and moved to scripts/ui/
// =========================================================================================
using Godot;
using System;
using System.Threading.Tasks;

// "YOU SURVIVED DAY N" banner + coin collection animation.
// Built entirely in code: GameManager creates it automatically, no scene setup needed.
// Coins fly from the banner to CoinTarget (the HUD CoinLabel) and the total ticks up as each one lands.
public partial class SurvivalCelebration : CanvasLayer
{
	[Export] public Control CoinTarget;          // Usually HUD CoinLabel (auto-found by GameManager)
	[Export] public Texture2D CoinTexture;       // Optional coin sprite; a gold circle is drawn if empty
	[Export] public int MaxCoinSprites = 20;
	[Export] public float CoinFlightTime = 0.6f;
	[Export] public float CoinStagger = 0.08f;

	private Control _root;
	private ColorRect _dim;
	private VBoxContainer _box;
	private Label _title;
	private Label _reward;

	public override void _Ready()
	{
		Layer = 90; // above HUD and fade, below JumpScareScreen (100)
		ProcessMode = ProcessModeEnum.Always;

		_root = new Control { Name = "Root", MouseFilter = Control.MouseFilterEnum.Ignore, Visible = false };
		_root.SetAnchorsPreset(Control.LayoutPreset.FullRect);
		AddChild(_root);

		_dim = new ColorRect { Name = "Dim", Color = new Color(0, 0, 0, 0), MouseFilter = Control.MouseFilterEnum.Ignore };
		_dim.SetAnchorsPreset(Control.LayoutPreset.FullRect);
		_root.AddChild(_dim);

		_box = new VBoxContainer { Name = "Banner", MouseFilter = Control.MouseFilterEnum.Ignore };
		_box.AddThemeConstantOverride("separation", 12);
		_root.AddChild(_box);

		_title = MakeLabel(64, Colors.Gold);
		_reward = MakeLabel(36, new Color(1f, 0.95f, 0.6f));
		_box.AddChild(_title);
		_box.AddChild(_reward);

		_box.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.Center);
		_box.GrowHorizontal = Control.GrowDirection.Both;
		_box.GrowVertical = Control.GrowDirection.Both;
	}

	private static Label MakeLabel(int size, Color color)
	{
		var l = new Label { HorizontalAlignment = HorizontalAlignment.Center, MouseFilter = Control.MouseFilterEnum.Ignore };
		l.AddThemeFontSizeOverride("font_size", size);
		l.AddThemeColorOverride("font_color", color);
		l.AddThemeColorOverride("font_outline_color", new Color(0, 0, 0, 1));
		l.AddThemeConstantOverride("outline_size", 12);
		return l;
	}

	public async Task Play(int day, int reward, Action<int> onCoinCollected)
	{
		_title.Text = $"YOU SURVIVED DAY {day}";
		_reward.Text = $"+{reward} COINS";

		_root.Visible = true;
		_dim.Color = new Color(0, 0, 0, 0);
		_box.Modulate = new Color(1, 1, 1, 0);
		_box.Scale = new Vector2(0.6f, 0.6f);

		// Wait one frame so the banner has its final size before we set the pivot.
		await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
		_box.PivotOffset = _box.Size * 0.5f;

		AudioManager.Sfx("banner_sting");

		var intro = CreateTween().SetParallel(true);
		intro.TweenProperty(_dim, "color:a", 0.5f, 0.4f);
		intro.TweenProperty(_box, "modulate:a", 1f, 0.4f);
		intro.TweenProperty(_box, "scale", Vector2.One, 0.5f)
			.SetTrans(Tween.TransitionType.Back)
			.SetEase(Tween.EaseType.Out);
		await ToSignal(intro, Tween.SignalName.Finished);

		await ToSignal(GetTree().CreateTimer(0.7), SceneTreeTimer.SignalName.Timeout);

		// Coin collection
		int count = Math.Clamp(reward, 1, MaxCoinSprites);
		int baseValue = reward / count;
		int remainder = reward % count;

		Vector2 start = _box.GetGlobalRect().GetCenter() + new Vector2(0, 70);
		Vector2 end = IsInstanceValid(CoinTarget)
			? CoinTarget.GetGlobalRect().GetCenter()
			: new Vector2(_root.GetViewportRect().Size.X - 80f, 40f);

		Tween last = null;
		for (int i = 0; i < count; i++)
		{
			int value = baseValue + (i < remainder ? 1 : 0);

			var coin = MakeCoin();
			coin.PivotOffset = coin.Size * 0.5f;
			coin.Position = start
				+ new Vector2((float)GD.RandRange(-90.0, 90.0), (float)GD.RandRange(-15.0, 15.0))
				- coin.Size * 0.5f;
			coin.Modulate = new Color(1, 1, 1, 0);
			_root.AddChild(coin);

			var tw = CreateTween();
			tw.TweenInterval(i * CoinStagger);
			tw.TweenProperty(coin, "modulate:a", 1f, 0.1f);
			tw.TweenProperty(coin, "position", end - coin.Size * 0.5f, CoinFlightTime)
				.SetTrans(Tween.TransitionType.Quad)
				.SetEase(Tween.EaseType.In);
			tw.Parallel().TweenProperty(coin, "scale", new Vector2(0.5f, 0.5f), CoinFlightTime);
			tw.TweenCallback(Callable.From(() =>
			{
				onCoinCollected?.Invoke(value);
				AudioManager.Sfx("coin_collect", -6f, 0.08f);
				PulseTarget();
				coin.QueueFree();
			}));
			last = tw;
		}

		if (last != null)
			await ToSignal(last, Tween.SignalName.Finished);

		await ToSignal(GetTree().CreateTimer(0.5), SceneTreeTimer.SignalName.Timeout);

		// Fade the banner only; the dim stays until GameManager has faded to black, then calls Dismiss().
		var outro = CreateTween();
		outro.TweenProperty(_box, "modulate:a", 0f, 0.4f);
		await ToSignal(outro, Tween.SignalName.Finished);
	}

	public void Dismiss()
	{
		if (_root != null)
			_root.Visible = false;
	}

	private void PulseTarget()
	{
		if (!IsInstanceValid(CoinTarget))
			return;

		CoinTarget.PivotOffset = CoinTarget.Size * 0.5f;
		var t = CreateTween();
		t.TweenProperty(CoinTarget, "scale", new Vector2(1.25f, 1.25f), 0.06f);
		t.TweenProperty(CoinTarget, "scale", Vector2.One, 0.1f);
	}

	private Control MakeCoin()
	{
		if (CoinTexture != null)
		{
			return new TextureRect
			{
				Texture = CoinTexture,
				ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
				StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
				Size = new Vector2(32, 32),
				MouseFilter = Control.MouseFilterEnum.Ignore
			};
		}

		var panel = new Panel { Size = new Vector2(28, 28), MouseFilter = Control.MouseFilterEnum.Ignore };
		var sb = new StyleBoxFlat
		{
			BgColor = new Color(1f, 0.82f, 0.15f),
			BorderColor = new Color(0.7f, 0.45f, 0f)
		};
		sb.SetCornerRadiusAll(14);
		sb.SetBorderWidthAll(3);
		panel.AddThemeStyleboxOverride("panel", sb);
		return panel;
	}
}
