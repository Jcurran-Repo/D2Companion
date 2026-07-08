namespace D2Companion.Brain;

/// <summary>An image the app captured for Claude to look at (encoded bytes + MIME type).</summary>
public sealed record CapturedImage(byte[] Data, string MediaType);
