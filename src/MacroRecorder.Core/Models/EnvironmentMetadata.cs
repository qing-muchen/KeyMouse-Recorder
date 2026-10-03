namespace MacroRecorder.Core.Models;

/// <summary>Stable screen metadata; contains no runtime handles or platform API types.</summary>
public sealed record EnvironmentMetadata(int ScreenWidth, int ScreenHeight, double DpiScale);
