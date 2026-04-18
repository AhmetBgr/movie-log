namespace MyPrivateWatchlist;

public static class Visuals
{
    public const string RatingDarkRed = "#8B0000";
    public const string RatingYellow = "#dfdf2dff";
    public const string RatingDarkGreen = "#006400";
    
    public const string PrimaryAccent = "#6366f1"; // Indigo
    public const string SecondaryAccent = "#f97316"; // Orange

    /// <summary>
    /// Calculates a color based on a 0-100 rating.
    /// Gradient: 0 (RatingDarkRed) -> 50 (RatingYellow) -> 100 (RatingDarkGreen)
    /// </summary>
    public static string GetRatingColor(double rating)
    {
        rating = Math.Clamp(rating, 0, 100);
        
        var start = HexToRgb(RatingDarkRed);
        var mid = HexToRgb(RatingYellow);
        var end = HexToRgb(RatingDarkGreen);

        int r, g, b;
        if (rating <= 50)
        {
            double t = rating / 50.0;
            r = (int)(start.r + (mid.r - start.r) * t);
            g = (int)(start.g + (mid.g - start.g) * t);
            b = (int)(start.b + (mid.b - start.b) * t);
        }
        else
        {
            double t = (rating - 50) / 50.0;
            r = (int)(mid.r + (end.r - mid.r) * t);
            g = (int)(mid.g + (end.g - mid.g) * t);
            b = (int)(mid.b + (end.b - mid.b) * t);
        }
        
        return $"rgb({r}, {g}, {b})";
    }

    private static (int r, int g, int b) HexToRgb(string hex)
    {
        try
        {
            hex = hex.Replace("#", "");
            if (hex.Length >= 8) hex = hex.Substring(0, 6);
            if (hex.Length != 6) return (128, 128, 128); // Fallback

            return (
                int.Parse(hex.Substring(0, 2), System.Globalization.NumberStyles.HexNumber),
                int.Parse(hex.Substring(2, 2), System.Globalization.NumberStyles.HexNumber),
                int.Parse(hex.Substring(4, 2), System.Globalization.NumberStyles.HexNumber)
            );
        }
        catch { return (128, 128, 128); }
    }

    /// <summary>
    /// Simplified HSL hue calculation if needed for legacy components.
    /// </summary>
    public static string GetRatingHue(double rating)
    {
        double hue = (Math.Clamp(rating, 0, 100) / 100.0) * 120;
        return $"hsl({hue}, 70%, 45%)";
    }
}
