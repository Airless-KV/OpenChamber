// =========================================================================================
// Original Author : air (Airless-KV) - Nov 15, 2026
// Responsibilities: - Cryo-Freezer storage chest for preserving harvested flowers & items
//                   - Minecraft-like chest inventory with 10 non-stackable item slots
//                   - Full item decay simulation when freezer has no battery power
//                   - Complete freshness freeze when cooling is active (car battery powered)
//                   - Car battery docking slot and 1% per 5s active cooling power burn
//                   - 3D visual chest, status labels, level gauges, and LED cooling indicators
//                   - Pick up, carry, and relocate freezer anywhere via [G]
// -----------------------------------------------------------------------------------------
// Change Log:
// - Nov 15, 2026 | air : Implemented Cryo-Freezer unit with 10-flower capacity and battery burn
// - Nov 15, 2026 | air : Added [Space] / [G] pick up and relocation mechanics with contents preservation and auto-facing player on drop
// - Nov 15, 2026 | air : Upgraded to full Minecraft chest non-stackable slot inventory system with UI and live decay
// - Nov 15, 2026 | air : Updated Seed display names to reflect FlowerTier level
// - Nov 17, 2026 | air : Accelerated cooling battery burn to 1% per 2.0s for power management tension
// - Nov 17, 2026 | air : Streamlined prompt format to '[E / LMB] Open Chest | [F / RMB] Quick Store | [Space / G] Relocate'
// - Nov 17, 2026 | air : Added FreezerInteracted event for interactive tutorial verification
// =========================================================================================
using Godot;
using System;
using System.Collections.Generic;
using System.Linq;

public partial class InteractableFreezer : InteractableBase
{
	public static event Action FreezerInteracted;

	public static InteractableFreezer Instance { get; private set; }
	public static InteractableFreezer HeldFreezerInstance { get; private set; }
	public bool IsBeingCarried { get; private set; } = false;

	public const int ChestCapacity = 10;
	public const float BatteryBurnInterval = 2.0f; // 1% power consumed per 2 seconds

	public struct ChestItem
	{
		public bool IsOccupied;
		public ItemType Type;
		public int FlowerTier;
		public int BatteryHealth;
		public float FreshnessTimer;

		public string GetDisplayName()
		{
			if (!IsOccupied) return "[Empty]";
			return Type switch
			{
				ItemType.Seed => $"Level {Mathf.Clamp(FlowerTier, 1, 5)} Seed",
				ItemType.Flower => $"{WorldItem.FlowerNames[Mathf.Clamp(FlowerTier - 1, 0, 4)]}",
				ItemType.CarBattery => $"Car Battery ({BatteryHealth}%)",
				_ => "Item"
			};
		}

		public string GetShortName()
		{
			if (!IsOccupied) return "[Empty]";
			return Type switch
			{
				ItemType.Seed => $"Seed (Lvl {Mathf.Clamp(FlowerTier, 1, 5)})",
				ItemType.Flower => $"T{FlowerTier} Flower\n{WorldItem.FlowerNames[Mathf.Clamp(FlowerTier - 1, 0, 4)].Split(' ')[0]}",
				ItemType.CarBattery => $"Battery\n({BatteryHealth}%)",
				_ => "Item"
			};
		}

		public Color GetItemColor()
		{
			if (!IsOccupied) return new Color(0.5f, 0.55f, 0.6f, 0.5f);
			return Type switch
			{
				ItemType.Seed => new Color(0.95f, 0.85f, 0.45f),
				ItemType.CarBattery => BatteryHealth switch
				{
					> 50 => new Color(0.2f, 0.9f, 0.4f),
					> 20 => new Color(0.95f, 0.75f, 0.2f),
					_ => new Color(0.95f, 0.25f, 0.25f)
				},
				ItemType.Flower => FlowerTier switch
				{
					1 => new Color(0.95f, 0.95f, 0.95f),
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

	[ExportGroup("Battery State")]
	[Export] public bool HasBattery { get; set; } = false;
	[Export] public int BatteryHealth { get; set; } = 100;

	[ExportGroup("Visual References")]
	[Export] public Node3D BatteryVisual;
	[Export] public MeshInstance3D LedIndicator;
	[Export] public Label3D StatusLabel;
	[Export] public OmniLight3D IndicatorLight;
	[Export] public MeshInstance3D LevelBarFill;

	public ChestItem[] Slots = new ChestItem[ChestCapacity];
	public bool IsCooling => HasBattery && BatteryHealth > 0;

	public int OccupiedCount => Slots.Count(s => s.IsOccupied);

	private float _burnTimer = 0f;
	private HUDManager _hud;
	private StandardMaterial3D _ledMat;
	private StandardMaterial3D _barMat;
	private CollisionShape3D _colShape;
	private Node _originalParent;

	public override void _EnterTree()
	{
		Instance = this;
	}

	public override void _Ready()
	{
		base._Ready();
		AddToGroup("Freezers");
		_hud ??= GetTree()?.Root.FindChild("HUD", true, false) as HUDManager;

		_colShape ??= GetNodeOrNull<CollisionShape3D>("CollisionShape3D");
		BatteryVisual ??= GetNodeOrNull<Node3D>("BatteryVisual");
		LedIndicator ??= GetNodeOrNull<MeshInstance3D>("LedIndicator");
		StatusLabel ??= GetNodeOrNull<Label3D>("StatusLabel");
		IndicatorLight ??= GetNodeOrNull<OmniLight3D>("IndicatorLight");
		LevelBarFill ??= GetNodeOrNull<MeshInstance3D>("LevelBarFill");

		if (GetNodeOrNull<MeshInstance3D>("ChestBody") == null)
		{
			BuildProceduralVisuals();
		}

		_colShape ??= GetNodeOrNull<CollisionShape3D>("CollisionShape3D");

		if (LedIndicator != null && LedIndicator.MaterialOverride is StandardMaterial3D sm)
		{
			_ledMat = sm;
		}

		if (LevelBarFill != null && LevelBarFill.MaterialOverride is StandardMaterial3D bm)
		{
			_barMat = bm;
		}

		UpdateVisuals();
	}

	public override void _UnhandledInput(InputEvent @event)
	{
		if (@event is InputEventKey { Pressed: true, Echo: false, Keycode: Key.G })
		{
			var player = GetTree()?.GetFirstNodeInGroup("Player") as PlayerController;
			if (player == null) return;

			if (IsBeingCarried)
			{
				PlaceDownFreezer(player);
				GetViewport().SetInputAsHandled();
			}
			else if (HeldFreezerInstance == null)
			{
				// Check if aiming at this freezer within range
				if (IsPlayerAimingAtThis(player, 4.0f))
				{
					PickUpFreezer(player);
					GetViewport().SetInputAsHandled();
				}
			}
		}
	}

	private bool IsPlayerAimingAtThis(PlayerController player, float maxDist)
	{
		Camera3D cam = GetViewport()?.GetCamera3D();
		if (cam == null) return false;

		var space = player.GetWorld3D()?.DirectSpaceState;
		if (space == null) return false;

		var hit = space.IntersectRay(new PhysicsRayQueryParameters3D
		{
			From = cam.GlobalPosition,
			To = cam.GlobalPosition - cam.GlobalTransform.Basis.Z * maxDist,
			Exclude = new Godot.Collections.Array<Rid> { player.GetRid() }
		});

		if (hit.Count > 0 && hit["collider"].Obj is GodotObject col)
		{
			if (col == this) return true;
			if (col is Node n && n.FindParent(Name) == this) return true;
		}

		return false;
	}

	public void PickUpFreezer(PlayerController player)
	{
		if (IsBeingCarried || HeldFreezerInstance != null) return;

		Camera3D cam = GetViewport()?.GetCamera3D();
		if (cam == null) return;

		FreezerInventoryUI.Close();

		HeldFreezerInstance = this;
		IsBeingCarried = true;
		IsInteractable = false;
		_originalParent = GetParent();

		if (_colShape != null)
			_colShape.Disabled = true;

		// Reparent to camera so it stays in hands / in front of player
		_originalParent?.RemoveChild(this);
		cam.AddChild(this);

		Position = new Vector3(0.0f, -0.45f, -1.35f);
		RotationDegrees = new Vector3(10f, 180f, 0f);

		AudioManager.Sfx("door_pound", 0.5f);
		_hud?.SetInteractionPrompt("[Space] Place Down Cryo-Freezer | Aim at ground/floor to position");
	}

	public void PlaceDownFreezer(PlayerController player)
	{
		if (!IsBeingCarried) return;

		Camera3D cam = GetViewport()?.GetCamera3D();
		Node targetParent = _originalParent != null && IsInstanceValid(_originalParent)
			? _originalParent
			: (GetTree()?.CurrentScene ?? (Node)this);

		Vector3 placePos = player.GlobalPosition;
		Vector3 placeForward = Vector3.Forward;

		if (cam != null)
		{
			placeForward = -cam.GlobalTransform.Basis.Z;
			placeForward.Y = 0;
			placeForward = placeForward.Normalized();
			placePos += placeForward * 1.5f;

			// Raycast down for floor/terrain alignment
			var space = player.GetWorld3D()?.DirectSpaceState;
			if (space != null)
			{
				var floorHit = space.IntersectRay(new PhysicsRayQueryParameters3D
				{
					From = placePos + new Vector3(0, 1.5f, 0),
					To = placePos - new Vector3(0, 4.0f, 0),
					CollisionMask = 1 // Layer 1 World/Floor
				});

				if (floorHit.Count > 0)
				{
					placePos = (Vector3)floorHit["position"];
				}
			}
		}

		cam?.RemoveChild(this);
		targetParent.AddChild(this);

		GlobalPosition = placePos;

		// Face directly towards the player regardless of the pick-up angle
		Vector3 toPlayer = player.GlobalPosition - placePos;
		toPlayer.Y = 0;
		if (toPlayer.LengthSquared() > 0.001f)
		{
			toPlayer = toPlayer.Normalized();
			float yaw = Mathf.Atan2(toPlayer.X, toPlayer.Z);
			Rotation = new Vector3(0f, yaw, 0f);
		}

		if (_colShape != null)
			_colShape.Disabled = false;

		IsBeingCarried = false;
		IsInteractable = true;
		HeldFreezerInstance = null;

		AudioManager.Sfx("door_pound", -1.0f);
		_hud?.SetInteractionPrompt($"[✓] Cryo-Freezer placed! Cooling: {(IsCooling ? "Active" : "Off")} | Items: {OccupiedCount}/{ChestCapacity}");
		UpdateVisuals();
	}

	public override void _Process(double delta)
	{
		float dt = (float)delta;

		if (IsCooling)
		{
			_burnTimer += dt;
			if (_burnTimer >= BatteryBurnInterval)
			{
				_burnTimer -= BatteryBurnInterval;
				BatteryHealth = Mathf.Max(0, BatteryHealth - 1);
				if (BatteryHealth <= 0)
				{
					AudioManager.Sfx("window_rattle", -2f);
				}
				UpdateVisuals();
			}
		}
		else
		{
			// Freezer is offline: items with decay timers spoil without active refrigeration!
			bool spoiledAny = false;
			for (int i = 0; i < ChestCapacity; i++)
			{
				if (Slots[i].IsOccupied && Slots[i].Type == ItemType.Flower)
				{
					Slots[i].FreshnessTimer -= dt;
					if (Slots[i].FreshnessTimer <= 0f)
					{
						Slots[i] = default;
						spoiledAny = true;
					}
				}
			}

			if (spoiledAny)
			{
				_hud?.SetInteractionPrompt("[!] Freezer offline! Flowers inside are spoiling without battery power!");
				AudioManager.Sfx("foliage_rustle", -2f);
				UpdateVisuals();
			}
		}
	}

	public int FindFirstEmptySlot()
	{
		for (int i = 0; i < ChestCapacity; i++)
		{
			if (!Slots[i].IsOccupied) return i;
		}
		return -1;
	}

	public void SetSlot(int slotIndex, ChestItem item)
	{
		if (slotIndex >= 0 && slotIndex < ChestCapacity)
		{
			Slots[slotIndex] = item;
			UpdateVisuals();
		}
	}

	public void ClearSlot(int slotIndex)
	{
		if (slotIndex >= 0 && slotIndex < ChestCapacity)
		{
			Slots[slotIndex] = default;
			UpdateVisuals();
		}
	}

	public override string GetPrompt()
	{
		string powerText = IsCooling ? $"Active ({BatteryHealth}%)" : (HasBattery ? "Depleted (0%)" : "No Battery");
		return $"[E / LMB] Open Chest | [F / RMB] Quick Store | [Space / G] Relocate ({OccupiedCount}/{ChestCapacity} | Cooling: {powerText})";
	}

	protected override void OnInteract(PlayerController player)
	{
		// Open Minecraft-style Chest Inventory UI
		FreezerInventoryUI.Open(this);
		FreezerInteracted?.Invoke();
	}

	protected override void OnInteractSecondary(PlayerController player)
	{
		var inv = player?.GetNodeOrNull<CarriedInventory>("CarriedInventory");
		if (inv == null) return;

		FreezerInteracted?.Invoke();

		// Quick-deposit active item into freezer if space available
		if (inv.Slots[inv.ActiveSlot].IsOccupied)
		{
			var held = inv.Slots[inv.ActiveSlot];

			// If holding a battery and dock is empty, dock it directly
			if (held.Type == ItemType.CarBattery && !HasBattery)
			{
				HasBattery = true;
				BatteryHealth = held.BatteryHealth;
				inv.ConsumeActiveItem();
				AudioManager.Sfx("door_pound", 1.5f);
				_hud?.SetInteractionPrompt($"[✓] Docked Car Battery in Freezer! ({BatteryHealth}% HP - Cooling Active)");
				UpdateVisuals();
				return;
			}

			int targetSlot = FindFirstEmptySlot();
			if (targetSlot >= 0)
			{
				SetSlot(targetSlot, new ChestItem
				{
					IsOccupied = true,
					Type = held.Type,
					FlowerTier = held.FlowerTier,
					BatteryHealth = held.BatteryHealth,
					FreshnessTimer = held.FreshnessTimer
				});

				inv.ConsumeActiveItem();
				AudioManager.Sfx("coin_collect", 2f);
				_hud?.SetInteractionPrompt($"[✓] Stored {held.GetDisplayName()} in freezer slot #{targetSlot + 1} ({OccupiedCount}/{ChestCapacity})");
				UpdateVisuals();
				return;
			}
			else
			{
				AudioManager.Sfx("window_rattle", -3f);
				_hud?.SetInteractionPrompt($"[!] Cryo-Freezer chest is full! ({ChestCapacity}/{ChestCapacity})");
				return;
			}
		}

		// If hands empty, quick retrieve first item
		for (int i = 0; i < ChestCapacity; i++)
		{
			if (Slots[i].IsOccupied)
			{
				var item = Slots[i];
				if (inv.AddItem(item.Type, item.FlowerTier, item.BatteryHealth, item.FreshnessTimer))
				{
					ClearSlot(i);
					AudioManager.Sfx("coin_collect", 1.8f);
					_hud?.SetInteractionPrompt($"[✓] Retrieved {item.GetDisplayName()} from freezer ({OccupiedCount}/{ChestCapacity} left)");
					UpdateVisuals();
					return;
				}
				else
				{
					AudioManager.Sfx("window_rattle", -3f);
					_hud?.SetInteractionPrompt("[!] Player carried inventory is full!");
					return;
				}
			}
		}

		_hud?.SetInteractionPrompt("[!] Cryo-Freezer chest is empty!");
	}

	private void BuildProceduralVisuals()
	{
		// 1. Insulated Metallic Chest Body
		var body = new MeshInstance3D
		{
			Name = "ChestBody",
			Mesh = new BoxMesh { Size = new Vector3(0.95f, 0.75f, 0.65f) },
			MaterialOverride = new StandardMaterial3D
			{
				AlbedoColor = new Color(0.72f, 0.78f, 0.85f),
				Metallic = 0.65f,
				Roughness = 0.35f
			},
			Position = new Vector3(0, 0.375f, 0)
		};
		AddChild(body);

		// 2. Chest Lid with handle
		var lid = new MeshInstance3D
		{
			Name = "ChestLid",
			Mesh = new BoxMesh { Size = new Vector3(0.98f, 0.12f, 0.68f) },
			MaterialOverride = new StandardMaterial3D
			{
				AlbedoColor = new Color(0.25f, 0.42f, 0.58f),
				Metallic = 0.4f,
				Roughness = 0.5f
			},
			Position = new Vector3(0, 0.81f, 0)
		};
		AddChild(lid);

		// Handle
		var handle = new MeshInstance3D
		{
			Name = "LidHandle",
			Mesh = new BoxMesh { Size = new Vector3(0.32f, 0.04f, 0.06f) },
			MaterialOverride = new StandardMaterial3D { AlbedoColor = new Color(0.12f, 0.14f, 0.16f) },
			Position = new Vector3(0, 0.88f, 0.28f)
		};
		AddChild(handle);

		// 3. Collision Box
		var col = new CollisionShape3D
		{
			Name = "CollisionShape3D",
			Shape = new BoxShape3D { Size = new Vector3(1.0f, 0.9f, 0.7f) },
			Position = new Vector3(0, 0.45f, 0)
		};
		AddChild(col);

		// 4. Battery Dock Visual
		BatteryVisual = new Node3D { Name = "BatteryVisual", Position = new Vector3(0.36f, 0.45f, 0.34f) };
		var bMesh = new MeshInstance3D
		{
			Mesh = new BoxMesh { Size = new Vector3(0.24f, 0.18f, 0.16f) },
			MaterialOverride = new StandardMaterial3D { AlbedoColor = new Color(0.12f, 0.12f, 0.14f) }
		};
		BatteryVisual.AddChild(bMesh);
		AddChild(BatteryVisual);

		// 5. LED Indicator
		LedIndicator = new MeshInstance3D
		{
			Name = "LedIndicator",
			Mesh = new SphereMesh { Radius = 0.035f, Height = 0.07f },
			Position = new Vector3(-0.35f, 0.68f, 0.34f)
		};
		_ledMat = new StandardMaterial3D
		{
			AlbedoColor = new Color(0.1f, 0.8f, 0.9f),
			EmissionEnabled = true,
			Emission = new Color(0.1f, 0.8f, 0.9f),
			EmissionEnergyMultiplier = 2.0f
		};
		LedIndicator.MaterialOverride = _ledMat;
		AddChild(LedIndicator);

		IndicatorLight = new OmniLight3D
		{
			Name = "IndicatorLight",
			LightColor = new Color(0.2f, 0.85f, 1.0f),
			LightEnergy = 0.8f,
			OmniRange = 1.2f,
			Position = new Vector3(-0.35f, 0.68f, 0.42f)
		};
		AddChild(IndicatorLight);

		// 6. 3D Floating Status Label
		StatusLabel = new Label3D
		{
			Name = "StatusLabel",
			Billboard = BaseMaterial3D.BillboardModeEnum.Enabled,
			FontSize = 24,
			OutlineSize = 6,
			Position = new Vector3(0, 1.15f, 0)
		};
		AddChild(StatusLabel);
	}

	public void UpdateVisuals()
	{
		if (BatteryVisual != null)
		{
			BatteryVisual.Visible = HasBattery;
		}

		bool active = IsCooling;
		Color ledColor = active ? new Color(0.1f, 0.9f, 0.95f) : (HasBattery ? new Color(0.9f, 0.6f, 0.1f) : new Color(0.85f, 0.15f, 0.15f));

		if (_ledMat != null)
		{
			_ledMat.AlbedoColor = ledColor;
			_ledMat.Emission = ledColor;
			_ledMat.EmissionEnergyMultiplier = active ? 2.5f : 0.6f;
		}

		if (IndicatorLight != null)
		{
			IndicatorLight.LightColor = ledColor;
			IndicatorLight.LightEnergy = active ? 1.0f : 0.2f;
		}

		if (StatusLabel != null)
		{
			string stateText = active ? $"[COOLING ACTIVE] Batt: {BatteryHealth}%" : (HasBattery ? "[BATTERY DEAD (0%)]" : "[NO BATTERY]");
			StatusLabel.Text = $"CRYO-FREEZER CHEST\n{stateText}\nItems: {OccupiedCount}/{ChestCapacity}";
			StatusLabel.Modulate = active ? new Color(0.4f, 0.95f, 1.0f) : new Color(0.95f, 0.4f, 0.4f);
		}

		// Real-time battery fill bar on the freezer
		if (LevelBarFill != null)
		{
			float ratio = HasBattery ? Mathf.Clamp(BatteryHealth / 100f, 0f, 1f) : 0f;
			LevelBarFill.Visible = HasBattery && ratio > 0.01f;
			LevelBarFill.Scale = new Vector3(ratio, 1f, 1f);

			Color barColor;
			if (BatteryHealth > 50)
				barColor = new Color(0.2f, 0.9f, 1.0f); // Cool Cryo Cyan
			else if (BatteryHealth > 20)
				barColor = new Color(0.95f, 0.75f, 0.15f); // Amber Warning
			else
				barColor = new Color(0.95f, 0.2f, 0.15f); // Depleted Red

			if (_barMat == null && LevelBarFill.MaterialOverride is StandardMaterial3D sm)
			{
				_barMat = sm;
			}

			if (_barMat != null)
			{
				_barMat.AlbedoColor = barColor;
				_barMat.Emission = barColor;
				_barMat.EmissionEnergyMultiplier = HasBattery && BatteryHealth > 0 ? 2.5f : 0f;
			}
		}
	}
}
