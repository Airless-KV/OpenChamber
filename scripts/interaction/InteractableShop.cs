using Godot;

public partial class InteractableShop : InteractableBase
{
	[Export] public GameManager GameManagerNode;
	[Export] public int SeedCost = 1;

	public override void _Ready()
	{
		base._Ready();
		if (GameManagerNode == null)
		{
			GameManagerNode = GetTree().Root.FindChild("GameManager", true, false) as GameManager;
		}
	}

	public override string GetPrompt()
	{
		return $"Press [E] to Buy Level 1 Seed ({SeedCost} Coin)";
	}

	protected override void OnInteract(PlayerController player)
	{
		if (GameManagerNode == null)
		{
			GameManagerNode = GetTree().Root.FindChild("GameManager", true, false) as GameManager;
		}

		if (GameManagerNode != null)
		{
			bool bought = GameManagerNode.TryBuyLevel1Seed();
			if (bought)
			{
				GD.Print("Successfully purchased 1 Level 1 Seed at the Flower Shop!");
			}
			else
			{
				GD.Print("Not enough coins to purchase a seed at the Flower Shop!");
			}
		}
	}
}
