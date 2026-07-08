namespace D2Companion.Voice;

/// <summary>
/// The one energy metric used everywhere (open-mic listening AND the settings tuner):
/// mean absolute amplitude of 16-bit PCM samples. Keeping it shared guarantees the
/// tuner measures exactly what real listening will act on.
/// </summary>
public static class FrameEnergy
{
    public static double Of(byte[] buffer)
    {
        if (buffer.Length < 2) return 0;
        long sum = 0;
        var samples = buffer.Length / 2;
        for (var i = 0; i + 1 < buffer.Length; i += 2)
        {
            var sample = (short)(buffer[i] | (buffer[i + 1] << 8));
            sum += Math.Abs((int)sample);
        }
        return (double)sum / samples;
    }
}
