using UnityEngine;

public class NoiseTextureGenerator : MonoBehaviour
{
    public enum NoiseType{PerlinNoise, Worms, PeaksAndValleys};
    [Header("Texture Settings")]
    public int width = 256;
    public int height = 256;

    [Header("Noise Settings")]
    public NoiseType noiseType;
    public float wormCaveNoiseFrequency = 0.07f;  // Same frequency as in your voxel logic
    public float perlinNoiseFrequency = 0.07f;  // Same frequency as in your voxel logic
    [Range(-1f, 1f)]
    public float wormBias = 0;
    [Range(-1f, 1f)]
    public float perlinBias = 0;
    [Range(0.0001f, 10)]
    public float wormCaveSizeMultiplier = 5f;
    [Range(0.0001f, 10)]
    public float perlinSizeMultiplier = 5f;
    public int seed = 0;

    [Header("Color Settings")]
    public Gradient colorGradient; // Gradient to interpolate colors

    private Texture2D noiseTexture;

    // Called when a value is changed in the Inspector
    void Update()
    {
        GenerateAndApplyTexture();
    }

    // Generates and applies the texture in real time
    void GenerateAndApplyTexture()
    {
        if (noiseTexture == null || noiseTexture.width != width || noiseTexture.height != height)
        {
            noiseTexture = new Texture2D(width, height);
        }

        for (int x = 0; x < width; x++)
        {
            for (int y = 0; y < height; y++)
            {
                float voxelNoiseValue = CalculateWormCaveNoise2D(x, y, seed);
                
                // Normalize the noise value (remap between 0 and 1)
                float normalizedNoiseValue = Mathf.InverseLerp(0, 1, voxelNoiseValue);
                
                // Get color from gradient based on normalized noise value
                Color color = colorGradient.Evaluate(normalizedNoiseValue);
                
                noiseTexture.SetPixel(x, y, color);
            }
        }

        noiseTexture.Apply();
        GetComponent<Renderer>().sharedMaterial.mainTexture = noiseTexture; // Apply texture to the object’s material
    }

    // Calculates the 2D noise value similar to the 3D logic you provided
    float CalculateWormCaveNoise2D(float x, float y, int seed)
    {
        // Similar to the 3D Perlin noise logic, but now it's 2D
        float wormNoiseX = (x + seed) * wormCaveNoiseFrequency / wormCaveSizeMultiplier;
        float wormNoiseY = (y + seed) * wormCaveNoiseFrequency / wormCaveSizeMultiplier;
        float perlinNoiseX = (x + seed) * perlinNoiseFrequency / perlinSizeMultiplier;
        float perlinNoiseY = (y + seed) * perlinNoiseFrequency / perlinSizeMultiplier;

        float perlinNoise = Mathf.PerlinNoise(perlinNoiseX, perlinNoiseY) - perlinBias;
        float wormCaveNoise = Mathf.Abs(Mathf.PerlinNoise(wormNoiseX, wormNoiseY) * 2f - 1f) - wormBias;
        float PandVNoise = perlinNoise - wormCaveNoise;

        float remappedNoise = 0;

        if (noiseType == NoiseType.PerlinNoise)
        {
            remappedNoise = perlinNoise;
        }
        else if (noiseType == NoiseType.Worms)
        {
            remappedNoise = wormCaveNoise;
        }
        else if (noiseType == NoiseType.PeaksAndValleys)
        {
            remappedNoise = PandVNoise;
        }

        return remappedNoise;
    }
}
