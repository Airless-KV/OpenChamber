// =========================================================================================
// Original Author : air (Airless-KV) - Nov 11, 2026
// Responsibilities: - Displays player item inventory in the Flower Shop panel
//                   - Dynamically populates and wires Car Battery and Rusted Crowbar shop buttons
//                   - Listens to GameManager and CarriedInventory changes without modifying HUDManager
// -----------------------------------------------------------------------------------------
// Change Log:
// - Nov 11, 2026 | air : Created ShopInventoryDisplay for streamlined plank inventory HUD
// - Nov 14, 2026 | air : Added dynamic Car Battery and Rusted Crowbar shop buttons and real-time inventory counter
// - Nov 15, 2026 | air : Added BuyFreezerButton integration for Cryo-Freezer flower storage
// - Nov 17, 2026 | air : Removed freezer purchase option; dynamically bound seed and battery button labels and tooltips to ShopCatalog prices
// - Nov 18, 2026 | air : Added dynamic merchant daily battery stock display to shop button and synced with day phase transitions
// =========================================================================================
using Godot;

public partial class ShopInventoryDisplay : Label
{
	private Button _buyBatteryBtn;
	private CarriedInventory _carriedInv;

	public override void _Ready()
	{
		var gm = GetTree().Root.FindChild("GameManager", true, false) as GameManager;
		if (gm != null)
		{
			gm.InventoryChanged += UpdateText;
			gm.PhaseChanged += OnPhaseChangedUpdate;
		}

		Callable.From(InitializeShopComponents).CallDeferred();
	}

	private void OnPhaseChangedUpdate(bool isDay, int currentDay)
	{
		UpdateText();
	}

	private void InitializeShopComponents()
	{
		var player = GetTree().GetFirstNodeInGroup("Player") as PlayerController;
		_carriedInv = player?.GetNodeOrNull<CarriedInventory>("CarriedInventory");
		if (_carriedInv != null)
		{
			_carriedInv.InventoryChanged += UpdateText;
		}

		SetupExtraButtons();
		UpdateText();
	}

	private void SetupExtraButtons()
	{
		var vbox = GetParent() as VBoxContainer;
		if (vbox == null) return;

		// Dynamically sync BuySeedButton text and tooltip with ShopCatalog
		var seedBtn = vbox.GetNodeOrNull<Button>("BuySeedButton");
		if (seedBtn != null)
		{
			var seedItem = ShopCatalog.Get(ShopCatalog.SeedId);
			if (seedItem != null)
			{
				seedBtn.Text = $"{seedItem.DisplayName} ({seedItem.Price}c)";
				seedBtn.TooltipText = seedItem.Description;
			}
		}

		// Hide legacy BuyPlankButton from the HUD shop UI so planks are not offered
		var plankBtn = vbox.GetNodeOrNull<Button>("BuyPlankButton");
		if (plankBtn != null)
		{
			plankBtn.Visible = false;
		}

		var normalStyle = plankBtn?.GetThemeStylebox("normal");
		var hoverStyle = plankBtn?.GetThemeStylebox("hover");
		int fontSize = plankBtn != null ? plankBtn.GetThemeFontSize("font_size") : 15;

		int insertIndex = GetIndex(); // Right above this label

		// Car Battery Button - dynamically reflects ShopCatalog price
		var batteryItem = ShopCatalog.Get(ShopCatalog.BatteryId);
		string batteryText = batteryItem != null ? $"{batteryItem.DisplayName} ({batteryItem.Price}c)" : "Car Battery (15c)";

		if (_buyBatteryBtn == null || !IsInstanceValid(_buyBatteryBtn))
		{
			_buyBatteryBtn = new Button
			{
				Name = "BuyBatteryButton",
				Text = batteryText,
				TooltipText = batteryItem?.Description ?? string.Empty,
				CustomMinimumSize = new Vector2(0, 42),
				LayoutMode = 2
			};
			if (fontSize > 0) _buyBatteryBtn.AddThemeFontSizeOverride("font_size", fontSize);
			if (normalStyle != null) _buyBatteryBtn.AddThemeStyleboxOverride("normal", normalStyle);
			if (hoverStyle != null) _buyBatteryBtn.AddThemeStyleboxOverride("hover", hoverStyle);

			_buyBatteryBtn.Pressed += OnBuyBatteryClicked;
			vbox.AddChild(_buyBatteryBtn);
			vbox.MoveChild(_buyBatteryBtn, insertIndex);
		}
		else
		{
			_buyBatteryBtn.Text = batteryText;
			if (batteryItem != null) _buyBatteryBtn.TooltipText = batteryItem.Description;
		}
	}

	private void OnBuyBatteryClicked()
	{
		var gm = GetTree().Root.FindChild("GameManager", true, false) as GameManager;
		if (gm != null)
		{
			if (gm.TryBuyItem(ShopCatalog.BatteryId))
			{
				AudioManager.Sfx("coin_collect", 2f);
			}
			else
			{
				AudioManager.Sfx("window_rattle", -3f);
			}
		}
	}

	private void UpdateText()
	{
		if (_carriedInv == null)
		{
			var player = GetTree().GetFirstNodeInGroup("Player") as PlayerController;
			_carriedInv = player?.GetNodeOrNull<CarriedInventory>("CarriedInventory");
			if (_carriedInv != null)
			{
				_carriedInv.InventoryChanged += UpdateText;
			}
		}

		int batteries = 0;

		if (_carriedInv != null)
		{
			for (int i = 0; i < CarriedInventory.MaxSlots; i++)
			{
				if (!_carriedInv.Slots[i].IsOccupied) continue;
				if (_carriedInv.Slots[i].Type == ItemType.CarBattery) batteries++;
			}
		}

		var gm = GetTree().Root.FindChild("GameManager", true, false) as GameManager;
		if (_buyBatteryBtn != null && IsInstanceValid(_buyBatteryBtn) && gm != null)
		{
			var batteryItem = ShopCatalog.Get(ShopCatalog.BatteryId);
			string baseText = batteryItem != null ? $"{batteryItem.DisplayName} ({batteryItem.Price}c)" : "Car Battery (15c)";
			_buyBatteryBtn.Text = $"{baseText} [{gm.DailyBatteryStock} Left]";
		}

		Text = $"Carried: {batteries} Battery";
	}
}
