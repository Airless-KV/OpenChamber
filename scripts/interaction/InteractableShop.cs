// =========================================================================================
// Original Author : anjivv (Anj Ferrer) - Oct 31, 2026
// Responsibilities: - Flower Shop 3D world interaction prompt (press [E] to open/close)
//                   - Interfaces player interaction with HUD ShopPanel
// -----------------------------------------------------------------------------------------
// Change Log:
// - Oct 31, 2026 | anjivv : Implemented 3D Flower Shop interaction stand and HUD ShopPanel toggle
// - Oct 31, 2026 | air : Applied standard header formatting and moved to scripts/interaction/
// - Nov 06, 2026 | air : Added dynamic open/close prompt state
// - Nov 10, 2026 | air : Updated prompt to Left Click to open/close
// =========================================================================================
using Godot;

// Flower Shop: Left Click (or [E]) opens / closes the shop menu on the HUD.
// Seeds, planks, barriers and the axe are all bought from the menu buttons.
public partial class InteractableShop : InteractableBase
{
	[Export] public GameManager GameManagerNode;
	[Export] public HUDManager HUDNode;

	public override void _Ready()
	{
		base._Ready();

		if (GameManagerNode == null)
			GameManagerNode = GetTree().Root.FindChild("GameManager", true, false) as GameManager;

		if (HUDNode == null)
			HUDNode = GetTree().Root.FindChild("HUD", true, false) as HUDManager;
	}

	public override string GetPrompt()
	{
		if (HUDNode != null && HUDNode.IsShopOpen)
			return "Left Click to close Flower Shop";
		return "Left Click to open Flower Shop";
	}

	protected override void OnInteract(PlayerController player)
	{
		if (HUDNode == null)
			HUDNode = GetTree().Root.FindChild("HUD", true, false) as HUDManager;

		if (HUDNode == null)
		{
			GD.PushError("InteractableShop: could not find the HUD node.");
			return;
		}

		HUDNode.ToggleShop();
	}
}
