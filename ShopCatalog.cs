// =========================================================================================
// Original Author : air (Airless-KV) - Oct 31, 2026
// Responsibilities: - Item definitions and catalog for Flower Shop
//                   - Shop item queries, prices, descriptions, and item identifiers
// -----------------------------------------------------------------------------------------
// Change Log:
// - Oct 31, 2026 | air : Created ShopCatalog with Plank, Barrier, Axe, and Seed definitions
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
		{ PlankId, new ShopItem { Id = PlankId, DisplayName = "Wooden Plank", Price = 2, Description = "Fortify doors and windows against night breaches." } },
		{ BarrierId, new ShopItem { Id = BarrierId, DisplayName = "Garden Barrier", Price = 5, Description = "Sturdy perimeter barrier to slow down intruders." } },
		{ AxeId, new ShopItem { Id = AxeId, DisplayName = "Woodcutter Axe", Price = 10, Description = "Durable axe to harvest lumber and clear debris." } },
		{ SeedId, new ShopItem { Id = SeedId, DisplayName = "Level 1 Seed", Price = 1, Description = "Plantable seed to cultivate valuable flowers." } }
	};

	public static ShopItem Get(string id)
	{
		if (string.IsNullOrEmpty(id)) return null;
		_items.TryGetValue(id, out var item);
		return item;
	}
}
