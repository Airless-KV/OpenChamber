// =========================================================================================
// Original Author : air (Airless-KV) - Nov 13, 2026
// Responsibilities: - Tactile 3-slot player carried inventory manager
//                   - Space bar to grab targeted 3D entity or drop currently held item
//                   - Scroll wheel to cycle active inventory slot (0, 1, 2)
//                   - First-person 3D held viewmodel in player hands attached to camera
//                   - Item consumption & addition API for farming, defense, and shop
// -----------------------------------------------------------------------------------------
// Change Log:
// - Nov 13, 2026 | air : Implemented 3-slot carried inventory, Space grab/drop, and scroll cycling
// - Nov 13, 2026 | air : Added CarBattery tracking with battery health preservation and 3D hand viewmodel
// - Nov 14, 2026 | air : Added ItemType.Crowbar viewmodel and slot display name
// =========================================================================================
using Godot;
using System;

public partial class CarriedInventory : Node
{
	[Signal] public delegate void InventoryChangedEventHandler();
	[Signal] public delegate void ActiveSlotChangedEventHandler(int slotIndex);

	public const int MaxSlots = 3;

	public struct SlotData
	{
		public bool IsOccupied;
		public ItemType Type;
		public int FlowerTier;
		public int BatteryHealth;

		public string GetDisplayName()
		{
			if (!IsOccupied) return "[Empty]";
			return Type switch
			{
				ItemType.Plank => "Wooden Plank",
				ItemType.Seed => "Level 1 Seed",
				ItemType.Flower => WorldItem.FlowerNames[Mathf.Clamp(FlowerTier - 1, 0, 4)],
				ItemType.CarBattery => $"Car Battery ({BatteryHealth}%)",
				ItemType.Crowbar => "Rusted Crowbar",
				_ => "Item"
			};
		}
	}

	public SlotData[] Slots = new SlotData[MaxSlots];
	public int ActiveSlot { get; private set; } = 0;

	[Export] public float GrabRange = 3.5f;
	[Export] public PlayerController Player;

	private Node3D _handViewModelRoot;
	private Camera3D _camera;
	private float _walkBobTimer = 0f;

	public override void _Ready()
	{
		Player ??= GetParent() as PlayerController;

		// Starter items for Day 1: 3 Level 1 seeds
		bool allEmpty = true;
		for (int i = 0; i < MaxSlots; i++) if (Slots[i].IsOccupied) allEmpty = false;
		if (allEmpty)
		{
			Slots[0] = new SlotData { IsOccupied = true, Type = ItemType.Seed, FlowerTier = 1 };
			Slots[1] = new SlotData { IsOccupied = true, Type = ItemType.Seed, FlowerTier = 1 };
			Slots[2] = new SlotData { IsOccupied = true, Type = ItemType.Seed, FlowerTier = 1 };
		}

		Callable.From(SetupViewModel).CallDeferred();
	}

	private void SetupViewModel()
	{
		_camera = GetViewport().GetCamera3D();
		if (_camera == null) return;

		_handViewModelRoot = new Node3D { Name = "HandViewModelRoot" };
		_camera.AddChild(_handViewModelRoot);
		_handViewModelRoot.Position = new Vector3(0.35f, -0.28f, -0.55f);
		_handViewModelRoot.RotationDegrees = new Vector3(5, -15, 0);

		UpdateViewModel();
	}

	public override void _UnhandledInput(InputEvent @event)
	{
		if (@event is InputEventMouseButton mb && mb.Pressed)
		{
			if (mb.ButtonIndex == MouseButton.WheelUp)
			{
				CycleSlot(-1);
				GetViewport().SetInputAsHandled();
			}
			else if (mb.ButtonIndex == MouseButton.WheelDown)
			{
				CycleSlot(1);
				GetViewport().SetInputAsHandled();
			}
		}
		else if (@event is InputEventKey key && key.Pressed && !key.Echo)
		{
			if (key.Keycode == Key.Space)
			{
				HandleSpaceAction();
				GetViewport().SetInputAsHandled();
			}
			else if (key.Keycode == Key.Key1)
			{
				SelectSlot(0);
				GetViewport().SetInputAsHandled();
			}
			else if (key.Keycode == Key.Key2)
			{
				SelectSlot(1);
				GetViewport().SetInputAsHandled();
			}
			else if (key.Keycode == Key.Key3)
			{
				SelectSlot(2);
				GetViewport().SetInputAsHandled();
			}
		}
	}

	public override void _Process(double delta)
	{
		if (_handViewModelRoot == null || Player == null) return;

		float speed = Player.Velocity.Length();
		if (speed > 0.1f && Player.IsOnFloor())
		{
			_walkBobTimer += (float)delta * (speed * 1.5f);
			float bobX = Mathf.Sin(_walkBobTimer * 0.5f) * 0.015f;
			float bobY = Mathf.Sin(_walkBobTimer) * 0.02f;
			_handViewModelRoot.Position = new Vector3(0.35f + bobX, -0.28f + bobY, -0.55f);
		}
		else
		{
			_handViewModelRoot.Position = _handViewModelRoot.Position.Lerp(new Vector3(0.35f, -0.28f, -0.55f), (float)delta * 8f);
		}
	}

	public void CycleSlot(int direction)
	{
		int newSlot = (ActiveSlot + direction + MaxSlots) % MaxSlots;
		SelectSlot(newSlot);
	}

	public void SelectSlot(int index)
	{
		if (index < 0 || index >= MaxSlots) return;
		ActiveSlot = index;
		EmitSignal(SignalName.ActiveSlotChanged, ActiveSlot);
		UpdateViewModel();
	}

	private void HandleSpaceAction()
	{
		// 1. If holding a flower and aiming at the balloon shop: sell it!
		if (Slots[ActiveSlot].IsOccupied && Slots[ActiveSlot].Type == ItemType.Flower && IsTargetingShop())
		{
			SellActiveFlower();
			return;
		}

		// 2. Grabbing targeted world item
		WorldItem targetedItem = FindTargetedWorldItem();
		if (targetedItem != null)
		{
			TryGrab(targetedItem);
		}
		else
		{
			TryDropActiveItem();
		}
	}

	public bool IsTargetingShop()
	{
		Camera3D cam = GetViewport().GetCamera3D();
		if (cam == null) return false;

		var space = Player?.GetWorld3D()?.DirectSpaceState;
		if (space == null) return false;

		Vector3 origin = cam.GlobalPosition;
		Vector3 dir = -cam.GlobalTransform.Basis.Z.Normalized();

		var exclude = Player != null ? new Godot.Collections.Array<Rid> { Player.GetRid() } : null;

		var hit = space.IntersectRay(new PhysicsRayQueryParameters3D
		{
			From = origin,
			To = origin + dir * 6.0f,
			Exclude = exclude
		});

		if (hit.Count > 0 && hit["collider"].Obj is GodotObject col)
		{
			if (col is HotAirBalloonShop || col is InteractableShop) return true;
			if (col is Node n)
			{
				Node cur = n;
				while (cur != null)
				{
					if (cur is HotAirBalloonShop || cur is InteractableShop) return true;
					cur = cur.GetParent();
				}
			}
		}

		return false;
	}

	public void SellActiveFlower()
	{
		if (!Slots[ActiveSlot].IsOccupied || Slots[ActiveSlot].Type != ItemType.Flower) return;

		int tier = Slots[ActiveSlot].FlowerTier;
		int reward = WorldItem.FlowerPrices[Mathf.Clamp(tier - 1, 0, 4)];
		string name = WorldItem.FlowerNames[Mathf.Clamp(tier - 1, 0, 4)];

		ConsumeActiveItem();

		var gm = GetTree().Root.FindChild("GameManager", true, false) as GameManager;
		gm?.AddCoins(reward);

		AudioManager.Sfx("coin_collect", 2f);

		var hud = GetTree().Root.FindChild("HUD", true, false) as HUDManager;
		hud?.SetInteractionPrompt($"[✓] Sold {name} for +{reward} Coins!");
	}

	public WorldItem FindTargetedWorldItem()
	{
		Camera3D cam = GetViewport().GetCamera3D();
		if (cam == null) return null;

		var space = Player?.GetWorld3D()?.DirectSpaceState;
		if (space == null) return null;

		Vector3 origin = cam.GlobalPosition;
		Vector3 dir = -cam.GlobalTransform.Basis.Z.Normalized();

		var exclude = Player != null ? new Godot.Collections.Array<Rid> { Player.GetRid() } : null;

		var hit = space.IntersectRay(new PhysicsRayQueryParameters3D
		{
			From = origin,
			To = origin + dir * GrabRange,
			Exclude = exclude
		});

		if (hit.Count > 0 && hit["collider"].Obj is GodotObject col)
		{
			if (col is WorldItem wi) return wi;
			if (col is Node n)
			{
				Node cur = n;
				while (cur != null)
				{
					if (cur is WorldItem item) return item;
					cur = cur.GetParent();
				}
			}
		}

		return null;
	}

	public bool TryGrab(WorldItem item)
	{
		if (item == null || !IsInstanceValid(item)) return false;

		// 1. Check if active slot is empty
		if (!Slots[ActiveSlot].IsOccupied)
		{
			Slots[ActiveSlot] = new SlotData
			{
				IsOccupied = true,
				Type = item.ItemType,
				FlowerTier = item.FlowerTier,
				BatteryHealth = item.BatteryHealth
			};
			item.QueueFree();
			AudioManager.Sfx("coin_collect", -4f);
			OnInventoryUpdated();
			return true;
		}

		// 2. Check if any other slot is empty
		for (int i = 0; i < MaxSlots; i++)
		{
			if (!Slots[i].IsOccupied)
			{
				Slots[i] = new SlotData
				{
					IsOccupied = true,
					Type = item.ItemType,
					FlowerTier = item.FlowerTier,
					BatteryHealth = item.BatteryHealth
				};
				item.QueueFree();
				AudioManager.Sfx("coin_collect", -4f);
				OnInventoryUpdated();
				return true;
			}
		}

		// 3. All slots full: SWAP held active item with the targeted world item!
		Vector3 swapPos = item.GlobalPosition;
		ItemType dropType = Slots[ActiveSlot].Type;
		int dropTier = Slots[ActiveSlot].FlowerTier;
		int dropBatteryHealth = Slots[ActiveSlot].BatteryHealth;

		// Pick up new item into active slot
		Slots[ActiveSlot] = new SlotData
		{
			IsOccupied = true,
			Type = item.ItemType,
			FlowerTier = item.FlowerTier,
			BatteryHealth = item.BatteryHealth
		};
		item.QueueFree();

		// Spawn previous held item at ground position
		Node root = GetTree().CurrentScene ?? (Node)this;
		WorldItem.Spawn(root, swapPos, dropType, dropTier, dropBatteryHealth);

		AudioManager.Sfx("door_thud", -2f);
		OnInventoryUpdated();
		return true;
	}

	public bool TryDropActiveItem()
	{
		if (!Slots[ActiveSlot].IsOccupied) return false;

		Camera3D cam = GetViewport().GetCamera3D();
		Vector3 dropPos = Player?.GlobalPosition ?? Vector3.Zero;
		if (cam != null)
		{
			Vector3 forward = -cam.GlobalTransform.Basis.Z;
			forward.Y = 0;
			forward = forward.Normalized();
			dropPos += forward * 1.3f + new Vector3(0, 0.4f, 0);
		}

		// Raycast down to find ground level
		var space = Player?.GetWorld3D()?.DirectSpaceState;
		if (space != null)
		{
			var floorHit = space.IntersectRay(new PhysicsRayQueryParameters3D
			{
				From = dropPos + new Vector3(0, 1.0f, 0),
				To = dropPos - new Vector3(0, 3.0f, 0)
			});
			if (floorHit.Count > 0)
			{
				dropPos = (Vector3)floorHit["position"];
			}
		}

		ItemType type = Slots[ActiveSlot].Type;
		int tier = Slots[ActiveSlot].FlowerTier;
		int batteryHealth = Slots[ActiveSlot].BatteryHealth;

		Slots[ActiveSlot] = default;

		Node root = GetTree().CurrentScene ?? (Node)this;
		WorldItem.Spawn(root, dropPos, type, tier, batteryHealth);

		AudioManager.Sfx("door_thud", -5f);
		OnInventoryUpdated();
		return true;
	}

	public bool AddItem(ItemType type, int tier = 1, int batteryHealth = 100)
	{
		// First check active slot
		if (!Slots[ActiveSlot].IsOccupied)
		{
			Slots[ActiveSlot] = new SlotData { IsOccupied = true, Type = type, FlowerTier = tier, BatteryHealth = batteryHealth };
			OnInventoryUpdated();
			return true;
		}

		// Then check other slots
		for (int i = 0; i < MaxSlots; i++)
		{
			if (!Slots[i].IsOccupied)
			{
				Slots[i] = new SlotData { IsOccupied = true, Type = type, FlowerTier = tier, BatteryHealth = batteryHealth };
				OnInventoryUpdated();
				return true;
			}
		}

		return false; // All 3 slots full
	}

	public bool HasActiveBattery(out int batteryHealth)
	{
		if (Slots[ActiveSlot].IsOccupied && Slots[ActiveSlot].Type == ItemType.CarBattery)
		{
			batteryHealth = Slots[ActiveSlot].BatteryHealth;
			return true;
		}
		batteryHealth = 0;
		return false;
	}

	public bool ConsumeActiveBattery(out int batteryHealth)
	{
		if (HasActiveBattery(out batteryHealth))
		{
			Slots[ActiveSlot] = default;
			OnInventoryUpdated();
			return true;
		}
		return false;
	}

	public bool HasAnyBattery(out int slotIndex, out int batteryHealth)
	{
		if (Slots[ActiveSlot].IsOccupied && Slots[ActiveSlot].Type == ItemType.CarBattery)
		{
			slotIndex = ActiveSlot;
			batteryHealth = Slots[ActiveSlot].BatteryHealth;
			return true;
		}

		for (int i = 0; i < MaxSlots; i++)
		{
			if (Slots[i].IsOccupied && Slots[i].Type == ItemType.CarBattery)
			{
				slotIndex = i;
				batteryHealth = Slots[i].BatteryHealth;
				return true;
			}
		}

		slotIndex = -1;
		batteryHealth = 0;
		return false;
	}

	public bool ConsumeBattery(out int batteryHealth)
	{
		if (HasAnyBattery(out int idx, out batteryHealth))
		{
			Slots[idx] = default;
			OnInventoryUpdated();
			return true;
		}
		return false;
	}

	public bool HasItem(ItemType type)
	{
		for (int i = 0; i < MaxSlots; i++)
			if (Slots[i].IsOccupied && Slots[i].Type == type) return true;
		return false;
	}

	public bool HasActiveItem(ItemType type)
	{
		return Slots[ActiveSlot].IsOccupied && Slots[ActiveSlot].Type == type;
	}

	public bool ConsumeActiveItem()
	{
		if (!Slots[ActiveSlot].IsOccupied) return false;
		Slots[ActiveSlot] = default;
		OnInventoryUpdated();
		return true;
	}

	public bool ConsumeItem(ItemType type)
	{
		// Try active slot first
		if (Slots[ActiveSlot].IsOccupied && Slots[ActiveSlot].Type == type)
		{
			Slots[ActiveSlot] = default;
			OnInventoryUpdated();
			return true;
		}

		// Otherwise search remaining slots
		for (int i = 0; i < MaxSlots; i++)
		{
			if (Slots[i].IsOccupied && Slots[i].Type == type)
			{
				Slots[i] = default;
				OnInventoryUpdated();
				return true;
			}
		}
		return false;
	}

	private void OnInventoryUpdated()
	{
		EmitSignal(SignalName.InventoryChanged);
		UpdateViewModel();
	}

	private void UpdateViewModel()
	{
		if (_handViewModelRoot == null || !IsInstanceValid(_handViewModelRoot)) return;

		foreach (Node child in _handViewModelRoot.GetChildren())
		{
			child.QueueFree();
		}

		if (!Slots[ActiveSlot].IsOccupied) return;

		var slot = Slots[ActiveSlot];
		switch (slot.Type)
		{
			case ItemType.Plank:
				var plankMesh = new MeshInstance3D();
				plankMesh.Mesh = new BoxMesh { Size = new Vector3(0.6f, 0.08f, 0.22f) };
				var plankMat = new StandardMaterial3D { AlbedoColor = new Color(0.58f, 0.38f, 0.22f), Roughness = 0.8f };
				plankMesh.MaterialOverride = plankMat;
				plankMesh.RotationDegrees = new Vector3(10, 25, -15);
				_handViewModelRoot.AddChild(plankMesh);
				break;

			case ItemType.Seed:
				var seedMesh = new MeshInstance3D();
				seedMesh.Mesh = new CylinderMesh { TopRadius = 0.12f, BottomRadius = 0.14f, Height = 0.22f };
				var seedMat = new StandardMaterial3D { AlbedoColor = new Color(0.85f, 0.72f, 0.42f), Roughness = 0.65f };
				seedMesh.MaterialOverride = seedMat;
				seedMesh.RotationDegrees = new Vector3(15, 10, -5);
				_handViewModelRoot.AddChild(seedMesh);
				break;

			case ItemType.Flower:
				int tier = Mathf.Clamp(slot.FlowerTier, 1, 5);
				string texPath = $"res://assets/textures/flowers/flower_lvl{tier}.png";
				var sprite = new Sprite3D
				{
					Billboard = BaseMaterial3D.BillboardModeEnum.FixedY,
					Shaded = true,
					AlphaCut = SpriteBase3D.AlphaCutMode.OpaquePrepass,
					TextureFilter = BaseMaterial3D.TextureFilterEnum.Nearest,
					PixelSize = 0.0022f
				};
				if (ResourceLoader.Exists(texPath))
				{
					sprite.Texture = GD.Load<Texture2D>(texPath);
				}
				if (tier == 5)
				{
					sprite.Modulate = new Color(1.2f, 1.2f, 1.1f, 1f);
				}
				sprite.Scale = Vector3.One * 0.7f;
				_handViewModelRoot.AddChild(sprite);
				break;

			case ItemType.CarBattery:
				var batteryRoot = new Node3D();

				// 1. Black Casing Body
				var batteryBody = new MeshInstance3D();
				batteryBody.Mesh = new BoxMesh { Size = new Vector3(0.32f, 0.22f, 0.2f) };
				batteryBody.MaterialOverride = new StandardMaterial3D { AlbedoColor = new Color(0.14f, 0.15f, 0.17f), Roughness = 0.6f };
				batteryRoot.AddChild(batteryBody);

				// 2. Red Terminal Post (+)
				var posPost = new MeshInstance3D();
				posPost.Mesh = new CylinderMesh { TopRadius = 0.025f, BottomRadius = 0.03f, Height = 0.06f };
				posPost.MaterialOverride = new StandardMaterial3D { AlbedoColor = new Color(0.85f, 0.15f, 0.15f) };
				posPost.Position = new Vector3(-0.09f, 0.13f, 0f);
				batteryRoot.AddChild(posPost);

				// 3. Blue Terminal Post (-)
				var negPost = new MeshInstance3D();
				negPost.Mesh = new CylinderMesh { TopRadius = 0.025f, BottomRadius = 0.03f, Height = 0.06f };
				negPost.MaterialOverride = new StandardMaterial3D { AlbedoColor = new Color(0.18f, 0.38f, 0.88f) };
				negPost.Position = new Vector3(0.09f, 0.13f, 0f);
				batteryRoot.AddChild(negPost);

				// 4. Handle
				var battHandle = new MeshInstance3D();
				battHandle.Mesh = new BoxMesh { Size = new Vector3(0.22f, 0.025f, 0.05f) };
				battHandle.MaterialOverride = new StandardMaterial3D { AlbedoColor = new Color(0.35f, 0.35f, 0.38f) };
				battHandle.Position = new Vector3(0f, 0.13f, 0f);
				batteryRoot.AddChild(battHandle);

				// 5. Warning Label
				var battLabel = new MeshInstance3D();
				battLabel.Mesh = new BoxMesh { Size = new Vector3(0.16f, 0.1f, 0.01f) };
				battLabel.MaterialOverride = new StandardMaterial3D { AlbedoColor = new Color(0.95f, 0.82f, 0.15f), Roughness = 0.4f };
				battLabel.Position = new Vector3(0f, 0f, 0.105f);
				batteryRoot.AddChild(battLabel);

				batteryRoot.RotationDegrees = new Vector3(15, 20, -10);
				_handViewModelRoot.AddChild(batteryRoot);
				break;

			case ItemType.Crowbar:
				var crowbarRoot = new Node3D();
				var rustMat = new StandardMaterial3D
				{
					AlbedoColor = new Color(0.38f, 0.24f, 0.18f),
					Metallic = 0.75f,
					Roughness = 0.65f
				};

				// Held crowbar shaft
				var cbShaft = new MeshInstance3D();
				cbShaft.Mesh = new CylinderMesh { TopRadius = 0.018f, BottomRadius = 0.018f, Height = 0.55f };
				cbShaft.MaterialOverride = rustMat;
				cbShaft.Position = new Vector3(0f, 0.1f, -0.1f);
				crowbarRoot.AddChild(cbShaft);

				// Hook curve
				var cbHook = new MeshInstance3D();
				cbHook.Mesh = new CylinderMesh { TopRadius = 0.016f, BottomRadius = 0.016f, Height = 0.16f };
				cbHook.MaterialOverride = rustMat;
				cbHook.Position = new Vector3(0.08f, 0.32f, -0.15f);
				cbHook.RotationDegrees = new Vector3(-35f, 0f, -40f);
				crowbarRoot.AddChild(cbHook);

				// Claw chisel head
				var cbClaw = new MeshInstance3D();
				cbClaw.Mesh = new BoxMesh { Size = new Vector3(0.05f, 0.012f, 0.06f) };
				cbClaw.MaterialOverride = rustMat;
				cbClaw.Position = new Vector3(0.12f, 0.36f, -0.18f);
				cbClaw.RotationDegrees = new Vector3(-35f, 0f, -40f);
				crowbarRoot.AddChild(cbClaw);

				// Straight chisel end
				var cbChisel = new MeshInstance3D();
				cbChisel.Mesh = new BoxMesh { Size = new Vector3(0.045f, 0.012f, 0.055f) };
				cbChisel.MaterialOverride = rustMat;
				cbChisel.Position = new Vector3(-0.02f, -0.16f, -0.05f);
				crowbarRoot.AddChild(cbChisel);

				crowbarRoot.RotationDegrees = new Vector3(25, -35, 20);
				_handViewModelRoot.AddChild(crowbarRoot);
				break;
		}
	}
}
