using Godot;

public partial class GameManager
{
	public bool TryUsePlank()
	{
		if (Planks <= 0)
			return false;

		Planks--;
		EmitSignal(SignalName.InventoryChanged);
		return true;
	}

	// Ibinabalik ang mga plank na tinanggal mula sa pinto o bintana.
	public void RefundPlanks(int amount)
	{
		if (amount <= 0)
			return;

		Planks += amount;
		EmitSignal(SignalName.InventoryChanged);
	}
}
