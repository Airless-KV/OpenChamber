// =========================================================================================
// Original Author : air (Airless-KV) - Oct 31, 2026
// Responsibilities: - Interfacing inventory and shop purchases with Anj's HUDManager
//                   - Extended inventory tracking for Planks, Barriers, and Axe
// -----------------------------------------------------------------------------------------
// Change Log:
// - Oct 31, 2026 | air : Added InventoryChanged signal, inventory tracking, and TryBuyItem
// =========================================================================================
using Godot;

public partial class GameManager
{
	[Signal]
	public delegate void InventoryChangedEventHandler();

	// TEMPORARY TEST: ibalik sa 0 bago mag-push!
	public int Planks { get; private set; } = 5;
	public int Barriers { get; private set; } = 0;
	public bool HasAxe { get; private set; } = false;

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
