using Godot;

public interface IInteractable
{
	string GetPrompt();
	void Interact(PlayerController player);
	bool CanInteract(PlayerController player);
}
