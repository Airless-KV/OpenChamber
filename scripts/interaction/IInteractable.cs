// =========================================================================================
// Original Author : air (Airless-KV) - Oct 2026
// Responsibilities: - Interface contract for all in-world interactable objects
// =========================================================================================
using Godot;

public interface IInteractable
{
	string GetPrompt();
	void Interact(PlayerController player);
	bool CanInteract(PlayerController player);
}
