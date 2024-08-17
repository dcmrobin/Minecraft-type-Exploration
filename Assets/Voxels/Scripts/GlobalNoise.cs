using SimplexNoise;

public static class GlobalNoise {

    public static float[,] GetNoise() {
        Noise.Seed = World.Instance.noiseSeed;
        // The number of points to generate in the 1st and 2nd dimension
        int width = World.Instance.chunkSize * World.Instance.worldSize; 
        int height = World.Instance.chunkSize * World.Instance.worldSize; 
        // The scale of the noise. The greater the scale, the denser the noise gets
        float scale = World.Instance.noiseScale;
        float[,] noise = Noise.Calc2D(width, height, scale); // Returns an array containing 2D Simplex noise
      
        return noise;
    }

    public static float GetGlobalNoiseValue(float globalX, float globalZ, float[,] globalNoiseMap)
    {
        // Convert global coordinates to noise map coordinates
        int noiseMapX = (int)globalX % globalNoiseMap.GetLength(0);
        int noiseMapZ = (int)globalZ % globalNoiseMap.GetLength(1);

        // Ensure that the coordinates are within the bounds of the noise map
        if (
            noiseMapX >= 0 && noiseMapX < globalNoiseMap.GetLength(0) && 
            noiseMapZ >= 0 && noiseMapZ < globalNoiseMap.GetLength(1))
        {
            return globalNoiseMap[noiseMapX, noiseMapZ];
        }
        else
        {
            return 0; // Default value if out of bounds
        }
    }

    public static float GetNoisePoint(int x, int z) 
    {     
        float scale = World.Instance.noiseScale;
        float noise = Noise.CalcPixel2D(x, z, scale);
        
        return noise;
    }

    public static void SetSeed() 
    {
        Noise.Seed = World.Instance.noiseSeed;
    }
}