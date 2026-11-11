// =========================================================================================
// Original Author : rbn (Robin Velasco) - Nov 06, 2026
// Responsibilities: - Plank barricade inventory refund handling
// -----------------------------------------------------------------------------------------
// Change Log:
// - Nov 06, 2026 | rbn : Implemented barricade plank refund handling
// - Nov 06, 2026 | air : Consolidated TryUsePlank with GameManager.Shop.cs
// =========================================================================================
using Godot;

public partial class GameManager
{
	// Ibinabalik ang mga plank na tinanggal mula sa pinto o bintana.
	public void RefundPlanks(int amount)
	{
		if (amount <= 0)
			return;

		AddPlanks(amount);
	}
}
