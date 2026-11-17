// =========================================================================================
// Original Author : air (Airless-KV) - Nov 15, 2026
// Responsibilities: - Seed Workbench station for breaking down harvested flowers into seeds
//                   - Converts 1 held flower into 2 seeds matching the flower's tier level
//                   - Places seeds into free inventory slots or spawns world items if full
//                   - 3D table visuals, dynamic prompt, and crafting audio feedback
// -----------------------------------------------------------------------------------------
// Change Log:
// - Nov 15, 2026 | air : Created InteractableWorkbench (1 Flower -> 2 Seeds conversion)
// - Nov 15, 2026 | air : Scaled seed outputs to match harvested flower tier (Lvl 1-5 Seeds)
// - Nov 17, 2026 | air : Streamlined prompt format to '[E / LMB]' convention
// - Nov 17, 2026 | air : Added WorkbenchCrafted event for interactive tutorial verification
// =========================================================================================
using Godot;
using System;

public partial class InteractableWorkbench : InteractableBase
{
	public static event Action WorkbenchCrafted;

	[Export] public Label3D StatusLabel;
	[Export] public Node3D SpawnPoint;

	public override void _Ready()
	{
		base._Ready();
		AddToGroup("Workbenches");

		StatusLabel ??= GetNodeOrNull<Label3D>("StatusLabel");
		SpawnPoint ??= GetNodeOrNull<Node3D>("SpawnPoint");

		UpdateStatusLabel(false);
	}

	public override string GetPrompt()
	{
		var player = GetTree()?.Root.FindChild("Player", true, false) as PlayerController;
		var inv = player?.GetNodeOrNull<CarriedInventory>("CarriedInventory");

		if (inv != null)
		{
			if (inv.HasActiveItem(ItemType.Flower))
			{
				int tier = Mathf.Clamp(inv.Slots[inv.ActiveSlot].FlowerTier, 1, 5);
				string flowerName = WorldItem.FlowerNames[tier - 1];
				return $"[E / LMB] Extract Level {tier} Seeds from {flowerName} (+2 Seeds)";
			}
			else if (inv.HasItem(ItemType.Flower))
			{
				return "Select Flower in hand to extract seeds [Scroll / 1-3]";
			}
		}

		return "[!] Seed Workbench [Hold 1 Flower to extract 2 Seeds]";
	}

	public override bool CanInteract(PlayerController player)
	{
		var inv = player?.GetNodeOrNull<CarriedInventory>("CarriedInventory");
		if (inv == null) return false;
		return inv.HasItem(ItemType.Flower);
	}

	protected override void OnInteract(PlayerController player)
	{
		var inv = player?.GetNodeOrNull<CarriedInventory>("CarriedInventory");
		if (inv == null) return;

		// 1. Check if holding flower in active slot; otherwise search other slots
		int flowerSlot = -1;
		if (inv.HasActiveItem(ItemType.Flower))
		{
			flowerSlot = inv.ActiveSlot;
		}
		else
		{
			for (int i = 0; i < CarriedInventory.MaxSlots; i++)
			{
				if (inv.Slots[i].IsOccupied && inv.Slots[i].Type == ItemType.Flower)
				{
					flowerSlot = i;
					break;
				}
			}
		}

		if (flowerSlot < 0)
		{
			AudioManager.Sfx("warning_stinger", -4f);
			return;
		}

		int flowerTier = Mathf.Clamp(inv.Slots[flowerSlot].FlowerTier, 1, 5);

		// 2. Consume the flower
		inv.Slots[flowerSlot] = default;
		// Trigger inventory update signal/viewmodel
		inv.SelectSlot(inv.ActiveSlot);

		// 3. Grant 2 seeds matching the flower's tier level
		Vector3 spawnOrigin = SpawnPoint != null ? SpawnPoint.GlobalPosition : GlobalPosition + new Vector3(0, 0.95f, 0);
		Node worldRoot = GetTree()?.CurrentScene ?? GetParent();
		int seedsAwarded = 2;

		for (int s = 0; s < seedsAwarded; s++)
		{
			bool addedToInv = inv.AddItem(ItemType.Seed, flowerTier);
			if (!addedToInv)
			{
				// Spawn as WorldItem drop on/near table
				Vector3 dropPos = spawnOrigin + new Vector3((s - 0.5f) * 0.25f, 0.05f, 0f);
				WorldItem.Spawn(worldRoot, dropPos, ItemType.Seed, flowerTier);
			}
		}

		// 4. Feedback effects
		AudioManager.Sfx("flower_merge", 0f);
		AudioManager.Sfx("foliage_rustle", 2f);

		UpdateStatusLabel(true, flowerTier);
		WorkbenchCrafted?.Invoke();
	}

	private void UpdateStatusLabel(bool craftedRecently, int seedTier = 1)
	{
		if (StatusLabel == null) return;

		if (craftedRecently)
		{
			StatusLabel.Text = $"Extracted 2 Level {seedTier} Seeds!\n[Workbench]";
			StatusLabel.Modulate = new Color(0.4f, 1.0f, 0.5f);

			GetTree()?.CreateTimer(1.5f).Connect("timeout", Callable.From(() =>
			{
				if (IsInstanceValid(StatusLabel))
				{
					StatusLabel.Text = "Seed Workbench\n1 Flower -> 2 Seeds";
					StatusLabel.Modulate = new Color(0.9f, 0.9f, 0.9f);
				}
			}));
		}
		else
		{
			StatusLabel.Text = "Seed Workbench\n1 Flower -> 2 Seeds";
			StatusLabel.Modulate = new Color(0.9f, 0.9f, 0.9f);
		}
	}
}
