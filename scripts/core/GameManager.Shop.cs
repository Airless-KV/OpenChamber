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
// =========================================================================================
using Godot;

public partial class GameManager
{
	[Signal]
	public delegate void InventoryChangedEventHandler();

	public int Planks { get; private set; } = 999;
	public int Barriers { get; private set; } = 0;
	public bool HasAxe { get; private set; } = false;

	public override void _EnterTree()
	{
		// Starter seeds allotment for Day 1 so the player can immediately engage in farming
		if (Level1Seeds == 0 && Coins == 0 && CurrentDay == 1)
		{
			Level1Seeds = 3;
		}
		Planks = 999;
	}

	public bool TryConsumeLevel1Seed()
	{
		if (Level1Seeds <= 0) return false;
		Level1Seeds--;
		EmitSignal(SignalName.SeedsChanged, Level1Seeds);
		return true;
	}

	public void AddSeeds(int amount)
	{
		Level1Seeds += amount;
		EmitSignal(SignalName.SeedsChanged, Level1Seeds);
	}

	public bool TryUsePlank()
	{
		// Infinite planks mode for testing
		Planks = 999;
		EmitSignal(SignalName.InventoryChanged);
		return true;
	}

	public void AddPlanks(int amount)
	{
		Planks = 999;
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
		if (id == ShopCatalog.AxeId && HasAxe) return false;

		Coins -= item.Price;
		EmitSignal(SignalName.CoinsChanged, Coins);

		switch (id)
		{
			case ShopCatalog.PlankId:
				Planks++;
				break;
			case ShopCatalog.BarrierId:
				Barriers++;
				break;
			case ShopCatalog.AxeId:
				HasAxe = true;
				break;
		}

		EmitSignal(SignalName.InventoryChanged);
		return true;
	}
}
