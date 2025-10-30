using UnityEngine;
using Unity.Collections;
using Unity.Jobs;
using Unity.Burst;
using System.Diagnostics; // For Stopwatch
using Debug = UnityEngine.Debug; // To avoid conflicts

public class ParallelImageProcessor : MonoBehaviour
{
    // Assign your texture in the Inspector
    public Texture2D textureToProcess;

    void Start()
    {
        if (textureToProcess == null)
        {
            Debug.LogError("No texture assigned to process!");
            return;
        }

        // --- 1. Setup & Timing ---
        // Use C#'s Stopwatch for high-precision timing
        Stopwatch stopwatch = new Stopwatch();
        stopwatch.Start();

        // Get a direct, read-only NativeArray view of the texture data.
        // This is extremely fast as it avoids copying to a managed array.
        // REQUIRES: Texture must be "Read/Write Enabled" in Import Settings.
        NativeArray<Color32> pixelData = textureToProcess.GetRawTextureData<Color32>();
        int pixelCount = pixelData.Length;

        // --- 2. Allocate NativeArrays ---
        // We use TempJob allocator, which is fast and lasts for a few frames.
        // We must manually .Dispose() these when done.
        
        // Intermediate array to hold the R-value from each pixel
        NativeArray<int> rValues = new NativeArray<int>(pixelCount, Allocator.TempJob);

        // Output array to hold the final sum.
        // We use a long (64-bit int) to prevent overflow from large images.
        // (e.g., 8 million pixels * 255 = ~2 billion, which is close to int.MaxValue)
        NativeArray<long> finalSum = new NativeArray<long>(1, Allocator.TempJob);
        finalSum[0] = 0; // Initialize

        // --- 3. Define the Jobs ---
        
        // Job 1: "Map" - Extracts the R-value for every pixel in parallel.
        var extractJob = new ExtractRChannelJob
        {
            pixels = pixelData, // [ReadOnly]
            rValues = rValues      // [WriteOnly]
        };

        // Job 2: "Reduce" - Sums the R-values into a single result.
        var sumJob = new SumValuesJob
        {
            rValues = rValues,     // [ReadOnly]
            totalSum = finalSum    // [WriteOnly]
        };

        // --- 4. Schedule the Jobs ---
        
        // Schedule Job 1 to run in parallel.
        // We divide the work into batches of 256 pixels per thread.
        JobHandle extractHandle = extractJob.Schedule(pixelCount, 256);

        // Schedule Job 2 to run *after* Job 1 is complete.
        // This creates a dependency chain.
        JobHandle sumHandle = sumJob.Schedule(extractHandle);

        // --- 5. Wait for Completion & Get Results ---
        
        // This line forces the main thread to wait until 'sumHandle' (and its
        // dependency 'extractHandle') are finished.
        sumHandle.Complete();

        long totalRedSum = finalSum[0];
        
        stopwatch.Stop();

        // --- 6. Log Results & Clean Up ---

        Debug.Log($"--- Parallel Image Processing Complete ---");
        Debug.Log($"Texture: {textureToProcess.name} ({textureToProcess.width}x{textureToProcess.height})");
        Debug.Log($"Total Pixels: {pixelCount:N0}");
        Debug.Log($"Total Red Channel Sum: {totalRedSum:N0}");
        Debug.Log($"Execution Time: {stopwatch.Elapsed.TotalMilliseconds} ms");

        // CRITICAL: Always dispose of NativeArrays you created.
        rValues.Dispose();
        finalSum.Dispose();
    }
}

// -------------------------------------------------------------------
// JOBS
// -------------------------------------------------------------------

/// <summary>
/// JOB 1: "Map"
/// Runs in parallel for every pixel. Extracts the 'R' value.
/// </summary>
[BurstCompile(CompileSynchronously = true)]
struct ExtractRChannelJob : IJobParallelFor
{
    // Input (read-only)
    [ReadOnly]
    public NativeArray<Color32> pixels;

    // Output (write-only)
    [WriteOnly]
    public NativeArray<int> rValues;

    // This Execute method is called once for each pixel (index)
    public void Execute(int index)
    {
        rValues[index] = pixels[index].r;
    }
}

/// <summary>
/// JOB 2: "Reduce"
/// Runs as a single job *after* Job 1. Sums the results.
/// </summary>
[BurstCompile(CompileSynchronously = true)]
struct SumValuesJob : IJob
{
    // Input (read-only)
    [ReadOnly]
    public NativeArray<int> rValues;

    // Output (write-only)
    [WriteOnly]
    public NativeArray<long> totalSum;

    // This Execute method is called once.
    public void Execute()
    {
        long sum = 0;
        for (int i = 0; i < rValues.Length; i++)
        {
            sum += rValues[i];
        }
        totalSum[0] = sum;
    }
}