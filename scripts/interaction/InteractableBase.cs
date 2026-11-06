// =========================================================================================
// Original Author : air (Airless-KV) - Oct 2026
// Responsibilities: - Interface contract and base class for all 3D physics body interactables
//                   - Standardized primary [E] and secondary [F] interaction dispatch
// -----------------------------------------------------------------------------------------
// Change Log:
// - Oct 31, 2026 | air : Merged IInteractable interface into InteractableBase.cs to reduce fragmentation
// - Nov 06, 2026 | air : Added InteractSecondary [F] hook for secondary actions like barricading
// =========================================================================================
using Godot;

public interface IInteractable
{
	string GetPrompt();
	void Interact(PlayerController player);
	void InteractSecondary(PlayerController player);
	bool CanInteract(PlayerController player);
}

public abstract partial class InteractableBase : StaticBody3D, IInteractable
{
	[Signal]
	public delegate void InteractedEventHandler(PlayerController player);

	[Signal]
	public delegate void InteractedSecondaryEventHandler(PlayerController player);

	[Export] public string PromptText = "Interact";
	[Export] public bool IsInteractable = true;

	public virtual string GetPrompt()
	{
		return PromptText;
	}

	public virtual bool CanInteract(PlayerController player)
	{
		return IsInteractable;
	}

	public virtual void Interact(PlayerController player)
	{
		if (!CanInteract(player))
			return;

		EmitSignal(SignalName.Interacted, player);
		OnInteract(player);
	}

	public virtual void InteractSecondary(PlayerController player)
	{
		if (!CanInteract(player))
			return;

		EmitSignal(SignalName.InteractedSecondary, player);
		OnInteractSecondary(player);
	}

	protected abstract void OnInteract(PlayerController player);
	protected virtual void OnInteractSecondary(PlayerController player) { }
}
