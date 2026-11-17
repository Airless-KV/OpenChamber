// =========================================================================================
// Original Author : air (Airless-KV) - Oct 31, 2026
// Responsibilities: - Interfacing inventory and shop purchases with Anj's HUDManager
//                   - Extended inventory tracking for Planks, Barriers, and Axe
//                   - Seed economy gating (consumption & replenishment)
//                   - Defense inventory consumption (Planks & Barriers)
// -----------------------------------------------------------------------------------------
// Change Log:
// - Oct 31, 2026 | air : Added InventoryChanged signal, inventory tracking, and TryBuyItem
// - Nov 06, 2026 | air : Added TryConsumeLevel1Seed, AddSeeds, starter seeds allotment, and defense usage methods
// - Nov 06, 2026 | air : Enabled infinite wooden planks mode for defense and barricade testing
// - Nov 11, 2026 | air : Simplified TryBuyItem following barrier and axe removal
// - Nov 13, 2026 | air : Integrated 3D entity item spawning and carried inventory synchronization on purchase
// - Nov 13, 2026 | air : Added Car Battery purchase handling and 3D spawning
// - Nov 14, 2026 | air : Removed infinite planks; added Rusted Crowbar shop purchasing and real-count tracking
// - Nov 15, 2026 | air : Removed Plank and Crowbar inventory tracking and purchasing
// - Nov 15, 2026 | air : Added Cryo-Freezer shop purchase handling and physical instantiation
// - Nov 15, 2026 | air : Added multi-freezer cabin placement support with dedicated battery per unit
// - Nov 15, 2026 | air : Expanded freezer purchase capacity and support for portable placement
// - Nov 16, 2026 | air : Reordered outdoor freezer spawning to AddChild before setting GlobalPosition
// - Nov 17, 2026 | air : Added daily limited Car Battery stock (1/day) to prevent battery hoarding
// - Nov 17, 2026 | air : Synchronized SeedCost with ShopCatalog seed price (100 coins) and removed freezer shop purchasing
// =========================================================================================
using Godot;
using System.Linq;

public partial class GameManager
{
	[Signal]
	public delegate void InventoryChangedEventHandler();

	public int Planks => 0;
	public int Barriers { get; private set; } = 0;
	public bool HasAxe { get; private set; } = false;
	public int DailyBatteryStock { get; private set; } = 1;

	private int _lastObservedSeeds = -1;

	public override void _EnterTree()
	{
		var seedItem = ShopCatalog.Get(ShopCatalog.SeedId);
		if (seedItem != null)
		{
			SeedCost = seedItem.Price;
		}

		// Starter seeds allotment for Day 1 so the player can immediately engage in farming
		if (Level1Seeds == 0 && Coins == 0 && CurrentDay == 1)
		{
			Level1Seeds = 3;
		}
		_lastObservedSeeds = Level1Seeds;

		SeedsChanged += OnSeedsChangedInventorySync;
		PhaseChanged += OnPhaseChangedShopStockReset;
	}

	private void OnPhaseChangedShopStockReset(bool isDay, int currentDay)
	{
		if (isDay)
		{
			DailyBatteryStock = 1;
		}
	}

	private void OnSeedsChangedInventorySync(int totalSeeds)
	{
		if (_lastObservedSeeds >= 0 && totalSeeds > _lastObservedSeeds)
		{
			int diff = totalSeeds - _lastObservedSeeds;
			for (int i = 0; i < diff; i++)
			{
				SpawnOrAddPurchasedItem(ItemType.Seed);
			}
		}
		_lastObservedSeeds = totalSeeds;
	}

	public void SpawnFreezerInCabin()
	{
		Node house = GetTree()?.Root.FindChild("House", true, false)
			?? GetTree()?.CurrentScene?.FindChild("House", true, false);

		var existingFreezers = GetTree().GetNodesInGroup("Freezers");
		int count = existingFreezers.Count;

		// Offset positions along cabin wall or near cabin entrance
		float xOffset = -2.1f + ((count % 4) * 1.15f);
		float zOffset = count < 4 ? -1.8f : (count < 8 ? 1.8f : 0f);

		var freezerScene = GD.Load<PackedScene>("res://scenes/interaction/freezer.tscn");
		InteractableFreezer freezer = freezerScene != null
			? freezerScene.Instantiate<InteractableFreezer>()
			: new InteractableFreezer();

		freezer.Name = $"CryoFreezer_{count + 1}";

		if (house != null)
		{
			freezer.Position = new Vector3(xOffset, 0.0f, zOffset);
			house.AddChild(freezer);
		}
		else
		{
			var player = GetTree()?.GetFirstNodeInGroup("Player") as PlayerController;
			Vector3 dropPos = player != null ? player.GlobalPosition + new Vector3(1.0f, 0.2f, 1.0f) : new Vector3(0, 0, 0);
			(GetTree()?.CurrentScene ?? (Node)this).AddChild(freezer);
			freezer.GlobalPosition = dropPos;
		}

		AudioManager.Sfx("door_pound", 1f);
		var successHud = GetTree()?.Root.FindChild("HUD", true, false) as HUDManager;
		successHud?.SetInteractionPrompt($"[✓] Cryo-Freezer #{count + 1} delivered! Press [G] to pick up and relocate.");
	}

	public void SpawnOrAddPurchasedItem(ItemType type, int batteryHealth = 100)
	{
		var player = GetTree()?.GetFirstNodeInGroup("Player") as PlayerController;
		var inv = player?.GetNodeOrNull<CarriedInventory>("CarriedInventory");

		if (inv != null && inv.AddItem(type, 1, batteryHealth))
		{
			return;
		}

		// Inventory full: spawn physical 3D entity near player / shop
		Node sceneRoot = GetTree()?.CurrentScene ?? (Node)this;
		Vector3 dropPos = player != null ? player.GlobalPosition + new Vector3(0.6f, 0.3f, 0.6f) : new Vector3(0, 1.5f, 0);
		WorldItem.Spawn(sceneRoot, dropPos, type, 1, batteryHealth);
	}

	public bool TryConsumeLevel1Seed()
	{
		if (Level1Seeds <= 0) return false;
		Level1Seeds--;
		_lastObservedSeeds = Level1Seeds;
		EmitSignal(SignalName.SeedsChanged, Level1Seeds);
		return true;
	}

	public void AddSeeds(int amount)
	{
		Level1Seeds += amount;
		_lastObservedSeeds = Level1Seeds;
		EmitSignal(SignalName.SeedsChanged, Level1Seeds);
	}

	public bool TryUsePlank() => false;
	public void AddPlanks(int amount) { }

	public bool TryUseBarrier()
	{
		if (Barriers <= 0) return false;
		Barriers--;
		EmitSignal(SignalName.InventoryChanged);
		return true;
	}

	public void AddBarriers(int amount)
	{
		Barriers += amount;
		EmitSignal(SignalName.InventoryChanged);
	}

	public bool TryBuyItem(string id)
	{
		var item = ShopCatalog.Get(id);
		if (item == null) return false;

		if (id == ShopCatalog.BatteryId && DailyBatteryStock <= 0)
		{
			var hud = GetTree()?.Root.FindChild("HUD", true, false) as HUDManager;
			hud?.SetInteractionPrompt("[!] OUT OF STOCK! The Balloon Merchant only carries 1 Car Battery per day.");
			return false;
		}

		if (Coins < item.Price) return false;

		Coins -= item.Price;
		EmitSignal(SignalName.CoinsChanged, Coins);

		switch (id)
		{
			case ShopCatalog.SeedId:
				SpawnOrAddPurchasedItem(ItemType.Seed);
				break;
			case ShopCatalog.BatteryId:
				DailyBatteryStock = Mathf.Max(0, DailyBatteryStock - 1);
				SpawnOrAddPurchasedItem(ItemType.CarBattery, 100);
				break;
		}

		EmitSignal(SignalName.InventoryChanged);
		return true;
	}
}
