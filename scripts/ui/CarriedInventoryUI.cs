// =========================================================================================
// Original Author : air (Airless-KV) - Nov 13, 2026
// Responsibilities: - 3-Slot tactile carried inventory hotbar HUD overlay
//                   - Real-time slot selection indicator and active slot border highlight
//                   - Display of carried item names, flower tier colors, and empty slots
//                   - Fully decoupled from Anj's HUDManager to preserve ownership boundaries
// -----------------------------------------------------------------------------------------
// Change Log:
// - Nov 13, 2026 | air : Created 3-slot carried inventory hotbar HUD overlay
// - Nov 13, 2026 | air : Added dynamic health color coding for Car Battery items in hotbar
// - Nov 14, 2026 | air : Fine-tuned hotbar bottom offset (-18px) to establish clean vertical margin with elevated interaction prompt
// =========================================================================================
using Godot;

public partial class CarriedInventoryUI : CanvasLayer
{
	[Export] public CarriedInventory Inventory;

	private PanelContainer[] _slotPanels = new PanelContainer[CarriedInventory.MaxSlots];
	private Label[] _slotLabels = new Label[CarriedInventory.MaxSlots];
	private Label[] _nameLabels = new Label[CarriedInventory.MaxSlots];
	private HBoxContainer _hotbarContainer;

	private static readonly Color ActiveBorderColor = new(1.0f, 0.85f, 0.25f, 1.0f);
	private static readonly Color InactiveBorderColor = new(0.35f, 0.40f, 0.45f, 0.5f);
	private static readonly Color SlotBgColor = new(0.08f, 0.10f, 0.14f, 0.82f);

	public override void _Ready()
	{
		Layer = 10; // Overlay atop standard 3D view
		BuildUI();

		Inventory ??= GetTree().Root.FindChild("CarriedInventory", true, false) as CarriedInventory;
		if (Inventory != null)
		{
			Inventory.InventoryChanged += RefreshUI;
			Inventory.ActiveSlotChanged += OnActiveSlotChanged;
			RefreshUI();
		}
		else
		{
			// Poll deferred in case Player is loaded after UI
			Callable.From(FindAndBindInventory).CallDeferred();
		}
	}

	private void FindAndBindInventory()
	{
		Inventory ??= GetTree().Root.FindChild("CarriedInventory", true, false) as CarriedInventory;
		if (Inventory != null)
		{
			Inventory.InventoryChanged += RefreshUI;
			Inventory.ActiveSlotChanged += OnActiveSlotChanged;
			RefreshUI();
		}
	}

	private void BuildUI()
	{
		var rootControl = new Control
		{
			Name = "HotbarRoot",
			MouseFilter = Control.MouseFilterEnum.Ignore
		};
		rootControl.SetAnchorsPreset(Control.LayoutPreset.FullRect);
		AddChild(rootControl);

		_hotbarContainer = new HBoxContainer
		{
			Name = "HotbarContainer",
			Alignment = BoxContainer.AlignmentMode.Center,
			MouseFilter = Control.MouseFilterEnum.Ignore
		};
		_hotbarContainer.SetAnchorsPreset(Control.LayoutPreset.CenterBottom);
		_hotbarContainer.OffsetLeft = -250;
		_hotbarContainer.OffsetRight = 250;
		_hotbarContainer.OffsetTop = -85;
		_hotbarContainer.OffsetBottom = -18;
		_hotbarContainer.AddThemeConstantOverride("separation", 14);
		rootControl.AddChild(_hotbarContainer);

		for (int i = 0; i < CarriedInventory.MaxSlots; i++)
		{
			var panel = new PanelContainer
			{
				Name = $"Slot_{i}",
				CustomMinimumSize = new Vector2(150, 58),
				MouseFilter = Control.MouseFilterEnum.Ignore
			};

			var vbox = new VBoxContainer
			{
				Alignment = BoxContainer.AlignmentMode.Center,
				MouseFilter = Control.MouseFilterEnum.Ignore
			};
			vbox.AddThemeConstantOverride("separation", 2);

			var slotNumLabel = new Label
			{
				Text = $"[{i + 1}]",
				HorizontalAlignment = HorizontalAlignment.Center
			};
			slotNumLabel.AddThemeFontSizeOverride("font_size", 12);
			slotNumLabel.Modulate = new Color(0.7f, 0.75f, 0.85f, 0.9f);

			var itemNameLabel = new Label
			{
				Text = "[Empty]",
				HorizontalAlignment = HorizontalAlignment.Center,
				AutowrapMode = TextServer.AutowrapMode.Off
			};
			itemNameLabel.AddThemeFontSizeOverride("font_size", 13);

			vbox.AddChild(slotNumLabel);
			vbox.AddChild(itemNameLabel);
			panel.AddChild(vbox);

			_slotPanels[i] = panel;
			_slotLabels[i] = slotNumLabel;
			_nameLabels[i] = itemNameLabel;

			_hotbarContainer.AddChild(panel);
		}
	}

	private void OnActiveSlotChanged(int activeSlot)
	{
		RefreshUI();
	}

	public void RefreshUI()
	{
		if (Inventory == null) return;

		for (int i = 0; i < CarriedInventory.MaxSlots; i++)
		{
			bool isActive = (i == Inventory.ActiveSlot);
			var slot = Inventory.Slots[i];

			var style = new StyleBoxFlat
			{
				BgColor = SlotBgColor,
				CornerRadiusBottomLeft = 6,
				CornerRadiusBottomRight = 6,
				CornerRadiusTopLeft = 6,
				CornerRadiusTopRight = 6,
				BorderColor = isActive ? ActiveBorderColor : InactiveBorderColor,
				BorderWidthBottom = isActive ? 3 : 1,
				BorderWidthTop = isActive ? 3 : 1,
				BorderWidthLeft = isActive ? 3 : 1,
				BorderWidthRight = isActive ? 3 : 1
			};

			_slotPanels[i].AddThemeStyleboxOverride("panel", style);

			if (slot.IsOccupied)
			{
				_nameLabels[i].Text = slot.GetDisplayName();
				_nameLabels[i].Modulate = GetItemColor(slot);
			}
			else
			{
				_nameLabels[i].Text = "[Empty]";
				_nameLabels[i].Modulate = new Color(0.5f, 0.55f, 0.6f, 0.6f);
			}

			_slotLabels[i].Modulate = isActive ? ActiveBorderColor : new Color(0.7f, 0.75f, 0.85f, 0.8f);
		}
	}

	private static Color GetItemColor(CarriedInventory.SlotData slot)
	{
		return slot.Type switch
		{
			ItemType.Plank => new Color(0.88f, 0.68f, 0.45f),
			ItemType.Seed => new Color(0.95f, 0.85f, 0.45f),
			ItemType.CarBattery => slot.BatteryHealth switch
			{
				> 50 => new Color(0.2f, 0.9f, 0.4f),
				> 20 => new Color(0.95f, 0.75f, 0.2f),
				_ => new Color(0.95f, 0.25f, 0.25f)
			},
			ItemType.Flower => slot.FlowerTier switch
			{
				1 => new Color(0.96f, 0.95f, 0.86f),
				2 => new Color(1.00f, 0.82f, 0.15f),
				3 => new Color(0.98f, 0.45f, 0.15f),
				4 => new Color(0.88f, 0.12f, 0.32f),
				5 => new Color(0.15f, 0.95f, 0.45f),
				_ => Colors.White
			},
			_ => Colors.White
		};
	}
}
