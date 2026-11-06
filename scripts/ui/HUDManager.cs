// =========================================================================================
// Original Author : anjivv (Anj Ferrer) - Oct 22, 2026
// Responsibilities: - Persistent Daytime/Nighttime HUD (timer, coin balance, day counter)
//                   - Shop UI buttons and purchase interactions for seeds, planks, and tools
//                   - Interaction prompts and phase transitions display
//                   - DayNightClock and Coin/Seed HUD art integration
// -----------------------------------------------------------------------------------------
// Change Log:
// - Oct 31, 2026 | air : Applied standard header formatting and moved to scripts/ui/
// - Nov 06, 2026 | air : Integrated Anj's Nov 6 HUD art, DayNightClock widget, and shopping freeze
// =========================================================================================
using Godot;

public partial class HUDManager : CanvasLayer
{
	[Export] public GameManager GameManagerNode;

	[ExportGroup("UI Elements")]
	[Export] public Label DayLabel;
	[Export] public Label PhaseLabel;
	[Export] public Label TimerLabel;
	[Export] public Label CoinLabel;
	[Export] public Label SeedLabel;
	[Export] public Button BuySeedButton;
	[Export] public Label InteractionPromptLabel;

	[ExportGroup("Shop UI (optional)")]
	[Export] public Button BuyPlankButton;
	[Export] public Button BuyBarrierButton;
	[Export] public Button BuyAxeButton;
	[Export] public Label InventoryLabel;
	[Export] public Control ShopPanel; // optional: a container holding all the shop controls
	[Export] public bool EnableDebugShopKey = true; // TEMPORARY: press B to open/close the shop for testing

	public bool IsShopOpen { get; private set; } = false;

	[ExportGroup("HUD Art (leave empty to use the built-in pixel art)")]
	[Export] public Texture2D CoinIconTexture;
	[Export] public Texture2D SeedIconTexture;
	[Export] public bool ShowClock = true;
	[Export] public bool ShowSeedIcon = true;

	private bool _coinIconActive = false;
	private bool _seedIconActive = false;

	public override void _Ready()
	{
		if (GameManagerNode == null)
		{
			GameManagerNode = GetTree().Root.FindChild("GameManager", true, false) as GameManager;
		}

		if (GameManagerNode == null)
		{
			GD.PushError(
                "Assign GameManagerNode in the HUD Inspector."
			);
			return;
		}

		// Connect to the GameManager signals.
		GameManagerNode.TimeUpdated += OnTimeUpdated;
		GameManagerNode.PhaseChanged += OnPhaseChanged;
		GameManagerNode.CoinsChanged += OnCoinsChanged;
		GameManagerNode.SeedsChanged += OnSeedsChanged;
		GameManagerNode.GameCompleted += OnGameCompleted;
		GameManagerNode.InventoryChanged += RefreshInventory;

		if (BuySeedButton != null)
			BuySeedButton.Pressed += OnBuySeedButtonPressed;

		WireShopButton(BuyPlankButton, ShopCatalog.PlankId);
		WireShopButton(BuyBarrierButton, ShopCatalog.BarrierId);
		WireShopButton(BuyAxeButton, ShopCatalog.AxeId);

		// Initialize the HUD.
		OnTimeUpdated(GameManagerNode.TimeRemaining);

		OnPhaseChanged(
			GameManagerNode.CurrentPhase == GameManager.GamePhase.Day,
			GameManagerNode.CurrentDay
		);

		OnCoinsChanged(GameManagerNode.Coins);
		OnSeedsChanged(GameManagerNode.Level1Seeds);
		RefreshInventory();
		SetShopVisible(false);
		Callable.From(SetupHudArt).CallDeferred();

		ClearInteractionPrompt();

		if (GameManagerNode.IsGameFinished)
			OnGameCompleted();
	}

	// ---------- HUD art (icons + clock) ----------

	private void SetupHudArt()
	{
		_coinIconActive = AddIcon(CoinLabel, CoinIconTexture ?? PixelArt.Coin());
		_seedIconActive = ShowSeedIcon && AddIcon(SeedLabel, SeedIconTexture ?? PixelArt.SeedPouch());

		if (GameManagerNode != null)
		{
			OnCoinsChanged(GameManagerNode.Coins);
			OnSeedsChanged(GameManagerNode.Level1Seeds);
		}

		if (ShowClock && GameManagerNode != null)
		{
			var clock = new DayNightClock { Name = "DayNightClock" };
			AddChild(clock);
			clock.SetAnchorsPreset(Control.LayoutPreset.TopRight);
			clock.OffsetLeft = -112f;
			clock.OffsetRight = -16f;
			clock.OffsetTop = 44f;
			clock.OffsetBottom = 140f;
			clock.Bind(GameManagerNode);
		}
	}

	// Puts an icon to the left of a label and nudges the label right to make room.
	private bool AddIcon(Label label, Texture2D texture)
	{
		if (label == null || texture == null)
			return false;

		const float iconSize = 28f;

		var icon = new TextureRect
		{
			Texture = texture,
			TextureFilter = CanvasItem.TextureFilterEnum.Nearest,
			ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
			StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
			CustomMinimumSize = new Vector2(iconSize, iconSize),
			Size = new Vector2(iconSize, iconSize),
			MouseFilter = Control.MouseFilterEnum.Ignore
		};

		label.GetParent().AddChild(icon);
		icon.Position = label.Position + new Vector2(0f, (label.Size.Y - iconSize) * 0.5f);
		label.Position += new Vector2(iconSize + 6f, 0f);
		return true;
	}

	// ---------- Shop open / close ----------

	public void OpenShop() => SetShopVisible(true);
	public void CloseShop() => SetShopVisible(false);
	public void ToggleShop() => SetShopVisible(!IsShopOpen);

	private void SetShopVisible(bool open)
	{
		IsShopOpen = open;

		// Freeze the player while shopping
		// (Pauses the player's physics so it stands still; works with any PlayerController version.)
		GetTree().GetFirstNodeInGroup("Player")?.SetPhysicsProcess(!open);

		if (ShopPanel != null)
		{
			ShopPanel.Visible = open;
			return;
		}

		// No panel assigned: show/hide each shop control individually.
		if (BuySeedButton != null) BuySeedButton.Visible = open;
		if (BuyPlankButton != null) BuyPlankButton.Visible = open;
		if (BuyBarrierButton != null) BuyBarrierButton.Visible = open;
		if (BuyAxeButton != null) BuyAxeButton.Visible = open;
		if (InventoryLabel != null) InventoryLabel.Visible = open;
	}

	public override void _UnhandledInput(InputEvent @event)
	{
		if (@event is InputEventKey key && key.Pressed && !key.Echo)
		{
			if (IsShopOpen && key.Keycode == Key.Escape)
			{
				CloseShop();
				GetViewport().SetInputAsHandled();
			}
			else if (EnableDebugShopKey && key.Keycode == Key.B)
			{
				ToggleShop();
			}
		}
	}

	public void SetInteractionPrompt(string text)
	{
		if (InteractionPromptLabel != null)
		{
			InteractionPromptLabel.Text = text;
			InteractionPromptLabel.Visible = !string.IsNullOrEmpty(text);
		}
	}

	public void ClearInteractionPrompt()
	{
		if (InteractionPromptLabel != null)
		{
			InteractionPromptLabel.Text = "";
			InteractionPromptLabel.Visible = false;
		}
	}

	private void OnTimeUpdated(float timeRemaining)
	{
		if (TimerLabel == null)
			return;

		int totalSeconds = Mathf.Max(
			0, Mathf.CeilToInt(timeRemaining)
		);

		int minutes = totalSeconds / 60;
		int seconds = totalSeconds % 60;

		TimerLabel.Text = $"{minutes:00}:{seconds:00}";
	}

	private void OnPhaseChanged(bool isDay, int currentDay)
	{
		if (DayLabel != null)
			DayLabel.Text = $"Day {currentDay} / 7";

		if (PhaseLabel != null)
		{
			PhaseLabel.Text = isDay ? "DAYTIME" : "NIGHTTIME";

			PhaseLabel.Modulate = isDay
				? Colors.Yellow
				: Colors.MediumPurple;
		}
	}

	private void OnCoinsChanged(int totalCoins)
	{
		if (CoinLabel != null)
			CoinLabel.Text = _coinIconActive ? $"x {totalCoins}" : $"Coins: {totalCoins}";
	}

	private void OnSeedsChanged(int totalSeeds)
	{
		if (SeedLabel != null)
			SeedLabel.Text = _seedIconActive ? $"Lv1 x {totalSeeds}" : $"Lvl 1 Seeds: {totalSeeds}";
	}

	private void OnBuySeedButtonPressed()
	{
		if (GameManagerNode == null)
			return;

		bool purchased = GameManagerNode.TryBuyLevel1Seed();

		if (!purchased)
			GD.Print("Not enough coins to buy a seed!");
	}

	private void WireShopButton(Button btn, string id)
	{
		if (btn == null)
			return;

		var item = ShopCatalog.Get(id);
		btn.Text = $"{item.DisplayName} ({item.Price}c)";
		btn.TooltipText = item.Description;
		btn.Pressed += () =>
		{
			if (!GameManagerNode.TryBuyItem(id))
				GD.Print($"Can't buy {item.DisplayName} (not enough coins or already owned).");
		};
	}

	private void RefreshInventory()
	{
		if (GameManagerNode == null)
			return;

		var gm = GameManagerNode;

		if (InventoryLabel != null)
			InventoryLabel.Text = $"Planks: {gm.Planks}  Barriers: {gm.Barriers}  Axe: {(gm.HasAxe ? "Yes" : "No")}";

		if (BuyAxeButton != null)
			BuyAxeButton.Disabled = gm.HasAxe;
	}

	private void OnGameCompleted()
	{
		if (PhaseLabel != null)
		{
			PhaseLabel.Text = "YOU SURVIVED 7 DAYS!";
			PhaseLabel.Modulate = Colors.Gold;
		}

		if (TimerLabel != null)
			TimerLabel.Text = "GAME COMPLETE!";

		if (BuySeedButton != null)
			BuySeedButton.Disabled = true;

		if (BuyPlankButton != null) BuyPlankButton.Disabled = true;
		if (BuyBarrierButton != null) BuyBarrierButton.Disabled = true;
		if (BuyAxeButton != null) BuyAxeButton.Disabled = true;
	}
}

// Built-in pixel art loader fallback for Anji's coin and seed icons
internal static class PixelArt
{
	public static Texture2D Coin()
	{
		const string path = "res://assets/textures/coin_icon.png";
		return ResourceLoader.Exists(path) ? GD.Load<Texture2D>(path) : null;
	}

	public static Texture2D SeedPouch()
	{
		const string path = "res://assets/textures/icon_sheet.png";
		return ResourceLoader.Exists(path) ? GD.Load<Texture2D>(path) : null;
	}
}
