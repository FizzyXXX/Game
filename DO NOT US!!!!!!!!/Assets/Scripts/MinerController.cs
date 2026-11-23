using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>Builds a small playable mining scene when the project starts.</summary>
public sealed class MinerController : MonoBehaviour
{
    private Transform miner;
    private Transform pickaxeArm;
    private Transform leftBoot;
    private Transform rightBoot;
    private Transform cameraTransform;
    private Camera mainCamera;
    private float swingTime;
    private float walkTime;
    private Vector3 swingDirection = Vector3.forward;

    private const float WalkSpeed = 5f;
    private const float SwingDuration = 0.42f;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void CreatePlayableMiner()
    {
        if (FindObjectOfType<MinerController>() != null) return;
        var root = new GameObject("Miner Game");
        root.AddComponent<MinerController>();
    }

    private void Start()
    {
        BuildScene();
    }

    private void BuildScene()
    {
        var grass = MakeMaterial("Mossy ground", new Color(0.20f, 0.30f, 0.22f));
        var stone = MakeMaterial("Warm slate", new Color(0.34f, 0.36f, 0.34f));
        var stoneLight = MakeMaterial("Stone highlights", new Color(0.47f, 0.46f, 0.39f));
        var floor = GameObject.CreatePrimitive(PrimitiveType.Plane);
        floor.name = "Mine floor";
        floor.transform.position = new Vector3(0, -0.06f, 0);
        floor.transform.localScale = new Vector3(12, 1, 12);
        floor.GetComponent<Renderer>().sharedMaterial = grass;

        // A few chunky ore rocks give the miner something to work around.
        Vector3[] rocks = {
            new Vector3(3.8f, 0, 2.4f), new Vector3(-4f, 0, 3.4f),
            new Vector3(4.5f, 0, -3.8f), new Vector3(-3.8f, 0, -3.5f),
            new Vector3(1.2f, 0, 5.6f), new Vector3(-1.8f, 0, -5.3f)
        };
        for (int i = 0; i < rocks.Length; i++)
        {
            var rock = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            rock.name = i % 2 == 0 ? "Ore boulder" : "Mine boulder";
            rock.transform.position = rocks[i] + Vector3.up * 0.45f;
            rock.transform.localScale = new Vector3(1.15f, 0.82f, 0.95f);
            rock.transform.rotation = Quaternion.Euler(0, i * 37f, i % 2 == 0 ? 7f : -8f);
            rock.GetComponent<Renderer>().sharedMaterial = i % 2 == 0 ? stoneLight : stone;
        }

        GameObject minerPrefab = Resources.Load<GameObject>("Miner");
        GameObject minerObject = minerPrefab != null ? Instantiate(minerPrefab) : MinerVisualFactory.CreateMiner();
        miner = minerObject.transform;
        miner.position = Vector3.zero;
        pickaxeArm = miner.Find("Rig/RightArm/SwingPivot");
        leftBoot = miner.Find("Rig/Body/Left Boot");
        rightBoot = miner.Find("Rig/Body/Right Boot");

        mainCamera = Camera.main;
        if (mainCamera == null)
        {
            var cameraObject = new GameObject("Main Camera");
            cameraObject.tag = "MainCamera";
            mainCamera = cameraObject.AddComponent<Camera>();
        }
        cameraTransform = mainCamera.transform;
        mainCamera.orthographic = false;
        mainCamera.fieldOfView = 48f;
        mainCamera.backgroundColor = new Color(0.10f, 0.15f, 0.18f);
        mainCamera.transform.position = new Vector3(0, 8.5f, -10f);
        mainCamera.transform.LookAt(new Vector3(0, 0.7f, 0));

        RenderSettings.ambientLight = new Color(0.62f, 0.62f, 0.62f);
    }

    private void Update()
    {
        if (miner == null) return;

        Keyboard keyboard = Keyboard.current;
        float x = keyboard == null ? 0 : (keyboard.dKey.isPressed || keyboard.rightArrowKey.isPressed ? 1 : 0) - (keyboard.aKey.isPressed || keyboard.leftArrowKey.isPressed ? 1 : 0);
        float z = keyboard == null ? 0 : (keyboard.wKey.isPressed || keyboard.upArrowKey.isPressed ? 1 : 0) - (keyboard.sKey.isPressed || keyboard.downArrowKey.isPressed ? 1 : 0);
        Vector3 move = new Vector3(x, 0, z).normalized;
        miner.position += move * WalkSpeed * Time.deltaTime;
        miner.position = new Vector3(Mathf.Clamp(miner.position.x, -9.5f, 9.5f), 0, Mathf.Clamp(miner.position.z, -9.5f, 9.5f));

        if (move.sqrMagnitude > 0.01f)
        {
            miner.rotation = Quaternion.Slerp(miner.rotation, Quaternion.LookRotation(move), 14f * Time.deltaTime);
            walkTime += Time.deltaTime * 10f;
        }
        if (leftBoot != null) leftBoot.localPosition = new Vector3(-0.19f, 0.12f + (move.sqrMagnitude > 0.01f ? Mathf.Abs(Mathf.Sin(walkTime)) * 0.06f : 0), 0.02f);
        if (rightBoot != null) rightBoot.localPosition = new Vector3(0.19f, 0.12f + (move.sqrMagnitude > 0.01f ? Mathf.Abs(Mathf.Sin(walkTime + Mathf.PI)) * 0.06f : 0), 0.02f);

        Mouse mouse = Mouse.current;
        if (mouse != null && mouse.leftButton.wasPressedThisFrame && swingTime <= 0)
        {
            swingTime = SwingDuration;
            swingDirection = GetMouseDirection();
            if (swingDirection.sqrMagnitude > 0.01f)
                miner.rotation = Quaternion.LookRotation(swingDirection);
        }

        if (swingTime > 0)
        {
            swingTime -= Time.deltaTime;
            float t = 1f - Mathf.Clamp01(swingTime / SwingDuration);
            float arc = Mathf.Sin(t * Mathf.PI);
            pickaxeArm.localRotation = Quaternion.Euler(-105f * arc, 0, -65f * arc);
        }
        else
        {
            pickaxeArm.localRotation = Quaternion.Slerp(pickaxeArm.localRotation, Quaternion.identity, 16f * Time.deltaTime);
        }

        if (cameraTransform != null)
        {
            Vector3 target = miner.position + new Vector3(0, 8.5f, -10f);
            cameraTransform.position = Vector3.Lerp(cameraTransform.position, target, 5f * Time.deltaTime);
            cameraTransform.LookAt(miner.position + Vector3.up * 0.7f);
        }
    }

    private Vector3 GetMouseDirection()
    {
        if (mainCamera == null) return miner.forward;
        Vector2 pointer = Mouse.current == null ? new Vector2(Screen.width * 0.5f, Screen.height * 0.5f) : Mouse.current.position.ReadValue();
        Ray ray = mainCamera.ScreenPointToRay(pointer);
        Plane ground = new Plane(Vector3.up, Vector3.zero);
        if (ground.Raycast(ray, out float distance))
        {
            Vector3 direction = ray.GetPoint(distance) - miner.position;
            direction.y = 0;
            if (direction.sqrMagnitude > 0.01f) return direction.normalized;
        }
        return miner.forward;
    }

    private void OnGUI()
    {
        GUI.color = new Color(0.95f, 0.91f, 0.80f);
        GUI.Label(new Rect(22, 18, 400, 32), "MINER  •  WASD to move  •  Click to swing pickaxe");
    }

    private static Material MakeMaterial(string materialName, Color color, float metallic = 0f, bool glow = false)
    {
        Shader shader = Shader.Find("Universal Render Pipeline/Lit");
        if (shader == null) shader = Shader.Find("Standard");
        var material = new Material(shader) { name = materialName, color = color };
        if (material.HasProperty("_Metallic")) material.SetFloat("_Metallic", metallic);
        if (glow && material.HasProperty("_EmissionColor"))
        {
            material.EnableKeyword("_EMISSION");
            material.SetColor("_EmissionColor", color * 1.8f);
        }
        return material;
    }
}
