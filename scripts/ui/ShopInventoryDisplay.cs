// =========================================================================================
// Original Author : air (Airless-KV) - Nov 11, 2026
// Responsibilities: - Displays player plank inventory in the Flower Shop panel
//                   - Listens to GameManager.InventoryChanged without modifying HUDManager
// -----------------------------------------------------------------------------------------
// Change Log:
// - Nov 11, 2026 | air : Created ShopInventoryDisplay for streamlined plank inventory HUD
// =========================================================================================
using Godot;

public partial class ShopInventoryDisplay : Label
{
	public override void _Ready()
	{
		var gm = GetTree().Root.FindChild("GameManager", true, false) as GameManager;
		if (gm != null)
		{
			gm.InventoryChanged += UpdateText;
			UpdateText();
		}
	}

	private void UpdateText()
	{
		var gm = GetTree().Root.FindChild("GameManager", true, false) as GameManager;
		if (gm != null)
		{
			Text = $"Wooden Planks: {gm.Planks}";
		}
	}
}
