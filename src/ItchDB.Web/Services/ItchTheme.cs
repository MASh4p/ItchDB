using MudBlazor;

namespace ItchDB.Web.Services;

public static class ItchTheme
{
    public const string Accent = "#FA5C5C";

    public static readonly MudTheme Theme = new()
    {
        PaletteDark = new PaletteDark
        {
            Primary = Accent,
            PrimaryContrastText = "#FFFFFF",
            Secondary = "#5CC8FA",
            Tertiary = "#B98CFF",
            Info = "#5CA9FA",
            Success = "#4CC38A",
            Warning = "#F5B84A",
            Error = "#FF6B6B",
            Black = "#0E0F13",
            Background = "#121318",
            BackgroundGray = "#17181E",
            Surface = "#1B1C23",
            DrawerBackground = "#16171D",
            DrawerText = "rgba(255,255,255,0.80)",
            DrawerIcon = "rgba(255,255,255,0.65)",
            AppbarBackground = "#1B1C23",
            AppbarText = "rgba(255,255,255,0.90)",
            TextPrimary = "rgba(255,255,255,0.90)",
            TextSecondary = "rgba(255,255,255,0.62)",
            ActionDefault = "rgba(255,255,255,0.62)",
            LinesDefault = "rgba(255,255,255,0.10)",
            TableLines = "rgba(255,255,255,0.08)",
            TableHover = "rgba(250,92,92,0.06)",
            Divider = "rgba(255,255,255,0.10)",
            OverlayLight = "rgba(18,19,24,0.6)",
        },
        Typography = new Typography
        {
            Default = new DefaultTypography
            {
                FontFamily = ["Segoe UI", "Inter", "Roboto", "Helvetica Neue", "Arial", "sans-serif"],
            },
        },
        LayoutProperties = new LayoutProperties
        {
            DefaultBorderRadius = "8px",
            DrawerWidthLeft = "232px",
        },
    };
}
