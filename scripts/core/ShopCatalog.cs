// =========================================================================================
// Original Author : air (Airless-KV) - Oct 31, 2026
// Responsibilities: - Item definitions and catalog for Flower Shop
//                   - Shop item queries, prices, descriptions, and item identifiers
// -----------------------------------------------------------------------------------------
// Change Log:
// - Oct 31, 2026 | air : Created ShopCatalog with Plank, Barrier, Axe, and Seed definitions
// - Nov 10, 2026 | air : Updated plank description for bare cabin opening fortification
// - Nov 11, 2026 | air : Removed unneeded Barrier and Axe items from active shop catalog
// - Nov 13, 2026 | air : Added Car Battery item definition to shop catalog
// - Nov 14, 2026 | air : Added Rusted Crowbar item definition to shop catalog
// - Nov 15, 2026 | air : Removed Plank and Crowbar item definitions following barricade decommission
// - Nov 15, 2026 | air : Added Cryo-Freezer item definition for flower preservation
// - Nov 15, 2026 | air : Increased seed price from 1 to 100 coins
// - Nov 15, 2026 | air : Preserved dummy entries for Plank, Barrier, and Axe to prevent HUD WireShopButton NullReferenceException
// - Nov 17, 2026 | air : Rebalanced Car Battery price to 15 coins for power economy anti-spam
// - Nov 17, 2026 | air : Decommissioned Cryo-Freezer from active purchase catalog
// - Nov 18, 2026 | air : Updated Car Battery description to reflect progressive daily stock scaling (Day 1-2: 1/day, Day 3-4: 2/day, Day 5+: 3/day)
// =========================================================================================
using System.Collections.Generic;

public class ShopItem
{
	public string Id { get; set; }
	public string DisplayName { get; set; }
	public int Price { get; set; }
	public string Description { get; set; }
}

public static class ShopCatalog
{
	public const string PlankId = "plank";
	public const string BarrierId = "barrier";
	public const string AxeId = "axe";
	public const string SeedId = "seed";
	public const string BatteryId = "battery";
	public const string FreezerId = "freezer";

	private static readonly Dictionary<string, ShopItem> _items = new()
	{
		{ PlankId, new ShopItem { Id = PlankId, DisplayName = "Plank", Price = 2, Description = "Barricade plank." } },
		{ BarrierId, new ShopItem { Id = BarrierId, DisplayName = "Barrier", Price = 5, Description = "Defense barrier." } },
		{ AxeId, new ShopItem { Id = AxeId, DisplayName = "Axe", Price = 10, Description = "Woodcutting axe." } },
		{ SeedId, new ShopItem { Id = SeedId, DisplayName = "Level 1 Seed", Price = 100, Description = "Plantable seed to cultivate valuable flowers." } },
		{ BatteryId, new ShopItem { Id = BatteryId, DisplayName = "Car Battery", Price = 15, Description = "Powers cabin halogen floodlights and sirens. Daily stock scales by day (Day 1-2: 1/day, Day 3-4: 2/day, Day 5+: 3/day)." } }
	};

	public static ShopItem Get(string id)
	{
		if (string.IsNullOrEmpty(id)) return null;
		_items.TryGetValue(id, out var item);
		return item;
	}
}
