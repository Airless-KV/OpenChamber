// =========================================================================================
// Original Author : air (Airless-KV) - Oct 31, 2026
// Responsibilities: - Item definitions and catalog for Flower Shop
//                   - Shop item queries, prices, descriptions, and item identifiers
// -----------------------------------------------------------------------------------------
// Change Log:
// - Oct 31, 2026 | air : Created ShopCatalog with Plank, Barrier, Axe, and Seed definitions
// - Nov 10, 2026 | air : Updated plank description for bare cabin opening fortification
// - Nov 11, 2026 | air : Removed unneeded Barrier and Axe items from active shop catalog
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

	private static readonly Dictionary<string, ShopItem> _items = new()
	{
		{ PlankId, new ShopItem { Id = PlankId, DisplayName = "Wooden Plank", Price = 2, Description = "Fortify bare cabin openings against night breaches." } },
		{ SeedId, new ShopItem { Id = SeedId, DisplayName = "Level 1 Seed", Price = 1, Description = "Plantable seed to cultivate valuable flowers." } }
	};

	public static ShopItem Get(string id)
	{
		if (string.IsNullOrEmpty(id)) return null;
		_items.TryGetValue(id, out var item);
		return item;
	}
}
