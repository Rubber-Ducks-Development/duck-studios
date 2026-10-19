namespace DuckStudio;

public static class OrbitControls
{
    public static bool IsSurfaceInput(System.Windows.DependencyObject? source, System.Windows.DependencyObject surface)
    {
        while (source != null && !ReferenceEquals(source, surface))
        {
            if (source is System.Windows.Controls.Primitives.ButtonBase or System.Windows.Controls.Primitives.Selector or System.Windows.Controls.Primitives.TextBoxBase or System.Windows.Controls.Slider) return false;
            source = PanelDismissal.Parent(source);
        }
        return ReferenceEquals(source, surface);
    }
    // Move the model with the pointer by orbiting the camera in the opposite direction.
    public static double HorizontalDelta(double pixels, bool inverted, double sensitivity = .008)
        => pixels * (inverted ? 1 : -1) * sensitivity;
}
