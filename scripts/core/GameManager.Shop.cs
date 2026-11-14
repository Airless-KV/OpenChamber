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
// =========================================================================================
using Godot;

public partial class GameManager
{
	[Signal]
	public delegate void InventoryChangedEventHandler();

	public int Planks { get; private set; } = 0;
	public int Barriers { get; private set; } = 0;
	public bool HasAxe { get; private set; } = false;

	private int _lastObservedSeeds = -1;

	public override void _EnterTree()
	{
		// Starter seeds allotment for Day 1 so the player can immediately engage in farming
		if (Level1Seeds == 0 && Coins == 0 && CurrentDay == 1)
		{
			Level1Seeds = 3;
		}
		_lastObservedSeeds = Level1Seeds;

		SeedsChanged += OnSeedsChangedInventorySync;
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

	public bool TryUsePlank()
	{
		if (Planks <= 0) return false;
		Planks--;
		EmitSignal(SignalName.InventoryChanged);
		return true;
	}

	public void AddPlanks(int amount)
	{
		Planks += amount;
		EmitSignal(SignalName.InventoryChanged);
	}

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
		if (Coins < item.Price) return false;

		Coins -= item.Price;
		EmitSignal(SignalName.CoinsChanged, Coins);

		switch (id)
		{
			case ShopCatalog.PlankId:
				Planks++;
				SpawnOrAddPurchasedItem(ItemType.Plank);
				break;
			case ShopCatalog.SeedId:
				SpawnOrAddPurchasedItem(ItemType.Seed);
				break;
			case ShopCatalog.BatteryId:
				SpawnOrAddPurchasedItem(ItemType.CarBattery, 100);
				break;
			case ShopCatalog.CrowbarId:
				SpawnOrAddPurchasedItem(ItemType.Crowbar);
				break;
		}

		EmitSignal(SignalName.InventoryChanged);
		return true;
	}
}
