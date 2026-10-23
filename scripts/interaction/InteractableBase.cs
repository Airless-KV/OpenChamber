using Godot;

public abstract partial class InteractableBase : StaticBody3D, IInteractable
{
	[Signal]
	public delegate void InteractedEventHandler(PlayerController player);

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

	protected abstract void OnInteract(PlayerController player);
}
