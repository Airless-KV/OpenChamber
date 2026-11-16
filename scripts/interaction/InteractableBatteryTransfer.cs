// =========================================================================================
// Original Author : air (Airless-KV) - Nov 15, 2026
// Responsibilities: - Dual-dock Battery Charge Transfer Station
//                   - Real-time automatic charge transfer between two docked batteries (1% per 2s)
//                   - Aim-aware dock targeting for placing/retrieving into Slot A or Slot B independently
//                   - Real-time LED bar levels, digital status displays, and electrical audio SFX
// -----------------------------------------------------------------------------------------
// Change Log:
// - Nov 15, 2026 | air : Created InteractableBatteryTransfer for combining partial batteries
// - Nov 15, 2026 | air : Aligned StatusLabel style and non-billboard behavior with Seed Workbench
// - Nov 16, 2026 | air : Implemented aim-aware dock placement and automatic transfer at 1% per 2s
// =========================================================================================
using Godot;
using System;

public partial class InteractableBatteryTransfer : InteractableBase
{
	[ExportGroup("Slot A (Donor / Source)")]
	[Export] public bool HasBatteryA { get; set; } = false;
	[Export] public int BatteryAHealth { get; set; } = 0;

	[ExportGroup("Slot B (Recipient / Target)")]
	[Export] public bool HasBatteryB { get; set; } = false;
	[Export] public int BatteryBHealth { get; set; } = 0;

	[ExportGroup("Transfer Settings")]
	[Export] public float TransferInterval { get; set; } = 2.0f; // 1% per 2 seconds

	[ExportGroup("Visual References")]
	[Export] public Node3D BatteryAVisual;
	[Export] public Node3D BatteryBVisual;
	[Export] public MeshInstance3D LedA;
	[Export] public MeshInstance3D LedB;
	[Export] public Label3D StatusLabel;
	[Export] public OmniLight3D IndicatorLight;

	private StandardMaterial3D _ledMatA;
	private StandardMaterial3D _ledMatB;
	private HUDManager _hud;
	private float _transferTimer = 0f;
	private bool _isTransferring = false;

	public override void _Ready()
	{
		base._Ready();
		AddToGroup("BatteryTransferStations");

		_hud ??= GetTree().Root.FindChild("HUD", true, false) as HUDManager;

		BatteryAVisual ??= GetNodeOrNull<Node3D>("BatteryAVisual");
		BatteryBVisual ??= GetNodeOrNull<Node3D>("BatteryBVisual");
		LedA ??= GetNodeOrNull<MeshInstance3D>("LedA");
		LedB ??= GetNodeOrNull<MeshInstance3D>("LedB");
		StatusLabel ??= GetNodeOrNull<Label3D>("StatusLabel");
		IndicatorLight ??= GetNodeOrNull<OmniLight3D>("IndicatorLight");

		if (LedA != null)
		{
			_ledMatA = (LedA.MaterialOverride as StandardMaterial3D)?.Duplicate() as StandardMaterial3D
				?? new StandardMaterial3D { EmissionEnabled = true };
			LedA.MaterialOverride = _ledMatA;
		}

		if (LedB != null)
		{
			_ledMatB = (LedB.MaterialOverride as StandardMaterial3D)?.Duplicate() as StandardMaterial3D
				?? new StandardMaterial3D { EmissionEnabled = true };
			LedB.MaterialOverride = _ledMatB;
		}

		UpdateVisuals();
	}

	public int GetTargetSlot()
	{
		Camera3D cam = GetViewport()?.GetCamera3D();
		if (cam == null) return !HasBatteryA ? 1 : (!HasBatteryB ? 2 : 1);

		Vector3 localCamPos = ToLocal(cam.GlobalPosition);
		Vector3 localCamDir = GlobalTransform.Basis.Inverse() * (-cam.GlobalTransform.Basis.Z.Normalized());

		// Raycast against the bench top plane Y = 0.88f
		if (Mathf.Abs(localCamDir.Y) > 0.001f)
		{
			float t = (0.88f - localCamPos.Y) / localCamDir.Y;
			if (t > 0)
			{
				Vector3 localHit = localCamPos + localCamDir * t;
				// Dock A is at X = -0.32, Dock B is at X = 0.32
				float distA = localHit.DistanceSquaredTo(new Vector3(-0.32f, 0.88f, 0f));
				float distB = localHit.DistanceSquaredTo(new Vector3(0.32f, 0.88f, 0f));
				return distA <= distB ? 1 : 2;
			}
		}

		return localCamPos.X < 0 ? 1 : 2;
	}

	public bool CanTransfer(out int donorSlot, out int recipientSlot)
	{
		donorSlot = 0;
		recipientSlot = 0;

		if (!HasBatteryA || !HasBatteryB) return false;

		// If A has charge and B has capacity: A -> B
		if (BatteryAHealth > 0 && BatteryBHealth < 100)
		{
			donorSlot = 1;
			recipientSlot = 2;
			return true;
		}

		// If B has charge and A has capacity: B -> A
		if (BatteryBHealth > 0 && BatteryAHealth < 100)
		{
			donorSlot = 2;
			recipientSlot = 1;
			return true;
		}

		return false;
	}

	public override void _Process(double delta)
	{
		base._Process(delta);

		if (CanTransfer(out int donor, out int recipient))
		{
			_isTransferring = true;
			_transferTimer += (float)delta;

			if (_transferTimer >= TransferInterval)
			{
				_transferTimer -= TransferInterval;

				if (donor == 1)
				{
					BatteryAHealth = Mathf.Max(0, BatteryAHealth - 1);
					BatteryBHealth = Mathf.Min(100, BatteryBHealth + 1);
				}
				else
				{
					BatteryBHealth = Mathf.Max(0, BatteryBHealth - 1);
					BatteryAHealth = Mathf.Min(100, BatteryAHealth + 1);
				}

				PlayTransferPulse();
				UpdateVisuals();

				if (!CanTransfer(out _, out _))
				{
					_isTransferring = false;
					_transferTimer = 0f;
					PlayTransferComplete();
				}
			}
		}
		else
		{
			if (_isTransferring)
			{
				_isTransferring = false;
				_transferTimer = 0f;
				UpdateVisuals();
			}
		}
	}

	public override string GetPrompt()
	{
		var player = GetTree()?.GetFirstNodeInGroup("Player") as PlayerController;
		var inv = player?.GetNodeOrNull<CarriedInventory>("CarriedInventory");

		bool holdingBattery = inv != null && inv.HasActiveItem(ItemType.CarBattery);
		int targetSlot = GetTargetSlot();
		string slotName = targetSlot == 1 ? "Slot A" : "Slot B";
		bool slotOccupied = targetSlot == 1 ? HasBatteryA : HasBatteryB;
		int slotHealth = targetSlot == 1 ? BatteryAHealth : BatteryBHealth;

		// Active automatic transfer notification
		if (CanTransfer(out int donor, out int recipient))
		{
			string dir = donor == 1 ? $"A ({BatteryAHealth}%) -> B ({BatteryBHealth}%)" : $"B ({BatteryBHealth}%) -> A ({BatteryAHealth}%)";
			if (slotOccupied)
				return $"[⚡] Transferring Charge (1% / 2s)... [{dir}] | [LMB] Retrieve {slotName}";
			return $"[⚡] Transferring Charge (1% / 2s)... [{dir}]";
		}

		// Aiming at an occupied slot: retrieve it
		if (slotOccupied)
		{
			return $"[LMB] Retrieve Battery from {slotName} ({slotHealth}% HP)";
		}

		// Aiming at an empty slot while holding a battery: insert it
		if (holdingBattery)
		{
			int heldHp = inv.Slots[inv.ActiveSlot].BatteryHealth;
			return $"[LMB] Insert Car Battery ({heldHp}% HP) into {slotName}";
		}

		return $"[!] Battery Transfer Machine - Aim at {slotName} to Insert Car Battery";
	}

	protected override void OnInteract(PlayerController player)
	{
		var inv = player?.GetNodeOrNull<CarriedInventory>("CarriedInventory");
		if (inv == null) return;

		bool holdingBattery = inv.HasActiveItem(ItemType.CarBattery);
		int targetSlot = GetTargetSlot();

		// Case 1: Aiming at empty slot while holding battery -> Insert!
		if (targetSlot == 1 && !HasBatteryA && holdingBattery)
		{
			int heldHp = inv.Slots[inv.ActiveSlot].BatteryHealth;
			inv.ConsumeActiveItem();
			HasBatteryA = true;
			BatteryAHealth = heldHp;
			_transferTimer = 0f;
			AudioManager.Sfx("door_thud", 2f);
			_hud?.SetInteractionPrompt($"[✓] Inserted Battery into Slot A ({BatteryAHealth}% HP)");
			UpdateVisuals();
			return;
		}

		if (targetSlot == 2 && !HasBatteryB && holdingBattery)
		{
			int heldHp = inv.Slots[inv.ActiveSlot].BatteryHealth;
			inv.ConsumeActiveItem();
			HasBatteryB = true;
			BatteryBHealth = heldHp;
			_transferTimer = 0f;
			AudioManager.Sfx("door_thud", 2f);
			_hud?.SetInteractionPrompt($"[✓] Inserted Battery into Slot B ({BatteryBHealth}% HP)");
			UpdateVisuals();
			return;
		}

		// Case 2: Aiming at an occupied slot -> Retrieve/Eject that slot!
		if (targetSlot == 1 && HasBatteryA)
		{
			EjectBattery(inv, true);
			return;
		}

		if (targetSlot == 2 && HasBatteryB)
		{
			EjectBattery(inv, false);
			return;
		}

		// Fallback: If aiming at empty slot without battery, but the other slot is occupied: retrieve that one
		if (HasBatteryA && !HasBatteryB)
		{
			EjectBattery(inv, true);
		}
		else if (HasBatteryB && !HasBatteryA)
		{
			EjectBattery(inv, false);
		}
	}

	protected override void OnInteractSecondary(PlayerController player)
	{
		OnInteract(player);
	}

	private void PlayTransferPulse()
	{
		AudioManager.Sfx("coin_collect", 4f, 0.05f);

		if (IndicatorLight != null)
		{
			IndicatorLight.LightColor = new Color(0.2f, 0.9f, 1.0f);
			IndicatorLight.LightEnergy = 1.8f;

			var tween = CreateTween();
			tween.TweenProperty(IndicatorLight, "light_energy", 0.4f, 0.3f);
		}
	}

	private void PlayTransferComplete()
	{
		AudioManager.Sfx("coin_collect", 2f);
		AudioManager.Sfx("door_thud", -1f);

		if (IndicatorLight != null)
		{
			IndicatorLight.LightColor = new Color(0.2f, 1.0f, 0.3f);
			IndicatorLight.LightEnergy = 3.0f;

			var tween = CreateTween();
			tween.TweenProperty(IndicatorLight, "light_energy", 0.4f, 0.6f);
		}

		_hud?.SetInteractionPrompt($"[✓] Battery Charge Transfer Complete! (A: {BatteryAHealth}% | B: {BatteryBHealth}%)");
	}

	private void EjectBattery(CarriedInventory inv, bool isSlotA)
	{
		int hp = isSlotA ? BatteryAHealth : BatteryBHealth;
		if (isSlotA)
		{
			HasBatteryA = false;
			BatteryAHealth = 0;
		}
		else
		{
			HasBatteryB = false;
			BatteryBHealth = 0;
		}

		_transferTimer = 0f;

		if (inv.AddItem(ItemType.CarBattery, 1, hp))
		{
			AudioManager.Sfx("door_thud", 1f);
			_hud?.SetInteractionPrompt($"[✓] Retrieved Car Battery ({hp}% HP) into backpack.");
		}
		else
		{
			Vector3 spawnPos = GlobalPosition + GlobalTransform.Basis.Z * 0.8f + new Vector3(0, 0.2f, 0);
			WorldItem.Spawn(GetTree().CurrentScene, spawnPos, ItemType.CarBattery, 1, hp);
			AudioManager.Sfx("door_thud", 1f);
			_hud?.SetInteractionPrompt($"[✓] Inventory full! Dropped Car Battery ({hp}% HP) on floor.");
		}

		UpdateVisuals();
	}

	private void UpdateVisuals()
	{
		if (BatteryAVisual != null) BatteryAVisual.Visible = HasBatteryA;
		if (BatteryBVisual != null) BatteryBVisual.Visible = HasBatteryB;

		if (_ledMatA != null)
		{
			if (!HasBatteryA)
			{
				_ledMatA.AlbedoColor = new Color(0.15f, 0.15f, 0.15f);
				_ledMatA.Emission = new Color(0.05f, 0.05f, 0.05f);
			}
			else if (BatteryAHealth > 50)
			{
				_ledMatA.AlbedoColor = new Color(0.1f, 0.9f, 0.2f);
				_ledMatA.Emission = new Color(0.1f, 0.9f, 0.2f);
			}
			else if (BatteryAHealth > 0)
			{
				_ledMatA.AlbedoColor = new Color(0.95f, 0.7f, 0.1f);
				_ledMatA.Emission = new Color(0.95f, 0.7f, 0.1f);
			}
			else
			{
				_ledMatA.AlbedoColor = new Color(0.9f, 0.1f, 0.1f);
				_ledMatA.Emission = new Color(0.9f, 0.1f, 0.1f);
			}
		}

		if (_ledMatB != null)
		{
			if (!HasBatteryB)
			{
				_ledMatB.AlbedoColor = new Color(0.15f, 0.15f, 0.15f);
				_ledMatB.Emission = new Color(0.05f, 0.05f, 0.05f);
			}
			else if (BatteryBHealth > 50)
			{
				_ledMatB.AlbedoColor = new Color(0.1f, 0.9f, 0.2f);
				_ledMatB.Emission = new Color(0.1f, 0.9f, 0.2f);
			}
			else if (BatteryBHealth > 0)
			{
				_ledMatB.AlbedoColor = new Color(0.95f, 0.7f, 0.1f);
				_ledMatB.Emission = new Color(0.95f, 0.7f, 0.1f);
			}
			else
			{
				_ledMatB.AlbedoColor = new Color(0.9f, 0.1f, 0.1f);
				_ledMatB.Emission = new Color(0.9f, 0.1f, 0.1f);
			}
		}

		if (StatusLabel != null)
		{
			string strA = HasBatteryA ? $"{BatteryAHealth}%" : "EMPTY";
			string strB = HasBatteryB ? $"{BatteryBHealth}%" : "EMPTY";

			if (CanTransfer(out int donor, out _))
			{
				string arrow = donor == 1 ? "-->" : "<--";
				StatusLabel.Text = $"⚡ TRANSFERRING (1%/2s)\nA: {strA} {arrow} B: {strB}";
				StatusLabel.Modulate = new Color(0.3f, 0.9f, 1.0f);
			}
			else if (HasBatteryA && HasBatteryB)
			{
				StatusLabel.Text = $"Battery Transfer Machine\nA: {strA} | B: {strB}\n[Transfer Complete]";
				StatusLabel.Modulate = new Color(0.4f, 1.0f, 0.4f);
			}
			else
			{
				StatusLabel.Text = $"Battery Transfer Machine\nA: {strA} | B: {strB}";
				StatusLabel.Modulate = new Color(0.9f, 0.9f, 0.9f);
			}
		}
	}
}
