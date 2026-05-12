using System.Windows.Media;
using LauncherCatalog.Models;
using MaterialDesignThemes.Wpf;

namespace LauncherCatalog.Services;

public sealed class ThemeService
{
    public void Apply(AppTheme theme)
    {
        if (theme == AppTheme.Dark)
        {
            ApplyDark();
            return;
        }

        ApplyLight();
    }

    private static void ApplyLight()
    {
        ApplyMaterialTheme(BaseTheme.Light, "#2563EB", "#2563EB");
        SetBrush("AccentBrush", "#2563EB");
        SetBrush("AccentSoftBrush", "#EAF1FF");
        SetBrush("SurfaceBrush", "#FFFFFFFF");
        SetBrush("PageBrush", "#F4F7FB");
        SetBrush("BorderBrush", "#DDE5F0");
        SetBrush("MutedTextBrush", "#65758B");
        SetBrush("StrongTextBrush", "#172033");
        SetBrush("DangerBrush", "#B42318");
        SetBrush("DangerSoftBrush", "#FFF1F0");
        SetBrush("SuccessBrush", "#087443");
        SetBrush("SuccessSoftBrush", "#EAF8F1");
        SetBrush("ControlBrush", "#FFFFFFFF");
        SetBrush("ControlMutedBrush", "#F8FAFC");
        SetBrush("ControlHoverBrush", "#F8FAFC");
        SetBrush("ControlPressedBrush", "#EDF2F7");
        SetBrush("PrimaryHoverBrush", "#1D4ED8");
        SetBrush("PrimaryPressedBrush", "#1E40AF");
        SetBrush("SelectedItemBrush", "#E8F1FF");
        SetBrush("SelectedItemBorderBrush", "#1E3A8A");
        SetBrush("MaterialDesign.Brush.ComboBox.HoverBackground", "#00FFFFFF");
        SetBrush("MaterialDesign.Brush.ComboBox.HoverBorder", "#DDE5F0");
        SetBrush("MaterialDesign.Brush.TextBox.ComboBoxHover", "#DDE5F0");
    }

    private static void ApplyDark()
    {
        ApplyMaterialTheme(BaseTheme.Dark, "#60A5FA", "#60A5FA");
        SetBrush("AccentBrush", "#60A5FA");
        SetBrush("AccentSoftBrush", "#17365F");
        SetBrush("SurfaceBrush", "#151B24");
        SetBrush("PageBrush", "#0D1118");
        SetBrush("BorderBrush", "#2A3545");
        SetBrush("MutedTextBrush", "#9AA8BA");
        SetBrush("StrongTextBrush", "#E7EDF6");
        SetBrush("DangerBrush", "#FCA5A5");
        SetBrush("DangerSoftBrush", "#3A1D22");
        SetBrush("SuccessBrush", "#86EFAC");
        SetBrush("SuccessSoftBrush", "#173226");
        SetBrush("ControlBrush", "#111721");
        SetBrush("ControlMutedBrush", "#1D2734");
        SetBrush("ControlHoverBrush", "#202B3A");
        SetBrush("ControlPressedBrush", "#263447");
        SetBrush("PrimaryHoverBrush", "#3B82F6");
        SetBrush("PrimaryPressedBrush", "#2563EB");
        SetBrush("SelectedItemBrush", "#122A4A");
        SetBrush("SelectedItemBorderBrush", "#60A5FA");
        SetBrush("MaterialDesign.Brush.ComboBox.HoverBackground", "#00FFFFFF");
        SetBrush("MaterialDesign.Brush.ComboBox.HoverBorder", "#2A3545");
        SetBrush("MaterialDesign.Brush.TextBox.ComboBoxHover", "#2A3545");
    }

    private static void SetBrush(string key, string hex)
    {
        var color = (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(hex);
        System.Windows.Application.Current.Resources[key] = new SolidColorBrush(color);
    }

    private static void ApplyMaterialTheme(BaseTheme baseTheme, string primaryHex, string secondaryHex)
    {
        var paletteHelper = new PaletteHelper();
        Theme theme = paletteHelper.GetTheme();
        theme.SetBaseTheme(baseTheme);
        theme.SetPrimaryColor(ParseColor(primaryHex));
        theme.SetSecondaryColor(ParseColor(secondaryHex));
        paletteHelper.SetTheme(theme);
    }

    private static System.Windows.Media.Color ParseColor(string hex)
    {
        return (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(hex);
    }
}
