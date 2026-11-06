// =========================================================================================
// Original Author : air (Airless-KV) - Nov 06, 2026
// Responsibilities: - Fallen perimeter timber / lumber harvesting station
//                   - Woodcutter Axe gating check via GameManager.HasAxe
//                   - Awards Wooden Planks (+2) and Coins (+1) with chopping SFX
//                   - Phase reset management across day cycles
// -----------------------------------------------------------------------------------------
// Change Log:
// - Nov 06, 2026 | air : Created InteractableLumber with axe gating and plank yield
// =========================================================================================
using Godot;

public partial class InteractableLumber : InteractableBase
{
	[ExportGroup("Lumber Settings")]
	[Export] public int PlanksAwarded = 2;
	[Export] public int CoinsAwarded = 1;
	[Export] public int ChopsPerDay = 1;

	public int RemainingChops { get; private set; }

	public override void _Ready()
	{
		base._Ready();
		RemainingChops = ChopsPerDay;

		var gm = GetTree().Root.FindChild("GameManager", true, false) as GameManager;
		if (gm != null)
		{
			gm.PhaseChanged += (isDay, _) =>
			{
				if (isDay) RemainingChops = ChopsPerDay;
			};
		}
	}

	public override string GetPrompt()
	{
		var gm = GetTree().Root.FindChild("GameManager", true, false) as GameManager;
		bool hasAxe = gm?.HasAxe ?? false;

		if (!hasAxe)
			return "[Locked] Fallen Timber (Requires Woodcutter Axe from Flower Shop [E])";

		if (RemainingChops <= 0)
			return "[Depleted] Fallen Timber (Harvested for today)";

		return $"Press [E] to Chop Timber (+{PlanksAwarded} Planks, +{CoinsAwarded} Coin)";
	}

	public override bool CanInteract(PlayerController player) => true;

	protected override void OnInteract(PlayerController player)
	{
		var gm = GetTree().Root.FindChild("GameManager", true, false) as GameManager;
		var hud = GetTree().Root.FindChild("HUD", true, false) as HUDManager;

		if (gm == null) return;

		if (!gm.HasAxe)
		{
			hud?.SetInteractionPrompt("[!] Need a Woodcutter Axe from the Flower Shop [E] to chop lumber!");
			return;
		}

		if (RemainingChops <= 0)
		{
			hud?.SetInteractionPrompt("[!] This timber has already been cleared today! Check back tomorrow.");
			return;
		}

		RemainingChops--;
		gm.AddPlanks(PlanksAwarded);
		gm.AddCoins(CoinsAwarded);

		AudioManager.Sfx("door_thud", -2f, 0.2f);
		hud?.SetInteractionPrompt($"[✓] Chopped timber! (+{PlanksAwarded} Planks, +{CoinsAwarded} Coin) - Planks: {gm.Planks}");

		Tween tw = CreateTween();
		tw.TweenProperty(this, "scale", Scale * 0.95f, 0.08f);
		tw.TweenProperty(this, "scale", Scale, 0.12f);
	}
}
