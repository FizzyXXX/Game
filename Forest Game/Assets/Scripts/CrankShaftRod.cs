using UnityEngine;

public class CrankshaftRod : MonoBehaviour
{
    [Header("Rotating Crank")]
    public Transform crankPin;

    [Header("Reciprocating Point")]
    public Transform reciprocatingPoint;

    [Header("Connecting Rod")]
    public Transform rod;
    public float rodLength = 3f;

    void LateUpdate()
    {
        // Read the actual crank pin position.
        Vector3 crankPos = crankPin.position;

        // The piston moves vertically along Y.
        // Keep its X and Z coordinates fixed.
        float dx = reciprocatingPoint.position.x - crankPos.x;
        float dz = reciprocatingPoint.position.z - crankPos.z;

        float remaining = rodLength * rodLength - dx * dx - dz * dz;

        // Stop if the connecting rod cannot reach the guide.
        if (remaining < 0f)
            return;

        float pistonY = crankPos.y - Mathf.Sqrt(remaining);

        Vector3 pistonPos = new Vector3(
            reciprocatingPoint.position.x,
            pistonY,
            reciprocatingPoint.position.z
        );

        reciprocatingPoint.position = pistonPos;

        // Position the rod between the crank pin and piston pin.
        Vector3 direction = crankPos - pistonPos;

        rod.position = (crankPos + pistonPos) * 0.5f;

        // Assumes the rod's lengthwise axis is local Y.
        rod.rotation = Quaternion.FromToRotation(
            Vector3.up,
            direction.normalized
        );

        // Assumes the cube pivot is at its center.
        Vector3 scale = rod.localScale;
        scale.y = direction.magnitude / 2f;
        rod.localScale = scale;
    }
}