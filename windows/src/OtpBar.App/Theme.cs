using System.Windows;
using System.Windows.Media;
using Microsoft.Win32;

namespace OtpBar.App;

/// <summary>The upstream palette, resolved once against the Windows app theme and published as app resources.</summary>
public static class Theme
{
    private const string PersonalizeKey = @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";

    public static bool IsDark { get; private set; }

    /// <summary>The taskbar follows its own setting, so the tray glyph is chosen separately from the window palette.</summary>
    public static bool IsTaskbarDark { get; private set; }

    public static void Apply(ResourceDictionary resources)
    {
        IsDark = ReadFlag("AppsUseLightTheme") == 0;
        IsTaskbarDark = ReadFlag("SystemUsesLightTheme") == 0;

        Add(resources, "Surface", 0xf8f8f8, 0x292a2d);
        Add(resources, "Sidebar", 0xeeeeef, 0x242528);
        Add(resources, "Field", 0xffffff, 0x35363a);
        Add(resources, "Line", 0xdedee2, 0x44464c);
        Add(resources, "Selected", 0xdedfe3, 0x42444a);
        Add(resources, "Text", 0x202124, 0xf1f1f3);
        Add(resources, "Muted", 0x62656b, 0xa8abb2);
        Add(resources, "Danger", 0xb42318, 0xff9c91);
        Add(resources, "Accent", 0x0765cf, 0x0765cf);
        Add(resources, "OnAccent", 0xffffff, 0xffffff);
    }

    private static void Add(ResourceDictionary resources, string name, int light, int dark)
    {
        var hex = IsDark ? dark : light;
        var colour = Color.FromRgb((byte)(hex >> 16), (byte)(hex >> 8), (byte)hex);
        resources[name + "Color"] = colour;
        resources[name + "Brush"] = new SolidColorBrush(colour);
    }

    private static int ReadFlag(string name)
    {
        using var key = Registry.CurrentUser.OpenSubKey(PersonalizeKey);
        return key?.GetValue(name) as int? ?? 1;
    }
}
