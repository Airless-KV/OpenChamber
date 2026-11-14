// =========================================================================================
// Original Author : air (Airless-KV) - Nov 11, 2026
// Responsibilities: - Displays player item inventory in the Flower Shop panel
//                   - Dynamically populates and wires Car Battery and Rusted Crowbar shop buttons
//                   - Listens to GameManager and CarriedInventory changes without modifying HUDManager
// -----------------------------------------------------------------------------------------
// Change Log:
// - Nov 11, 2026 | air : Created ShopInventoryDisplay for streamlined plank inventory HUD
// - Nov 14, 2026 | air : Added dynamic Car Battery and Rusted Crowbar shop buttons and real-time inventory counter
// =========================================================================================
using Godot;

public partial class ShopInventoryDisplay : Label
{
	private Button _buyBatteryBtn;
	private Button _buyCrowbarBtn;
	private CarriedInventory _carriedInv;

	public override void _Ready()
	{
		var gm = GetTree().Root.FindChild("GameManager", true, false) as GameManager;
		if (gm != null)
		{
			gm.InventoryChanged += UpdateText;
		}

		Callable.From(InitializeShopComponents).CallDeferred();
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

		// Find sibling BuyPlankButton for style matching
		var plankBtn = vbox.GetNodeOrNull<Button>("BuyPlankButton");
		var normalStyle = plankBtn?.GetThemeStylebox("normal");
		var hoverStyle = plankBtn?.GetThemeStylebox("hover");
		int fontSize = plankBtn != null ? plankBtn.GetThemeFontSize("font_size") : 15;

		int insertIndex = GetIndex(); // Right above this label

		// 1. Car Battery Button
		if (_buyBatteryBtn == null || !IsInstanceValid(_buyBatteryBtn))
		{
			_buyBatteryBtn = new Button
			{
				Name = "BuyBatteryButton",
				Text = "Car Battery (5c)",
				CustomMinimumSize = new Vector2(0, 42),
				LayoutMode = 2
			};
			if (fontSize > 0) _buyBatteryBtn.AddThemeFontSizeOverride("font_size", fontSize);
			if (normalStyle != null) _buyBatteryBtn.AddThemeStyleboxOverride("normal", normalStyle);
			if (hoverStyle != null) _buyBatteryBtn.AddThemeStyleboxOverride("hover", hoverStyle);

			_buyBatteryBtn.Pressed += OnBuyBatteryClicked;
			vbox.AddChild(_buyBatteryBtn);
			vbox.MoveChild(_buyBatteryBtn, insertIndex);
			insertIndex++;
		}

		// 2. Rusted Crowbar Button
		if (_buyCrowbarBtn == null || !IsInstanceValid(_buyCrowbarBtn))
		{
			_buyCrowbarBtn = new Button
			{
				Name = "BuyCrowbarButton",
				Text = "Rusted Crowbar (4c)",
				CustomMinimumSize = new Vector2(0, 42),
				LayoutMode = 2
			};
			if (fontSize > 0) _buyCrowbarBtn.AddThemeFontSizeOverride("font_size", fontSize);
			if (normalStyle != null) _buyCrowbarBtn.AddThemeStyleboxOverride("normal", normalStyle);
			if (hoverStyle != null) _buyCrowbarBtn.AddThemeStyleboxOverride("hover", hoverStyle);

			_buyCrowbarBtn.Pressed += OnBuyCrowbarClicked;
			vbox.AddChild(_buyCrowbarBtn);
			vbox.MoveChild(_buyCrowbarBtn, insertIndex);
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

	private void OnBuyCrowbarClicked()
	{
		var gm = GetTree().Root.FindChild("GameManager", true, false) as GameManager;
		if (gm != null)
		{
			if (gm.TryBuyItem(ShopCatalog.CrowbarId))
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

		int planks = 0;
		int batteries = 0;
		int crowbars = 0;

		if (_carriedInv != null)
		{
			for (int i = 0; i < CarriedInventory.MaxSlots; i++)
			{
				if (!_carriedInv.Slots[i].IsOccupied) continue;
				if (_carriedInv.Slots[i].Type == ItemType.Plank) planks++;
				else if (_carriedInv.Slots[i].Type == ItemType.CarBattery) batteries++;
				else if (_carriedInv.Slots[i].Type == ItemType.Crowbar) crowbars++;
			}
		}

		Text = $"Carried: {planks} Planks | {batteries} Battery | {crowbars} Crowbar";
	}
}
