// =========================================================================================
// Original Author : anjivv (Anj Ferrer) - Oct 31, 2026
// Responsibilities: - Flower Shop 3D world interaction prompt (press [E] to open/close)
//                   - Interfaces player interaction with HUD ShopPanel
// -----------------------------------------------------------------------------------------
// Change Log:
// - Oct 31, 2026 | air : Applied standard header formatting and moved to scripts/interaction/
// =========================================================================================
using Godot;

// Flower Shop: pressing [E] opens / closes the shop menu on the HUD.
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
		return "Press [E] to open the Flower Shop";
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
