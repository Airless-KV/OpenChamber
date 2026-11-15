// =========================================================================================
// Original Author : air (Airless-KV) - Nov 15, 2026
// Responsibilities: - Minecraft chest-style GUI overlay for Cryo-Freezer storage
//                   - Non-stackable slot grid with item icons, tier names, and real-time durability/freshness bars
//                   - Two-way click transfer between player carried hotbar and freezer slots
//                   - Real-time battery docking slot & battery health indicator
//                   - Automatic mouse release and capture management with CameraPivot
// -----------------------------------------------------------------------------------------
// Change Log:
// - Nov 15, 2026 | air : Implemented Minecraft-style chest inventory UI for Cryo-Freezer
// =========================================================================================
using Godot;
using System;

public partial class FreezerInventoryUI : CanvasLayer
{
	public static FreezerInventoryUI Instance { get; private set; }

	public bool IsOpen => Visible;
	private InteractableFreezer _activeFreezer;
	private CarriedInventory _playerInv;
	private CameraPivot _cameraPivot;

	// UI Elements
	private Control _rootOverlay;
	private PanelContainer _chestPanel;
	private Label _titleLabel;
	private Label _statusLabel;
	private Button _closeButton;
	private Button _batteryDockButton;
	private Label _batteryStatusLabel;
	private ProgressBar _batteryBar;

	// Slot buttons
	private Button[] _freezerSlotButtons = new Button[InteractableFreezer.ChestCapacity];
	private ProgressBar[] _freezerSlotBars = new ProgressBar[InteractableFreezer.ChestCapacity];
	private Label[] _freezerSlotLabels = new Label[InteractableFreezer.ChestCapacity];

	private Button[] _playerSlotButtons = new Button[CarriedInventory.MaxSlots];
	private ProgressBar[] _playerSlotBars = new ProgressBar[CarriedInventory.MaxSlots];
	private Label[] _playerSlotLabels = new Label[CarriedInventory.MaxSlots];

	private static readonly Color SlotBgColor = new(0.12f, 0.14f, 0.18f, 0.95f);
	private static readonly Color SlotHoverColor = new(0.22f, 0.28f, 0.36f, 1.0f);
	private static readonly Color BorderActiveCooling = new(0.2f, 0.85f, 1.0f, 0.8f);
	private static readonly Color BorderOffline = new(0.85f, 0.25f, 0.25f, 0.8f);

	public override void _EnterTree()
	{
		Instance = this;
	}

	public override void _Ready()
	{
		Layer = 25; // Top overlay
		BuildUI();
		Visible = false;
	}

	public static void Open(InteractableFreezer freezer)
	{
		if (Instance == null)
		{
			// Instantiate or create if not present
			var tree = (Godot.SceneTree)Godot.Engine.GetMainLoop();
			var ui = new FreezerInventoryUI();
			tree?.Root.AddChild(ui);
		}

		Instance?.ShowFreezer(freezer);
	}

	public static void Close()
	{
		Instance?.HideFreezer();
	}

	public void ShowFreezer(InteractableFreezer freezer)
	{
		_activeFreezer = freezer;
		var player = GetTree()?.GetFirstNodeInGroup("Player") as PlayerController;
		_playerInv = player?.GetNodeOrNull<CarriedInventory>("CarriedInventory");
		_cameraPivot = CameraPivot.Instance ?? GetTree()?.Root.FindChild("CameraPivot", true, false) as CameraPivot;

		if (_playerInv != null)
		{
			_playerInv.InventoryChanged -= RefreshUI;
			_playerInv.InventoryChanged += RefreshUI;
		}

		Visible = true;
		Input.MouseMode = Input.MouseModeEnum.Visible;

		if (player != null)
		{
			player.IsMovementLocked = true;
		}

		RefreshUI();
		AudioManager.Sfx("door_pound", 1.8f);
	}

	public void HideFreezer()
	{
		if (!Visible) return;

		Visible = false;
		_activeFreezer = null;

		var player = GetTree()?.GetFirstNodeInGroup("Player") as PlayerController;
		if (player != null)
		{
			player.IsMovementLocked = false;
		}

		// Re-capture mouse for gameplay unless shop or cctv is open
		Input.MouseMode = Input.MouseModeEnum.Captured;
		AudioManager.Sfx("door_pound", 1.2f);
	}

	public override void _UnhandledInput(InputEvent @event)
	{
		if (!Visible) return;

		if (@event is InputEventKey { Pressed: true, Echo: false } keyEvent)
		{
			if (keyEvent.Keycode == Key.Escape || keyEvent.Keycode == Key.E || keyEvent.Keycode == Key.Tab)
			{
				HideFreezer();
				GetViewport().SetInputAsHandled();
			}
		}
	}

	public override void _Process(double delta)
	{
		if (!Visible || _activeFreezer == null || !IsInstanceValid(_activeFreezer))
		{
			if (Visible) HideFreezer();
			return;
		}

		// Close if player moves too far away
		var player = GetTree()?.GetFirstNodeInGroup("Player") as PlayerController;
		if (player != null && player.GlobalPosition.DistanceTo(_activeFreezer.GlobalPosition) > 5.5f)
		{
			HideFreezer();
			return;
		}

		// Real-time UI refresh for decay and power consumption
		RefreshUI();
	}

	private void BuildUI()
	{
		_rootOverlay = new Control
		{
			Name = "RootOverlay",
			LayoutMode = 1,
			AnchorsPreset = (int)Control.LayoutPreset.FullRect,
			MouseFilter = Control.MouseFilterEnum.Pass
		};
		AddChild(_rootOverlay);

		// Dark background backdrop
		var backdrop = new ColorRect
		{
			Name = "Backdrop",
			LayoutMode = 1,
			AnchorsPreset = (int)Control.LayoutPreset.FullRect,
			Color = new Color(0.04f, 0.05f, 0.08f, 0.72f),
			MouseFilter = Control.MouseFilterEnum.Stop
		};
		_rootOverlay.AddChild(backdrop);

		// Center container
		var center = new CenterContainer
		{
			Name = "CenterContainer",
			LayoutMode = 1,
			AnchorsPreset = (int)Control.LayoutPreset.FullRect
		};
		_rootOverlay.AddChild(center);

		_chestPanel = new PanelContainer
		{
			Name = "ChestPanel",
			CustomMinimumSize = new Vector2(620, 520)
		};
		var panelStyle = new StyleBoxFlat
		{
			BgColor = new Color(0.09f, 0.11f, 0.15f, 0.98f),
			BorderColor = new Color(0.22f, 0.45f, 0.65f, 1f),
			BorderWidthLeft = 2,
			BorderWidthTop = 2,
			BorderWidthRight = 2,
			BorderWidthBottom = 2,
			CornerRadiusBottomLeft = 10,
			CornerRadiusBottomRight = 10,
			CornerRadiusTopLeft = 10,
			CornerRadiusTopRight = 10,
			ContentMarginLeft = 20,
			ContentMarginRight = 20,
			ContentMarginTop = 16,
			ContentMarginBottom = 20
		};
		_chestPanel.AddThemeStyleboxOverride("panel", panelStyle);
		center.AddChild(_chestPanel);

		var mainVbox = new VBoxContainer
		{
			Name = "MainVBox"
		};
		mainVbox.AddThemeConstantOverride("separation", 14);
		_chestPanel.AddChild(mainVbox);

		// Header row
		var headerHbox = new HBoxContainer();
		_titleLabel = new Label
		{
			Text = "CRYO-FREEZER CHEST",
			SizeFlagsHorizontal = Control.SizeFlags.ExpandFill
		};
		_titleLabel.AddThemeFontSizeOverride("font_size", 20);
		_titleLabel.AddThemeColorOverride("font_color", new Color(0.4f, 0.85f, 1.0f));
		headerHbox.AddChild(_titleLabel);

		_closeButton = new Button
		{
			Text = " ✕ ",
			CustomMinimumSize = new Vector2(36, 32),
			FocusMode = Control.FocusModeEnum.None
		};
		_closeButton.Pressed += HideFreezer;
		headerHbox.AddChild(_closeButton);
		mainVbox.AddChild(headerHbox);

		// Battery & Cooling status section
		var batteryBox = new HBoxContainer();
		batteryBox.AddThemeConstantOverride("separation", 12);

		_statusLabel = new Label
		{
			Text = "[COOLING ACTIVE]",
			SizeFlagsHorizontal = Control.SizeFlags.ExpandFill
		};
		_statusLabel.AddThemeFontSizeOverride("font_size", 14);
		batteryBox.AddChild(_statusLabel);

		_batteryBar = new ProgressBar
		{
			CustomMinimumSize = new Vector2(160, 22),
			MinValue = 0,
			MaxValue = 100,
			Step = 1,
			Value = 100,
			ShowPercentage = true
		};
		batteryBox.AddChild(_batteryBar);

		_batteryDockButton = new Button
		{
			Text = "Insert Battery",
			CustomMinimumSize = new Vector2(120, 30),
			FocusMode = Control.FocusModeEnum.None
		};
		_batteryDockButton.Pressed += OnBatteryDockClicked;
		batteryBox.AddChild(_batteryDockButton);
		mainVbox.AddChild(batteryBox);

		// Divider
		mainVbox.AddChild(new HSeparator());

		// Freezer grid section title
		var freezerHeader = new Label
		{
			Text = "FREEZER STORAGE (Non-Stackable - Preserves Perishables when Powered)",
			HorizontalAlignment = HorizontalAlignment.Center
		};
		freezerHeader.AddThemeFontSizeOverride("font_size", 13);
		freezerHeader.AddThemeColorOverride("font_color", new Color(0.7f, 0.8f, 0.9f));
		mainVbox.AddChild(freezerHeader);

		// Freezer Grid: 2 rows x 5 columns
		var freezerGrid = new GridContainer
		{
			Columns = 5,
			SizeFlagsHorizontal = Control.SizeFlags.ShrinkCenter
		};
		freezerGrid.AddThemeConstantOverride("h_separation", 10);
		freezerGrid.AddThemeConstantOverride("v_separation", 10);
		mainVbox.AddChild(freezerGrid);

		for (int i = 0; i < InteractableFreezer.ChestCapacity; i++)
		{
			int slotIndex = i;
			var slotContainer = CreateSlotWidget(out Button btn, out ProgressBar bar, out Label lbl);
			_freezerSlotButtons[i] = btn;
			_freezerSlotBars[i] = bar;
			_freezerSlotLabels[i] = lbl;

			btn.Pressed += () => OnFreezerSlotClicked(slotIndex);
			freezerGrid.AddChild(slotContainer);
		}

		// Divider
		mainVbox.AddChild(new HSeparator());

		// Player Carried Inventory section title
		var playerHeader = new Label
		{
			Text = "CARRIED INVENTORY (3-Slot Hotbar)",
			HorizontalAlignment = HorizontalAlignment.Center
		};
		playerHeader.AddThemeFontSizeOverride("font_size", 13);
		playerHeader.AddThemeColorOverride("font_color", new Color(0.9f, 0.85f, 0.5f));
		mainVbox.AddChild(playerHeader);

		// Player Grid: 1 row x 3 columns
		var playerGrid = new HBoxContainer
		{
			Alignment = BoxContainer.AlignmentMode.Center
		};
		playerGrid.AddThemeConstantOverride("separation", 14);
		mainVbox.AddChild(playerGrid);

		for (int i = 0; i < CarriedInventory.MaxSlots; i++)
		{
			int slotIndex = i;
			var slotContainer = CreateSlotWidget(out Button btn, out ProgressBar bar, out Label lbl);
			_playerSlotButtons[i] = btn;
			_playerSlotBars[i] = bar;
			_playerSlotLabels[i] = lbl;

			btn.Pressed += () => OnPlayerSlotClicked(slotIndex);
			playerGrid.AddChild(slotContainer);
		}

		// Bottom Hint
		var hintLabel = new Label
		{
			Text = "Left Click slot to transfer item between Freezer and Carried Inventory | Press [E] or [Esc] to Close",
			HorizontalAlignment = HorizontalAlignment.Center
		};
		hintLabel.AddThemeFontSizeOverride("font_size", 12);
		hintLabel.AddThemeColorOverride("font_color", new Color(0.6f, 0.65f, 0.72f));
		mainVbox.AddChild(hintLabel);
	}

	private Control CreateSlotWidget(out Button btn, out ProgressBar bar, out Label label)
	{
		var panel = new PanelContainer
		{
			CustomMinimumSize = new Vector2(106, 92)
		};

		var style = new StyleBoxFlat
		{
			BgColor = SlotBgColor,
			BorderColor = new Color(0.3f, 0.35f, 0.45f, 0.8f),
			BorderWidthLeft = 1,
			BorderWidthTop = 1,
			BorderWidthRight = 1,
			BorderWidthBottom = 1,
			CornerRadiusBottomLeft = 6,
			CornerRadiusBottomRight = 6,
			CornerRadiusTopLeft = 6,
			CornerRadiusTopRight = 6
		};
		panel.AddThemeStyleboxOverride("panel", style);

		var vbox = new VBoxContainer();
		vbox.AddThemeConstantOverride("separation", 2);

		btn = new Button
		{
			Text = "[Empty]",
			CustomMinimumSize = new Vector2(102, 60),
			ClipText = true,
			FocusMode = Control.FocusModeEnum.None,
			SizeFlagsVertical = Control.SizeFlags.ExpandFill
		};
		btn.AddThemeFontSizeOverride("font_size", 12);

		label = new Label
		{
			Text = "",
			HorizontalAlignment = HorizontalAlignment.Center
		};
		label.AddThemeFontSizeOverride("font_size", 11);
		label.AddThemeColorOverride("font_color", new Color(0.75f, 0.8f, 0.85f));

		bar = new ProgressBar
		{
			CustomMinimumSize = new Vector2(100, 8),
			MinValue = 0,
			MaxValue = 100,
			Value = 100,
			ShowPercentage = false
		};

		vbox.AddChild(btn);
		vbox.AddChild(label);
		vbox.AddChild(bar);
		panel.AddChild(vbox);

		return panel;
	}

	public void RefreshUI()
	{
		if (_activeFreezer == null || !IsInstanceValid(_activeFreezer)) return;

		bool cooling = _activeFreezer.IsCooling;
		bool hasBatt = _activeFreezer.HasBattery;
		int battHp = _activeFreezer.BatteryHealth;

		// Battery & Status header
		if (cooling)
		{
			_statusLabel.Text = $"⚡ COOLING ACTIVE (Power: {battHp}%) - Decay Frozen";
			_statusLabel.AddThemeColorOverride("font_color", new Color(0.2f, 0.9f, 1.0f));
		}
		else if (hasBatt && battHp <= 0)
		{
			_statusLabel.Text = "⚠ BATTERY DEPLETED (0%) - Items Decaying!";
			_statusLabel.AddThemeColorOverride("font_color", new Color(0.95f, 0.3f, 0.2f));
		}
		else
		{
			_statusLabel.Text = "⚠ NO BATTERY DOCKED - Items Decaying!";
			_statusLabel.AddThemeColorOverride("font_color", new Color(0.95f, 0.5f, 0.2f));
		}

		_batteryBar.Value = hasBatt ? battHp : 0;
		_batteryDockButton.Text = hasBatt ? $"Eject Battery ({battHp}%)" : "Dock Battery";

		// Update Freezer Slots
		for (int i = 0; i < InteractableFreezer.ChestCapacity; i++)
		{
			var item = _activeFreezer.Slots[i];
			var btn = _freezerSlotButtons[i];
			var bar = _freezerSlotBars[i];
			var lbl = _freezerSlotLabels[i];

			if (item.IsOccupied)
			{
				btn.Text = item.GetShortName();
				btn.Modulate = item.GetItemColor();

				if (item.Type == ItemType.Flower)
				{
					float maxLife = WorldItem.GetMaxShelfLife(item.FlowerTier);
					float ratio = Mathf.Clamp(item.FreshnessTimer / maxLife, 0f, 1f);
					bar.Visible = true;
					bar.Value = ratio * 100f;
					lbl.Text = $"{Mathf.CeilToInt(item.FreshnessTimer)}s left";
					lbl.Visible = true;
				}
				else if (item.Type == ItemType.CarBattery)
				{
					bar.Visible = true;
					bar.Value = item.BatteryHealth;
					lbl.Text = $"{item.BatteryHealth}% HP";
					lbl.Visible = true;
				}
				else
				{
					bar.Visible = false;
					lbl.Text = "Non-perishable";
					lbl.Visible = true;
				}
			}
			else
			{
				btn.Text = "[Empty]";
				btn.Modulate = new Color(0.5f, 0.55f, 0.6f, 0.5f);
				bar.Visible = false;
				lbl.Text = "";
				lbl.Visible = false;
			}
		}

		// Update Player Slots
		if (_playerInv != null)
		{
			for (int i = 0; i < CarriedInventory.MaxSlots; i++)
			{
				var slot = _playerInv.Slots[i];
				var btn = _playerSlotButtons[i];
				var bar = _playerSlotBars[i];
				var lbl = _playerSlotLabels[i];

				if (slot.IsOccupied)
				{
					btn.Text = slot.GetDisplayName();
					btn.Modulate = GetPlayerSlotColor(slot);

					if (slot.Type == ItemType.Flower)
					{
						float maxLife = WorldItem.GetMaxShelfLife(slot.FlowerTier);
						float ratio = Mathf.Clamp(slot.FreshnessTimer / maxLife, 0f, 1f);
						bar.Visible = true;
						bar.Value = ratio * 100f;
						lbl.Text = $"{Mathf.CeilToInt(slot.FreshnessTimer)}s";
						lbl.Visible = true;
					}
					else if (slot.Type == ItemType.CarBattery)
					{
						bar.Visible = true;
						bar.Value = slot.BatteryHealth;
						lbl.Text = $"{slot.BatteryHealth}%";
						lbl.Visible = true;
					}
					else
					{
						bar.Visible = false;
						lbl.Text = "Non-perishable";
						lbl.Visible = true;
					}
				}
				else
				{
					btn.Text = $"Slot {i + 1}\n[Empty]";
					btn.Modulate = new Color(0.5f, 0.55f, 0.6f, 0.5f);
					bar.Visible = false;
					lbl.Text = "";
					lbl.Visible = false;
				}
			}
		}
	}

	private void OnFreezerSlotClicked(int slotIndex)
	{
		if (_activeFreezer == null || _playerInv == null) return;

		var freezerItem = _activeFreezer.Slots[slotIndex];
		if (freezerItem.IsOccupied)
		{
			// Move item from freezer to first open player slot
			if (_playerInv.AddItem(freezerItem.Type, freezerItem.FlowerTier, freezerItem.BatteryHealth, freezerItem.FreshnessTimer))
			{
				_activeFreezer.ClearSlot(slotIndex);
				AudioManager.Sfx("coin_collect", 1.8f);
				RefreshUI();
			}
			else
			{
				AudioManager.Sfx("window_rattle", -3f);
			}
		}
		else
		{
			// If freezer slot is empty, transfer active player item into it
			if (_playerInv.Slots[_playerInv.ActiveSlot].IsOccupied)
			{
				var carried = _playerInv.Slots[_playerInv.ActiveSlot];
				_activeFreezer.SetSlot(slotIndex, new InteractableFreezer.ChestItem
				{
					IsOccupied = true,
					Type = carried.Type,
					FlowerTier = carried.FlowerTier,
					BatteryHealth = carried.BatteryHealth,
					FreshnessTimer = carried.FreshnessTimer
				});
				_playerInv.ConsumeActiveItem();
				AudioManager.Sfx("coin_collect", 2f);
				RefreshUI();
			}
		}
	}

	private void OnPlayerSlotClicked(int playerSlotIndex)
	{
		if (_activeFreezer == null || _playerInv == null) return;

		var carried = _playerInv.Slots[playerSlotIndex];
		if (!carried.IsOccupied) return;

		// Move item from player slot to first available freezer slot
		int targetFreezerSlot = _activeFreezer.FindFirstEmptySlot();
		if (targetFreezerSlot >= 0)
		{
			_activeFreezer.SetSlot(targetFreezerSlot, new InteractableFreezer.ChestItem
			{
				IsOccupied = true,
				Type = carried.Type,
				FlowerTier = carried.FlowerTier,
				BatteryHealth = carried.BatteryHealth,
				FreshnessTimer = carried.FreshnessTimer
			});

			_playerInv.ConsumeItemAt(playerSlotIndex);
			AudioManager.Sfx("coin_collect", 2f);
			RefreshUI();
		}
		else
		{
			AudioManager.Sfx("window_rattle", -3f);
		}
	}

	private void OnBatteryDockClicked()
	{
		if (_activeFreezer == null || _playerInv == null) return;

		if (_activeFreezer.HasBattery)
		{
			// Eject battery into inventory
			int ejectedHp = _activeFreezer.BatteryHealth;
			if (_playerInv.AddItem(ItemType.CarBattery, 1, ejectedHp))
			{
				_activeFreezer.HasBattery = false;
				_activeFreezer.BatteryHealth = 0;
				AudioManager.Sfx("door_thud", -2f);
			}
			else
			{
				// Drop into world if player inventory full
				Node sceneRoot = GetTree()?.CurrentScene ?? (Node)_activeFreezer;
				Vector3 dropPos = _activeFreezer.GlobalPosition + new Vector3(0.6f, 0.4f, 0);
				WorldItem.Spawn(sceneRoot, dropPos, ItemType.CarBattery, 1, ejectedHp);
				_activeFreezer.HasBattery = false;
				_activeFreezer.BatteryHealth = 0;
				AudioManager.Sfx("door_thud", -2f);
			}
		}
		else
		{
			// Try docking battery from player inventory
			int batterySlot = -1;
			for (int i = 0; i < CarriedInventory.MaxSlots; i++)
			{
				if (_playerInv.Slots[i].IsOccupied && _playerInv.Slots[i].Type == ItemType.CarBattery)
				{
					batterySlot = i;
					break;
				}
			}

			if (batterySlot >= 0)
			{
				int hp = _playerInv.Slots[batterySlot].BatteryHealth;
				_playerInv.ConsumeItemAt(batterySlot);
				_activeFreezer.HasBattery = true;
				_activeFreezer.BatteryHealth = hp;
				AudioManager.Sfx("door_pound", 1.5f);
			}
			else
			{
				AudioManager.Sfx("window_rattle", -2f);
			}
		}

		_activeFreezer.UpdateVisuals();
		RefreshUI();
	}

	private static Color GetPlayerSlotColor(CarriedInventory.SlotData slot)
	{
		return slot.Type switch
		{
			ItemType.Seed => new Color(0.95f, 0.85f, 0.45f),
			ItemType.CarBattery => slot.BatteryHealth switch
			{
				> 50 => new Color(0.2f, 0.9f, 0.4f),
				> 20 => new Color(0.95f, 0.75f, 0.2f),
				_ => new Color(0.95f, 0.25f, 0.25f)
			},
			ItemType.Flower => slot.FlowerTier switch
			{
				1 => new Color(0.9f, 0.9f, 0.95f),
				2 => new Color(1.0f, 0.85f, 0.2f),
				3 => new Color(1.0f, 0.45f, 0.2f),
				4 => new Color(0.95f, 0.15f, 0.35f),
				5 => new Color(0.2f, 0.95f, 0.6f),
				_ => Colors.White
			},
			_ => Colors.White
		};
	}
}
