using UnityEngine;

/// <summary>Named attachment points for equipment that can be upgraded independently.</summary>
public sealed class MinerUpgradeSlots : MonoBehaviour
{
    public Transform helmetSlot;
    public Transform helmetLightSlot;
    public Transform pickaxeSlot;
    public Transform backpackSlot;
    public Transform bodyGearSlot;

    public void Equip(Transform slot, GameObject equipment)
    {
        if (slot == null || equipment == null) return;

        for (int i = slot.childCount - 1; i >= 0; i--)
        {
            GameObject oldItem = slot.GetChild(i).gameObject;
            if (Application.isPlaying) Destroy(oldItem);
            else DestroyImmediate(oldItem);
        }

        GameObject item = Instantiate(equipment, slot);
        item.transform.localPosition = Vector3.zero;
        item.transform.localRotation = Quaternion.identity;
        item.transform.localScale = Vector3.one;
    }
}
