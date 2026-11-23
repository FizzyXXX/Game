using UnityEngine;

/// <summary>Creates the miner's modular, prefab-ready visual rig.</summary>
public static class MinerVisualFactory
{
    public static GameObject CreateMiner()
    {
        Material denim = MaterialFor("Miner Denim", new Color(0.20f, 0.34f, 0.48f));
        Material dark = MaterialFor("Miner Leather", new Color(0.18f, 0.15f, 0.13f));
        Material skin = MaterialFor("Miner Skin", new Color(0.88f, 0.62f, 0.42f));
        Material gold = MaterialFor("Miner Brass", new Color(0.95f, 0.61f, 0.14f));
        Material lamp = MaterialFor("Miner Lamp", new Color(1f, 0.86f, 0.40f), true);
        Material wood = MaterialFor("Pickaxe Wood", new Color(0.38f, 0.22f, 0.12f));
        Material steel = MaterialFor("Pickaxe Steel", new Color(0.68f, 0.75f, 0.78f));

        var root = new GameObject("Miner");
        var rig = Child("Rig", root.transform);
        var body = Child("Body", rig);

        Part("Left Boot", PrimitiveType.Cube, body, new Vector3(-0.19f, 0.12f, 0.02f), new Vector3(0.22f, 0.24f, 0.38f), dark);
        Part("Right Boot", PrimitiveType.Cube, body, new Vector3(0.19f, 0.12f, 0.02f), new Vector3(0.22f, 0.24f, 0.38f), dark);
        Part("Left Leg", PrimitiveType.Capsule, body, new Vector3(-0.19f, 0.30f, 0), new Vector3(0.22f, 0.40f, 0.22f), denim);
        Part("Right Leg", PrimitiveType.Capsule, body, new Vector3(0.19f, 0.30f, 0), new Vector3(0.22f, 0.40f, 0.22f), denim);
        Part("Work Shirt", PrimitiveType.Capsule, body, new Vector3(0, 0.75f, 0), new Vector3(0.66f, 0.48f, 0.43f), denim);
        Part("Belt", PrimitiveType.Cube, body, new Vector3(0, 0.48f, 0), new Vector3(0.54f, 0.11f, 0.43f), dark);
        Part("Head", PrimitiveType.Sphere, body, new Vector3(0, 1.43f, 0.02f), new Vector3(0.48f, 0.48f, 0.45f), skin);

        var slots = root.AddComponent<MinerUpgradeSlots>();
        slots.helmetSlot = Slot("HelmetSlot", rig, new Vector3(0, 1.60f, 0.01f));
        Part("Starter Helmet", PrimitiveType.Sphere, slots.helmetSlot, new Vector3(0, 0.06f, -0.01f), new Vector3(0.63f, 0.32f, 0.58f), gold);
        Part("Helmet Brim", PrimitiveType.Cube, slots.helmetSlot, new Vector3(0, -0.05f, 0.20f), new Vector3(0.70f, 0.09f, 0.50f), gold);

        slots.helmetLightSlot = Slot("HelmetLightSlot", rig, new Vector3(0, 1.67f, 0.29f));
        Part("Starter Headlamp", PrimitiveType.Sphere, slots.helmetLightSlot, Vector3.zero, new Vector3(0.16f, 0.16f, 0.12f), lamp);

        var arm = Child("RightArm", rig);
        arm.localPosition = new Vector3(0.32f, 1.12f, 0.05f);
        Part("Sleeve", PrimitiveType.Capsule, arm, new Vector3(0.10f, -0.08f, 0), new Vector3(0.21f, 0.34f, 0.21f), denim);
        Part("Glove", PrimitiveType.Sphere, arm, new Vector3(0.12f, -0.35f, 0.03f), new Vector3(0.18f, 0.16f, 0.18f), dark);
        var swingPivot = Child("SwingPivot", arm);
        slots.pickaxeSlot = Slot("PickaxeSlot", swingPivot, new Vector3(0.12f, 0, 0));
        Part("Starter Handle", PrimitiveType.Cylinder, slots.pickaxeSlot, new Vector3(0, -0.37f, 0), new Vector3(0.065f, 0.46f, 0.065f), wood);
        Part("Starter Pickaxe Head", PrimitiveType.Cube, slots.pickaxeSlot, new Vector3(0, -0.77f, 0), new Vector3(0.52f, 0.15f, 0.17f), steel);

        slots.backpackSlot = Slot("BackpackSlot", rig, new Vector3(0, 0.86f, -0.28f));
        slots.bodyGearSlot = Slot("BodyGearSlot", rig, new Vector3(0, 0.75f, 0));
        return root;
    }

    private static Transform Slot(string name, Transform parent, Vector3 position)
    {
        Transform slot = Child(name, parent);
        slot.localPosition = position;
        return slot;
    }

    private static Transform Child(string name, Transform parent)
    {
        var child = new GameObject(name).transform;
        child.SetParent(parent, false);
        return child;
    }

    private static Transform Part(string name, PrimitiveType shape, Transform parent, Vector3 position, Vector3 scale, Material material)
    {
        GameObject part = GameObject.CreatePrimitive(shape);
        part.name = name;
        part.transform.SetParent(parent, false);
        part.transform.localPosition = position;
        part.transform.localScale = scale;
        part.GetComponent<Renderer>().sharedMaterial = material;
        Object.DestroyImmediate(part.GetComponent<Collider>());
        return part.transform;
    }

    private static Material MaterialFor(string name, Color color, bool glow = false)
    {
#if UNITY_EDITOR
        string path = "Assets/Resources/MinerMaterials/" + name.Replace(" ", "") + ".mat";
        Material existing = UnityEditor.AssetDatabase.LoadAssetAtPath<Material>(path);
        if (existing != null) return existing;
#endif
        Shader shader = Shader.Find("Universal Render Pipeline/Lit");
        if (shader == null) shader = Shader.Find("Standard");
        var material = new Material(shader) { name = name, color = color };
        if (glow && material.HasProperty("_EmissionColor"))
        {
            material.EnableKeyword("_EMISSION");
            material.SetColor("_EmissionColor", color * 1.8f);
        }
#if UNITY_EDITOR
        UnityEditor.AssetDatabase.CreateAsset(material, path);
#endif
        return material;
    }
}
