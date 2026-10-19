namespace DuckStudio;

public static class OrbitControls
{
    // Move the model with the pointer by orbiting the camera in the opposite direction.
    public static double HorizontalDelta(double pixels, bool inverted, double sensitivity = .008)
        => pixels * (inverted ? 1 : -1) * sensitivity;
}
