using UnityEngine;

// Oyuncunun tek elle taşıdığı, LootItem olmayan dış eşya (ör. ofis-garajdaki CompanySim.CarryableItem; köprü: CompanyPlayerBridge).
// ThirdPersonPlayer.HeldProp'ta durur: PlayerCombat sağ tıkla fırlatır (LootItem ile aynı balistik yol), çarpılma / yeniden doğma
// elden düşürür, CrewAnimation tek el taşıma pozunu oynatır, PlayerInteraction "elde 1 şey" kuralını uygular.
public interface IHeldProp
{
    // Eşyanın dünya merkezi (fırlatma başlangıcı, nişan)
    Vector3 Center { get; }
    // Elden fırlat: eşya verilen hızla uçar (çağıran HeldProp'u önce boşaltır)
    void ThrowFrom(ThirdPersonPlayer by, Vector3 velocity);
    // Elden düş (çarpılma, can 0, yeniden doğma): eşya oyuncunun yakınına bırakılır
    void DropFrom(ThirdPersonPlayer by);
}
